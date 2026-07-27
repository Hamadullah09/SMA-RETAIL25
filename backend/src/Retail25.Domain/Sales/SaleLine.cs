using Retail25.Domain.Common;

namespace Retail25.Domain.Sales;

/// <summary>
/// Immutable snapshot of a cart line at sale time. Never updated after creation.
/// Contains all values needed to reprint the receipt identically (guide p.56).
/// </summary>
public sealed class SaleLine : Entity
{
    public SaleLine()
    {
    }

    public Guid TransactionId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public string? StockCodeSnapshot { get; set; }

    public string? NameSnapshot { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountPct { get; set; }

    /// <summary>Net amount after discount: unitPrice * quantity * (1 - discount/100).</summary>
    public decimal ExtendedNet { get; set; }

    public decimal Tax1Amount { get; set; }

    public decimal Tax2Amount { get; set; }

    /// <summary>AvgCost at the time of sale for COGS reporting (guide p.14).</summary>
    public decimal UnitCostSnapshot { get; set; }

    public PriceOrigin PriceOrigin { get; set; }

    public LineType LineType { get; set; }
}
