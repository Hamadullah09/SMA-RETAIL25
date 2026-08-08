using Retail25.Domain.Common;

namespace Retail25.Domain.Sales;

/// <summary>
/// The server-authoritative cart. It lives in Redis with a Postgres write-behind, because RFID reads
/// arrive from a daemon rather than from the browser: a cart held in page state could never see a
/// bulk read, a second station, or a browser refresh.
/// </summary>
public sealed class Cart : AggregateRoot, IAuditable
{
    public static readonly Error NotActive = new("cart.not_active", "This cart is no longer active.");
    public static readonly Error Empty = new("cart.empty", "The cart has no lines.");
    public static readonly Error RevisionConflict = new("cart.revision_conflict", "The cart changed since you last read it. Resync and retry.");

    public Cart()
    {
    }

    public long StationId { get; set; }

    public long LocationId { get; set; }

    public long StaffId { get; set; }

    public long? CustomerId { get; set; }

    public CartStatus Status { get; set; } = CartStatus.Active;

    /// <summary>Label a cashier gives a suspended cart so it can be found again (guide p.11).</summary>
    public string? HeldName { get; set; }

    public DateTimeOffset? SuspendedAt { get; set; }

    public long? SuspendedByStaffId { get; set; }

    /// <summary>Next line sequence. Stamped onto a tax override to make it non-retroactive (doc 04 §3).</summary>
    public int NextLineSequence { get; set; } = 1;

    /// <summary>
    /// Monotonic revision carried on every hub message. A client that sees a gap calls
    /// <c>RequestCartResync</c> rather than quietly drifting out of step with the server.
    /// </summary>
    public int Revision { get; set; } = 1;

    /// <summary>Set when the sale completes, so the cart can be traced to the transaction it became.</summary>
    public long? CompletedTransactionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    /// <summary>Abandoned carts expire; suspended carts never do.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsActive => Status == CartStatus.Active;

    public static Cart Open(long stationId, long locationId, long staffId, DateTimeOffset now, int timeoutMinutes)
        => new()
        {
            StationId = stationId,
            LocationId = locationId,
            StaffId = staffId,
            Status = CartStatus.Active,
            NextLineSequence = 1,
            Revision = 1,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(timeoutMinutes),
        };

    /// <summary>Hands out the next line sequence.</summary>
    public int TakeNextSequence()
    {
        var sequence = NextLineSequence;
        NextLineSequence++;
        return sequence;
    }

    public int Touch(DateTimeOffset now, int timeoutMinutes)
    {
        Revision++;
        ModifiedAt = now;
        if (Status == CartStatus.Active)
        {
            ExpiresAt = now.AddMinutes(timeoutMinutes);
        }

        return Revision;
    }

    public Result Suspend(string? label, long staffId, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return Result.Failure(NotActive);
        }

        Status = CartStatus.Suspended;
        HeldName = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        SuspendedAt = now;
        SuspendedByStaffId = staffId;
        ExpiresAt = null;
        return Result.Success();
    }

    public Result Recall(long stationId, long staffId, DateTimeOffset now, int timeoutMinutes)
    {
        if (Status != CartStatus.Suspended)
        {
            return Result.Failure(new Error("cart.not_suspended", "Only a suspended cart can be recalled."));
        }

        Status = CartStatus.Active;
        StationId = stationId;
        StaffId = staffId;
        SuspendedAt = null;
        SuspendedByStaffId = null;
        ExpiresAt = now.AddMinutes(timeoutMinutes);
        return Result.Success();
    }

    public void Complete(long transactionId, DateTimeOffset now)
    {
        Status = CartStatus.Completed;
        CompletedTransactionId = transactionId;
        ModifiedAt = now;
        ExpiresAt = null;
    }

    public void Abandon(DateTimeOffset now)
    {
        Status = CartStatus.Voided;
        ModifiedAt = now;
        ExpiresAt = null;
    }
}
