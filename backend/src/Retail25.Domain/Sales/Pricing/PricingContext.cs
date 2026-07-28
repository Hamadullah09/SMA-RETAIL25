using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// What the operator is permitted to do to a price. Supplied by the caller from the permission
/// catalogue; the engine enforces it rather than trusting the request (doc 04 §2, §3).
/// </summary>
/// <param name="CanSelectPriceLevel">May pick a different price level at the till (F5).</param>
/// <param name="CanDiscount">May enter a discount (legacy "Staff May Discount", guide p.77).</param>
/// <param name="CanOverrideTax">May flip a tax flag (F6/F7), subject to the policy allowing it.</param>
/// <param name="CanOverridePrice">May type a unit price over the resolved one.</param>
public sealed record PricingPermissions(
    bool CanSelectPriceLevel,
    bool CanDiscount,
    bool CanOverrideTax,
    bool CanOverridePrice)
{
    /// <summary>No discretion at all — every override request is ignored.</summary>
    public static readonly PricingPermissions None = new(false, false, false, false);

    /// <summary>Full discretion. Used by supervisor step-up and by tests.</summary>
    public static readonly PricingPermissions All = new(true, true, true, true);
}

/// <summary>
/// Everything the pricing engine needs, captured as a snapshot. The engine is a pure function of
/// this plus its line inputs: no I/O, no clock, no ambient configuration (doc 04 §1).
/// </summary>
/// <param name="BusinessDate">The location's business date — decides sale-pricing windows.</param>
/// <param name="Tax">The effective-dated tax configuration for that date.</param>
/// <param name="Policy">Store point-of-sale policy.</param>
/// <param name="Rounding">Currency-derived rounding rules.</param>
/// <param name="Customer">Attached customer's pricing profile, if any.</param>
/// <param name="SaleOverride">Per-sale tax suspension (F11 → F6 Taxes), non-retroactive.</param>
/// <param name="Loyalty">Bonus-points policy.</param>
/// <param name="Permissions">What the operator is allowed to override.</param>
public sealed record PricingContext(
    DateOnly BusinessDate,
    TaxConfiguration Tax,
    PosPolicy Policy,
    RoundingPolicy Rounding,
    CustomerPricingProfile? Customer,
    CartTaxOverride? SaleOverride,
    LoyaltyPolicy Loyalty,
    PricingPermissions Permissions);
