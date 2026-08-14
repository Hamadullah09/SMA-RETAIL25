using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Retail25.Api.Common;
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
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuditWriter audit,
        IAntiforgery antiforgery,
        IOptions<AntiforgeryOptions> antiforgeryOptions,
        IConfiguration configuration,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _audit = audit;
        _antiforgery = antiforgery;
        _antiforgeryOptions = antiforgeryOptions.Value;
        _configuration = configuration;
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
            //
            // The options are not optional. A deletion is just a Set-Cookie with an expiry in the
            // past, so it has to satisfy the same rules the browser applied when it stored the thing
            // — and this cookie is `__Host-` prefixed, which requires Secure and Path=/. The
            // no-argument Delete sends neither (CookieOptions defaults to Secure=false), so the
            // browser rejected the deletion exactly as silently as it would reject a bad set, the
            // stale cookie survived, and the next POST presented it again. That turns "one retry
            // fixes it" into a loop with no way out but clearing cookies by hand, which is the state
            // this block exists to prevent. Building from the same CookieBuilder that issued it is
            // what keeps the two in step if either is ever reconfigured.
            Response.Cookies.Delete(
                _antiforgeryOptions.Cookie.Name ?? ".AspNetCore.Antiforgery",
                _antiforgeryOptions.Cookie.Build(HttpContext));

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
                user.Id.ToString(CultureInfo.InvariantCulture),
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
                user.Id.ToString(CultureInfo.InvariantCulture),
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

    /// <summary>
    /// The application's own root, which is not necessarily the origin's.
    /// <para>
    /// Hosted as an IIS sub-application the API answers under a prefix — <c>/backend</c> on the
    /// shop deployment — and every path this controller writes into HTML or a Location header has
    /// to carry it. A bare <c>/account/login</c> resolves against the origin, where the front end
    /// lives, so the browser is sent to Next.js and shown its 404. That is what the sign-in form
    /// did: the page rendered, the credentials were typed, and posting them left the API entirely.
    /// </para>
    /// <para>
    /// <see cref="HttpRequest.PathBase"/> is empty when the app owns its origin, so this is correct
    /// for both shapes rather than a special case for one.
    /// </para>
    /// </summary>
    /// <summary>
    /// The open eye — shown when the password is hidden, because the icon offers the action rather
    /// than reporting the state. Matches the lucide "eye" the rest of the application uses, so the
    /// server-rendered page and the React pages do not have visibly different controls.
    /// </summary>
    private const string EyeIcon =
        """<svg class="icon-show" viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7-10-7-10-7z"/><circle cx="12" cy="12" r="3"/></svg>""";

    /// <summary>The struck-through eye, shown while the password is visible.</summary>
    private const string EyeOffIcon =
        """<svg class="icon-hide" viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9.9 4.24A9.1 9.1 0 0 1 12 4c6.4 0 10 7 10 7a18.5 18.5 0 0 1-2.2 3.2M6.6 6.6A18.5 18.5 0 0 0 2 11s3.6 7 10 7a9.1 9.1 0 0 0 4.2-1M2 2l20 20"/><path d="M9.9 9.9a3 3 0 0 0 4.2 4.2"/></svg>""";

    private string AppPath(string path) => Request.PathBase + path;

    private string LoginUrl(string returnUrl, string error, string? username = null)
    {
        var url = AppPath($"/account/login?returnUrl={WebUtility.UrlEncode(returnUrl)}&error={WebUtility.UrlEncode(error)}");

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
              <title>Sign in — SMA Retail</title>
              <style>
                /* The password field and its reveal, which sits inside the field's own box. */
                .pw { position: relative; }
                .pw input { padding-right: 2.9rem; }
                .pw button {
                  position: absolute; top: 0; right: 0; height: 100%;
                  width: 2.75rem; border: 0; background: none; cursor: pointer;
                  display: flex; align-items: center; justify-content: center; color: #64748b;
                }
                .pw button:hover, .pw button:focus-visible { color: #0f172a; }

                /* One icon at a time: the eye offers "show", the struck eye offers "hide". */
                .pw button .icon-hide { display: none; }
                .pw button.revealed .icon-show { display: none; }
                .pw button.revealed .icon-hide { display: block; }
                /* The one screen every user of this system sees, and the only one the identity
                   server draws itself. It sat on the app's defaults from before there was a design:
                   a black button, square corners, no mark. Arriving here from the app read as being
                   handed off to something else half-finished, so it now carries the same surface,
                   radius, indigo action and mark as everything on the other side of the redirect.
                   Values are literal rather than tokens because nothing here shares a stylesheet
                   with the front end — they are copied from it deliberately. */
                :root {
                  color-scheme: light dark;
                  --page:#f2f3f8; --card:#ffffff; --line:#e3e5ed;
                  --ink:#171a26; --muted:#646a80; --ring:#5f63d8;
                }
                @media (prefers-color-scheme: dark) {
                  :root { --page:#0d0e14; --card:#1b1d27; --line:#2c2f3c; --ink:#edeff6; --muted:#989eb2; }
                }
                * { box-sizing:border-box; }
                body { margin:0; min-height:100vh; display:grid; place-items:center; padding:24px;
                       background:var(--page); color:var(--ink);
                       font:15px/1.55 'Onest', ui-sans-serif, system-ui, -apple-system, 'Segoe UI', sans-serif; }
                /* Same shape as the account screens the web app serves: the mark and the product
                   name sit above the card, and the card holds the heading and the form. Those
                   screens are one click away in either direction, and a sign-in that rearranges
                   itself between them looks like a different site — which is the one impression
                   this page cannot afford to give. */
                .page { width:min(400px, 100%); }
                .brand { display:flex; align-items:center; justify-content:center; gap:10px;
                         margin-bottom:24px; }
                .brand .mark { width:36px; height:36px; display:block; }
                .brand strong { display:block; font-size:18px; font-weight:600; letter-spacing:-.01em; }
                .brand span span { display:block; font-size:12px; color:var(--muted); }
                form { border:1px solid var(--line); border-radius:14px;
                       background:var(--card); padding:32px;
                       box-shadow:0 1px 2px rgba(18,20,45,.05), 0 4px 14px rgba(18,20,45,.06); }
                h1 { margin:0 0 6px; font-size:26px; font-weight:600; letter-spacing:-.02em; }
                p.sub { margin:0 0 26px; color:var(--muted); font-size:14px; line-height:1.5; }
                label { display:block; margin-bottom:6px; font-size:13px; font-weight:500; }
                input { width:100%; padding:10px 12px; margin-bottom:16px; min-height:40px;
                        border:1px solid var(--line); border-radius:10px; background:transparent;
                        color:inherit; font:inherit; }
                input:focus-visible { outline:2px solid var(--ring); outline-offset:1px; border-color:var(--ring); }
                button { width:100%; min-height:44px; border:0; border-radius:10px; color:#fff;
                         background:linear-gradient(180deg,#6165da 0%,#4f51cc 100%);
                         font-size:15px; font-weight:600; cursor:pointer; margin-top:4px; }
                button:hover { background:linear-gradient(180deg,#565ad2 0%,#4547c0 100%); }
                button:active { background:linear-gradient(180deg,#4f51cc 0%,#4547c0 100%); }
                .error { margin:0 0 16px; padding:10px 12px; border-radius:10px; font-size:13px;
                         background:rgba(220,38,38,.1); color:#dc2626; }

                /* The two ways off this page. Separated from the form by a rule so the eye reads
                   them as somewhere else to go rather than as more of the thing being filled in. */
                /* No rule above these. They sit outside the card now, and the card's own edge is
                   already the division — which is how the web app's account screens set the same
                   pair of links. */
                .links { margin:8px 0 0; text-align:center; font-size:13px; color:var(--muted); }
                .links.first { margin-top:24px; }
                .links a { color:var(--ring); font-weight:500; text-decoration:none; }
                .links a:hover { text-decoration:underline; }
                .links.quiet a { color:var(--muted); font-weight:400; }
                @media (prefers-reduced-motion: reduce) { * { transition:none !important; } }
              </style>
            </head>
            <body>
              <div class="page">
              <div class="brand">
                <img class="mark" alt="" src="data:image/png;base64,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">
                <span><strong>SMA Retail</strong><span>Retail management</span></span>
              </div>

              <form method="post" action="{{FORM_ACTION}}">
                <h1>Sign in</h1>
                <p class="sub">This is the only page where your password is typed.</p>
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

            // Wrapped so the reveal button can sit inside the field. The button is a real
            // <button type="button"> rather than a span: it has to be reachable by keyboard, and
            // without the explicit type it would submit the form on Enter instead of toggling.
            .Append("<div class=\"pw\">")
            .Append("<input id=\"password\" name=\"password\" type=\"password\" autocomplete=\"current-password\" required")
            .Append(remembered.Length == 0 ? string.Empty : " autofocus")
            .Append('>')
            // Both icons ship in the markup and CSS shows one; the script only toggles a class.
            // Drawn inline rather than fetched — the policy allows no external origin, and an icon
            // font on the sign-in page would be a network round trip before anyone can type.
            .Append("<button type=\"button\" id=\"pw-toggle\" aria-controls=\"password\" aria-pressed=\"false\" ")
            .Append("aria-label=\"Show password\">")
            .Append(EyeIcon)
            .Append(EyeOffIcon)
            .Append("</button>")
            .Append("</div>");

        page.Append("<input type=\"hidden\" name=\"returnUrl\" value=\"")
            .Append(WebUtility.HtmlEncode(returnUrl ?? "/"))
            .Append("\">")
            .Append("<input type=\"hidden\" name=\"")
            .Append(WebUtility.HtmlEncode(antiforgery.FormFieldName))
            .Append("\" value=\"")
            .Append(WebUtility.HtmlEncode(antiforgery.RequestToken))
            .Append("\">")
            .Append("<button type=\"submit\">Sign in</button></form>");

        // The way on and the way back.
        //
        // Both were only on the application's own landing page, which is the one screen a person
        // has already left by the time they need either: you discover you have forgotten the
        // password here, in front of the box asking for it, and if you arrived by accident this was
        // a dead end with no marked exit. They live on the other origin, so they are absolute — and
        // they are ordinary links rather than a script, which is what keeps this page free of one.
        var webOrigin = (_configuration["Auth:WebOrigin"] ?? "http://localhost:3000").TrimEnd('/');

        // Worded as they are on the account screens the web app serves, so the pair reads as one
        // product rather than as two that happen to share a logo.
        page.Append("<p class=\"links first\">Forgotten your password? <a href=\"")
            .Append(WebUtility.HtmlEncode(webOrigin))
            .Append("/forgot-password\">Reset it</a></p>")
            .Append("<p class=\"links quiet\"><a href=\"")
            .Append(WebUtility.HtmlEncode(webOrigin))
            .Append("/\">Back to SMA Retail</a></p>")
            .Append("""
              </div>
            """)

            // Emitted verbatim from the constant the CSP hash is computed over. Any edit to the
            // script changes the hash automatically, so the two cannot fall out of step.
            .Append("<script>")
            .Append(LoginPageScript.Source)
            .Append("</script>")
            .Append("""
            </body>
            </html>
            """);

        // The form posts back to this controller, so its action has to carry the application's own
        // prefix. Substituted rather than written inline because the surrounding markup is one raw
        // string literal, and splitting it to interpolate a single attribute would make the page
        // harder to read than this placeholder does.
        return page.ToString().Replace("{{FORM_ACTION}}", WebUtility.HtmlEncode(AppPath("/account/login")), StringComparison.Ordinal);
    }

    public sealed record LoginForm(string? Username, string? Password, string? ReturnUrl);
}
