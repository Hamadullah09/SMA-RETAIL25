using Retail25.Domain.Catalog;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// One line presented to the pricing pipeline (doc 04 §1). Everything the resolver may consult is
/// carried on the record, so the engine never reaches for a repository.
/// </summary>
/// <param name="LineId">Correlates the result back to the cart line it came from.</param>
/// <param name="Sequence">Position in the cart; drives the non-retroactive tax override (doc 04 §3).</param>
/// <param name="ManualUnitPrice">Staff price override from the item-detail window (guide p.6).</param>
/// <param name="EmbeddedPrice">Net price read out of a Type 2 random-weight barcode (guide p.98).</param>
public sealed record LineInput(
    long LineId,
    int Sequence,
    Product Product,
    ProductVariant? Variant,
    decimal Quantity,
    decimal? ManualUnitPrice,
    decimal? ManualDiscountPct,
    int? RequestedPriceLevel,
    bool? Tax1Override,
    bool? Tax2Override,
    LineType Type,
    LineSource Source,
    decimal? EmbeddedPrice = null,
    IReadOnlyList<ProductPrice>? Prices = null,
    IReadOnlyList<PriceBreak>? Breaks = null,
    BonusPricing? Bonus = null,
    SalePricing? Sale = null,
    decimal UnitCost = 0m)
{
    public IReadOnlyList<ProductPrice> PriceLevels { get; } = Prices ?? [];

    public IReadOnlyList<PriceBreak> BreakPoints { get; } = Breaks ?? [];

    /// <summary>Returns and trade-ins reverse the sign of the line net and of its taxes.</summary>
    public bool IsCredit => Type is LineType.Return or LineType.TradeIn;
}

/// <summary>
/// A cart-level adjustment presented to the pipeline (guide p.7). Gift certificates are excluded
/// here on purpose: they are a tender, not a discount (doc 04 §4 step 3f).
/// </summary>
public sealed record AdjustmentInput(AdjustmentType Type, string Label, decimal Amount, decimal Percent);
