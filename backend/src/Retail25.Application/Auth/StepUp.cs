using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Common;
using Retail25.Domain.Security;

namespace Retail25.Application.Auth;

/// <summary>
/// A request that can carry a supervisor's grant.
/// <para>
/// Paired with <c>[SupportsSupervisorApproval]</c>. The attribute says a refusal should read "fetch a
/// supervisor"; this is how the retry, once one has approved, says "and here is what they approved".
/// </para>
/// </summary>
public interface ISupervisorApprovable
{
    /// <summary>The approval to spend, or null on a first attempt.</summary>
    long? ApprovalId { get; }
}

/// <summary>
/// Who stepped this request up, if anybody did. One per request scope.
/// <para>
/// Filled in by the authorisation behaviour when it spends a grant, read by the handler that needs to
/// record the approver — a void names the supervisor who allowed it. A handler never spends a grant
/// itself: the behaviour runs first, and spending it twice would fail the second time.
/// </para>
/// </summary>
public sealed class StepUpContext
{
    public long? ApprovedByStaffId { get; private set; }

    public void Record(long approvedByStaffId) => ApprovedByStaffId = approvedByStaffId;
}

/// <summary>Spends a supervisor's grant on behalf of the request that is about to run.</summary>
public interface IStepUpGrants
{
    /// <returns>The approving supervisor's staff id.</returns>
    Task<Result<long>> SpendAsync(long approvalId, string action, string permission, CancellationToken ct);
}

public sealed class StepUpGrants : IStepUpGrants
{
    public static readonly Error NotFound = new("approval.not_found", "No such approval request.");
    public static readonly Error NotYours = new("approval.not_yours", "That approval was asked for by somebody else.");

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IDateTime _clock;

    public StepUpGrants(IApplicationDbContext db, ICurrentUser currentUser, IAuditWriter audit, IDateTime clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<long>> SpendAsync(long approvalId, string action, string permission, CancellationToken ct)
    {
        var approval = await _db.SupervisorApprovals.FirstOrDefaultAsync(a => a.Id == approvalId, ct);
        if (approval is null)
        {
            return Result.Failure<long>(NotFound.With("approvalId", approvalId));
        }

        // A grant belongs to the person who asked for it. Without this, one cashier's approved
        // request is a token any colleague at any till can pick up and spend inside its two minutes.
        if (approval.RequestedByStaffId != _currentUser.StaffId)
        {
            return Result.Failure<long>(NotYours);
        }

        var spent = approval.Consume(action, permission, _clock.Now);

        // Saved either way: an expired grant is marked expired, so a second try does not re-evaluate
        // it as approved.
        await _db.SaveChangesAsync(ct);

        if (spent.IsFailure)
        {
            return Result.Failure<long>(spent.Error);
        }

        // The approval itself was recorded when it was given. This is the other half: the moment it
        // was used, and on what — so the log can say not only that a manager allowed something, but
        // that the thing they allowed actually happened.
        await _audit.RecordAsync(
            AuditAction.Executed,
            nameof(SupervisorApproval),
            approval.Id.ToString(CultureInfo.InvariantCulture),
            action,
            approverStaffId: approval.ApprovedByStaffId,
            reason: approval.Context,
            ct: ct);

        return Result.Success(approval.ApprovedByStaffId ?? 0L);
    }
}
