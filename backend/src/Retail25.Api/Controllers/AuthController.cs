using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Identity;

namespace Retail25.Api.Controllers;

/// <summary>
/// Sign-in for the web till.
/// <para>
/// The session is an <b>httpOnly, same-site cookie</b>. No token is ever handed to JavaScript, so a
/// script injected into a page has nothing to steal — the brief's "no JWTs in localStorage" rule
/// enforced by the transport rather than by convention.
/// </para>
/// <para>
/// This covers the browser. Machine clients — the terminal agent, and any future third-party
/// integration — need OpenIddict with authorization code and PKCE, which is still outstanding
/// (benchmark <c>STF-001</c>).
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> users,
        ICurrentUser currentUser,
        ILogger<AuthController> logger)
    {
        _users = users;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _users.FindByEmailAsync(request.Email)
                   ?? await _users.FindByNameAsync(request.Email);

        // One message for "no such user" and "wrong password": telling them apart lets an attacker
        // enumerate who works here.
        if (user is null || !user.IsEnabled || !await _users.CheckPasswordAsync(user, request.Password))
        {
            _logger.LogWarning("Failed sign-in attempt for {Email}.", request.Email);
            return Unauthorized(new { error = "auth.invalid_credentials" });
        }

        var roles = await _users.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty),
            new("display_name", user.DisplayName),
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        // The station and location a cashier is working at travel on the session, so a request
        // never has to say which till it came from and cannot lie about it.
        if (request.StationId is { } stationId)
        {
            claims.Add(new Claim("station_id", stationId.ToString()));
        }

        if (request.LocationId is { } locationId)
        {
            claims.Add(new Claim("location_id", locationId.ToString()));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = request.RememberMe });

        _logger.LogInformation("{Email} signed in.", user.Email);

        return Ok(new LoginResponse(user.Id, user.DisplayName, roles.ToArray()));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>
    /// Who the caller is and what they may do. The till calls this on load to decide which keys to
    /// enable — the same permission set the pricing engine enforces server-side.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => Ok(new CurrentUserResponse(
        _currentUser.UserId,
        User.FindFirst("display_name")?.Value ?? string.Empty,
        _currentUser.StationId,
        _currentUser.LocationId,
        _currentUser.Permissions.ToArray()));
}

/// <param name="Email">Email address or user name.</param>
/// <param name="Password">The password.</param>
/// <param name="StationId">Which till the cashier is signing in at.</param>
/// <param name="LocationId">Which store they are working in.</param>
/// <param name="RememberMe">Keep the session across browser restarts.</param>
public sealed record LoginRequest(
    string Email,
    string Password,
    Guid? StationId = null,
    Guid? LocationId = null,
    bool RememberMe = false);

/// <param name="UserId">Signed-in user.</param>
/// <param name="DisplayName">Name to show in the corner of the screen.</param>
/// <param name="Roles">Roles held.</param>
public sealed record LoginResponse(Guid UserId, string DisplayName, string[] Roles);

/// <param name="UserId">Signed-in user, or null when anonymous.</param>
/// <param name="DisplayName">Name to show.</param>
/// <param name="StationId">Till in use.</param>
/// <param name="LocationId">Store in use.</param>
/// <param name="Permissions">Everything this user may do.</param>
public sealed record CurrentUserResponse(
    Guid? UserId,
    string DisplayName,
    Guid? StationId,
    Guid? LocationId,
    string[] Permissions);
