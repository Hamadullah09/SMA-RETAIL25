using Retail25.Domain.Catalog;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// Result of unit-price resolution for a single line (doc 04 §2).
/// Records the resolved price and its origin for receipt reproducibility.
/// </summary>
public sealed record PriceResolution(
    decimal UnitPrice,
    PriceOrigin Origin,
    decimal ChargeableQuantity,
    bool Tax1Applies,
    bool Tax2Applies,
    bool IsReturn);
