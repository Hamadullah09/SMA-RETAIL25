using Retail25.Domain.Common;

namespace Retail25.Domain.Security;

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Denied = 2,
    Expired = 3,
    Consumed = 4,
}

/// <summary>
/// A supervisor override, in the modern form of the legacy supervisor password (guide p.11, p.82,
/// doc 07 §Step-up).
/// <para>
/// The legacy prompt had two problems: the supervisor had to walk to the till, and nothing recorded
/// who had authorised what. This fixes both. A cashier can take a supervisor PIN inline, or any
/// supervisor can approve from any station because the request is broadcast — and either way the
/// grant is single-use, short-lived, scoped to one action, and names both people.
/// </para>
/// <para>
/// Short-lived and single-use together are what stop a grant becoming a standing privilege: an
/// approval that could be replayed is just a shared password with extra steps.
/// </para>
/// </summary>
public sealed class SupervisorApproval : Entity
{
    public static readonly Error NotPending = new("approval.not_pending", "That approval request has already been answered.");
    public static readonly Error Expired = new("approval.expired", "That approval request has expired. Ask again.");
    public static readonly Error AlreadyUsed = new("approval.already_used", "That approval has already been used.");
    public static readonly Error WrongAction = new("approval.wrong_action", "That approval was granted for a different action.");
    public static readonly Error SelfApproval = new("approval.self_approval", "A supervisor cannot approve their own request.");

    /// <summary>Two minutes: long enough to fetch someone, short enough that a granted override cannot be banked.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public SupervisorApproval()
    {
    }

    /// <summary>The permission being stepped up to, e.g. <c>pos.void_sale</c>.</summary>
    public string Permission { get; set; } = string.Empty;

    /// <summary>The request type the grant is good for. A grant for one action never unlocks another.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Human-readable context shown to the approver: "Void sale #1042, $55.99".</summary>
    public string? Context { get; set; }

    public long RequestedByStaffId { get; set; }

    public long StationId { get; set; }

    public long LocationId { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    public long? ApprovedByStaffId { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AnsweredAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public string? DenialReason { get; set; }

    public bool IsUsable(DateTimeOffset now)
        => Status == ApprovalStatus.Approved && now <= ExpiresAt;

    public static SupervisorApproval Request(
        string permission,
        string action,
        string? context,
        long requestedByStaffId,
        long stationId,
        long locationId,
        DateTimeOffset now) => new()
        {
            Permission = permission,
            Action = action,
            Context = context,
            RequestedByStaffId = requestedByStaffId,
            StationId = stationId,
            LocationId = locationId,
            Status = ApprovalStatus.Pending,
            RequestedAt = now,
            ExpiresAt = now.Add(Lifetime),
        };

    public Result Approve(long approverStaffId, DateTimeOffset now)
    {
        if (Status != ApprovalStatus.Pending)
        {
            return Result.Failure(NotPending.With("status", Status.ToString()));
        }

        if (now > ExpiresAt)
        {
            Status = ApprovalStatus.Expired;
            return Result.Failure(Expired);
        }

        // The whole point is a second pair of eyes; approving your own request is one pair.
        if (approverStaffId == RequestedByStaffId)
        {
            return Result.Failure(SelfApproval);
        }

        Status = ApprovalStatus.Approved;
        ApprovedByStaffId = approverStaffId;
        AnsweredAt = now;
        return Result.Success();
    }

    public Result Deny(long approverStaffId, string? reason, DateTimeOffset now)
    {
        if (Status != ApprovalStatus.Pending)
        {
            return Result.Failure(NotPending.With("status", Status.ToString()));
        }

        Status = ApprovalStatus.Denied;
        ApprovedByStaffId = approverStaffId;
        DenialReason = reason;
        AnsweredAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Spends the grant. Single-use by construction: the second attempt fails, so a cashier cannot
    /// void one sale on an approval and then quietly void another.
    /// </summary>
    public Result Consume(string action, DateTimeOffset now)
    {
        if (Status == ApprovalStatus.Consumed)
        {
            return Result.Failure(AlreadyUsed);
        }

        if (Status != ApprovalStatus.Approved)
        {
            return Result.Failure(NotPending.With("status", Status.ToString()));
        }

        if (now > ExpiresAt)
        {
            Status = ApprovalStatus.Expired;
            return Result.Failure(Expired);
        }

        if (!string.Equals(Action, action, StringComparison.Ordinal))
        {
            return Result.Failure(WrongAction.With("granted", Action).With("attempted", action));
        }

        Status = ApprovalStatus.Consumed;
        ConsumedAt = now;
        return Result.Success();
    }
}
