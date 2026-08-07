using System.Globalization;
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
              <title>Sign in — SMA Retail</title>
              <style>
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
                form { width:min(400px, 100%); border:1px solid var(--line); border-radius:14px;
                       background:var(--card); padding:32px;
                       box-shadow:0 1px 2px rgba(18,20,45,.05), 0 4px 14px rgba(18,20,45,.06); }
                .mark { display:block; width:44px; height:44px; margin-bottom:18px; }
                h1 { margin:0 0 2px; font-size:22px; font-weight:600; letter-spacing:-.01em; }
                p.sub { margin:0 0 24px; color:var(--muted); font-size:14px; }
                label { display:block; margin-bottom:6px; font-size:13px; font-weight:500; }
                input { width:100%; padding:10px 12px; margin-bottom:16px; min-height:40px;
                        border:1px solid var(--line); border-radius:10px; background:transparent;
                        color:inherit; font:inherit; }
                input:focus-visible { outline:2px solid var(--ring); outline-offset:1px; border-color:var(--ring); }
                button { width:100%; min-height:44px; border:0; border-radius:10px; color:#fff;
                         background:linear-gradient(180deg,#6165da 0%,#4f51cc 100%);
                         font-size:15px; font-weight:600; cursor:pointer; }
                button:hover { background:linear-gradient(180deg,#565ad2 0%,#4547c0 100%); }
                .error { margin:0 0 16px; padding:10px 12px; border-radius:10px; font-size:13px;
                         background:rgba(220,38,38,.1); color:#dc2626; }
                @media (prefers-reduced-motion: reduce) { * { transition:none !important; } }
              </style>
            </head>
            <body>
              <form method="post" action="/account/login">
                <img class="mark" alt="" src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAGAAAABgCAYAAADimHc4AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAADIOSURBVHhe7X0FeBVn9n5oCy1S3KUQiDsRQgSXQLAQQoi7uyvE3d3d3UMgaHB3aIAioTjFKZp73/+Z4dJ/t7vbLbuFX3fhfZ7zzNz7fTNJznt05psJ32d8xmd8xltwb/0g0bu32IzTFh7/pmlDxZsG/8pXTSFZvV3ZPm9Ob17M5XJH86Z+xp8BLvfFtN591Tac5rCy3mLrM5w0TSB9CZA0B4hTJVEBEmcDqQuBjFXozdJ93FvudobTmZXHvXhg1SMudzjvVJ/xPnh6/tDC3s2puzgFNq9YhUfLAmGSQIgMuJFK4CSqgZuyjGQFOMnq4MTMpTGaEyROIkrEKAG5BnhdF3T75dEON95pP+OP4PmBlkBuhTspkSzbfwq4/pPADRYHN0IB3GhFUvhicEpNwa20BCqtaGsFTq42kTCLyFEEN0QanPV0nPcEIkMaKLHEi6057bc+h6ffB4Avn20pqEChGRAgCU6gODghYqRQEXBDJYmA6UCULDjxs8DJWwtOkT64jBQbgpO5ChwihxspB04YeUkwHUPCCZICx18CSFmO583xFx69eDGV9+M+47d43FlYjhxdcDZIk7JVyNpJIsnqw8j6Q4XADZeg72mMSOBSiOHEq7DCjVcl659Jc0nx4TQeSvODhYkI2mfCVdQc9AYqALGL8bQ+6vLTz57w97i7syoQucbgBMiDm7CELFwH3NSlRMIMUjopPmTaWwklxYaSZYeK0j6zZbzj7ZbdDyGigqfSVpDImA4uE5YyV4ObRR4TRkk7YSl+akzYSdXS17wf/Rn3vz+t/HOeA7hBpGxG+RTbOU1OFOcNwU2aB04EhZ+QqRTPx9OcCSQTgcDJtOWnEEOxPnAK+xmBlCsCaZyRYEFwGG9IWghuhRk4za7gFFCoCpsFZBvgVnOaA+/Hf9qguP/VjbLgM4iYR9ZOyio3B7fFBb0N9uBUk+JSFxEBEuCQVSNgLCmXIYGUzsT3UPKMUCkihxknzwjkBzdgPHnRGJozlZRN8T+dKqQaa3DonNwWN3Cy1pEXLMatwCUhvF/h08bNXQ0Gb9IN8CZYGdwCI3CbXNFbbwduDVU3+WvYxMqGG8biAyaSxZNiSeHcuDngplMZmr4S3LRlVJYueJt8gyhMMR4Q8B2RIgpOLOUJStS99UQoQ0CjG5WnmngeoLKB9yt8umCqniu53qffhFCcjqfQU0NKqncAp9aWSkcD+m7OW2UHC7DKB4UabhDth1EllKUJbonxWykl4vIpeceqkvKFyVMYT5jIzuWEUPhKmk9hzZy8is7d5gU0OeJ12ILPBFw/tGXhgxRj/LyBysdkKiOLjMApJmWWUY2fsZIsX4YsnpIqY80bxpFyGcVOo5CkwJahYHqBUhOgzIRKUTqW+gNuEJWtzJz1b72AG0ShKVwOXKZPqLF5S26zI3oTVvz1CCCL7MPb/Si4XBGZ8SxSHS9CZlMYIYtO16SYvYq2JNRQcQIZ5VPI8R9LMpqUSmFoPZHANGTZa6kHIC8pNABIOIykUkhiCKAmjCGAs55yxgaaH0jfxc8lL9Ghho1yQJkBelNW/98RwJRg27u61Jpb20PLK6qbCgpKTubkFnRn5xaczy8o6S4sLjtZUlLZVlPTkLBlS5dxd3e3CO/QPw0M2d0Zbud+Wq+CV5HzqVHSoFBBCkxaSnGf4nsAKZ9RpB9Zsu8IkpHg0j7Hl+mKKekmUjecxsT/FSTkLUQaJ34BexzHjyzfn5Kx3wgijkLRBgpbQXRMHCX6JHUgXwuv/y8IoD96UFNLi1tRWcXFgpJyMJKdW4jU9CwkJqchKjoOEZHRiI1LRHJqBrJoLL+oDDn5hZyS0vIz9Y3NMTv37FHine4/wsNbt6acizN9c9NrBl7FLAZI8dwENbLURVTVTH+rRF9SuDcp3nMobYej12cM3niPI8smcpgGK5aIiiXviZ37VrkRiuj1n0rzGKJGA3QMl7YcfzrXekrOYfJEEv2sXA30Jn3kENTR2bm2tLz6h5LyauSRUnMLS5BbVIoc2mYXFCMztwDxickIj4hCbEIykoiU9Ow8ZOTkIzOvEGU19aiub0JxeRVKyqqOtbS0eVy9enUc7/TvjQtdLYsvR+uhhwh4EU0JOI4sOpYsOGo23mwQxSuvCXjlORYvPYfhpfu3tB2OF56j8cJrHF77k5WHU4iKUqL5yrQliaYqKlQBr/2m4iXNeeM1Cr2eQ8DxGoZe37EkREKgJHXM86gPWI7euKUfjwAKNbGVtQ1k8RWswvOKiQBS/q8lp7CUJSGOlM+QEB2biKS0TGTmFNB4GTp27MLGbTvRtLETNU1tqKhhzlf+sKGhOfbMmTPf8X7UH8bZtkLDy+FauOo1E0+IgBdRi/AiVBkvAqTwzHsanrqPxzP3sXjiOpRkIJ64DafvxtB34/DcTxhvSNm95AVvZSaJIt6EyOGlrwCee9AcDyLLfQheuQ8mMkfhlfcE9JLnIEIFyFiC1zGLPjwBTJPT2NxeWVXXiLSsXCSnZSE5PZskCwkUYuKT0hGfnI6ElHTEJqUiikJPeHQ8AoJD4b8+ABsCQxAWGUPz0lBSWYva5jY0dXSijYho3bID9S0bUVpRg8KS8id1DU1Rjx49+sPX3s+3FXlcCNHERc+ZeBitjp8CZuK20xjccRyOn5xH477LWDxwGYWHTkPwwGkQHjqPwCPnkXjoMgZPfIQocSvgVbjSr0QRL0Pk35LnNppIG0kyGE9dBxFpo1hSXvrwozdEEUhVw8uoj1CG1tU3VRRTyEkkS06lcJJZQNZfVomiyhqUkgWX1jaimEJLflkVssgzkjNziYQk+AUEwz8wCMFhkURIHEtMZGwCu40nojIpLFU1tKCqsYUlNodCWGFpBfILS35sb99kyvvxv4vvW0v9zoVq4bSbIm6GLcIVZ35cc+LHTQ9x3HAYiRv2w3HTYRRu2w/BHfvBuOMwjMgZgduORI7nNDwJlMOToJl4yggp9VnIDPwcJIeHnlPxk8tIImsEkfYtySA8ps9PXcfgqSc/XgQTAclqeBbxgT2gsb7Zr4SsMyO/iFV6BSmrsLL6dVFl9dnSyuqOyrrG/LLa+ozqhtbc8uqGZtrfV1Jbf6u4uhaVNJc5JpE8JZJCUQQpP46SdEpmDktmdHwSookMJkSxeSI3H6nMGJO46XNpaXnH4cOHp/F+lX+IC53VeqciDXHEcQYu+Kqg214AV9wVcM1DDj1OU3HFZjB67MfiR8cJJONx3WkCbvLkjrsA7vnK4Cc/OdwnYbfr5fDAXxr3PKbhrssE3HMeRzKGvGkUETKWZDweuBNxATOAhEV49CEbsW3btgkXllb25pFlFxAJucVlL5vbN7kfPXpUgDflH+LOnTuDOrZtE2/u2GxS27qxpKy+8XoVhZ3i6jokMIqn/JCYmok0UjJDRlxiCpsrGGESd0x8AkLCIhBP+zk5Bffb2jp0eaf+O/ywt2PZmTR3HPRSx3GXmThpL4luNwVccJXFRUdRdFsOwUXbceQZouhxFkGPizB6XIXxo6sQfnQTxXV38hRPSVZuekiRSOKWpzhuu4uSCLNyx4PEXYhEBHc9RHHXS5LIoqYvejFuB31AD6hpbKmupvicQYk1ICQM8fHxBryh9wJLyPbtqxraOqrK6xp/rmhoRiol6mhSPOMRTFhjQhDjDYFBIdgQEIjQiEjE0HgaEZRHP7+xpnrjgzNH/i5J37l2TeBwblDvwUA97LGVwyF7ORx3VsApZ1mccZTECfMROG0+DOcdRHDRWRyXnMRwmYi44iKKq6wwpBA5riRuYrjmyogoESSG627iRJAESxKzveFORDEkecriupc8fg6Zh6t+8z4MAYzrV9Y39eZWVMPGwREaq5Zv4w39Rzhw4AR/y6bOkKqm1pu1RC6TMxgiElIykEFlakpGNpEQilDyACY0MeVrBvURxaXlaCnOvXl2a+Ms3qlYMAXCkYqEqwcjzNFpo4guW0Xsd5iJg0TEYfvpOGw6FkcM+uO41RQiRAJnHcXwvYMYuml7geQiyQ+OIrjsREJewpBzmUhh9q84ixFBJK7iJBJEkBSuuMngsrssiTweBM3DBZ8PFIK2bt9t3bBxM3zJIpWUlOHp5raWN/R3CAwMnKivrz9r6dKl883NzWc0NDSM4A39U1w/d31E6+YtvtWNrfeqW9qRkpVH4SeV+oZs2s9FRFQsmyMYApj+giEqv6AQ9VmJbw43VWjxTsPiVFtp+dE0T7TazUWHlRK22Spjp7UCdtvIY48pP3bpfIP9JuNwzE4cJ+zFKEyJ4pSdKE7T9oyDKM6Rd3xP2+8ZcshDGHK6ncTfCnlNt5MEzjtJ4byzNLpdZNDtKkdhThG3ghbjjI/ahyGgsX1zUCXF7bW6+pg5c+arLVu2/J376+rqmiorK+9etmzZmeXLl7cuXry4ZuXKlTsWLFjQo6Sk1O7v76/Km/pPcf78+QlN7ZuyKxqbUVxVR8k5iy1XmSQdEROPWPKOLCoA2EaP6Tvy8lCXHs/d1VD9S17oObln5YmyGNQ7LEW9hSrarGdjo+UMbLaQwxZTEXRq98d2veHYYyGM/TZiOERyxEYUx2xFcILkpJ0wESLyVogUhpjT5CWnHSRw2lGchNlK4ZSTNE46TccJZ3nKN8q4HKiOw34fqBNu6ticmkfd6hL1lZilqnr/4cOHQ3lDfFeuXPlm/vz5TYMHD4a4uPhVCgNf8IZY2NraSk2ePPnexIkTQftzeF/zWVhYqK1YsSLd29t77m+P2da1d3Zda8f+mtaNrKLjKSTFJaYijPEESsZZBe9IoDI1Jxu1KRHY0163jjmWztVvX3XGDy0bjFFqOgu1VgtQZ6aMJlMFtJhIollzINrWfIstRlOx01ISuyxEscdSBPtIDliJ4qC1CI5Yi7KkHLUVw1E7CVaO2UniKCX1w/ZSrByikMbkmIMU4vY5z8HZwNXYvX7dhyGgtXNrdnp+MebOW4DZs2ffpz9yAG+Iz9raWn/o0KGgXYwdO/YBhR7fhIQE+V/PUVdXTxw3bhxI4Vm8r/jIW2r79u2LUaNGQVpa+pSOjo77r8MVHd+ndfO2IKZ8LWQrpixEUWUUHB7FJut3lzuyi8qRExOK8pgg7s7WVnXm2JO7Wlx35Ych23QBis0XEhGzUW6oiCojBVRpjkTdygFoXjcRHaaS5BUS2GYmip1mIugyF8Euc2HyDhEiRQx7rcSwz1qCRJJEikQa+2xksNd6OhvSdtnMwE47ZWx3mo/jIfrYEWL8YQho37YjOJY6W0lJKcybN+9VZ2enPG+Iz8nJacH48eNZAhj5+uuvMWbMGAgICFxXVFRstLS0NL558+bkmpqa7xITE6cwx3C53P40fvvdMYwwHkTnTmfGf41t27oWVzY0/1jGVEvZ+VSWJiEgKOxtf0CewDSCmbl5SF3vitLEiKfHD+4WJ/K+3FGTfanExwgpRnORZ7YIuYbKKDSciUKtaShU749yzQloMBAnr5BCm4k4Okg6TcWw1VSUCBEnkcB2c0nssJAikcY2CxmS6dhiKUsih80U1jqsZ6LDdg42OqvjYLQVdiS6BvF+7T8Xuw8fM45PTYeEuDjk5GQRGx//A/2RX/GGmfjvRgq9xyiRPv6NDBs2DBISEqc8PDxU6DMLZn/kyJH49ttv/2auiIhIPjP+W5w7d24cJehtVQ2tyC0pZ0PS+sAQ9tIHUy1lFZUiLSkRiV4OKEmNv8AQcO7Y7kVbS+IRY7QQScaLkWY0D+l6KsjUlkHaom+Ru2wMyrRFUWMoi3pDSTQaSaDZWAKtJG0mJOQZG82kSKTRbiaDNlZk0WpGoYyk0UwRDZaz0GCnhkb3NThRGIJjbSUevF/5z8X1u3eFqFHizFRUIiUJw9nVHQeOnVzFG2ZBf3RfU1NTozlz5uSLiYkdJa948c033/yi3GnTpnF9fHwUmbkUxiIoXEFbW3vjF198wY736dMH8vLyecz4PwITkuqb20sq6pqQX1qFeFK+P0tCOktCTlEZ4kIDkezrirKc9CLmmL2b6zNrE/0QoD0HsSZLEWcwHwk6yohZPA7xC4Ygc7UgigzkUKo/HeX6MqjUl0a1gSSRIo06QxkS2prIkSigzlgBNSYzUG2siArjmSg3UUUF5ZcKJw1sjXPBoebCs/Q7/pIb/3SUVdaeWLFyJYUhCaxYuQp5RaU7eUN8Wlpa6zQ0NIp5H1lUV1fza2pqhpOlP6ePrIIXLlzIhhhhYeELQkJCh21sbHSYkMWMf/nll6DKKZkZZ8B4GOUFewpLnVTa/lJBtWzsDCmvbQJTFDC5gPEENhxRjsrMK0CYpxPSQvzQWl+9mpm/rblsV2GYG7w1VRFmoEYesRhhKyQRqNIfsUumIF1bDtnrZJGnI4sC3eko1JuOYh1plKwej7KVg1GqOR4l2sIo0ZVGkb4ChTFl5BupIt9sIQrtVqE5zA77arMf3L59+8Oujtux74Cnq5s7pKWkGEtFMDVHh4+fZsMKlaaHR48ejeDgYNbCfw0pKal9tAETcqg/WF1aWjp46tSpjLJt16xZs2DgwIEsAQwRampq7IJXqpY0yIvOvQtp/Pz8rynZ/1JqNrZ2BJXWNrDXlpKpOw6JiEYyNW3M5e/UlFQEOVsjOTL4NpE4gKnYNjeWHswJdYHzCiX4as1F4NrZcJ85FIELpiB2zQzErZ6O5DWySNOWR7qOIjI0hJC24CtkLeRDzmI+ZKv1RZbaQGSoDUPaahGkmS5Gpu1q1IU7oasq89G5Uwf/lBtKv4unXO6YuJi4Z8ozZ7AE6OgZMPcCtjNjVEpKy8rKnlNQULhPtX/e6tWrg6gXCKceoJOUB1L4vVWrVrEVAnmBDeULkIcMd3Nz0xg+fDir5P79+8PR0TFATk6u4LvvvsOECRNeM9+/E/oM8ihn8ga2ZG1s7wgqqalHbmkF25hFxiYhJTOXKiPaDwtCmLcLinIyI5m5TGjY2lK5pSwpGPaLpGAiNw5WimPhpDIBvmqiCFQXR9hKGURryiNWawZiV8sicokAwucMRrgqHyJn90HU7C8RN/srxM8dgCSTRWhKCcCuhoLuK2eOyzA/46Ogra0tw9zIAAozZkBJWRmBIRHYunP3CmaM/kgmByykcORIluyroqLiS1buQd8tvXHjxkj2BIS5c+fuIqIuMvt6hF8nbkbJTK6gpG5LDd3md/nhnRA5z3ft2jWK9lnUNrQUFlfUIocSc25xORuKmGtJ6Tl5WO/pgogNvk9Pnbo0hjedb++uTRFFke5cT+URMJEeAlMlflgqfgebGRPgPIsf7nOmwF15FDyUh8NdaThcZnwLJ7mv4aHwDYJU+yNy1jeIV+JDjZcW9m9tqqC/eRjv1B8HVD6OiYmKejR7lipIwVi5SgPhUXE3f92Y/SuQt0wnK5Zg9qmBM3+XA5hqiYhpvn79+ggjIyOLd6HpnZAX3bW3t/+l/GVACuhTVde4s4CScn55JUqqatneIC27gHqGWAR5uyMvIy2QN53F5R8vKzbGe3TGrBSEtdxQWC9VhM3SmTCWGw99sYHQF+aDkSgfzCT6wFr6S1hKfw0X+YEInTsMSQsGo8FErPdoWUw473QfH1u27XB1dnSCspISNWWzYGJuiZTUjDLe8HuBeggJCl2nBQUFX5DSbZnvfH19ZzFNG+3+IpS0b4WEhIgz47/FgZMnJxZX1dzLovhfWFmD8ppGFJRWUnWUBW9PDwT4eF5mvJM3/Recbs7VrPDS6orQmo4A3dnwM9eAu54aLOcIwFj8S1hI8sFcsg/sZPpjvfJQxC0chpxF/bA7WItpRD+u5f8a9MO/zCsqOaKhqYm5c+dg/vwF8PD0pZheZ86b8l5glrM0NzfzM/tUpgpQL3CPdlnFM5UThZ3rRUVF/1D579DSvkmjqLqOytGit3fm6hpRTN6QkJyO0ID1qC4v/uUSyG9xuaNYrdJHrzjFac3ZJDd9BK1TgZvqWLhSfrCXGwwXJgTN6As/pT7IXMCHHfrDcT7V4eT1+9xJvFN8fNy6dV88MibuFYUQKi0XQH3ZcoSER7/avmv/31VB7wNDQ8O0AQMGsMpn4r+kpOQ5ppx9O/r7qKhtqGQsn0nIhVU1KKuuR0V9M9JjIpAWFRbMm/ZPcWd/S3iHnx6itZQQRh4RuFIafnMmkvX3RcQsPqSp9UHLmj44qMuHGw4jcCvL/uKDB8/fe+HAn4auPftsfHx9yQvmYtGihVi7Toe5Zn+TuaLJm/LeKC8vH0ZVUtWIESOY60NMYzOIN8RWMn5+frLUb+hSdRVEpW82GUBFWFjYDGb8zJkzY4tKK5+kkxfkU4/AJOcSKlMTPGwQH+T7u/cvLtQmeh8P0Uar+2oUO2sh3WwREjQkkbhCCBkr+FGxYgA2avHhgGEfdFv2wS0b+nX8JuJZge3Fn376eSLvNB8fza3tWRTHsYA8YfGiRTAyMUdmdsGZS5f+f+Xx74Bqft/KykpRV1dXSSI4hHqJHVSavpw0aRKYq6pMz8F4CuMlFKIO8Q7jq6xtDC0oq0YWlaKFVVSipqcizHwdIr1cbhCB3/Cm/Q0utxVpX4/RxgG3Bdi6wRCNXrooMJ2DHG1qztZKoFhzGtr0+LHfYDDOmvXBVVL+T458eOnxDRAqiOfl7sfu/MpQPiroj+pT39y2ydLahvUCKjthbm3LrH47fe7cv7/AioGlpaWXjIwMU5aC8QjKDc3UFTuamJisSEhIUKHv79I0tnpydnZmbxCdOHFidHZB6bM0Zt0RhaIkHyf4GmohwtXuzel/cEP/5tHtcleTTJ6fdZREl+dy7AwxxUZfHVSbq6DCQA41xjNRpS2Ajbr8OGQ2GResBuGW7Rd4QAQ8d+sDrtcwIFYOz2sCNvJO+fHBhInSytqDJmZm1MkuZrpZWFjZIiO34GxPT8+/3Z67u7uPJhKWdnV1jaOeoICU38Ib4lNVVU3r16/fL1WShITEuX379vVnxvJLyotySqqQGhGCUCMNOJkaIsrdDptqK5WZ8Xd4AAy5lOl0qcdHHlv0J6PFfh52R1lj63pdtFipoMVMDi3MdSAdAbTqTEaX0VicNR+MG7bf4oHzIDz3+BYc7yGA71j2GeMnTTFxvFN/fFAlM6KorPKwKZGwdOkSLFmiBmMzc6Rm5d7Ze/DobN60fxtHjhwZoKys/LOVldUS6t0Mf3vVVVxc/D4RwC7gIo9cklNcifjwUPgYaiLY1wuRLtbc/Tt3ijLj73CxMKD1TvA87DOZhCadSUhWG4UW95XYTV6w2UoJmwzI8un7Zq3RaNIchg6tQThk0Bc/WA3AbUrCTzyn4LXXWPICIiFIkF1N/bQj6Z/eqv3gYJJkYWl5l5W1NZapqxMRS6Gto8csvnrdvqnzP352ihS/jnqGh8OHD39JH39RPuWG10zvwMxh8ODBgyEUgh7ERMfA29YScUHrEelq8/gxGQlvCiXdZO97MRo4bs2PTt3RaNCbimiqdLLWSeJAuAW2WSugU2cEOrWHoF1rABo1v0X96pHoXPs1Tpr0R4/tUPzkPBYv/IXR6z0aXB8KRVmL8CpR4/+uQWNAJPQrLa8qd3ZxxfLly7F8mTp75XR9YChKq2rLafyXSxLvCybuv7tu9E6YSxe2trbsrchfIz0r70BSRg581wcg0N4cKUF+p3lDfBd31s27EqODbldZdBmMYe+K1erwI242H6LUxmF/qAm67BSxXWcgtq/rj01r+6NKYzDytSVQs3YSdul+jfNWQ3DTfhh5AT84ITLoZbwgjshIe3u96/8cja3tocw60NWaq7FixXIKSUthaWOPqNika7W1Df90YdU/Q1xcnBDhMe3+jfItLCz0mfHfIjuvqD4tpxAunj5wNdZBcWY6e5n73t6WBT1Jhvev+M3GQdvp7J2vFhMZVJFiY+f2ha/yIOxer409jsro0h+EnXr9sZmsPk/tCzgrDkL4glFo1h6FYyZD0GM3Avcch+NlsCy44bJAyEi8Dpj61yCAwe59B7Ujo2MfGhkbsySoU0hasWIlbOycmJUObceOnZbmTf2XSE5OnjZ+/Phe2mWVT+Vor42NjR47+A+QU1BcmpyRC1NzK7g7OXLOnjhte78qKPlpkiaehc/FcXsJ7LCcjnZTKdStGYEKzQkIVu0PB7n+2OGgggMuc7DPcBT26H+DbTr90bRmMCLnDYaN7CDKt8OxXW8Uum0m4AZ5wSO3MewSeMRJUk4Y8NchgMHdx48F8wqLtrh7eGL16tVYuXI5VUlLsE5HH/4Bwb0V1fWpP/54V5A3/XdBjVczU/dTOXrX09NzAe/rf4icwuLOqIQUGFjYwt9an3srcgXnmbcAbtmNQY/TJBwxHIbN5jKo0Z6A0mV8yFH7Cm6Kg+G9VARbjCbhkMtcHLYUxAH9fjhsRvPNx2CrTj9sUOkLd/l+KFk5FMcsp+Gq3TjccxiM5+uFgYKV6A2S+msR8A6bOne4hIVHPjGlymjVypWUH5ZhKYUlMwtrRCWkvty4bWdh96We3103pK+vb0FN2dGioqLfXaCbl1e4LCUzh+MbGAYdEyuE2qzFZXdJHPdSJUUOxknLoThuOw2bDKeiiDrcdDWK/Sp88FutjCJ/Q7Sv/gIH7WTZJSmHDb/CSetp+N5mDE6Z9EHhsj5wkP0SkbP7o01nNL53FMENu2F47DyUStH56M3X/2sSwODx45eCJWWVtX7+G2BgYMh6g7r6UixZqg5rW3sEh0ciKT3zaHV9k+upU91SvMPeC+Hh4fM8vH2fRMQmwt7OnnLQWmitWwdvB2v4B0ci1YJiv8kwHHaWQ7v2tyhc3h8xc/gQsmAcMsI37DjdkOy/1WAQ9ppPxjEbYfKUL3DWZjIuOQriotVASsh8CJrVByHzRiBbfQj2WQjisgM/6wUI4Udv3AdaG/pn4vTpc4tzcwt2+/r6Q1/fgIhYCXWqlpgmbo2WNuwcHOHp44+gsIhTsYlJ2VlZuRYtLR1KT58+Hc1UWSR/99Rld3f3SD8/v2AjI6M3AeGxCPJwgN4qdawzMIWntw/MLS1hYmGJMBt9NJlKo01vEho0+FCylA9JVP1kOWs/uEEV2vE8x8VdFsOxy3gMjlkJ4ogBH85ZDkMPszbUZhD20+esJXzIWDkZySRVGkNwxkEKP9pTM+YzHM+9xv/1CXiHI0dOrc7JK9izPiAQFqSgNWs0WTKY0lWd+ggNDU3ok6dYWlnD0dkNZNnP/DYE3wgNi+wOCgo9HBQUcig0LKzd2cmpxNDQ4K6evj48/DYgOiwUlqsWYrW2LtaHhCMyLhHObp6g4+Dp7ASajyBvV0Sbq5MSx6LCeuad/R21c5nfaWu8ufsOk4HYbTwWR62m4oAhH06bD8BVZ2FcsR2IY8Z8qCHiKnWmotxQFnFzv8AWg/E47ywFjhc1Zy6D/3sIeIcz3T/ML6usqQiLiH7MJGsTU1OsXbsWqzU0sGrVKpaUFStW0L4GJXJNIkoLWlprWcI0NTWhs06HylErBJHlh8cmwHrtCjqHBWJTM9mlKoGkeF//QAQRGesDg2BrbQXmsglVUQh2tERVRnID71fh64rU8txt+DX2GI/DYYsJ2GfEhxPmX+GK41QioD9OmvGhjcJQI3nQdpeliFcbjqx5fDhkLYRXfkJ45DT8v4+Ad+ByueMpWVvm5BZsjIiMfrghMBgeHt5MowVjKmV19fSgo6PzVnR1We+wsXfC+tAYxGQUISI+BaEmSxG1Vh4JmXnsulF2xQTzPFpiKkLDo5CYkY3YhET4+fvBx98XsZHhiPZw4pYkRNa3VRZZWRusO+WhPBg7DCdQ9TMUe4mAY6Z8uGQ7Gpdt+uOoOR826pDoT8ROFzVs9tMiL+iHLbpD8SZIEk/8pvz3EvBrEBmjjp7oXtLesSW4pLyqITM770xKWsZDUuTLmISk14mpGVzmkVfmaZqEiGAkOushQ0sIJcuGoVZXAOnW6giKiEV0ciZSMpgnb3IRk5SGsOg4JFOHzHTJobQfR14SHx+PYFcbhLjaQkV1FgT4p2G1zGSUawzCYVL+EZJzlt/gB6t+OEQesFmXD1uNpmCrjQrOFEc+a/Fe+mPHmgG47z4WVx2++N8g4B+BueD3/PnzSVdv3eLvvnJDpD0r6mS1oQgKFvdB8SI+FFM5mbukL7IWD0DiomGIjiECKP6HhMcgLb8IqTn5FI6i2AcE2YcDmTWmoRFUBqcgLT0Txvo6EBESgBjJlCn8kBf6DkHzBmEflZ9nyfLPmn+BfaZ9yNr5sM1UAFutlXCyMPz+7nizo9u0vqSGbCD1BXz/uwT8Ftf3bpne6bu6t3H5V6jT+BpVqwagdMVA5Kp/i7QFfRG9aio8Zo+Ev9lKpBZUIL+iml1DxDxgmJyVS4pPZldaM8sbI6IT2Ku40jJSUFZShISIAAQFBIgQEejJjUar9lc4QyR0UUjaqtcXO8zE0Gk3Fzt9VmGP6Sga64vX3n3wzO0TIoDBvqZim92ei7DXWhy7XZTZJecVKwciW+1LRKvywUeeD3ay/ZCYkICM4kpkUVLOzi9hF/gWlFeBuXHDPNdsbevArLxglsVASVkFs1SUICk0FUIC0yAoKgllMX4kkGftpjK0y+BbbLWUxybL6ejSGYRu869xzfFLIIAPvX79Pi0CGOzMj8456T0XR6ke32Y8FbWr+iGDavUwqu89Z3yNMNNlSI8Lbwjx8XielJSClJwi9jmzzLwilFTXIZUStrKyKqZPn848/UNbWSjOVIIqkSAjKgChqVMgIiEJMTFJmCkOR7PuOHSaS2Gr7jCcsRqNKw598cSND4gajt6UT/B9QdSYfbU1yXvnIVsJbNEZgLKVfEig0jBgznDEuRqhubnGipmXE+ghE+tmfS48wB8xKZlIZ29blkFr7TrmvjK70ExRUREzZsxgPyvMUIDCdClICfBDeMokiIqJQFBSHvPlxFC7ZhhO2gnhqtNwPHDhA5fCDwoWoTf/Az0h81fHBaqaNoVZXGpd8xUy5vMhYpkAkgLcr3VeuDknft+1/mGW6ss2aCm6rV+rtMd1sRjW25ogMS2bXUQgIyONOXPmYNHixVBVkscMSVHMlJGAjIggJKdNheRUfkjwT4bwdxMxmV8AaxUmYr8V88TkNNx1pLDjQQREiwHlOngdJfNpEsDg0MnvhSs8tX9MNl/CzUqK29mUExZYoCdcmqUt/LDQWBZ5xooIWTwZtuJ80FYWo35CH8qqqqT82cyTOZg3fz7mqKpARpAf0oJTIEPVkOQ0Uj6FIPEpk8E/8TsoTB6GZj1B9HjK4pZDH7yk0MMNHM2+OQtZs9Ab9D/SB/y76Lr6bNypSz2LzmZa1OwxGoVz1pNwxl4Ex+xEsd14PKo1BiNx3hdQEx5Cli8PlVmzMGv2LNYDmPVNDAmzlGdCRngqpIX42fAjOuU7TJs0EfzjRiF52URc9VHFTYeBeEahh+PTH5yMZeBmLgcShdEb8okTwOBirkftPW8BXLcZiJt2/XHTfih6rPrigsUXOGrMh0YtPkQRCcuEB0GQwoukzHQ2/jMksESQN6gqK0NSYCpEJ38H4ckTwaxVdZk5Et+7zcBd14l44kzK9+oDbtxMcAp0wYmWJgKE0Bv1F70f8LHQXR4T8TBAAXdJ+Y+cB+Khcz/8ZMeHu3Z9iYyBuGjdBweosarR6IMEyhXmMv0gKzgRwhLSVPnMZJ4AZYXxBtVZKhAXFsDo0WOhIT4ch61EcM9bkpTf523cDxMi5euDkzwXnNDJQJoMepPmfLoEXDvcKXk7bi1e+IlRMlRh33L1wm0QnnuPwzPvKbhtPwCXrZmOlg879fhQsYIPKUSC54w+WCQxGmISUpBVUIQq5QWGBMYbFFXnYLa0IFrXjsYdT0n2ZU0cUj6Hjfta4OasBidCBJwwfiCbckDaB3pS/r8Bl7I968Hcm01SBzdGFb1eI6k7HU/JcS3exC8mDxiE7y34cNrsa+w16Ic2qpgKlvZF7Lx+CFD5BiYK46A0XQJScjPY1zGozpoNRdrmrp6CW/bDKeEOBdd3GLgbRoGTshDcgnXgxiqQ9U8BN4qqoAI19Gb9BV9b+THQc+rQ1DsJ+r2IoXAQroA3XmPwxmMIXq8XBDdxEZ4HyqDHpj/O2ozHUTtpdFlKo9VYHGXrRJG6QgBh8ybAb8YA2Mt8gyXSkyAlJQkRaXm4qonjpqsIKV0IiBQDN5ysPYQkQfmt8sOnkRABsbJA4VJqxD5RD7hZFx+OVA0gejbe+PHjFSn/ufswvAwQw8swJdzzEsVFR2HstZVHvYEUqgzlUExlaY6xMlIMVRFjMAuh+nMRtE71TYjOrMeuptq358yZhTJdEZzzno9L3ip4FERKD2H+kwZ5FRN2opm3r094S0KsPJBLjVjCR3hn3F8N1An3vVXg2UOxhMpAGYrTI/CMeSGf+3g89ZfEHS9xXHYRw0G76aggi882nfuyNMSppDo10rm9otCys7lBe3tnq9qhQ4emX+jpmfaEmjpmAVm0i9HdbCN5pC6figKzhagzV8Fem+m47iyA1wHCQM4KqnyUKQRNpRAkRaFuPiXhT9ADbh3eOe9VjhXehCqyL9B76jYSjz0m4aEn84YrIVxxFqZeQALthuLIM5r5uqu9fj7v0N9FgtO6nel2yxBnuRKxZsuRaquFTBsNlFgsxA6L6fjRTYzKTmV2iTr7GvyMOUTAR3xt5V8F9+oScpCoQeFGgSoeQTzxEsYDLxFSviCuOgviPIWeQzYSaNAXQYHX2kbeYf8SucFOabEawigOc0GUvS7i7bSR5mKADFc9ZNqtQZnZbBy0lsQjL0HAfyyQIE+EfGIh6BqX2/92rvutVxSff/aTwhMfMTxklS+AHhch/EDKP2Mngi4LSVSbyKA6zot9EPCPoDE/wypKfTLSdGVREOKMKCcDxDvqIM3dCJkeJsh00UOupTrazBSIaFFK0nJ4FvSJvb7+/tEdS15lmeOp33Q885XAI28R3KXQ86OLMC47CeO8gzCO2Yhik5EIikxmYktD8b98adQ77N+/XzFBVwEx84ahwFgJef6WiPEwR5yzAZFABHiZIYPISLNehSoTJVz3UcJVv1mfFgH3mlPykbAcT/xk8NRHAvep2rnhLkJxX+St9duLUJgQRbOuILKtl7y4+uzZH35qhxLxkHjzRQ9jF45AzhoxFOtKo8BxFZI8iQQPa3ab6mXKekSS1Wq0OC3DDp81nw4BXC7369uF3tefByjjia8UHvqI47aHOPvWQ9b6HUVwylYEey1FUKcjhHx3g+9JqV/yDv9DyPQw2B9HBBRoS6FknSTK1wqi3EgOOQ4rkexhingvSySQxLgYI3+DHRI8zD8dAh6fPqT8LMsSj3yn47GPJO55M6+UlMBVJ1H2zYfnHERx3EaEfRNWLdX+pUGuTbxD/zDK4tanJ62YjFwtUVToSqJWTwz1esJo0BNBFRGRZaiEUG1FpDmthdXqhRg5/hO6GnpvY248N0kTD32lyfqlcMtTAj2uYqz1XyACztiL4aiNGLZQ/K+hGr4uNeq9n17pqC3VTTNSReYqAVTpS6HBQAxNhiLURYuhzVgCNXoSiFabhFLbhehJMkDMmk/ohsyd0sBTzH/JeEAecN9bmmI/Wb/L29j/PcX+U0TAQWtxtOkLodpRHZ3Vpe/9YMj5np6paXZr36SuEGQJaDYUxUYjYWw2EUOnKfNWLRkU6ysi12IBbvnOwDWH7z4NAp5eOyfxMNuW+9BXDvd9pMn6pXDNTRxXGOunyucsEXDSjnnpnjgadEVQ4rYOx07/8QdC3oFyRp9sf9uzKRriqCJrZ64fbSECdpgKU2gTxVZzCTSZKaLAchH2OcjhnJ3Ip0HA3S2lbtw0XVL+dPzEWL8HY/2iuETW320vzL7r84S9OHZZSKBOXwLFvmb3f+JyB/MOfy9UJoWkZeopo3StCNqNxLDdRBBdZgLYbSFI5xfDJnNZ1NioYY/PMpzx+0RWRdyujtz1ImwBKV8Gdzyl8aM7z/rthXCWefEqE//tJLHDXAq1xvIoD3Hbzzv0vbFtc/P8DFsN5K4SRBuFoB1EwG4zQeyzFGIrrB2WMmi3noNjHirYa/hfuDr6ffH8/v1Jd7KdXt33lWfvUN1irN+VrN9BCN12AtT5CuMkEXDIVgpbzIgAyzmoTQ4p4B3+3mBK15xAlwtpmpKop3J2m5EA9pIHHLISwQHqMXZbSaHLZgYuODFP1Az63yfg7u5as1fpxtTxSpMwpacYz/oFcM5WEKftRHDCThT7baSw0ZhKR4dlaC1Oc+cd/m9hS1v9klw3fRRoTEO7Pj+6TPhxwEIAhynJ77eWxEFbGdzwkkO30ydwT/hWbULjz5Fq7Hv7b5P1M6XnJUchqnwEcNZGkH3n83FKwHvIMpso/le6a+PQru3/9H1BfxSNpbnWRc5aqFjDj406E7HbeAoOWAqzrzo+ZCOOax4y9LPF/rcJuMflfnst1+Pefb8ZZP2SVHpS7HcWwUV7QbL+aThDHsC8fPuorTi6LKTYmy8l3qY/McfxTvEfoaO2QrvM2/hmjYk8mrQnY+O6iegkj9hKYekQVVwdhv/jVdDtU3uU7qWYU9yn0tNDkkpParzI+s/bTcM5G36cshHACVthHCaLZF47XG+igKow1928w/8U3OZyxzSlx4SX+pidKXRc87rEfjmK7Jaj3s8Iqfba7Fsa/2fx47ZazYcJOrjlLkWxX5xtvC45COJ7Uv5Za36ctBZgn248YMU0SuKot5mHxpSQDN7hfzq6bz3kLysp8CjJz0mqqipbX1JS8h+9Mewvj5vHdircTTLEDZe3/1qEsf4LZP1nrafgtNU0HCcCjlozbz8XpZJRDA1umthWV2zIO/wz/lNQSfjFD0UbOp4EqpLlC+AiyVlbRvn8OGFFyidhnnDfaSpEJaMwKn2McObixd/9J0Of8Z54xOUOv1TgfeCGvzLOWk7CKYvJOE4EHCUPOGI5FXtMJ2PTuglot1VFfUJAK0Ma79DP+LPA3Ir8vjo++kiQ5rPTbjNxykEcJ23f/seLvXbTsdF1KaczLbCIlP/LP5H4jA+ASzeef3ewJNl2Z5RNSoPDopTtYeYpe/NCPXrOnv3d94x+xicPPr7/B3moLI3wh9PeAAAAAElFTkSuQmCC">
                <h1>SMA Retail</h1>
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
