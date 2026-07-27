using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;
using Retail25.Domain.Sales;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// Context for the pricing engine. Carries all configuration needed for pure calculations
/// without I/O. The business date, tax config, policy and customer profile are snapshots
/// captured at the start of the cart quote (doc 04 §1).
/// </summary>
public sealed record PricingContext(
    DateOnly BusinessDate,
    TaxConfiguration Tax,
    PosPolicy Policy,
    CustomerPricingProfile? Customer,
    CartTaxOverride? SaleOverride,
    LoyaltyPolicy Loyalty);
