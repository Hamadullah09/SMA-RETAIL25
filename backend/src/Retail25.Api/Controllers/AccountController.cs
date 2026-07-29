using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuditWriter audit)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _audit = audit;
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null, string? error = null)
        => Content(RenderLoginPage(returnUrl, error), "text/html; charset=utf-8");

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([FromForm] LoginForm form)
    {
        // Only ever a path on this host: an open redirect on a login page hands an attacker a
        // credible way to bounce a freshly authenticated user anywhere they like.
        var returnUrl = Url.IsLocalUrl(form.ReturnUrl) ? form.ReturnUrl! : "/";

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
            // password" turns the form into an account-enumeration oracle.
            return Redirect(LoginUrl(returnUrl, "Those details were not recognised."));
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

            return Redirect(LoginUrl(returnUrl, "This account is temporarily locked. Try again shortly."));
        }

        if (!result.Succeeded)
        {
            await _audit.RecordAsync(
                AuditAction.SignInFailed,
                nameof(ApplicationUser),
                user.Id.ToString(),
                "password",
                reason: "Incorrect password");

            return Redirect(LoginUrl(returnUrl, "Those details were not recognised."));
        }

        return Redirect(returnUrl);
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Redirect("/");
    }

    private static string LoginUrl(string returnUrl, string error)
        => $"/account/login?returnUrl={WebUtility.UrlEncode(returnUrl)}&error={WebUtility.UrlEncode(error)}";

    /// <summary>
    /// Hand-rendered rather than templated. The API has no view engine, and adding one for a single
    /// form would pull a rendering pipeline into the process that holds the signing keys.
    /// </summary>
    private string RenderLoginPage(string? returnUrl, string? error)
    {
        var antiforgery = HttpContext.RequestServices
            .GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>()
            .GetAndStoreTokens(HttpContext);

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

        page.Append("""
                <label for="username">Username or email</label>
                <input id="username" name="username" autocomplete="username" autofocus required>

                <label for="password">Password</label>
                <input id="password" name="password" type="password" autocomplete="current-password" required>
            """);

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
