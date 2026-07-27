using Retail25.Domain.Common;

namespace Retail25.Domain.Purchasing;

/// <summary>
/// A shipment receipt against a PO (guide p.67–68). Multiple receipts per PO line are supported.
/// Freight is distributed across received items into AvgCost.
/// </summary>
public sealed class PurchaseOrderReceipt : Entity, IAuditable
{
    private PurchaseOrderReceipt()
    {
    }

    public Guid PurchaseOrderId { get; set; }

    public DateOnly ReceivedOn { get; set; }

    /// <summary>Freight/shipping cost distributed across received items (guide p.68).</summary>
    public decimal FreightTotal { get; set; }

    public Guid StaffId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
