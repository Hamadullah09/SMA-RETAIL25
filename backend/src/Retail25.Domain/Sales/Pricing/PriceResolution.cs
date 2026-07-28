namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// Result of unit-price resolution for a single line (doc 04 §2).
/// <para>
/// <see cref="Origin"/> is persisted on the sale line so any receipt can be explained months later —
/// "why was this 4.20?" is answerable without re-running the engine against configuration that may
/// since have changed.
/// </para>
/// </summary>
/// <param name="UnitPrice">The price each chargeable unit is sold at.</param>
/// <param name="Origin">Which rule in the ladder produced the price.</param>
/// <param name="ChargeableQuantity">Units actually charged for.</param>
/// <param name="FreeQuantity">Units given away by bonus pricing. These still leave stock.</param>
/// <param name="ResolvedPriceLevel">The price level used, when the price came from one.</param>
public sealed record PriceResolution(
    decimal UnitPrice,
    PriceOrigin Origin,
    decimal ChargeableQuantity,
    decimal FreeQuantity,
    int? ResolvedPriceLevel)
{
    /// <summary>Units removed from stock: everything charged for plus everything given away.</summary>
    public decimal StockQuantity => ChargeableQuantity + FreeQuantity;
}
