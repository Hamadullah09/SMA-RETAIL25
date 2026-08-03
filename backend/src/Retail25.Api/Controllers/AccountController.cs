using System.Net;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Retail25.Application.Abstractions;
using Retail25.Domain.Security;
using Retail25.Infrastructure.Identity;

namespace Retail25.Api.Controllers;

/// <summary>
/// The sign-in page the authorization endpoint challenges to.
/// <para>
/// It is served by the identity provider rather than the BFF on purpose: credentials are posted to
/// the only process that can check them, and the application never sees a password even in transit.
/// It is deliberately plain — a self-contained form with no scripts, no fonts and no third-party
/// origins, so the strictest possible content-security policy applies to the one page where a
/// password is typed.
/// </para>
/// </summary>
[AllowAnonymous]
[Route("account")]
public sealed class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditWriter _audit;
    private readonly IAntiforgery _antiforgery;
    private readonly AntiforgeryOptions _antiforgeryOptions;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuditWriter audit,
        IAntiforgery antiforgery,
        IOptions<AntiforgeryOptions> antiforgeryOptions,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _audit = audit;
        _antiforgery = antiforgery;
        _antiforgeryOptions = antiforgeryOptions.Value;
        _logger = logger;
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null, string? error = null, string? username = null)
    {
        // Never cached, and never restored from the back-forward cache.
        //
        // The page carries a single-use antiforgery token bound to the cookie set alongside it. A
        // browser that re-shows a copy from history hands back a token the server has already moved
        // past, and the only symptom is "that form had expired" on a form the user has just this
        // moment filled in. no-store is what keeps the token on screen and the token on the cookie the
        // same one.
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";

        return Content(RenderLoginPage(returnUrl, error, username), "text/html; charset=utf-8");
    }

    // Validated by hand rather than [ValidateAntiForgeryToken]: that attribute resolves
    // ValidateAntiforgeryTokenAuthorizationFilter, which only ASP.NET Core's MVC *Views* feature
    // registers (AddControllersWithViews/AddMvc). This API deliberately stays on plain
    // AddControllers — no view engine in the process holding the signing keys — so the attribute
    // had no filter behind it and every login POST 500'd before the action ever ran.
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromForm] LoginForm form)
    {
        // Only ever a path on this host: an open redirect on a login page hands an attacker a
        // credible way to bounce a freshly authenticated user anywhere they like.
        var returnUrl = Url.IsLocalUrl(form.ReturnUrl) ? form.ReturnUrl! : "/";

        try
        {
            await _antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException failure)
        {
            // Throw the cookie away before redirecting.
            //
            // This is what makes the retry actually work. The common cause is a cookie this process
            // cannot decrypt — one issued under a keyring that has since been replaced. The framework
            // treats an undecryptable cookie as absent and mints a new one, but the old one is still
            // in the browser, and if it is ever the one presented back the failure repeats: the user
            // sees "that form had expired" every single time, with no way out but clearing cookies.
            //
            // Deleting it explicitly means the next GET starts from nothing, so the pair it issues is
            // guaranteed self-consistent. One retry, deterministically, instead of a loop.
            Response.Cookies.Delete(_antiforgeryOptions.Cookie.Name ?? ".AspNetCore.Antiforgery");

            // The reason is logged, not shown. "Could not be decrypted" and "did not match" have very
            // different causes — a rotated keyring against a genuine forgery — and telling them apart
            // from the outside would take a support call.
            _logger.LogInformation(
                failure, "A sign-in form was rejected and its antiforgery cookie cleared; the retry will mint a new one.");

            // A stale form, not an attack — and certainly not a server fault.
            //
            // The token is bound to the identity that fetched the page, so it stops matching the
            // moment that identity changes: a login tab left open while you sign in somewhere else,
            // a back-button submit after signing out, a page restored by the browser on restart. All
            // ordinary. Left to propagate it produced a raw 500 on the one screen where a bare
            // "something went wrong" is least useful, and the user had no way to know that simply
            // reloading would fix it.
            //
            // Redirecting re-renders the form with a fresh token, so the next attempt just works.
            await _audit.RecordAsync(
                AuditAction.SignInFailed,
                nameof(ApplicationUser),
                operation: "password",
                reason: "Stale antiforgery token");

            return Redirect(LoginUrl(returnUrl, "That form had expired. Please try again.", form.Username));
        }

        var user = await _userManager.FindByNameAsync(form.Username ?? string.Empty)
                   ?? await _userManager.FindByEmailAsync(form.Username ?? string.Empty);

        if (user is null || !user.IsEnabled)
        {
            await _audit.RecordAsync(
                AuditAction.SignInFailed,
                nameof(ApplicationUser),
                operation: "password",
                reason: "Unknown or disabled account");

            // One message for every failure mode: distinguishing "no such user" from "wrong
            // password" turns the form into an account-enumeration oracle. The username is echoed
            // back for the same reason it is on every other path — it is not a secret, and retyping
            // an email address after each attempt is how a typo becomes a lockout.
            return Redirect(LoginUrl(returnUrl, "Those details were not recognised.", form.Username));
        }

        var result = await _signInManager.PasswordSignInAsync(
            user,
            form.Password ?? string.Empty,
            isPersistent: false,
            lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            await _audit.RecordAsync(
                AuditAction.SignInFailed,
                nameof(ApplicationUser),
                user.Id.ToString(),
                "password",
                reason: "Account locked out");

            // How long, not "shortly". Someone locked out with no idea whether to wait a minute or an
            // hour retries immediately, which on a sliding lockout is how they stay locked out.
            var until = await _userManager.GetLockoutEndDateAsync(user);
            var minutes = until is { } end
                ? (int)Math.Max(1, Math.Ceiling((end - DateTimeOffset.UtcNow).TotalMinutes))
                : 0;

            var message = minutes > 0
                ? $"This account is locked. Try again in {minutes} minute{(minutes == 1 ? string.Empty : "s")}."
                : "This account is temporarily locked. Try again shortly.";

            return Redirect(LoginUrl(returnUrl, message, form.Username));
        }

        if (!result.Succeeded)
        {
            await _audit.RecordAsync(
                AuditAction.SignInFailed,
                nameof(ApplicationUser),
                user.Id.ToString(),
                "password",
                reason: "Incorrect password");

            return Redirect(LoginUrl(returnUrl, "Those details were not recognised.", form.Username));
        }

        return Redirect(returnUrl);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            await _antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            // Signing out is the one action where refusing on a stale token serves nobody. The whole
            // point of the check is to stop a third-party page from acting as you, and the worst a
            // forged sign-out can do is sign you out — while failing it leaves someone stuck in a
            // session they have explicitly asked to leave. So the token failure is noted and the
            // sign-out proceeds.
            await _audit.RecordAsync(
                AuditAction.SignedOut,
                nameof(ApplicationUser),
                operation: "logout",
                reason: "Stale antiforgery token; signed out anyway");
        }

        await _signInManager.SignOutAsync();
        return Redirect("/");
    }

    private static string LoginUrl(string returnUrl, string error, string? username = null)
    {
        var url = $"/account/login?returnUrl={WebUtility.UrlEncode(returnUrl)}&error={WebUtility.UrlEncode(error)}";

        return string.IsNullOrWhiteSpace(username)
            ? url
            : $"{url}&username={WebUtility.UrlEncode(username.Trim())}";
    }

    /// <summary>
    /// Hand-rendered rather than templated. The API has no view engine, and adding one for a single
    /// form would pull a rendering pipeline into the process that holds the signing keys.
    /// </summary>
    private string RenderLoginPage(string? returnUrl, string? error, string? username)
    {
        var antiforgery = _antiforgery.GetAndStoreTokens(HttpContext);

        var page = new StringBuilder();

        page.Append("""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Sign in — Retail25</title>
              <style>
                :root { color-scheme: light dark; }
                body { margin:0; min-height:100vh; display:grid; place-items:center;
                       background:#fafafa; color:#18181b;
                       font:15px/1.5 ui-sans-serif, system-ui, -apple-system, sans-serif; }
                @media (prefers-color-scheme: dark) { body { background:#09090b; color:#f4f4f5; } }
                form { width:min(360px, 92vw); border:1px solid #e4e4e7; border-radius:4px;
                       background:#fff; padding:24px; }
                @media (prefers-color-scheme: dark) { form { background:#18181b; border-color:#27272a; } }
                h1 { margin:0 0 4px; font-size:18px; font-weight:600; }
                p.sub { margin:0 0 20px; color:#71717a; font-size:13px; }
                label { display:block; margin-bottom:4px; font-size:13px; color:#71717a; }
                input { width:100%; box-sizing:border-box; padding:8px 10px; margin-bottom:14px;
                        border:1px solid #e4e4e7; border-radius:4px; background:transparent;
                        color:inherit; font-size:15px; }
                @media (prefers-color-scheme: dark) { input { border-color:#27272a; } }
                input:focus-visible { outline:2px solid #18181b; outline-offset:1px; }
                @media (prefers-color-scheme: dark) { input:focus-visible { outline-color:#f4f4f5; } }
                button { width:100%; min-height:44px; border:0; border-radius:4px;
                         background:#18181b; color:#fafafa; font-size:15px; font-weight:500;
                         cursor:pointer; }
                @media (prefers-color-scheme: dark) { button { background:#f4f4f5; color:#18181b; } }
                .error { margin:0 0 14px; padding:8px 10px; border-radius:4px; font-size:13px;
                         background:rgba(220,38,38,.1); color:#dc2626; }
              </style>
            </head>
            <body>
              <form method="post" action="/account/login">
                <h1>Retail25</h1>
                <p class="sub">Sign in to continue</p>
            """);

        if (!string.IsNullOrWhiteSpace(error))
        {
            page.Append("<p class=\"error\" role=\"alert\">")
                .Append(WebUtility.HtmlEncode(error))
                .Append("</p>");
        }

        // The username is put back and the cursor moves to the password. After a failed attempt the
        // thing that needs retyping is the password, and sending the caret back to a field that is
        // already correct is how the second attempt gets the same typo as the first.
        var remembered = username?.Trim() ?? string.Empty;

        page.Append("<label for=\"username\">Username or email</label>")
            .Append("<input id=\"username\" name=\"username\" autocomplete=\"username\" required value=\"")
            .Append(WebUtility.HtmlEncode(remembered))
            .Append('"')
            .Append(remembered.Length == 0 ? " autofocus" : string.Empty)
            .Append('>')
            .Append("<label for=\"password\">Password</label>")
            .Append("<input id=\"password\" name=\"password\" type=\"password\" autocomplete=\"current-password\" required")
            .Append(remembered.Length == 0 ? string.Empty : " autofocus")
            .Append('>');

        page.Append("<input type=\"hidden\" name=\"returnUrl\" value=\"")
            .Append(WebUtility.HtmlEncode(returnUrl ?? "/"))
            .Append("\">")
            .Append("<input type=\"hidden\" name=\"")
            .Append(WebUtility.HtmlEncode(antiforgery.FormFieldName))
            .Append("\" value=\"")
            .Append(WebUtility.HtmlEncode(antiforgery.RequestToken))
            .Append("\">")
            .Append("""
                <button type="submit">Sign in</button>
              </form>
            </body>
            </html>
            """);

        return page.ToString();
    }

    public sealed record LoginForm(string? Username, string? Password, string? ReturnUrl);
}
