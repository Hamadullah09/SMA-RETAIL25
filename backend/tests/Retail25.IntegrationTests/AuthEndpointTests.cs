using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Infrastructure.Identity;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// Sign-up and password recovery, end to end against a running API.
/// <para>
/// The properties under test are as much about what these endpoints <em>refuse to say</em> as about
/// what they do. An account system that answers "no such email" is an account system that will hand
/// an attacker the staff list, and that is not something a unit test on a handler can catch — it is a
/// property of the HTTP responses.
/// </para>
/// </summary>
[Collection(AuthApiCollection.Name)]
public sealed class AuthEndpointTests
{
    private readonly AuthApiFixture _api;

    public AuthEndpointTests(AuthApiFixture api) => _api = api;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@retail25.test";

    private const string GoodPassword = "Correct-Horse-Battery-9!";

    // ---------------------------------------------------------------------------------------------
    // Sign-up
    // ---------------------------------------------------------------------------------------------

    [RequiresDockerFact]
    public async Task A_new_account_is_created()
    {
        var client = _api.CreateClient();
        var email = Unique("new");

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "Sam Taylor", password = GoodPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var created = await users.FindByEmailAsync(email);

        created.Should().NotBeNull();
        created!.DisplayName.Should().Be("Sam Taylor");
    }

    /// <summary>
    /// The safest possible landing place. A stranger who signs up gets an access level whose sales
    /// commit nothing — no stock movement, no drawer, no loyalty — so the worst they can do is
    /// practise.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_new_account_lands_in_training_mode()
    {
        var client = _api.CreateClient();
        var email = Unique("trainee");

        await client.PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "Trainee", password = GoodPassword });

        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var created = await users.FindByEmailAsync(email);
        var roles = await users.GetRolesAsync(created!);

        roles.Should().ContainSingle().Which.Should().Be("Trainee");
    }

    /// <summary>
    /// The enumeration test. Signing up with an address that already has an account must be
    /// indistinguishable from signing up with one that does not — otherwise the form answers "does
    /// this person work here?" for anyone who asks.
    /// </summary>
    [RequiresDockerFact]
    public async Task Signing_up_twice_does_not_reveal_that_the_account_exists()
    {
        var client = _api.CreateClient();
        var email = Unique("duplicate");

        var first = await client.PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "First", password = GoodPassword });

        var second = await client.PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "Impostor", password = GoodPassword });

        first.IsSuccessStatusCode.Should().BeTrue();
        second.IsSuccessStatusCode.Should().BeTrue();
        second.StatusCode.Should().NotBe(HttpStatusCode.Conflict);

        // And the second attempt must not have overwritten anything.
        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        (await users.FindByEmailAsync(email))!.DisplayName.Should().Be("First");
    }

    /// <summary>
    /// Two separate gates, and both have to answer usefully.
    /// <para>
    /// A password under the length floor is stopped by model validation before the handler runs; one
    /// that is long enough but trivial gets through to Identity's own validators. They produce
    /// different response shapes, which is exactly why both are worth asserting — a form that only
    /// handles one of them shows "invalid" with no reason for the other.
    /// </para>
    /// </summary>
    [RequiresDockerTheory]
    [InlineData("short", "under the length floor")]
    [InlineData("aaaaaaaaaaaaaa", "long enough but trivial")]
    public async Task A_weak_password_is_refused_with_a_reason(string password, string why)
    {
        var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/register",
            new { email = Unique("weak"), displayName = "Weak", password });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, why);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The reason has to survive to the client, or the form can only say "invalid" and the user
        // guesses which rule they broke. The key's casing differs between the two gates, so the
        // assertion is on the field being named at all, not on how.
        var errors = problem.GetProperty("errors")
            .EnumerateObject()
            .Where(p => p.Name.Contains("password", StringComparison.OrdinalIgnoreCase))
            .ToList();

        errors.Should().NotBeEmpty("the response must say which field was rejected");
        errors.Should().Contain(p => p.Value.GetArrayLength() > 0, "and why");
    }

    [RequiresDockerFact]
    public async Task A_malformed_email_is_refused()
    {
        var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/register",
            new { email = "not-an-address", displayName = "Nobody", password = GoodPassword });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------------------------------------
    // Password recovery
    // ---------------------------------------------------------------------------------------------

    [RequiresDockerFact]
    public async Task An_unknown_address_gets_the_same_answer_as_a_known_one()
    {
        var client = _api.CreateClient();
        var email = Unique("known");

        await client.PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "Known", password = GoodPassword });

        var known = await client.PostAsJsonAsync("/api/v1/account/forgot-password", new { email });
        var unknown = await client.PostAsJsonAsync(
            "/api/v1/account/forgot-password",
            new { email = Unique("never-existed") });

        known.StatusCode.Should().Be(HttpStatusCode.Accepted);
        unknown.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [RequiresDockerFact]
    public async Task No_link_is_sent_for_an_address_with_no_account()
    {
        var client = _api.CreateClient();
        var email = Unique("ghost");

        var before = _api.Notifier.Resets.Count;

        await client.PostAsJsonAsync("/api/v1/account/forgot-password", new { email });

        // The 202 above is a courtesy to the enquirer. It must not also be a mail to a stranger.
        _api.Notifier.Resets.Should().HaveCount(before);
    }

    /// <summary>The whole loop: ask, follow the link, sign in with the new password.</summary>
    [RequiresDockerFact]
    public async Task A_reset_link_can_be_redeemed_for_a_new_password()
    {
        var client = _api.CreateClient();
        var email = Unique("recover");
        const string NewPassword = "Entirely-Different-77!";

        await client.PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "Recovering", password = GoodPassword });

        await client.PostAsJsonAsync("/api/v1/account/forgot-password", new { email });

        var token = TokenFrom(email);

        var reset = await client.PostAsJsonAsync(
            "/api/v1/account/reset-password",
            new { email, token, password = NewPassword });

        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);

        (await users.CheckPasswordAsync(user!, NewPassword)).Should().BeTrue();
        (await users.CheckPasswordAsync(user!, GoodPassword)).Should().BeFalse();
    }

    /// <summary>
    /// Single use. A reset link sits in an inbox indefinitely, and an inbox is exactly the thing that
    /// gets compromised — a link that still works next month is a standing key.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_reset_link_cannot_be_used_twice()
    {
        var client = _api.CreateClient();
        var email = Unique("replay");

        await client.PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "Replay", password = GoodPassword });

        await client.PostAsJsonAsync("/api/v1/account/forgot-password", new { email });

        var token = TokenFrom(email);

        var first = await client.PostAsJsonAsync(
            "/api/v1/account/reset-password",
            new { email, token, password = "First-Change-Here-1!" });

        var second = await client.PostAsJsonAsync(
            "/api/v1/account/reset-password",
            new { email, token, password = "Second-Change-Here-2!" });

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [RequiresDockerFact]
    public async Task A_forged_token_is_refused()
    {
        var client = _api.CreateClient();
        var email = Unique("forged");

        await client.PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "Forged", password = GoodPassword });

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/reset-password",
            new { email, token = "bm90LWEtcmVhbC10b2tlbg", password = "Does-Not-Matter-1!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A token for one account must not work on another. This is the mistake that turns a recovery
    /// flow into an account-takeover flow, and it is invisible unless a test tries it.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_token_issued_for_one_account_does_not_work_on_another()
    {
        var client = _api.CreateClient();
        var victim = Unique("victim");
        var attacker = Unique("attacker");

        foreach (var email in new[] { victim, attacker })
        {
            await client.PostAsJsonAsync(
                "/api/v1/account/register",
                new { email, displayName = "Someone", password = GoodPassword });
        }

        await client.PostAsJsonAsync("/api/v1/account/forgot-password", new { email = attacker });

        var attackersToken = TokenFrom(attacker);

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/reset-password",
            new { email = victim, token = attackersToken, password = "Taken-Over-Now-1!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(victim);

        (await users.CheckPasswordAsync(user!, GoodPassword)).Should().BeTrue();
    }

    /// <summary>
    /// Recovery is what someone does when they think their account is compromised. Leaving the
    /// intruder's session alive would defeat the point, so the security stamp has to move.
    /// </summary>
    [RequiresDockerFact]
    public async Task Resetting_a_password_invalidates_existing_sessions()
    {
        var client = _api.CreateClient();
        var email = Unique("stamp");

        await client.PostAsJsonAsync(
            "/api/v1/account/register",
            new { email, displayName = "Stamped", password = GoodPassword });

        string before;

        using (var scope = _api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            before = (await users.FindByEmailAsync(email))!.SecurityStamp!;
        }

        await client.PostAsJsonAsync("/api/v1/account/forgot-password", new { email });

        await client.PostAsJsonAsync(
            "/api/v1/account/reset-password",
            new { email, token = TokenFrom(email), password = "Rotated-Stamp-Now-3!" });

        using (var scope = _api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.FindByEmailAsync(email))!.SecurityStamp.Should().NotBe(before);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The provider itself
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The discovery document is what makes the whole authorization-code flow work. If PKCE with
    /// S256 is not advertised, a client is entitled to fall back to something weaker.
    /// </summary>
    [RequiresDockerFact]
    public async Task The_discovery_document_advertises_authorization_code_with_pkce()
    {
        var client = _api.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");

        Values(document, "grant_types_supported").Should().Contain("authorization_code");
        Values(document, "code_challenge_methods_supported").Should().Contain("S256");
        Values(document, "response_types_supported").Should().Contain("code");
    }

    /// <summary>
    /// The authorization endpoint must not issue a code without a challenge. Advertising S256 while
    /// accepting a request that omits it would be worse than not advertising it.
    /// </summary>
    [RequiresDockerFact]
    public async Task The_authorization_endpoint_refuses_a_request_with_no_pkce_challenge()
    {
        var client = _api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(
            "/connect/authorize"
            + "?client_id=retail25-web"
            + "&response_type=code"
            + "&redirect_uri=" + HttpUtility.UrlEncode("http://localhost:3000/api/auth/callback")
            + "&scope=openid");

        // Either an outright error or a redirect carrying one — what must not happen is a challenge
        // to sign in, which would mean the request was accepted.
        var location = response.Headers.Location?.ToString() ?? string.Empty;

        var accepted = response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found
            && location.Contains("/account/login", StringComparison.OrdinalIgnoreCase);

        accepted.Should().BeFalse("a request with no code_challenge must not reach the sign-in page");
    }

    /// <summary>
    /// A stale login form must not produce a 500.
    /// <para>
    /// The antiforgery token is bound to the identity that fetched the page, so it stops matching as
    /// soon as that identity changes — a login tab left open while you sign in elsewhere, a
    /// back-button submit after signing out, a session the browser restored on restart. All ordinary
    /// things, and all of them used to surface as a bare "An unexpected error occurred" on the one
    /// screen where that is least helpful.
    /// </para>
    /// </summary>
    [RequiresDockerFact]
    public async Task A_login_post_with_no_antiforgery_token_is_not_a_server_error()
    {
        var client = _api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = "someone@retail25.test",
                ["password"] = "does-not-matter",
                ["returnUrl"] = "/",
            }));

        ((int)response.StatusCode).Should().BeLessThan(500, "a stale form is the user's browser, not a server fault");

        // And it must land back on the form, where a fresh token is issued, rather than anywhere the
        // user has to work out for themselves.
        response.Headers.Location?.ToString().Should().Contain("/account/login");
    }

    /// <summary>
    /// Signing out is the one action where refusing on a stale token serves nobody: the worst a
    /// forged sign-out can do is sign you out, while failing it strands someone in a session they
    /// have explicitly asked to leave.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_logout_with_no_antiforgery_token_still_signs_out()
    {
        var client = _api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/account/logout", new FormUrlEncodedContent([]));

        ((int)response.StatusCode).Should().BeLessThan(500);
    }

    [RequiresDockerFact]
    public async Task The_seeded_administrator_can_sign_in()
    {
        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await users.FindByEmailAsync(AuthApiFixture.AdminEmail);

        admin.Should().NotBeNull();
        (await users.CheckPasswordAsync(admin!, AuthApiFixture.AdminPassword)).Should().BeTrue();
        (await users.GetRolesAsync(admin!)).Should().Contain("Administrator");
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>Pulls the token out of the link the notifier captured, the way a browser would.</summary>
    private string TokenFrom(string email)
    {
        var link = _api.Notifier.Resets.Last(r => string.Equals(r.Email, email, StringComparison.OrdinalIgnoreCase)).Link;

        return HttpUtility.ParseQueryString(new Uri(link).Query)["token"]
            ?? throw new InvalidOperationException("The reset link carried no token.");
    }

    private static IEnumerable<string> Values(JsonElement document, string property)
        => document.GetProperty(property).EnumerateArray().Select(e => e.GetString() ?? string.Empty);
}
