using Retail25.Domain.Common;

namespace Retail25.Domain.Purchasing;

public enum PurchaseOrderStatus
{
    Draft = 0,
    Posted = 1,
    PartiallyReceived = 2,
    Received = 3,
    Closed = 4,
    Cancelled = 5,
}

public enum OrderQuantityStrategy
{
    Blank = 0,
    OneWeek = 1,
    TwoWeeks = 2,
    ReorderPointFixed = 3,
    ReorderPointToBase = 4,
    MonthlySales = 5,
}

/// <summary>
/// Purchase order (guide p.63–71). Created, edited, posted and received through the UI.
/// Posting updates OnOrder; receiving updates stock and AvgCost with landed-cost allocation.
/// </summary>
public sealed class PurchaseOrder : AggregateRoot, IAuditable
{
    public PurchaseOrder()
    {
    }

    public long PoNumber { get; set; }

    public long SupplierId { get; set; }

    public long LocationId { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    public OrderQuantityStrategy QuantityStrategy { get; set; }

    public string? HeaderText { get; set; }

    public DateOnly? PostedOn { get; set; }

    /// <summary>Default +30 days for A/P bill (guide p.71).</summary>
    public DateOnly? DueOn { get; set; }

    public decimal Total { get; set; }

    /// <summary>External reference when synced to accounting system.</summary>
    public string? AccountingBillRef { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }
}
