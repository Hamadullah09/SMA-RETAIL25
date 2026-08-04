using Retail25.Domain.Common;

namespace Retail25.Domain.Purchasing;

/// <summary>
/// A line on a purchase order (guide p.66–67). Supports split-case ordering.
/// </summary>
public sealed class PurchaseOrderLine : Entity, IAuditable
{
    public PurchaseOrderLine()
    {
    }

    public long PurchaseOrderId { get; set; }

    public long ProductId { get; set; }

    public long? VariantId { get; set; }

    /// <summary>Order quantity in cases if CaseQty > 1; split cases allowed (guide p.66).</summary>
    public decimal OrderQty { get; set; }

    public decimal CaseQty { get; set; }

    public decimal CostEach { get; set; }

    public decimal OrderCost { get; set; }

    public decimal QtyReceived { get; set; }

    /// <summary>Snapshot at PO generation time for the review grid (guide p.66).</summary>
    public decimal InStockAtGeneration { get; set; }

    public decimal OnOrderAtGeneration { get; set; }

    public decimal BackOrders { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }
}
