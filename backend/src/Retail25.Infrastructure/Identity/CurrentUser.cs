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

    /// <summary>
    /// OpenIddict writes granted scopes as the standard <c>scope</c> claim on the introspected
    /// principal, and as its own private claim on the raw one. Both are checked, because which of
    /// them is present depends on how the token was validated.
    /// </summary>
    public const string ScopeClaim = "scope";

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

    /// <summary>
    /// What a till agent may do, and nothing else.
    /// <para>
    /// The agent authenticates as a machine — <c>client_credentials</c>, no user behind it — so it has
    /// no roles and no access level to derive permissions from. Without this it resolved to an empty
    /// set and every call it made was refused: the agent could not even fetch its own device profile,
    /// so it silently kept the built-in Simulator defaults and never opened a socket to the real
    /// reader. A till that reads nothing, for no stated reason.
    /// </para>
    /// <para>
    /// Deliberately narrow. It is what a reader needs to publish what it saw and be told how it is
    /// configured — read the profile, ring tags onto the open cart at its own station. It cannot
    /// commission a tag, void a sale, discount a line or open a drawer on its own initiative; those
    /// stay with a signed-in human.
    /// </para>
    /// </summary>
    private static readonly string[] TerminalAgentPermissions =
    [
        PermissionKeys.Terminals.Read,
        PermissionKeys.Terminals.Operate,
        PermissionKeys.Pos.Sell,
    ];

    private static IReadOnlySet<string> ResolvePermissions(ClaimsPrincipal user)
    {
        var granted = new HashSet<string>(
            user.FindAll(PermissionClaim).Select(c => c.Value),
            StringComparer.Ordinal);

        if (granted.Count > 0)
        {
            return granted;
        }

        // A machine client holding the terminal scope. Checked before the level and role fallbacks
        // because it has neither, and after explicit claims because an operator who has narrowed the
        // agent's grants should not have them widened back.
        if (user.HasClaim(OpenIddictConstants.Claims.Private.Scope, AuthConstants.TerminalScope)
            || user.FindAll(ScopeClaim).Any(c => c.Value.Split(' ').Contains(AuthConstants.TerminalScope, StringComparer.Ordinal)))
        {
            return new HashSet<string>(TerminalAgentPermissions, StringComparer.Ordinal);
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
