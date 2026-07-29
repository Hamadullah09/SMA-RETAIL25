using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// Tax on one taxable amount. <see cref="NetOfTax"/> differs from the amount handed in only under
/// tax-inclusive pricing, where the sticker price already contains the tax.
/// </summary>
public sealed record TaxAmounts(decimal Tax1, decimal Tax2, decimal NetOfTax);

/// <summary>
/// The arithmetic half of the tax engine (doc 04 §4). Given an amount that has already been
/// discounted and prorated, it produces the two tax figures.
/// <para>
/// Rounding happens exactly once per amount per tax, never on a running total — that is the classic
/// penny-drift bug, and it is pinned by the golden files.
/// </para>
/// </summary>
public static class TaxCalculator
{
    public static TaxAmounts Calculate(
        decimal amount,
        bool tax1Applies,
        bool tax2Applies,
        TaxConfiguration tax,
        MoneyRounding rounding)
    {
        ArgumentNullException.ThrowIfNull(tax);
        ArgumentNullException.ThrowIfNull(rounding);

        var rate1 = tax1Applies ? tax.Tax1Rate.Rate : 0m;
        var rate2 = tax2Applies ? tax.Tax2Rate.Rate : 0m;

        if (rate1 == 0m && rate2 == 0m)
        {
            return new TaxAmounts(0m, 0m, amount);
        }

        return tax.TaxationType == TaxationType.Inclusive
            ? Inclusive(amount, rate1, rate2, tax.Tax2Compound, rounding)
            : Exclusive(amount, rate1, rate2, tax.Tax2Compound, rounding);
    }

    /// <summary>Tax is added on top of the amount. The North American default (guide p.77).</summary>
    private static TaxAmounts Exclusive(decimal net, decimal rate1, decimal rate2, bool compound, MoneyRounding rounding)
    {
        var tax1 = rounding.Round(net * rate1);
        var base2 = compound ? net + tax1 : net;
        var tax2 = rounding.Round(base2 * rate2);

        return new TaxAmounts(tax1, tax2, net);
    }

    /// <summary>
    /// The sticker price already contains the tax, so the engine back-solves the net (guide p.77).
    /// A compound tax 2 divides by the product of the two rates rather than by their sum.
    /// </summary>
    private static TaxAmounts Inclusive(decimal gross, decimal rate1, decimal rate2, bool compound, MoneyRounding rounding)
    {
        var divisor = compound
            ? (1m + rate1) * (1m + rate2)
            : 1m + rate1 + rate2;

        if (divisor <= 0m)
        {
            return new TaxAmounts(0m, 0m, gross);
        }

        var net = gross / divisor;
        var tax1 = rounding.Round(net * rate1);
        var base2 = compound ? net + tax1 : net;
        var tax2 = rounding.Round(base2 * rate2);

        // The net absorbs the residue so that net + tax1 + tax2 reproduces the shelf price exactly.
        return new TaxAmounts(tax1, tax2, rounding.Round(gross) - tax1 - tax2);
    }
}
