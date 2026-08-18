using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Staff;

namespace Retail25.Application.Staff;

/// <summary>What the Users screen needs to fill its role picker.</summary>
public sealed record AssignableRoleDto(string Name, int? LegacyLevel, string? Description);

/// <summary>
/// Onboarding a colleague: one sign-in and one staff record, created together.
/// <para>
/// Both halves are required. A sign-in with no staff profile can authenticate but cannot be
/// attributed a sale; a staff profile with no sign-in appears on a commission report but cannot
/// work a till. Creating them in one command — inside one transaction — means the system never
/// holds one without the other.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Staff.Write)]
public sealed record CreateStaffCommand(
    string Email,
    string FirstName,
    string LastName,
    string StaffCode,
    string Password,
    string Role,
    int AccessLevel,
    long? LocationId = null,
    string? Pin = null) : IRequest<Result<StaffRowDto>>;

/// <summary>
/// An administrator setting someone's password for them. Separate permission from
/// <see cref="CreateStaffCommand"/>: a shift supervisor may well need to get a cashier back on
/// the till without also being able to mint new accounts.
/// </summary>
[RequiresPermission(PermissionKeys.Staff.Write)]
public sealed record ResetStaffPasswordCommand(long StaffId, string NewPassword) : IRequest<Result>;

[RequiresPermission(PermissionKeys.Staff.Read)]
public sealed record ListAssignableRolesQuery : IRequest<IReadOnlyList<AssignableRoleDto>>;

/// <summary>
/// Takes somebody's access away.
/// <para>
/// Deactivation, not deletion, and the distinction is not squeamishness. A staff row is what a sale
/// is attributed to, what a commission is owed against and what an audit entry points at — deleting
/// one would either break those references or silently rewrite who did what, which is the one thing
/// an audit trail exists to prevent. The sign-in stops working, the person disappears from the
/// active list, and every record they touched still says their name.
/// </para>
/// <para>
/// Reversible by design: <see cref="ReactivateStaffCommand"/> puts it back. A shop that walks
/// somebody out on Friday and rehires them in March should not need a database restore.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Staff.Write)]
public sealed record DeactivateStaffCommand(long StaffId) : IRequest<Result>;

[RequiresPermission(PermissionKeys.Staff.Write)]
public sealed record ReactivateStaffCommand(long StaffId) : IRequest<Result>;

/// <summary>
/// Removes a colleague outright — the staff profile and the sign-in behind it.
/// <para>
/// Only for somebody who never traded. Every sale, drawer count and audit entry names the staff who
/// did it, so deleting one who has history would leave last month's takings with no cashier against
/// them and the audit log unable to say who changed a price. Where there is history the answer is
/// Deactivate, which removes the access and keeps the books readable, and this refuses and says so.
/// </para>
/// <para>
/// What it is genuinely for: clearing up accounts created by mistake, and the half-finished ones
/// left by a failed creation.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Staff.Write)]
public sealed record DeleteStaffCommand(long StaffId) : IRequest<Result>;

public sealed class StaffProvisioningHandlers :
    IRequestHandler<CreateStaffCommand, Result<StaffRowDto>>,
    IRequestHandler<ResetStaffPasswordCommand, Result>,
    IRequestHandler<DeactivateStaffCommand, Result>,
    IRequestHandler<ReactivateStaffCommand, Result>,
    IRequestHandler<DeleteStaffCommand, Result>,
    IRequestHandler<ListAssignableRolesQuery, IReadOnlyList<AssignableRoleDto>>
{
    public static readonly Error CannotDeactivateSelf = new(
        "staff.cannot_deactivate_self",
        "You cannot remove your own access. Ask another administrator.");

    public static readonly Error LastAdministrator = new(
        "staff.last_administrator",
        "This is the only administrator left. Give somebody else administrator access first.");

    public static readonly Error EmailRequired = new("staff.email_required", "An email address is required.");

    public static readonly Error EmailMalformed = new("staff.email_malformed", "That does not look like an email address.");

    public static readonly Error EmailTaken = new("staff.email_taken", "An account already uses that email address.");

    public static readonly Error NameRequired = new("staff.name_required", "A first and last name are required.");

    public static readonly Error StaffCodeRequired = new("staff.code_required", "A staff code is required.");

    public static readonly Error StaffCodeTaken = new("staff.code_taken", "Another member of staff already uses that code.");

    public static readonly Error UnknownRole = new("staff.unknown_role", "That role does not exist.");

    public static readonly Error AccessLevelOutOfRange = new(
        "staff.access_level_out_of_range",
        "Access level must be between 0 and 4.");

    public static readonly Error PinNotNumeric = new("staff.pin_not_numeric", "A PIN must be digits only.");

    public static readonly Error StaffNotFound = new("staff.not_found", "No such member of staff.");

    public static readonly Error HasHistory = new(
        "staff.has_history",
        "This person has already worked — sales, drawer counts or changes are recorded against them. "
        + "Deactivate them instead, which removes their access and leaves the records readable.");

    /// <summary>
    /// The longest staff code the schema accepts. Checked here so an over-long code comes back as a
    /// business error rather than a truncation or a constraint violation at SaveChanges.
    /// </summary>
    private const int MaxStaffCodeLength = 16;

    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _provisioner;
    private readonly IPinHasher _pinHasher;
    private readonly ICurrentUser _currentUser;

    public StaffProvisioningHandlers(
        IApplicationDbContext db,
        IUserProvisioner provisioner,
        IPinHasher pinHasher,
        ICurrentUser currentUser)
    {
        _db = db;
        _provisioner = provisioner;
        _pinHasher = pinHasher;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<AssignableRoleDto>> Handle(ListAssignableRolesQuery request, CancellationToken ct)
    {
        var roles = await _provisioner.RolesAsync(ct);

        return roles
            .Select(r => new AssignableRoleDto(r.Name, r.LegacyLevel, r.Description))
            .ToList();
    }

    public async Task<Result<StaffRowDto>> Handle(CreateStaffCommand request, CancellationToken ct)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var first = request.FirstName?.Trim() ?? string.Empty;
        var last = request.LastName?.Trim() ?? string.Empty;
        var code = request.StaffCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var role = request.Role?.Trim() ?? string.Empty;
        var pin = request.Pin?.Trim();

        // Everything that can be judged without touching the database or Identity, first. A caller
        // who sent three bad fields should not need three round trips to find that out.
        if (email.Length == 0)
        {
            return Result.Failure<StaffRowDto>(EmailRequired);
        }

        if (!LooksLikeEmail(email))
        {
            return Result.Failure<StaffRowDto>(EmailMalformed);
        }

        if (first.Length == 0 || last.Length == 0)
        {
            return Result.Failure<StaffRowDto>(NameRequired);
        }

        if (code.Length == 0)
        {
            return Result.Failure<StaffRowDto>(StaffCodeRequired);
        }

        if (code.Length > MaxStaffCodeLength)
        {
            return Result.Failure<StaffRowDto>(
                StaffCodeRequired.With("maxLength", MaxStaffCodeLength));
        }

        if (request.AccessLevel is < 0 or > 4)
        {
            return Result.Failure<StaffRowDto>(AccessLevelOutOfRange);
        }

        if (pin is { Length: > 0 })
        {
            if (!pin.All(char.IsAsciiDigit))
            {
                return Result.Failure<StaffRowDto>(PinNotNumeric);
            }

            if (pin.Length < 4)
            {
                return Result.Failure<StaffRowDto>(StaffProfile.PinTooShort);
            }
        }

        if (!await _provisioner.RoleExistsAsync(role, ct))
        {
            return Result.Failure<StaffRowDto>(UnknownRole.With("role", role));
        }

        // A sign-in already on this address is usually a real colleague, and refusing is right. But
        // it is sometimes the wreckage of a half-finished creation: the sign-in is written first and
        // the staff profile second, so a failure between them leaves a login with no profile —
        // invisible on the users screen, and refusing every retry as "that address is taken" with
        // nothing on screen to see or remove. An administrator has no way out of that except a
        // database edit.
        //
        // So: taken by somebody with a profile is a refusal; taken by nothing is an orphan, and the
        // creation adopts it rather than starting an argument it cannot win.
        var orphanUserId = (long?)null;

        if (await _provisioner.EmailTakenAsync(email, ct))
        {
            var existingUserId = await _provisioner.FindIdByEmailAsync(email, ct);

            var hasProfile = existingUserId is { } id
                && await _db.StaffProfiles.AnyAsync(s => s.UserId == id, ct);

            if (existingUserId is null || hasProfile)
            {
                return Result.Failure<StaffRowDto>(EmailTaken);
            }

            orphanUserId = existingUserId;
        }

        if (await _db.StaffProfiles.AnyAsync(s => s.StaffCode == code, ct))
        {
            return Result.Failure<StaffRowDto>(StaffCodeTaken.With("staffCode", code));
        }

        // Identity's own password validator decides whether the password is acceptable, so the
        // rule configured at startup is the only rule in play.
        long userId;

        if (orphanUserId is { } adopted)
        {
            // Re-set the password and enable it, so an adopted sign-in is in exactly the state a
            // freshly created one would be. The administrator typed a password on this form and
            // expects it to be the one that works; silently keeping whatever the abandoned attempt
            // set would leave them holding a credential that does not.
            var reset = await _provisioner.ResetPasswordAsync(adopted, request.Password ?? string.Empty, ct);

            if (reset.IsFailure)
            {
                return Result.Failure<StaffRowDto>(reset.Error);
            }

            var enabled = await _provisioner.SetEnabledAsync(adopted, true, ct);

            if (enabled.IsFailure)
            {
                return Result.Failure<StaffRowDto>(enabled.Error);
            }

            userId = adopted;
        }
        else
        {
            var created = await _provisioner.CreateAsync(
                email,
                $"{first} {last}",
                request.Password ?? string.Empty,
                role,
                request.LocationId,
                ct);

            if (created.IsFailure)
            {
                return Result.Failure<StaffRowDto>(created.Error);
            }

            userId = created.Value;
        }

        var staff = StaffProfile.Create(userId, code, first, last, request.AccessLevel);

        if (pin is { Length: >= 4 })
        {
            staff.SetPin(_pinHasher.Hash(pin));
        }

        _db.StaffProfiles.Add(staff);
        await _db.SaveChangesAsync(ct);

        return Result.Success(new StaffRowDto(
            staff.Id,
            staff.StaffCode,
            staff.FullName,
            staff.AccessLevel,
            staff.IsActive,
            false,
            null));
    }

    public async Task<Result> Handle(ResetStaffPasswordCommand request, CancellationToken ct)
    {
        var staff = await _db.StaffProfiles.FirstOrDefaultAsync(s => s.Id == request.StaffId, ct);

        return staff is null
            ? Result.Failure(StaffNotFound)
            : await _provisioner.ResetPasswordAsync(staff.UserId, request.NewPassword ?? string.Empty, ct);
    }

    public async Task<Result> Handle(DeactivateStaffCommand request, CancellationToken ct)
    {
        var staff = await _db.StaffProfiles.FirstOrDefaultAsync(s => s.Id == request.StaffId, ct);

        if (staff is null)
        {
            return Result.Failure(StaffNotFound);
        }

        // Locking yourself out is the mistake this is most likely to be. Refusing it costs nothing
        // and the alternative is an administrator who cannot undo what they just did.
        if (_currentUser.StaffId == staff.Id)
        {
            return Result.Failure(CannotDeactivateSelf);
        }

        // And locking *everybody* out. Counted across the account rather than the location: an
        // administrator is an administrator everywhere, and a shop with one left is one careless
        // click from having none and no way back in short of a database edit.
        if (await IsLastAdministratorAsync(staff, ct))
        {
            return Result.Failure(LastAdministrator);
        }

        var disabled = await _provisioner.SetEnabledAsync(staff.UserId, false, ct);
        if (disabled.IsFailure)
        {
            return disabled;
        }

        staff.SetActive(false);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> Handle(DeleteStaffCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var staff = await _db.StaffProfiles.FirstOrDefaultAsync(s => s.Id == request.StaffId, ct);

        if (staff is null)
        {
            return Result.Failure(StaffNotFound);
        }

        // The same two guards deactivation has, for the same reasons, and more sharply here: this
        // one cannot be undone by a second click the way Reactivate undoes the other.
        if (_currentUser.StaffId == staff.Id)
        {
            return Result.Failure(CannotDeactivateSelf);
        }

        if (await IsLastAdministratorAsync(staff, ct))
        {
            return Result.Failure(LastAdministrator);
        }

        // Anything that names them. A sale attributed to a staff row that no longer exists is a
        // receipt with no cashier and a commission report that cannot be reconciled; an audit entry
        // pointing at a deleted actor is an audit trail that has stopped being one. So the answer
        // for anybody who has worked is deactivation, and this says so rather than doing damage the
        // administrator cannot see from this screen.
        var traded = await _db.SalesTransactions.AnyAsync(t => t.StaffId == staff.Id, ct)
            || await _db.SalesTransactions.AnyAsync(t => t.VoidApprovedByStaffId == staff.Id, ct)
            || await _db.DrawerSessions.AnyAsync(d => d.OpenedByStaffId == staff.Id || d.ClosedByStaffId == staff.Id, ct)
            || await _db.AuditLogEntries.AnyAsync(a => a.ActorStaffId == staff.Id || a.ApproverStaffId == staff.Id, ct);

        if (traded)
        {
            return Result.Failure(HasHistory);
        }

        var userId = staff.UserId;

        _db.StaffProfiles.Remove(staff);
        await _db.SaveChangesAsync(ct);

        // The profile goes first and the sign-in second, so a failure here leaves a sign-in with no
        // profile — which is recoverable, because creating the same address again adopts it. The
        // other order would leave a profile whose sign-in has gone, and nothing recovers that.
        return await _provisioner.DeleteAsync(userId, ct);
    }

    public async Task<Result> Handle(ReactivateStaffCommand request, CancellationToken ct)
    {
        var staff = await _db.StaffProfiles.FirstOrDefaultAsync(s => s.Id == request.StaffId, ct);

        if (staff is null)
        {
            return Result.Failure(StaffNotFound);
        }

        var enabled = await _provisioner.SetEnabledAsync(staff.UserId, true, ct);
        if (enabled.IsFailure)
        {
            return enabled;
        }

        staff.SetActive(true);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    /// <summary>
    /// Whether this person is the only administrator still able to sign in.
    /// <para>
    /// Asks Identity rather than reading the legacy access level, because the level is a preset and
    /// the role is the thing authorisation actually uses — somebody can hold level 4 and not be in
    /// the Administrator role, and it is the role that would be lost.
    /// </para>
    /// </summary>
    private async Task<bool> IsLastAdministratorAsync(StaffProfile staff, CancellationToken ct)
    {
        if (!await _provisioner.IsInRoleAsync(staff.UserId, AdministratorRole, ct))
        {
            return false;
        }

        var administrators = await _provisioner.CountEnabledInRoleAsync(AdministratorRole, ct);

        return administrators <= 1;
    }

    /// <summary>The role the seeder creates and the one that can reach every permission.</summary>
    private const string AdministratorRole = "Administrator";

    /// <summary>
    /// Deliberately permissive: one <c>@</c> with something either side and a dot in the domain.
    /// This exists to catch a mistyped address, not to adjudicate RFC 5322 — the authoritative
    /// check is that a confirmation reaches the inbox.
    /// </summary>
    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');

        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
        {
            return false;
        }

        var domain = value[(at + 1)..];

        return domain.Contains('.')
            && !domain.StartsWith('.')
            && !domain.EndsWith('.')
            && !value.Contains(' ');
    }
}
