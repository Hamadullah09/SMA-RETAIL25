using Retail25.Domain.Common;

namespace Retail25.Domain.Inventory;

public enum TransferStatus
{
    Draft = 0,
    InTransit = 1,
    Received = 2,
    Cancelled = 3,
}

/// <summary>
/// Stock transfer between locations (guide p.20–21). Draft → InTransit → Received.
/// Replaces the legacy file-exchange FTP transfer mechanism.
/// </summary>
public sealed class StockTransfer : AggregateRoot, IAuditable
{
    private StockTransfer()
    {
    }

    public Guid FromLocationId { get; set; }

    public Guid ToLocationId { get; set; }

    public TransferStatus Status { get; set; } = TransferStatus.Draft;

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
