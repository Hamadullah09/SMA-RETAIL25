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

public sealed class StaffProvisioningHandlers :
    IRequestHandler<CreateStaffCommand, Result<StaffRowDto>>,
    IRequestHandler<ResetStaffPasswordCommand, Result>,
    IRequestHandler<ListAssignableRolesQuery, IReadOnlyList<AssignableRoleDto>>
{
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

    /// <summary>
    /// The longest staff code the schema accepts. Checked here so an over-long code comes back as a
    /// business error rather than a truncation or a constraint violation at SaveChanges.
    /// </summary>
    private const int MaxStaffCodeLength = 16;

    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _provisioner;
    private readonly IPinHasher _pinHasher;

    public StaffProvisioningHandlers(
        IApplicationDbContext db,
        IUserProvisioner provisioner,
        IPinHasher pinHasher)
    {
        _db = db;
        _provisioner = provisioner;
        _pinHasher = pinHasher;
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

        if (await _provisioner.EmailTakenAsync(email, ct))
        {
            return Result.Failure<StaffRowDto>(EmailTaken);
        }

        if (await _db.StaffProfiles.AnyAsync(s => s.StaffCode == code, ct))
        {
            return Result.Failure<StaffRowDto>(StaffCodeTaken.With("staffCode", code));
        }

        // Identity's own password validator decides whether the password is acceptable, so the
        // rule configured at startup is the only rule in play.
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

        var staff = StaffProfile.Create(created.Value, code, first, last, request.AccessLevel);

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
