using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Security;
using Retail25.Domain.Staff;

namespace Retail25.Application.Auth;

public sealed record StaffSessionDto(
    Guid StaffId,
    string StaffCode,
    string FullName,
    int AccessLevel,
    IReadOnlyList<string> Permissions);

/// <summary>
/// POS fast user switching (guide p.13 Ctrl+I, doc 07).
/// <para>
/// Cashiers cannot type a full password between customers, so a PIN switches who a sale is attributed
/// to <b>within an already-authenticated station session</b>. That distinction is the whole security
/// argument: the PIN is not a login. It never mints a session on its own, it only re-attributes one
/// the station already holds, so a four-digit secret is never the only thing between the outside
/// world and the till.
/// </para>
/// </summary>
public sealed record VerifyStaffPinCommand(string StaffCode, string Pin, Guid StationId) : IRequest<Result<StaffSessionDto>>;

/// <summary>Sets or replaces a staff PIN. A supervisor does this; a cashier cannot set their own.</summary>
[RequiresPermission(PermissionKeys.Staff.Write)]
public sealed record SetStaffPinCommand(Guid StaffId, string Pin) : IRequest<Result>;

/// <summary>Clears a lockout after the five attempts ran out (doc 07).</summary>
[RequiresPermission(PermissionKeys.Staff.Write)]
public sealed record UnlockStaffPinCommand(Guid StaffId) : IRequest<Result>;

public sealed class StaffPinHandlers
    : IRequestHandler<VerifyStaffPinCommand, Result<StaffSessionDto>>,
      IRequestHandler<SetStaffPinCommand, Result>,
      IRequestHandler<UnlockStaffPinCommand, Result>
{
    /// <summary>
    /// One error for "no such staff code" and for "wrong PIN". Distinguishing them would let anyone
    /// with a keypad enumerate who works here, which is the first half of a targeted attempt.
    /// </summary>
    public static readonly Error InvalidCredentials = new("staff.pin_invalid", "That staff code or PIN was not recognised.");

    public static readonly Error StaffNotFound = new("staff.not_found", "No such staff member.");

    private const int MinimumPinLength = 4;

    private readonly IApplicationDbContext _db;
    private readonly IPinHasher _hasher;
    private readonly IPermissionResolver _permissions;
    private readonly IAuditWriter _audit;
    private readonly IDateTime _clock;

    public StaffPinHandlers(
        IApplicationDbContext db,
        IPinHasher hasher,
        IPermissionResolver permissions,
        IAuditWriter audit,
        IDateTime clock)
    {
        _db = db;
        _hasher = hasher;
        _permissions = permissions;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<StaffSessionDto>> Handle(VerifyStaffPinCommand request, CancellationToken ct)
    {
        var code = request.StaffCode.Trim().ToUpperInvariant();
        var now = _clock.Now;

        var staff = await _db.StaffProfiles.FirstOrDefaultAsync(s => s.StaffCode == code, ct);

        if (staff is null || !staff.IsActive || !staff.HasPin)
        {
            await _audit.RecordAsync(
                AuditAction.SignInFailed,
                nameof(StaffProfile),
                staff?.Id.ToString(),
                nameof(VerifyStaffPinCommand),
                reason: "Unknown staff code or no PIN set",
                ct: ct);

            return Result.Failure<StaffSessionDto>(InvalidCredentials);
        }

        if (staff.IsPinLocked(now))
        {
            return Result.Failure<StaffSessionDto>(
                StaffProfile.PinLocked.With("until", staff.PinLockedUntil));
        }

        if (!_hasher.Verify(request.Pin, staff.PinHash!))
        {
            staff.RecordPinFailure(now);
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(
                AuditAction.SignInFailed,
                nameof(StaffProfile),
                staff.Id.ToString(),
                nameof(VerifyStaffPinCommand),
                reason: $"Incorrect PIN (attempt {staff.FailedPinAttempts} of {StaffProfile.MaxPinAttempts})",
                ct: ct);

            return staff.IsPinLocked(now)
                ? Result.Failure<StaffSessionDto>(StaffProfile.PinLocked.With("until", staff.PinLockedUntil))
                : Result.Failure<StaffSessionDto>(InvalidCredentials);
        }

        staff.RecordPinSuccess();
        await _db.SaveChangesAsync(ct);

        var permissions = await _permissions.ResolveForUserAsync(staff.UserId, ct);

        await _audit.RecordAsync(
            AuditAction.SignedIn,
            nameof(StaffProfile),
            staff.Id.ToString(),
            nameof(VerifyStaffPinCommand),
            reason: "Staff switch at the till",
            ct: ct);

        return Result.Success(new StaffSessionDto(
            staff.Id,
            staff.StaffCode,
            staff.FullName,
            staff.AccessLevel,
            permissions.ToList()));
    }

    public async Task<Result> Handle(SetStaffPinCommand request, CancellationToken ct)
    {
        if (request.Pin is null || request.Pin.Trim().Length < MinimumPinLength)
        {
            return Result.Failure(StaffProfile.PinTooShort);
        }

        var staff = await _db.StaffProfiles.FirstOrDefaultAsync(s => s.Id == request.StaffId, ct);
        if (staff is null)
        {
            return Result.Failure(StaffNotFound.With("staffId", request.StaffId));
        }

        // The domain is handed a hash, never the PIN, so the plaintext cannot reach an audit diff.
        var set = staff.SetPin(_hasher.Hash(request.Pin.Trim()));
        if (set.IsFailure)
        {
            return set;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            AuditAction.Updated,
            nameof(StaffProfile),
            staff.Id.ToString(),
            nameof(SetStaffPinCommand),
            reason: "PIN set",
            ct: ct);

        return Result.Success();
    }

    public async Task<Result> Handle(UnlockStaffPinCommand request, CancellationToken ct)
    {
        var staff = await _db.StaffProfiles.FirstOrDefaultAsync(s => s.Id == request.StaffId, ct);
        if (staff is null)
        {
            return Result.Failure(StaffNotFound.With("staffId", request.StaffId));
        }

        staff.UnlockPin();
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            AuditAction.Updated,
            nameof(StaffProfile),
            staff.Id.ToString(),
            nameof(UnlockStaffPinCommand),
            reason: "PIN lockout cleared",
            ct: ct);

        return Result.Success();
    }
}
