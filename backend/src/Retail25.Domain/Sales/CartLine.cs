using Retail25.Domain.Common;

namespace Retail25.Domain.Sales;

/// <summary>How the item got onto the line. Drives the badge shown on the POS list and the audit trail.</summary>
public enum LineSource
{
    Rfid = 0,
    Barcode = 1,
    StockCode = 2,
    Manual = 3,
    Unknown = 4,
    KitComponent = 5,
    RandomWeight = 6,
    Serial = 7,
    Variant = 8,
    TagAlong = 9,
}

/// <summary>
/// Which rung of the precedence ladder produced the unit price (doc 04 §2). Persisted so a
/// supervisor can answer "why did it ring at that price?" without re-running the engine.
/// </summary>
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
/// A line on the server-authoritative cart.
/// <para>
/// The line stores the cashier's <b>intent</b> — quantity, any override they typed, the level they
/// picked — alongside a snapshot of the last price the engine resolved. Intent is what gets
/// re-priced when a customer is attached mid-sale or a level changes; the snapshot is only what the
/// screen shows between quotes.
/// </para>
/// </summary>
public sealed class CartLine : Entity
{
    public CartLine()
    {
    }

    public long CartId { get; set; }

    public long ProductId { get; set; }

    public long? VariantId { get; set; }

    public long? SerializedUnitId { get; set; }

    /// <summary>The EPC that put this line on the cart, kept for the live feed and for the sale commit.</summary>
    public string? Epc { get; set; }

    public LineSource Source { get; set; }

    public decimal Quantity { get; set; } = 1m;

    // --- Cashier intent (re-priced on every quote) --------------------------------------------

    /// <summary>Price typed on the item-detail window (guide p.6). Null means "let the engine decide".</summary>
    public decimal? ManualUnitPrice { get; set; }

    public decimal? ManualDiscountPct { get; set; }

    /// <summary>Price level chosen with F5 (guide p.6, p.34).</summary>
    public int? RequestedPriceLevel { get; set; }

    /// <summary>F6 on the item detail. Null means "use the product flag and the store policy".</summary>
    public bool? Tax1Override { get; set; }

    /// <summary>F7 on the item detail.</summary>
    public bool? Tax2Override { get; set; }

    /// <summary>Net price read out of a Type 2 random-weight barcode (guide p.98).</summary>
    public decimal? EmbeddedPrice { get; set; }

    public LineType LineType { get; set; }

    /// <summary>For returns: whether the item goes back on the shelf (guide p.7).</summary>
    public bool ReturnToStock { get; set; } = true;

    /// <summary>Free-text note printed under the line when the store allows item-list editing (guide p.77).</summary>
    public string? Note { get; set; }

    /// <summary>Sequence within the cart. Drives the non-retroactive tax override (doc 04 §3).</summary>
    public int Sequence { get; set; }

    // --- Snapshot of the last quote ------------------------------------------------------------

    public decimal UnitPrice { get; set; }

    public PriceOrigin PriceOrigin { get; set; }

    public decimal LineDiscountPct { get; set; }

    public bool Tax1Applies { get; set; }

    public bool Tax2Applies { get; set; }

    public decimal ExtendedNet { get; set; }

    public decimal Tax1Amount { get; set; }

    public decimal Tax2Amount { get; set; }

    public string? StockCodeSnapshot { get; set; }

    public string? NameSnapshot { get; set; }

    public decimal UnitCostSnapshot { get; set; }

    /// <summary>Copies the engine's answer onto the line so the UI has something to render between quotes.</summary>
    public void ApplyQuote(
        decimal unitPrice,
        PriceOrigin origin,
        decimal discountPct,
        bool tax1Applies,
        bool tax2Applies,
        decimal extendedNet,
        decimal tax1Amount,
        decimal tax2Amount)
    {
        UnitPrice = unitPrice;
        PriceOrigin = origin;
        LineDiscountPct = discountPct;
        Tax1Applies = tax1Applies;
        Tax2Applies = tax2Applies;
        ExtendedNet = extendedNet;
        Tax1Amount = tax1Amount;
        Tax2Amount = tax2Amount;
    }
}
