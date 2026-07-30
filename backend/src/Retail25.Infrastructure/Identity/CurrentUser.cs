using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
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
/// <para>
/// Every member reads <c>HttpContext.User</c> live rather than snapshotting it in the constructor.
/// A policy that names explicit authentication schemes (every business endpoint does, since the API
/// is bearer-only while the sign-in page is cookie-based) re-authenticates and reassigns
/// <c>HttpContext.User</c> during authorization — which runs after this type would otherwise have
/// been constructed and its claims already cached, silently freezing every request into "anonymous,
/// no permissions" regardless of the caller.
/// </para>
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    public const string StaffIdClaim = "staff_id";
    public const string StationIdClaim = "station_id";
    public const string LocationIdClaim = "location_id";
    public const string AccessLevelClaim = "access_level";
    public const string PermissionClaim = "permission";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    // "sub", not ClaimTypes.NameIdentifier: IdentityRegistration configures
    // IdentityOptions.ClaimsIdentity.UserIdClaimType = OpenIddictConstants.Claims.Subject, so that is
    // the claim type every issued token actually carries — the long-form URI is never present.
    public Guid? UserId => IsAuthenticated ? ParseGuid(User!.FindFirstValue(OpenIddictConstants.Claims.Subject)) : null;

    public Guid? StaffId => IsAuthenticated ? ParseGuid(User!.FindFirstValue(StaffIdClaim)) : null;

    public Guid? StationId => IsAuthenticated ? ParseGuid(User!.FindFirstValue(StationIdClaim)) : null;

    public Guid? LocationId => IsAuthenticated ? ParseGuid(User!.FindFirstValue(LocationIdClaim)) : null;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public IReadOnlySet<string> Permissions => IsAuthenticated
        ? ResolvePermissions(User!)
        : new HashSet<string>(StringComparer.Ordinal);

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
