using Retail25.Domain.Catalog;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>Which of the two taxes a line attracts, after every override has been applied.</summary>
public sealed record TaxFlags(bool Tax1Applies, bool Tax2Applies);

/// <summary>
/// Resolves the per-line tax flags in the documented precedence: sale-level override → line
/// override → product flag ∧ policy ∧ customer exemption (doc 04 §3).
/// <para>
/// Two legacy behaviours are load-bearing here. The per-sale override is <b>not retroactive</b> —
/// it only reaches lines whose sequence is at or after the sequence it was stamped with
/// (guide p.11) — and an override of any kind is rejected outright when the store has turned
/// <c>AllowTaxOverride</c> off.
/// </para>
/// </summary>
public static class TaxFlagResolver
{
    public static TaxFlags Resolve(LineInput line, PricingContext context)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        // A gift card is a stored-value instrument, not merchandise: tax is charged when it is
        // spent, never when it is sold (guide p.106).
        if (line.Product.Type == ProductType.GiftCard)
        {
            return new TaxFlags(false, false);
        }

        var baseline1 = context.Tax.Tax1Enabled
            && context.Policy.ApplyTax1
            && line.Product.Tax1Applies
            && context.Customer?.ExemptTax1 != true;

        var baseline2 = context.Tax.Tax2Enabled
            && context.Policy.ApplyTax2
            && line.Product.Tax2Applies
            && context.Customer?.ExemptTax2 != true;

        if (!context.Policy.AllowTaxOverride)
        {
            return new TaxFlags(baseline1, baseline2);
        }

        var saleOverride = AppliesToLine(context.SaleOverride, line.Sequence) ? context.SaleOverride : null;

        var tax1 = saleOverride?.Tax1 ?? line.Tax1Override ?? baseline1;
        var tax2 = saleOverride?.Tax2 ?? line.Tax2Override ?? baseline2;

        // An override can exempt a line, but it can never invent a tax the location has not enabled.
        return new TaxFlags(tax1 && context.Tax.Tax1Enabled, tax2 && context.Tax.Tax2Enabled);
    }

    private static bool AppliesToLine(CartTaxOverride? saleOverride, int lineSequence)
        => saleOverride is not null && lineSequence >= saleOverride.AppliesFromSequence;
}
