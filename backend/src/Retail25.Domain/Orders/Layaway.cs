using Retail25.Domain.Common;

namespace Retail25.Domain.Orders;

public enum LayawayStatus
{
    Open = 0,
    PaidInFull = 1,
    Cancelled = 2,
}

/// <summary>
/// A layaway (guide p.9) — merchandise set aside against a series of deposits, released to the
/// customer when the balance reaches zero. Every line's quantity reserves stock via
/// <see cref="Inventory.StockLevel.Committed"/> for as long as the layaway is open, the same way a
/// customer order does.
/// </summary>
public sealed class Layaway : AggregateRoot, IAuditable
{
    public Layaway()
    {
    }

    public long LayawayNumber { get; set; }

    public long CustomerId { get; set; }

    public long LocationId { get; set; }

    public LayawayStatus Status { get; set; } = LayawayStatus.Open;

    public decimal Total { get; set; }

    /// <summary>Derived from <see cref="LayawayPayment"/>, rebuildable by replay — the same discipline as an AR balance.</summary>
    public decimal AmountPaid { get; set; }

    public DateOnly CreatedOn { get; set; }

    public long StaffId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }
}

public sealed class LayawayLine : Entity
{
    public LayawayLine()
    {
    }

    public long LayawayId { get; set; }

    public long ProductId { get; set; }

    public long? VariantId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}

/// <summary>A deposit toward a layaway (guide p.9) — mirrors <c>InvoicePayment</c>'s shape.</summary>
public sealed class LayawayPayment : Entity
{
    public LayawayPayment()
    {
    }

    public long LayawayId { get; set; }

    public decimal Amount { get; set; }

    public long TenderTypeId { get; set; }

    public DateOnly PaidOn { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
