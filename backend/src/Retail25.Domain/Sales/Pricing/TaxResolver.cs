using Retail25.Domain.Catalog;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>Whether each of the two sales taxes applies to a line, and why.</summary>
/// <param name="Tax1Applies">Tax 1 is charged on this line.</param>
/// <param name="Tax2Applies">Tax 2 is charged on this line.</param>
/// <param name="Tax1Source">Which rule decided tax 1.</param>
/// <param name="Tax2Source">Which rule decided tax 2.</param>
public sealed record LineTaxFlags(
    bool Tax1Applies,
    bool Tax2Applies,
    TaxDecisionSource Tax1Source,
    TaxDecisionSource Tax2Source);

/// <summary>Why a line ended up taxed or untaxed. Stored so a tax audit can be answered.</summary>
public enum TaxDecisionSource
{
    /// <summary>The tax is not configured for this location and date.</summary>
    NotConfigured = 0,

    /// <summary>Store policy has the tax switched off by default.</summary>
    PolicyDefault = 1,

    /// <summary>The item's own taxable flag.</summary>
    ProductFlag = 2,

    /// <summary>The customer holds an exemption.</summary>
    CustomerExemption = 3,

    /// <summary>Overridden for this line at the item-detail window (F6/F7).</summary>
    LineOverride = 4,

    /// <summary>Suspended or applied for the remainder of this sale (F11 → F6 Taxes).</summary>
    SaleOverride = 5,

    /// <summary>Gift cards are never taxed at issue; tax is charged when the card is spent.</summary>
    GiftCardExempt = 6,
}

/// <summary>
/// Decides tax applicability per line (doc 04 §3).
/// <para>
/// Precedence: sale-level override → line override → (policy default ∧ product flag ∧ customer
/// exemption). Two legacy behaviours are load-bearing and implemented literally:
/// </para>
/// <list type="number">
///   <item>
///     A per-sale override is <b>not retroactive</b>. It carries the cart sequence it was raised at,
///     and lines already on the screen keep the flags they were rung up with (guide p.11).
///   </item>
///   <item>
///     Overrides are honoured only when the store policy allows them <i>and</i> the operator holds
///     the permission. Otherwise the request is ignored rather than rejected, matching a till where
///     the key simply does nothing (guide p.77).
///   </item>
/// </list>
/// </summary>
public static class TaxResolver
{
    public static LineTaxFlags Resolve(LineInput input, PricingContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        var overridesAllowed = context.Policy.AllowTaxOverride && context.Permissions.CanOverrideTax;

        var (tax1, source1) = ResolveOne(
            configured: context.Tax.Tax1Enabled,
            policyApplies: context.Policy.ApplyTax1,
            productApplies: input.Product.Tax1Applies,
            customerExempt: context.Customer?.ExemptTax1 ?? false,
            lineOverride: input.Tax1Override,
            saleOverride: SaleOverrideFor(context, input.Sequence)?.Tax1,
            isGiftCard: input.Product.Type == ProductType.GiftCard,
            overridesAllowed: overridesAllowed);

        var (tax2, source2) = ResolveOne(
            configured: context.Tax.Tax2Enabled,
            policyApplies: context.Policy.ApplyTax2,
            productApplies: input.Product.Tax2Applies,
            customerExempt: context.Customer?.ExemptTax2 ?? false,
            lineOverride: input.Tax2Override,
            saleOverride: SaleOverrideFor(context, input.Sequence)?.Tax2,
            isGiftCard: input.Product.Type == ProductType.GiftCard,
            overridesAllowed: overridesAllowed);

        return new LineTaxFlags(tax1, tax2, source1, source2);
    }

    /// <summary>
    /// Returns the sale-level override only if it was raised at or before this line — the
    /// non-retroactive rule.
    /// </summary>
    private static CartTaxOverride? SaleOverrideFor(PricingContext context, int lineSequence)
        => context.SaleOverride is { } o && lineSequence >= o.AppliesFromSequence ? o : null;

    private static (bool Applies, TaxDecisionSource Source) ResolveOne(
        bool configured,
        bool policyApplies,
        bool productApplies,
        bool customerExempt,
        bool? lineOverride,
        bool? saleOverride,
        bool isGiftCard,
        bool overridesAllowed)
    {
        // A tax that does not exist for this location and date cannot be conjured by an override.
        if (!configured)
        {
            return (false, TaxDecisionSource.NotConfigured);
        }

        // Gift cards are sold untaxed; the tax lands when the card is spent (guide p.106).
        // This is a hard gate — no override re-enables it.
        if (isGiftCard)
        {
            return (false, TaxDecisionSource.GiftCardExempt);
        }

        if (overridesAllowed)
        {
            if (saleOverride.HasValue)
            {
                return (saleOverride.Value, TaxDecisionSource.SaleOverride);
            }

            if (lineOverride.HasValue)
            {
                return (lineOverride.Value, TaxDecisionSource.LineOverride);
            }
        }

        if (!policyApplies)
        {
            return (false, TaxDecisionSource.PolicyDefault);
        }

        if (!productApplies)
        {
            return (false, TaxDecisionSource.ProductFlag);
        }

        if (customerExempt)
        {
            return (false, TaxDecisionSource.CustomerExemption);
        }

        return (true, TaxDecisionSource.ProductFlag);
    }
}
