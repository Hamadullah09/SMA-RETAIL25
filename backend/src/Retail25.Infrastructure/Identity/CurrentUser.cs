using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Retail25.Application.Abstractions;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// Resolves the current user from HttpContext claims.
/// </summary>
public class CurrentUser : ICurrentUser
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentUser(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        var user = httpContextAccessor.HttpContext?.User;

        IsAuthenticated = user?.Identity?.IsAuthenticated == true;
        UserId = IsAuthenticated ? GetUserId(user!) : null;
        StaffId = IsAuthenticated && user is not null ? ParseGuid(GetClaim(user, "staff_id")) : null;
        StationId = IsAuthenticated && user is not null ? ParseGuid(GetClaim(user, "station_id")) : null;
        LocationId = IsAuthenticated && user is not null ? ParseGuid(GetClaim(user, "location_id")) : null;
        Permissions = IsAuthenticated && user is not null
            ? GetPermissionsAsync(user).GetAwaiter().GetResult()
            : new HashSet<string>();
    }

    public Guid? UserId { get; }
    public Guid? StaffId { get; }
    public Guid? StationId { get; }
    public Guid? LocationId { get; }
    public bool IsAuthenticated { get; }
    public IReadOnlySet<string> Permissions { get; }

    private static Guid? GetUserId(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is not null && Guid.TryParse(id, out var guid) ? guid : null;
    }

    private static string? GetClaim(ClaimsPrincipal user, string type)
        => user.FindFirst(type)?.Value;

    private static Guid? ParseGuid(string? value)
        => value is { Length: > 0 } && Guid.TryParse(value, out var guid) ? guid : null;

    private async Task<HashSet<string>> GetPermissionsAsync(ClaimsPrincipal user)
    {
        var appUser = await _userManager.GetUserAsync(user);
        if (appUser is null) return new HashSet<string>();

        var roles = await _userManager.GetRolesAsync(appUser);
        if (roles.Contains("Administrator"))
        {
            return Identity.Permissions.AllPermissions.ToHashSet();
        }

        return new HashSet<string>();
    }
}
