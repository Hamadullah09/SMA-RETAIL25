using Retail25.Domain.Common;

namespace Retail25.Domain.Sales;

/// <summary>
/// An immutable snapshot of a cart line at the moment the sale was saved. It holds everything a
/// reprint needs — resolved price, why that price won, the tax basis and the two tax amounts — so a
/// receipt is reproduced rather than recalculated (guide p.56).
/// </summary>
public sealed class SaleLine : Entity
{
    public SaleLine()
    {
    }

    public Guid TransactionId { get; set; }

    public int Sequence { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public Guid? SerializedUnitId { get; set; }

    public string? Epc { get; set; }

    public string? SerialNumber { get; set; }

    public string? StockCodeSnapshot { get; set; }

    public string? NameSnapshot { get; set; }

    public LineSource Source { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Units actually charged. Lower than <see cref="Quantity"/> when bonus pricing gave some away.</summary>
    public decimal ChargeableQuantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountPct { get; set; }

    /// <summary>Net after the line discount, before any prorated sale-level adjustment.</summary>
    public decimal ExtendedNet { get; set; }

    /// <summary>This line's share of the sale-level discount, prorated by net contribution (doc 04 §4).</summary>
    public decimal ProratedAdjustment { get; set; }

    /// <summary>The amount tax was actually computed on.</summary>
    public decimal TaxableNet { get; set; }

    public bool Tax1Applies { get; set; }

    public bool Tax2Applies { get; set; }

    public decimal Tax1Amount { get; set; }

    public decimal Tax2Amount { get; set; }

    /// <summary>AvgCost at sale time, so COGS reports do not move when costs later change (guide p.14).</summary>
    public decimal UnitCostSnapshot { get; set; }

    public PriceOrigin PriceOrigin { get; set; }

    public LineType LineType { get; set; }

    public bool ReturnedToStock { get; set; }

    public string? Note { get; set; }
}

/// <summary>
/// A sale-level adjustment as it was applied (guide p.7). Frozen alongside the lines so the receipt
/// can print "Coupon SAVE10 −$10.00" exactly as the customer saw it.
/// </summary>
public sealed class SaleAdjustment : Entity
{
    public SaleAdjustment()
    {
    }

    public Guid TransactionId { get; set; }

    public AdjustmentType Type { get; set; }

    public string Label { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string? Serial { get; set; }
}
