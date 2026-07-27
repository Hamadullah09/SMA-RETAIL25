using Retail25.Domain.Common;

namespace Retail25.Domain.Inventory;

public enum StockCountStatus
{
    InProgress = 0,
    Posted = 1,
    Cancelled = 2,
}

/// <summary>
/// A stock-count session (guide p.22). Used for batch onhand adjustments from a CSV or
/// manual count. Variance report generated after posting.
/// </summary>
public sealed class StockCount : AggregateRoot, IAuditable
{
    private StockCount()
    {
    }

    public Guid LocationId { get; set; }

    public StockCountStatus Status { get; set; } = StockCountStatus.InProgress;

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
