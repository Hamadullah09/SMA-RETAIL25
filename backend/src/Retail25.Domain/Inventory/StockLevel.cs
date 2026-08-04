using Retail25.Domain.Common;

namespace Retail25.Domain.Inventory;

/// <summary>
/// Per-product, per-variant, per-location stock snapshot (guide p.31). Derived from the stock
/// ledger and rebuilt by replay. Updated in the same transaction as the ledger write.
/// </summary>
public sealed class StockLevel : Entity
{
    public StockLevel()
    {
    }

    public long ProductId { get; set; }

    public long? VariantId { get; set; }

    public long LocationId { get; set; }

    public decimal OnHand { get; set; }

    public decimal OnOrder { get; set; }

    /// <summary>Quantity committed to customer orders and layaways.</summary>
    public decimal Committed { get; set; }

    public DateTimeOffset? LastSoldOn { get; set; }

    /// <summary>Available = OnHand - Committed.</summary>
    public decimal Available => OnHand - Committed;

    public static StockLevel Create(long productId, long? variantId, long locationId)
    {
        return new StockLevel
        {
            ProductId = productId,
            VariantId = variantId,
            LocationId = locationId,
        };
    }
}
