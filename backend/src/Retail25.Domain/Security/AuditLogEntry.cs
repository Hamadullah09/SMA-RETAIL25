using Retail25.Domain.Common;

namespace Retail25.Domain.Security;

public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    /// <summary>A command ran — used for actions that are not a single entity write, like a void.</summary>
    Executed = 3,
    SignedIn = 4,
    SignedOut = 5,
    SignInFailed = 6,
    PermissionDenied = 7,
    StepUpRequested = 8,
    StepUpApproved = 9,
    StepUpDenied = 10,
}

/// <summary>
/// Who did what, when, from where — and what the values were before and after (doc 07 §Audit).
/// <para>
/// Append-only. Nothing in the application updates or deletes one of these, and in production the
/// role that owns the connection has UPDATE and DELETE revoked on the table, because an audit trail
/// an application can rewrite is not an audit trail.
/// </para>
/// <para>
/// <see cref="ApproverStaffId"/> is the reason this exists in its present shape: a legacy void asked
/// for a supervisor's password and recorded nothing, so afterwards nobody could say who authorised
/// it. Here both the actor and the approver are named on the same row.
/// </para>
/// </summary>
public sealed class AuditLogEntry : Entity
{
    public AuditLogEntry()
    {
    }

    public DateTimeOffset OccurredAt { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>The Identity user, if the actor was authenticated.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>The staff member, which is what a manager actually recognises.</summary>
    public Guid? ActorStaffId { get; set; }

    public string? ActorName { get; set; }

    public Guid? StationId { get; set; }

    public Guid? LocationId { get; set; }

    /// <summary>Recorded because "which till" and "which machine" are different questions after a theft.</summary>
    public string? IpAddress { get; set; }

    /// <summary>CLR type name of what changed, e.g. <c>Product</c>.</summary>
    public string EntityType { get; set; } = string.Empty;

    public string? EntityId { get; set; }

    /// <summary>The request that caused it, e.g. <c>VoidSaleCommand</c>, or the endpoint.</summary>
    public string? Operation { get; set; }

    /// <summary>Changed columns before the write, as JSONB. Null for creates.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>Changed columns after the write. Null for deletes.</summary>
    public string? AfterJson { get; set; }

    /// <summary>Ties every row produced by one request together, and to the logs and traces.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>The supervisor who approved a step-up, when one occurred.</summary>
    public Guid? ApproverStaffId { get; set; }

    public string? Reason { get; set; }

    public static AuditLogEntry For(
        AuditAction action,
        string entityType,
        DateTimeOffset occurredAt,
        string? entityId = null,
        string? operation = null) => new()
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Operation = operation,
            OccurredAt = occurredAt,
        };

    /// <summary>Stamps the actor and request context onto a row the interceptor built.</summary>
    public AuditLogEntry WithActor(
        Guid? userId,
        Guid? staffId,
        string? actorName,
        Guid? stationId,
        Guid? locationId,
        string? ipAddress,
        string? correlationId)
    {
        ActorUserId = userId;
        ActorStaffId = staffId;
        ActorName = actorName;
        StationId = stationId;
        LocationId = locationId;
        IpAddress = ipAddress;
        CorrelationId = correlationId;
        return this;
    }
}
