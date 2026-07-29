using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// The acting user, resolved from claims on the request.
/// <para>
/// Permissions come from <c>permission</c> claims when the token carries them, and otherwise from the
/// legacy access level (guide p.82) mapped through <see cref="PermissionKeys.LegacyLevelPresets"/>.
/// Reading them off claims rather than querying per request matters: the authorisation behaviour runs
/// on every command, and a database round trip there would sit inside the till's quote budget.
/// </para>
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    public const string StaffIdClaim = "staff_id";
    public const string StationIdClaim = "station_id";
    public const string LocationIdClaim = "location_id";
    public const string AccessLevelClaim = "access_level";
    public const string PermissionClaim = "permission";

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        var user = httpContextAccessor.HttpContext?.User;

        IsAuthenticated = user?.Identity?.IsAuthenticated == true;

        if (!IsAuthenticated || user is null)
        {
            Permissions = new HashSet<string>(StringComparer.Ordinal);
            return;
        }

        UserId = ParseGuid(user.FindFirstValue(ClaimTypes.NameIdentifier));
        StaffId = ParseGuid(user.FindFirstValue(StaffIdClaim));
        StationId = ParseGuid(user.FindFirstValue(StationIdClaim));
        LocationId = ParseGuid(user.FindFirstValue(LocationIdClaim));
        Permissions = ResolvePermissions(user);
    }

    public Guid? UserId { get; }

    public Guid? StaffId { get; }

    public Guid? StationId { get; }

    public Guid? LocationId { get; }

    public bool IsAuthenticated { get; }

    public IReadOnlySet<string> Permissions { get; }

    private static IReadOnlySet<string> ResolvePermissions(ClaimsPrincipal user)
    {
        var granted = new HashSet<string>(
            user.FindAll(PermissionClaim).Select(c => c.Value),
            StringComparer.Ordinal);

        if (granted.Count > 0)
        {
            return granted;
        }

        // No explicit grants: fall back to the level preset so a migrated user works on day one.
        if (int.TryParse(user.FindFirstValue(AccessLevelClaim), out var level)
            && PermissionKeys.LegacyLevelPresets.TryGetValue(level, out var preset))
        {
            return new HashSet<string>(preset, StringComparer.Ordinal);
        }

        return user.IsInRole("Administrator")
            ? new HashSet<string>(PermissionKeys.All, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    private static Guid? ParseGuid(string? value)
        => Guid.TryParse(value, out var guid) ? guid : null;
}
