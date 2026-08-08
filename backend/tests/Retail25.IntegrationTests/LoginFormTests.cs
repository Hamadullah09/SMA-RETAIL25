using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Infrastructure.Identity;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// The sign-in form, driven the way a browser drives it.
/// <para>
/// The rest of the auth suite checks the pieces — that a password verifies, that a role is granted,
/// that a token is refused. None of it posts the form, and the form is where every reported sign-in
/// problem has actually been: an antiforgery token that no longer matched its cookie, a page restored
/// from history, a redirect that threw away what had been typed. So this suite starts at
/// <c>GET /account/login</c> and ends at a session cookie, with nothing stubbed in between.
/// </para>
/// </summary>
[Collection(AuthApiCollection.Name)]
public sealed class LoginFormTests
{
    private readonly AuthApiFixture _api;

    public LoginFormTests(AuthApiFixture api) => _api = api;

    private const string GoodPassword = "Correct-Horse-Battery-9!";

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@retail25.test";

    /// <summary>
    /// A client that keeps cookies and does not chase redirects — both essential here. The
    /// antiforgery cookie has to survive from the GET to the POST, and the outcome of a sign-in
    /// attempt <em>is</em> the redirect, so following it would discard the thing under test.
    /// </summary>
    private HttpClient Browser() =>
        _api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The end-to-end path: sign up, then sign in through the form with the account just created.
    /// <para>
    /// Registration and sign-in are tested separately elsewhere and both passed while an account
    /// created by one could not be used by the other — a role that failed to grant produced a working
    /// password attached to no permissions at all. Joining them is the only way that shows up.
    /// </para>
    /// </summary>
    [RequiresDockerFact]
    public async Task An_account_created_by_signing_up_can_then_sign_in()
    {
        var email = Unique("roundtrip");

        var registered = await _api.CreateClient().PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "Rowan Price", password = GoodPassword });

        registered.StatusCode.Should().Be(HttpStatusCode.Created);

        var browser = Browser();
        var outcome = await SignInAsync(browser, email, GoodPassword, returnUrl: "/");

        outcome.Should().Be("/", "a successful sign-in returns to where it started, with no error");

        // A session, not merely a redirect. Without this the assertion above passes on any redirect.
        browser.DefaultRequestHeaders.Should().NotBeNull();
        (await IsSignedInAsync(browser)).Should().BeTrue("the sign-in should have issued a session cookie");

        // And the account can actually do something: a role, and the permissions behind it.
        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);

        (await users.GetRolesAsync(user!)).Should().NotBeEmpty(
            "an account with no role has a working password and no permission to use it");
    }

    [RequiresDockerFact]
    public async Task A_wrong_password_is_refused_without_saying_which_half_was_wrong()
    {
        var email = Unique("wrong");

        await _api.CreateClient().PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "Wrong Password", password = GoodPassword });

        var outcome = await SignInAsync(Browser(), email, "not-the-right-password", returnUrl: "/");

        outcome.Should().Contain("error=");
        Message(outcome).Should().Be("Those details were not recognised.");
    }

    /// <summary>
    /// An address with no account gets the same words as a wrong password. Different messages here
    /// turn the form into a way to ask "does this person bank with you".
    /// </summary>
    [RequiresDockerFact]
    public async Task An_unknown_account_gets_the_same_message_as_a_wrong_password()
    {
        var outcome = await SignInAsync(Browser(), Unique("ghost"), GoodPassword, returnUrl: "/");

        Message(outcome).Should().Be("Those details were not recognised.");
    }

    /// <summary>
    /// The username survives a failed attempt. It is not a secret, and making someone retype an
    /// email address after every miss is how a typo turns into a lockout.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_failed_attempt_puts_the_username_back_in_the_form()
    {
        var email = Unique("remembered");

        await _api.CreateClient().PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "Remembered", password = GoodPassword });

        var browser = Browser();
        var outcome = await SignInAsync(browser, email, "wrong", returnUrl: "/");

        var page = await browser.GetStringAsync(outcome);

        page.Should().Contain(
            $"value=\"{WebUtility.HtmlEncode(email)}\"",
            "the address that was typed should still be there");

        // And the caret belongs on the field that needs retyping.
        Regex.IsMatch(page, "id=\"password\"[^>]*autofocus").Should().BeTrue();
    }

    /// <summary>
    /// A POST with no antiforgery token is a stale form, not a server fault. It has to come back as
    /// the form again with a fresh token — a 500 here is the one place a bare "something went wrong"
    /// helps least.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_post_with_no_antiforgery_token_re_renders_the_form_rather_than_failing()
    {
        var browser = Browser();

        var response = await browser.PostAsync("/account/login", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Username", "someone@retail25.test"),
            new KeyValuePair<string, string>("Password", "whatever"),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
        ]));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var location = response.Headers.Location!.ToString();
        Message(location).Should().Be("That form had expired. Please try again.");

        // Following it gets a usable form back, not another failure.
        var page = await browser.GetStringAsync(location);
        page.Should().Contain("__RequestVerificationToken");
    }

    /// <summary>
    /// The login page must never be cached or restored from the back-forward cache: a page redrawn
    /// from history carries a token the server has moved past, and the only symptom is "that form had
    /// expired" on a form filled in seconds earlier.
    /// </summary>
    [RequiresDockerFact]
    public async Task The_login_page_is_never_cached()
    {
        var response = await Browser().GetAsync("/account/login");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    /// <summary>
    /// An open redirect on a sign-in page hands an attacker a credible way to bounce a freshly
    /// authenticated user onto a site of their choosing.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_return_url_pointing_off_this_host_is_ignored()
    {
        var email = Unique("redirect");

        await _api.CreateClient().PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "Redirected", password = GoodPassword });

        var outcome = await SignInAsync(Browser(), email, GoodPassword, returnUrl: "https://evil.example/steal");

        outcome.Should().Be("/", "an off-host return URL is discarded rather than honoured");
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Fetches the form, reads its token out of the HTML, and posts it — exactly the two requests a
    /// browser makes. Returns the Location the server redirected to.
    /// </summary>
    private static async Task<string> SignInAsync(HttpClient browser, string username, string password, string returnUrl)
    {
        var form = await browser.GetStringAsync($"/account/login?returnUrl={WebUtility.UrlEncode(returnUrl)}");

        var token = Regex.Match(
            form,
            "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"").Groups["token"].Value;

        token.Should().NotBeNullOrWhiteSpace("the form must carry an antiforgery token");

        var response = await browser.PostAsync("/account/login", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Username", username),
            new KeyValuePair<string, string>("Password", password),
            new KeyValuePair<string, string>("ReturnUrl", returnUrl),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        ]));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect, "every outcome of the form is a redirect");

        return response.Headers.Location!.ToString();
    }

    /// <summary>
    /// Asks the authorization endpoint whether this client is already authenticated. A signed-in
    /// browser is sent onward with a code; an anonymous one is sent to the login page.
    /// </summary>
    private static async Task<bool> IsSignedInAsync(HttpClient browser)
    {
        var response = await browser.GetAsync(
            "/connect/authorize?client_id=retail25-web&response_type=code"
            + "&redirect_uri=http%3A%2F%2Flocalhost%3A3000%2Fapi%2Fauth%2Fcallback"
            + "&scope=openid+profile&state=probe&nonce=probe"
            + "&code_challenge=aRdG-DVuMbj2fJ-CDHbp-OD0XKaG3LPmSc0jvVeIEPc&code_challenge_method=S256");

        var location = response.Headers.Location?.ToString() ?? string.Empty;

        return !location.Contains("/account/login", StringComparison.OrdinalIgnoreCase);
    }

    private static string Message(string location)
        => HttpUtility.ParseQueryString(new Uri(location, UriKind.RelativeOrAbsolute).ToString().Split('?').Last())["error"]
           ?? string.Empty;
}
