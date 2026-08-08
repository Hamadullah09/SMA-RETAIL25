using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Security;
using Retail25.Domain.Staff;

namespace Retail25.Application.Auth;

public sealed record ApprovalRequestDto(
    long Id,
    string Permission,
    string Action,
    string? Context,
    long RequestedByStaffId,
    string RequestedByName,
    long StationId,
    ApprovalStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Raises a supervisor override request (doc 07 §Step-up).
/// <para>
/// It is broadcast to every till at the location rather than only shown at the one that asked. That
/// is the improvement over the legacy supervisor password: the supervisor approves from wherever
/// they are standing instead of walking to the till and typing into someone else's session.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record RequestSupervisorApprovalCommand(
    string Permission,
    string Action,
    string? Context,
    long StationId) : IRequest<Result<ApprovalRequestDto>>;

/// <summary>Approves inline with a supervisor's PIN, without leaving the till.</summary>
public sealed record ApproveWithPinCommand(long ApprovalId, string SupervisorStaffCode, string Pin)
    : IRequest<Result<ApprovalRequestDto>>;

/// <summary>Approves from another station, by a supervisor already signed in there.</summary>
[RequiresPermission(PermissionKeys.Pos.VoidSale)]
public sealed record ApproveSupervisorRequestCommand(long ApprovalId) : IRequest<Result<ApprovalRequestDto>>;

[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record DenySupervisorRequestCommand(long ApprovalId, string? Reason = null)
    : IRequest<Result<ApprovalRequestDto>>;

/// <summary>What is waiting for a supervisor at this location right now.</summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record ListPendingApprovalsQuery(long LocationId) : IRequest<IReadOnlyList<ApprovalRequestDto>>;

public sealed class SupervisorApprovalHandlers
    : IRequestHandler<RequestSupervisorApprovalCommand, Result<ApprovalRequestDto>>,
      IRequestHandler<ApproveWithPinCommand, Result<ApprovalRequestDto>>,
      IRequestHandler<ApproveSupervisorRequestCommand, Result<ApprovalRequestDto>>,
      IRequestHandler<DenySupervisorRequestCommand, Result<ApprovalRequestDto>>,
      IRequestHandler<ListPendingApprovalsQuery, IReadOnlyList<ApprovalRequestDto>>
{
    public static readonly Error NotFound = new("approval.not_found", "No such approval request.");
    public static readonly Error NotPermitted = new("approval.not_permitted", "That staff member cannot approve this action.");
    public static readonly Error NoStaffContext = new("approval.no_staff", "No staff member is signed in at this till.");

    private readonly IApplicationDbContext _db;
    private readonly IPinHasher _hasher;
    private readonly IPermissionResolver _permissions;
    private readonly IPosNotifier _notifier;
    private readonly IAuditWriter _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    public SupervisorApprovalHandlers(
        IApplicationDbContext db,
        IPinHasher hasher,
        IPermissionResolver permissions,
        IPosNotifier notifier,
        IAuditWriter audit,
        ICurrentUser currentUser,
        IDateTime clock)
    {
        _db = db;
        _hasher = hasher;
        _permissions = permissions;
        _notifier = notifier;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<ApprovalRequestDto>> Handle(RequestSupervisorApprovalCommand request, CancellationToken ct)
    {
        if (_currentUser.StaffId is not { } staffId)
        {
            return Result.Failure<ApprovalRequestDto>(NoStaffContext);
        }

        var station = await _db.Stations.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.StationId, ct);
        if (station is null)
        {
            return Result.Failure<ApprovalRequestDto>(new Error("station.not_found", "That station is not registered."));
        }

        var approval = SupervisorApproval.Request(
            request.Permission,
            request.Action,
            request.Context,
            staffId,
            request.StationId,
            station.LocationId,
            _clock.Now);

        _db.SupervisorApprovals.Add(approval);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            AuditAction.StepUpRequested,
            nameof(SupervisorApproval),
            approval.Id.ToString(CultureInfo.InvariantCulture),
            request.Action,
            reason: request.Context,
            ct: ct);

        var dto = await ToDtoAsync(approval, ct);
        await _notifier.SupervisorApprovalRequestedAsync(station.LocationId, dto, ct);

        return Result.Success(dto);
    }

    public async Task<Result<ApprovalRequestDto>> Handle(ApproveWithPinCommand request, CancellationToken ct)
    {
        var approval = await _db.SupervisorApprovals.FirstOrDefaultAsync(a => a.Id == request.ApprovalId, ct);
        if (approval is null)
        {
            return Result.Failure<ApprovalRequestDto>(NotFound.With("approvalId", request.ApprovalId));
        }

        var code = request.SupervisorStaffCode.Trim().ToUpperInvariant();
        var supervisor = await _db.StaffProfiles.FirstOrDefaultAsync(s => s.StaffCode == code, ct);
        var now = _clock.Now;

        if (supervisor is null || !supervisor.IsActive || !supervisor.HasPin)
        {
            return Result.Failure<ApprovalRequestDto>(StaffPinHandlers.InvalidCredentials);
        }

        if (supervisor.IsPinLocked(now))
        {
            return Result.Failure<ApprovalRequestDto>(StaffProfile.PinLocked.With("until", supervisor.PinLockedUntil));
        }

        if (!_hasher.Verify(request.Pin, supervisor.PinHash!))
        {
            supervisor.RecordPinFailure(now);
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(
                AuditAction.StepUpDenied,
                nameof(SupervisorApproval),
                approval.Id.ToString(CultureInfo.InvariantCulture),
                approval.Action,
                reason: "Incorrect supervisor PIN",
                ct: ct);

            return Result.Failure<ApprovalRequestDto>(StaffPinHandlers.InvalidCredentials);
        }

        supervisor.RecordPinSuccess();

        // Holding a PIN is not the same as holding the permission. A cashier's PIN must not approve
        // a void just because it was typed at the supervisor prompt.
        var granted = await _permissions.ResolveForUserAsync(supervisor.UserId, ct);
        if (!granted.Contains(approval.Permission))
        {
            await _audit.RecordAsync(
                AuditAction.StepUpDenied,
                nameof(SupervisorApproval),
                approval.Id.ToString(CultureInfo.InvariantCulture),
                approval.Action,
                reason: $"{supervisor.StaffCode} does not hold {approval.Permission}",
                ct: ct);

            return Result.Failure<ApprovalRequestDto>(NotPermitted.With("permission", approval.Permission));
        }

        return await ApproveAsync(approval, supervisor.Id, ct);
    }

    public async Task<Result<ApprovalRequestDto>> Handle(ApproveSupervisorRequestCommand request, CancellationToken ct)
    {
        if (_currentUser.StaffId is not { } approverId)
        {
            return Result.Failure<ApprovalRequestDto>(NoStaffContext);
        }

        var approval = await _db.SupervisorApprovals.FirstOrDefaultAsync(a => a.Id == request.ApprovalId, ct);
        if (approval is null)
        {
            return Result.Failure<ApprovalRequestDto>(NotFound.With("approvalId", request.ApprovalId));
        }

        if (!_currentUser.HasPermission(approval.Permission))
        {
            return Result.Failure<ApprovalRequestDto>(NotPermitted.With("permission", approval.Permission));
        }

        return await ApproveAsync(approval, approverId, ct);
    }

    public async Task<Result<ApprovalRequestDto>> Handle(DenySupervisorRequestCommand request, CancellationToken ct)
    {
        var approval = await _db.SupervisorApprovals.FirstOrDefaultAsync(a => a.Id == request.ApprovalId, ct);
        if (approval is null)
        {
            return Result.Failure<ApprovalRequestDto>(NotFound.With("approvalId", request.ApprovalId));
        }

        var denied = approval.Deny(_currentUser.StaffId ?? 0L, request.Reason, _clock.Now);
        if (denied.IsFailure)
        {
            return Result.Failure<ApprovalRequestDto>(denied.Error);
        }

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            AuditAction.StepUpDenied,
            nameof(SupervisorApproval),
            approval.Id.ToString(CultureInfo.InvariantCulture),
            approval.Action,
            approverStaffId: _currentUser.StaffId,
            reason: request.Reason,
            ct: ct);

        return Result.Success(await ToDtoAsync(approval, ct));
    }

    public async Task<IReadOnlyList<ApprovalRequestDto>> Handle(ListPendingApprovalsQuery request, CancellationToken ct)
    {
        var now = _clock.Now;

        var approvals = await _db.SupervisorApprovals
            .Where(a => a.LocationId == request.LocationId
                        && a.Status == ApprovalStatus.Pending
                        && a.ExpiresAt > now)
            .OrderBy(a => a.RequestedAt)
            .ToListAsync(ct);

        var dtos = new List<ApprovalRequestDto>(approvals.Count);
        foreach (var approval in approvals)
        {
            dtos.Add(await ToDtoAsync(approval, ct));
        }

        return dtos;
    }

    private async Task<Result<ApprovalRequestDto>> ApproveAsync(SupervisorApproval approval, long approverId, CancellationToken ct)
    {
        var approved = approval.Approve(approverId, _clock.Now);
        if (approved.IsFailure)
        {
            return Result.Failure<ApprovalRequestDto>(approved.Error);
        }

        await _db.SaveChangesAsync(ct);

        // Both people on one row: the legacy prompt recorded neither, so afterwards nobody could say
        // who had authorised what.
        await _audit.RecordAsync(
            AuditAction.StepUpApproved,
            nameof(SupervisorApproval),
            approval.Id.ToString(CultureInfo.InvariantCulture),
            approval.Action,
            approverStaffId: approverId,
            reason: approval.Context,
            ct: ct);

        return Result.Success(await ToDtoAsync(approval, ct));
    }

    private async Task<ApprovalRequestDto> ToDtoAsync(SupervisorApproval approval, CancellationToken ct)
    {
        var name = await _db.StaffProfiles.AsNoTracking()
            .Where(s => s.Id == approval.RequestedByStaffId)
            .Select(s => s.FullName)
            .FirstOrDefaultAsync(ct);

        return new ApprovalRequestDto(
            approval.Id,
            approval.Permission,
            approval.Action,
            approval.Context,
            approval.RequestedByStaffId,
            name ?? string.Empty,
            approval.StationId,
            approval.Status,
            approval.RequestedAt,
            approval.ExpiresAt);
    }
}
