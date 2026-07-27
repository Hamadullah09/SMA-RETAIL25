using Retail25.Domain.Common;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.Sales;

public enum LineSource
{
    Rfid = 0,
    Barcode = 1,
    StockCode = 2,
    Manual = 3,
    Unknown = 4,
    KitComponent = 5,
}

public enum PriceOrigin
{
    Regular = 0,
    Level2 = 1,
    Level3 = 2,
    Level4 = 3,
    Break = 4,
    Sale = 5,
    Bonus = 6,
    Manual = 7,
    RandomWeight = 8,
    ClientLevel = 9,
}

public enum LineType
{
    Sale = 0,
    Return = 1,
    TradeIn = 2,
}

/// <summary>
/// A single line in the cart. Pricing, tax flags and snapshot values are resolved at add-time
/// and stored on the line so the receipt is reproducible.
/// </summary>
public sealed class CartLine : Entity
{
    public CartLine()
    {
    }

    public Guid CartId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public Guid? SerializedUnitId { get; set; }

    public LineSource Source { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Resolved unit price at the time the line was added.</summary>
    public decimal UnitPrice { get; set; }

    public PriceOrigin PriceOrigin { get; set; }

    public decimal LineDiscountPct { get; set; }

    public bool Tax1Applies { get; set; }

    public bool Tax2Applies { get; set; }

    /// <summary>For returns: whether to restock the item (guide p.7).</summary>
    public bool ReturnToStock { get; set; } = true;

    public LineType LineType { get; set; }

    /// <summary>Sequence number for the non-retroactive tax override check.</summary>
    public int Sequence { get; set; }

    // --- Snapshot columns for reproducible receipts ---

    public string? StockCodeSnapshot { get; set; }

    public string? NameSnapshot { get; set; }

    public decimal UnitCostSnapshot { get; set; }
}
