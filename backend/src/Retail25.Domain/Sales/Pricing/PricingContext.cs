using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// Everything the pricing pipeline needs, captured as a snapshot (doc 04 §1). There is no clock and
/// no I/O below this type: <see cref="BusinessDate"/> is supplied by the caller, so replaying a
/// historical sale produces the historical answer.
/// </summary>
public sealed record PricingContext(
    DateOnly BusinessDate,
    TaxConfiguration Tax,
    PosPolicy Policy,
    CustomerPricingProfile? Customer,
    CartTaxOverride? SaleOverride,
    LoyaltyPolicy? Loyalty,
    IReadOnlyList<PricingRuleSetting> Rules,
    MoneyRounding Rounding)
{
    /// <summary>Rules in evaluation order, with disabled rows dropped.</summary>
    public IReadOnlyList<PricingRuleSetting> ActiveRules { get; } =
        (Rules ?? []).Where(rule => rule.Enabled).OrderBy(rule => rule.Order).ToList();
}
