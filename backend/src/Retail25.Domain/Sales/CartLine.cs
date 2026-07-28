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
/// A single line in the cart.
/// <para>
/// The line separates what the cashier <i>asked for</i> from what the engine <i>decided</i>.
/// The request fields — quantity, any typed price or discount, a chosen price level, tax key presses
/// — are the durable truth. The resolved fields below them are a cache of the last quote, refreshed
/// whenever the cart is re-priced.
/// </para>
/// <para>
/// This matters because pricing is contextual: attaching a customer, crossing a volume break or
/// suspending a tax must re-price lines that are already on the screen. Freezing a price at
/// add-time would silently keep the old one. Prices are frozen only once, onto
/// <see cref="SaleLine"/>, when the sale is committed.
/// </para>
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

    // --- What the cashier asked for -------------------------------------------------------

    /// <summary>A unit price typed by staff. Applied only if they hold the override permission.</summary>
    public decimal? ManualUnitPrice { get; set; }

    /// <summary>
    /// Net price read out of a Type 2 random-weight barcode. Kept apart from
    /// <see cref="ManualUnitPrice"/> because it derives a weight rather than replacing a price
    /// (guide p.98).
    /// </summary>
    public decimal? EmbeddedUnitPrice { get; set; }

    /// <summary>A discount typed by staff. Applied only if policy and permission allow it.</summary>
    public decimal? ManualDiscountPct { get; set; }

    /// <summary>Price level chosen at the till with F5.</summary>
    public int? RequestedPriceLevel { get; set; }

    /// <summary>Tax 1 forced on or off for this line with F6. Null means "no override".</summary>
    public bool? Tax1Override { get; set; }

    /// <summary>Tax 2 forced on or off for this line with F7. Null means "no override".</summary>
    public bool? Tax2Override { get; set; }

    // --- What the engine decided (cache of the latest quote) -------------------------------

    /// <summary>Unit price from the most recent quote.</summary>
    public decimal UnitPrice { get; set; }

    public PriceOrigin PriceOrigin { get; set; }

    /// <summary>Effective discount from the most recent quote, whether typed or from the customer.</summary>
    public decimal LineDiscountPct { get; set; }

    public bool Tax1Applies { get; set; }

    public bool Tax2Applies { get; set; }

    /// <summary>Units charged for after bonus pricing gave some away.</summary>
    public decimal ChargeableQuantity { get; set; }

    /// <summary>Units given away by bonus pricing. They still leave stock.</summary>
    public decimal FreeQuantity { get; set; }

    /// <summary>Line value after its own discount, signed for returns and trade-ins.</summary>
    public decimal NetAmount { get; set; }

    /// <summary>Tax 1 on this line from the most recent quote.</summary>
    public decimal Tax1Amount { get; set; }

    /// <summary>Tax 2 on this line from the most recent quote.</summary>
    public decimal Tax2Amount { get; set; }

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
