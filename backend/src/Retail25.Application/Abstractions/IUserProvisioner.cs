using Retail25.Domain.Common;

namespace Retail25.Application.Abstractions;

/// <summary>An assignable role, as the Users screen needs to show it.</summary>
/// <param name="LegacyLevel">Legacy access level 0–4, used to preselect the matching preset.</param>
public sealed record RoleInfo(string Name, int? LegacyLevel, string? Description);

/// <summary>
/// The sign-in behind a member of staff, as an administrator needs to see it.
/// <para>
/// Every field here answers a question somebody actually asks while administering accounts: which
/// address does this person sign in with, what are they allowed to do, can they get in at all, and
/// is the account locked right now. None of them is a secret, and there is deliberately nowhere in
/// this record to put one — no hash, no PIN, no token. A screen cannot leak a field that its data
/// never carried, and a record shaped this way stays safe when it is logged, cached or serialised
/// by something that has no idea what it is holding.
/// </para>
/// </summary>
/// <param name="LockedOutUntil">
/// When the lockout expires, or null when there is none. The time is given rather than a bare flag
/// so the screen can say when somebody can try again instead of only that they cannot.
/// </param>
public sealed record UserAccountInfo(
    long UserId,
    string? Email,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    bool CanSignIn,
    DateTimeOffset? LockedOutUntil);

/// <summary>
/// Creating a sign-in is an Identity concern, and Identity lives in the infrastructure layer. This
/// is the port an application handler uses to make one, so onboarding a colleague stays a business
/// operation here rather than an <c>ApplicationUser</c> here and a <c>UserManager</c> there.
/// <para>
/// Password rules are deliberately not restated on this side. The single source of truth is the
/// Identity options configured at startup; a handler asks this port to create the account and
/// surfaces whatever the validator says, so the rule and its error message never drift apart.
/// </para>
/// </summary>
public interface IUserProvisioner
{
    /// <summary>The assignable roles, in ascending order of privilege.</summary>
    Task<IReadOnlyList<RoleInfo>> RolesAsync(CancellationToken ct);

    Task<bool> RoleExistsAsync(string roleName, CancellationToken ct);

    /// <summary>True when the address already belongs to an account.</summary>
    Task<bool> EmailTakenAsync(string email, CancellationToken ct);

    /// <summary>
    /// Creates the sign-in and puts it in <paramref name="role"/>. Returns the new user's Id.
    /// Failures carry the Identity validator's own codes, e.g. <c>PasswordTooShort</c>.
    /// </summary>
    Task<Result<long>> CreateAsync(
        string email,
        string displayName,
        string password,
        string role,
        long? locationId,
        CancellationToken ct);

    /// <summary>
    /// Sets a new password without knowing the old one — the administrator's answer to a member of
    /// staff who is locked out. Deliberately separate from <see cref="CreateAsync"/> so the
    /// permission to reset can be granted apart from the permission to onboard.
    /// </summary>
    Task<Result> ResetPasswordAsync(long userId, string newPassword, CancellationToken ct);

    /// <summary>Enables or disables the sign-in, leaving the staff record and its history intact.</summary>
    Task<Result> SetEnabledAsync(long userId, bool enabled, CancellationToken ct);

    Task<bool> IsInRoleAsync(long userId, string role, CancellationToken ct);

    /// <summary>
    /// How many people in this role can still sign in.
    /// <para>
    /// Disabled accounts are excluded deliberately: the question being asked is "would anybody be
    /// left who can actually get in", and a disabled administrator answers no.
    /// </para>
    /// </summary>
    Task<int> CountEnabledInRoleAsync(string role, CancellationToken ct);

    /// <summary>
    /// The accounts behind a set of staff records, keyed by user id.
    /// <para>
    /// Takes the whole set rather than one id at a time. The Users screen lists everybody, and
    /// asking per row turns one screen into one query per member of staff plus one per role — the
    /// kind of thing that is invisible on a laptop with four test accounts and noticeable in a shop
    /// with forty.
    /// </para>
    /// <para>
    /// Ids with no account are simply absent from the result rather than raising. A staff record
    /// whose sign-in was removed underneath it is a state the screen has to render anyway, and it
    /// should render as "no sign-in", not as a failed page.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<long, UserAccountInfo>> AccountsAsync(
        IReadOnlyCollection<long> userIds,
        CancellationToken ct);
}
