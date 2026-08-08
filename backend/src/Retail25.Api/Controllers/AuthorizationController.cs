using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Immutable;
using Microsoft.AspNetCore;
using System.Linq;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Security;
using Retail25.Infrastructure.Identity;

namespace Retail25.Api.Controllers;

/// <summary>
/// The OpenID Connect endpoints (doc 07 §Topology).
/// <para>
/// The browser never sees a token. It talks to the Next.js BFF, which exchanges the code here over
/// a back channel and keeps the tokens in an httpOnly cookie. That removes XSS token theft as a
/// class rather than mitigating it, which is why the brief forbids JWTs in localStorage.
/// </para>
/// </summary>
[ApiController]
[Route("connect")]
public sealed class AuthorizationController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOpenIddictApplicationManager _applications;
    private readonly IOpenIddictAuthorizationManager _authorizations;
    private readonly IOpenIddictScopeManager _scopes;
    private readonly IAuditWriter _audit;
    private readonly IDateTime _clock;

    public AuthorizationController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IOpenIddictApplicationManager applications,
        IOpenIddictAuthorizationManager authorizations,
        IOpenIddictScopeManager scopes,
        IAuditWriter audit,
        IDateTime clock)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _applications = applications;
        _authorizations = authorizations;
        _scopes = scopes;
        _audit = audit;
        _clock = clock;
    }

    /// <summary>
    /// The authorization endpoint. An unauthenticated caller is sent to the sign-in page and comes
    /// back here; an authenticated one gets a code.
    /// </summary>
    [HttpGet("authorize")]
    [HttpPost("authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request could not be read.");

        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);

        if (result.Succeeded is not true)
        {
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                        Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList()),
                },
                IdentityConstants.ApplicationScheme);
        }

        var user = await _userManager.GetUserAsync(result.Principal)
            ?? throw new InvalidOperationException("The signed-in user could not be resolved.");

        if (!user.IsEnabled)
        {
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.AccessDenied,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "This account is disabled.",
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var principal = await _signInManager.CreateUserPrincipalAsync(user);
        principal.SetScopes(request.GetScopes());
        principal.SetResources(await ResourcesAsync(principal.GetScopes()));

        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(DestinationsFor(claim).ToArray());
        }

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// The token endpoint: code exchange, refresh rotation, and client credentials for the agent.
    /// PKCE is enforced by the server configuration, so a code cannot be exchanged without a verifier.
    /// </summary>
    [HttpPost("token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request could not be read.");

        if (request.IsClientCredentialsGrantType())
        {
            return await ExchangeClientCredentialsAsync(request);
        }

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Reject(OpenIddictConstants.Errors.UnsupportedGrantType, "That grant type is not supported.");
        }

        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var userId = result.Principal?.GetClaim(OpenIddictConstants.Claims.Subject);
        var user = userId is null ? null : await _userManager.FindByIdAsync(userId);

        // A refresh token that outlives its user's account must not keep working.
        if (user is null || !user.IsEnabled)
        {
            return Reject(OpenIddictConstants.Errors.InvalidGrant, "This account can no longer sign in.");
        }

        // Rebuilt from scratch rather than carried over, so a permission revoked five minutes ago
        // does not survive on the next refresh.
        var principal = await _signInManager.CreateUserPrincipalAsync(user);
        principal.SetScopes(result.Principal!.GetScopes());
        principal.SetResources(await ResourcesAsync(principal.GetScopes()));

        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(DestinationsFor(claim).ToArray());
        }

        user.LastSignedInAt = _clock.Now;
        await _userManager.UpdateAsync(user);

        await _audit.RecordAsync(
            AuditAction.SignedIn,
            nameof(ApplicationUser),
            user.Id.ToString(CultureInfo.InvariantCulture),
            request.IsRefreshTokenGrantType() ? "refresh_token" : "authorization_code");

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>The terminal agent: confidential client, one credential per station (doc 07).</summary>
    private async Task<IActionResult> ExchangeClientCredentialsAsync(OpenIddictRequest request)
    {
        var application = await _applications.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException("The client application could not be found.");

        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        identity.SetClaim(OpenIddictConstants.Claims.Subject, await _applications.GetClientIdAsync(application));
        identity.SetClaim(OpenIddictConstants.Claims.Name, await _applications.GetDisplayNameAsync(application));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        principal.SetResources(await ResourcesAsync(principal.GetScopes()));

        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(new[] { OpenIddictConstants.Destinations.AccessToken });
        }

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("logout")]
    [HttpPost("logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(OpenIddictConstants.Claims.Subject);

        await _signInManager.SignOutAsync();

        if (userId is not null)
        {
            await _audit.RecordAsync(AuditAction.SignedOut, nameof(ApplicationUser), userId, "logout");
        }

        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Who the caller is, for the BFF's session endpoint. Returns permissions so the UI can hide
    /// affordances — convenience only; the server checks every command regardless.
    /// </summary>
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    [HttpGet("userinfo")]
    [HttpPost("userinfo")]
    [Produces("application/json")]
    public async Task<IActionResult> Userinfo()
    {
        var userId = User.GetClaim(OpenIddictConstants.Claims.Subject);
        var user = userId is null ? null : await _userManager.FindByIdAsync(userId);

        if (user is null || !user.IsEnabled)
        {
            return Challenge(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidToken,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "This account can no longer sign in.",
                }),
                OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        }

        return Ok(new
        {
            sub = user.Id.ToString(CultureInfo.InvariantCulture),
            name = user.DisplayName,
            email = user.Email,
            staffId = User.GetClaim(AuthConstants.StaffIdClaim),
            locationId = User.GetClaim(AuthConstants.LocationIdClaim),
            accessLevel = User.GetClaim(AuthConstants.AccessLevelClaim),
            roles = User.FindAll(OpenIddictConstants.Claims.Role).Select(c => c.Value).ToArray(),
            permissions = User.FindAll(AuthConstants.PermissionClaim).Select(c => c.Value).ToArray(),
        });
    }

    /// <summary>
    /// Decides which token a claim rides on.
    /// <para>
    /// Permissions go to the access token because the API reads them on every request, and to the
    /// identity token only when the profile scope was asked for. Email never goes on the access
    /// token: the API has no use for it, and every claim on an access token is a claim that leaks
    /// if one is ever captured.
    /// </para>
    /// </summary>
    private static IEnumerable<string> DestinationsFor(Claim claim)
    {
        switch (claim.Type)
        {
            case OpenIddictConstants.Claims.Name:
                yield return OpenIddictConstants.Destinations.AccessToken;

                if (claim.Subject?.HasScope(OpenIddictConstants.Scopes.Profile) == true)
                {
                    yield return OpenIddictConstants.Destinations.IdentityToken;
                }

                yield break;

            case OpenIddictConstants.Claims.Email:
                if (claim.Subject?.HasScope(OpenIddictConstants.Scopes.Email) == true)
                {
                    yield return OpenIddictConstants.Destinations.IdentityToken;
                }

                yield break;

            case OpenIddictConstants.Claims.Role:
            case AuthConstants.PermissionClaim:
            case AuthConstants.StaffIdClaim:
            case AuthConstants.LocationIdClaim:
            case AuthConstants.AccessLevelClaim:
                yield return OpenIddictConstants.Destinations.AccessToken;
                yield break;

            // The security stamp is Identity's revocation mechanism; putting it on a token would
            // publish exactly the value that invalidates the token.
            case "AspNet.Identity.SecurityStamp":
                yield break;

            default:
                yield return OpenIddictConstants.Destinations.AccessToken;
                yield break;
        }
    }

    /// <summary>
    /// Which API a token is good for. Drained from the async enumerator here so the three call sites
    /// stay readable.
    /// </summary>
    private async Task<List<string>> ResourcesAsync(IEnumerable<string> scopes)
    {
        var resources = new List<string>();

        await foreach (var resource in _scopes.ListResourcesAsync(scopes.ToImmutableArray()))
        {
            resources.Add(resource);
        }

        return resources;
    }

    private IActionResult Reject(string error, string description) => Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }),
        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
