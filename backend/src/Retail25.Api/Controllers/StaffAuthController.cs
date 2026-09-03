using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Security;
using Retail25.Infrastructure.Identity;

namespace Retail25.Api.Controllers;

public sealed record SignInRequest(string Username, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt);

/// <summary>
/// Signing in, without an authorization server.
/// <para>
/// This replaces the OpenIddict authorization-code flow: a browser redirect to a server-rendered
/// login page, a code, a back-channel exchange and a reference token looked up on every request.
/// That is the right shape when third parties need to sign in to your API. Here there is exactly one
/// client — this shop's own front end, talking through its own back end — and the redirect dance
/// bought nothing but a login page that could not use the design system and a token lookup on every
/// call.
/// </para>
/// <para>
/// What has deliberately not changed: the browser never sees a token. The front end's BFF holds it
/// in an encrypted, httpOnly cookie and attaches it server-side, exactly as it held the reference
/// token before. A JWT in localStorage is one cross-site script away from being stolen, and this is
/// a till.
/// </para>
/// </summary>
[ApiController]
[Route("auth")]
public sealed class StaffAuthController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly StaffTokenIssuer _tokens;
    private readonly IAuditWriter _audit;
    private readonly IDateTime _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<StaffAuthController> _logger;

    public StaffAuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        StaffTokenIssuer tokens,
        IAuditWriter audit,
        IDateTime clock,
        ICurrentUser currentUser,
        ILogger<StaffAuthController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _tokens = tokens;
        _audit = audit;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>Username and password in, a token pair out.</summary>
    [AllowAnonymous]
    [HttpPost("token")]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _userManager.FindByEmailAsync(request.Username ?? string.Empty)
            ?? await _userManager.FindByNameAsync(request.Username ?? string.Empty);

        // One message for "no such account" and for "wrong password", and the password is still
        // checked when the account does not exist so the two take the same time. Telling an attacker
        // which addresses are real is half of a credential-stuffing run.
        if (user is null)
        {
            await Task.Delay(Random.Shared.Next(120, 260), ct);
            return Refused();
        }

        if (!user.IsEnabled)
        {
            return Refused("This account can no longer sign in. Ask an administrator.");
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password ?? string.Empty, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            // Says when, not just no. "Try again later" sends somebody back every thirty seconds.
            var until = user.LockoutEnd?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

            return Refused(until is null
                ? "Too many attempts. Try again shortly."
                : $"Too many attempts. Try again after {until}.");
        }

        if (!result.Succeeded)
        {
            return Refused();
        }

        user.LastSignedInAt = _clock.Now;
        await _userManager.UpdateAsync(user);

        await _audit.RecordAsync(
            AuditAction.SignedIn,
            nameof(ApplicationUser),
            user.Id.ToString(CultureInfo.InvariantCulture),
            "password");

        return Ok(await IssueAsync(user));
    }

    /// <summary>A refresh token in, a fresh pair out.</summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = _tokens.Issuer,
            ValidAudience = _tokens.Audience,
            IssuerSigningKey = _tokens.SigningKey,
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        var validated = await new JsonWebTokenHandler().ValidateTokenAsync(request.RefreshToken ?? string.Empty, parameters);

        if (!validated.IsValid)
        {
            return Refused("That session has expired. Sign in again.");
        }

        // A refresh token must not be usable as an access token, and an access token must not be
        // usable to refresh. One key signs both, so the use is what tells them apart.
        if (validated.Claims.TryGetValue(StaffAuthentication.TokenUseClaim, out var use)
            && use as string != StaffAuthentication.RefreshTokenUse)
        {
            return Refused("That session has expired. Sign in again.");
        }

        var subject = validated.Claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var sub) ? sub as string : null;

        if (!long.TryParse(subject, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
        {
            return Refused("That session has expired. Sign in again.");
        }

        var user = await _userManager.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));

        if (user is null || !user.IsEnabled)
        {
            return Refused("This account can no longer sign in. Ask an administrator.");
        }

        // The revocation check. Identity moves the stamp on a password reset or a disable, so a
        // refresh minted before either no longer matches and the session ends there — which is what
        // a self-contained token otherwise cannot offer.
        var stamp = validated.Claims.TryGetValue(StaffAuthentication.SecurityStampClaim, out var s) ? s as string : null;

        if (!string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal))
        {
            _logger.LogInformation("Refresh refused for user {UserId}: the security stamp has moved.", user.Id);

            return Refused("Your password was changed. Sign in again.");
        }

        return Ok(await IssueAsync(user));
    }

    /// <summary>Who the caller is, for the BFF's session endpoint.</summary>
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
        => Ok(new
        {
            subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            name = User.FindFirstValue("name") ?? User.Identity?.Name,
            staffId = User.FindFirstValue(AuthConstants.StaffIdClaim),
            locationId = User.FindFirstValue(AuthConstants.LocationIdClaim),
            accessLevel = User.FindFirstValue(AuthConstants.AccessLevelClaim),
            roles = User.FindAll("role").Select(c => c.Value).ToArray(),

            // Asked of the same resolver every [RequiresPermission] check asks, rather than read off
            // the claims here. The token packs its permissions into one claim now, and a second
            // reading of that shape is a second thing to keep in step — the front end would have
            // hidden buttons the API was perfectly willing to honour.
            permissions = _currentUser.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToArray(),
        });

    /// <summary>
    /// Ends every session this account has, everywhere.
    /// <para>
    /// Rotating the stamp is the whole mechanism: existing access tokens die at their next expiry,
    /// and no refresh token can be redeemed after it. There is no token table to clear because there
    /// is no token table.
    /// </para>
    /// </summary>
    [Authorize]
    [HttpPost("sign-out-everywhere")]
    public async Task<IActionResult> SignOutEverywhere()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user is null) return Unauthorized();

        await _userManager.UpdateSecurityStampAsync(user);

        return NoContent();
    }

    private async Task<TokenResponse> IssueAsync(ApplicationUser user)
    {
        // Built by Identity's own claims factory, the same one the authorization server used, so the
        // permissions in a token are the permissions the tables say — not a second opinion assembled
        // here that could quietly fall out of step.
        var principal = await _signInManager.CreateUserPrincipalAsync(user);

        var issued = _tokens.Issue(user.Id, user.SecurityStamp ?? string.Empty, principal.Claims);

        return new TokenResponse(
            issued.AccessToken,
            issued.AccessTokenExpires,
            issued.RefreshToken,
            issued.RefreshTokenExpires);
    }

    private IActionResult Refused(string message = "That username or password is not right.")
        => Unauthorized(new { error = "invalid_grant", message });
}
