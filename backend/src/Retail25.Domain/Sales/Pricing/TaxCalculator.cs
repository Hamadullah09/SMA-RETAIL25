using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// Tax computed for one taxable amount.
/// </summary>
/// <param name="NetAmount">
/// The amount excluding tax. Equals the input in exclusive mode; in inclusive mode it is the
/// input with tax backed out.
/// </param>
/// <param name="Tax1Amount">Tax 1 charged.</param>
/// <param name="Tax2Amount">Tax 2 charged.</param>
/// <param name="TaxableBase1">The base tax 1 was charged on.</param>
/// <param name="TaxableBase2">The base tax 2 was charged on, including tax 1 when compounding.</param>
public sealed record TaxResult(
    decimal NetAmount,
    decimal Tax1Amount,
    decimal Tax2Amount,
    decimal TaxableBase1,
    decimal TaxableBase2)
{
    public static readonly TaxResult Zero = new(0m, 0m, 0m, 0m, 0m);

    public decimal TotalTax => Tax1Amount + Tax2Amount;

    /// <summary>What the customer pays for this amount, tax included.</summary>
    public decimal GrossAmount => NetAmount + Tax1Amount + Tax2Amount;
}

/// <summary>
/// The single place tax arithmetic happens (doc 04 §4). Every caller — line, add-on charge,
/// reprint — routes through here, so inclusive pricing and compounding cannot drift between them.
/// <para>
/// Rounding is applied <b>once per amount per tax</b> using the currency's configured rule, never
/// to a running total. Rounding a running total is the classic penny-drift defect and it is pinned
/// by tests.
/// </para>
/// </summary>
public static class TaxCalculator
{
    /// <summary>
    /// Computes tax for <paramref name="amount"/>.
    /// </summary>
    /// <param name="amount">
    /// The line or charge amount after discounts. In inclusive mode this is understood to contain
    /// tax already; in exclusive mode tax is added on top. May be negative for returns, in which
    /// case the tax is negative too.
    /// </param>
    /// <param name="tax1Applies">Whether tax 1 is charged, as decided by <see cref="TaxResolver"/>.</param>
    /// <param name="tax2Applies">Whether tax 2 is charged.</param>
    /// <param name="tax">The effective tax configuration.</param>
    /// <param name="rounding">Currency rounding rules.</param>
    public static TaxResult Calculate(
        decimal amount,
        bool tax1Applies,
        bool tax2Applies,
        TaxConfiguration tax,
        RoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(tax);
        ArgumentNullException.ThrowIfNull(rounding);

        var rate1 = tax1Applies && tax.Tax1Enabled ? tax.Tax1Rate.Rate : 0m;
        var rate2 = tax2Applies && tax.Tax2Enabled ? tax.Tax2Rate.Rate : 0m;

        if (rate1 == 0m && rate2 == 0m)
        {
            return new TaxResult(rounding.Round(amount), 0m, 0m, 0m, 0m);
        }

        return tax.TaxationType == TaxationType.Inclusive
            ? CalculateInclusive(amount, rate1, rate2, tax.Tax2Compound, rounding)
            : CalculateExclusive(amount, rate1, rate2, tax.Tax2Compound, rounding);
    }

    private static TaxResult CalculateExclusive(
        decimal amount,
        decimal rate1,
        decimal rate2,
        bool compound,
        RoundingPolicy rounding)
    {
        var net = rounding.Round(amount);

        var base1 = rate1 > 0m ? net : 0m;
        var tax1 = rounding.Round(base1 * rate1);

        // Where tax 2 compounds, tax 1 forms part of its base — unusual, but required in some
        // jurisdictions and supported by the legacy system (guide p.77).
        var base2 = rate2 > 0m ? (compound ? net + tax1 : net) : 0m;
        var tax2 = rounding.Round(base2 * rate2);

        return new TaxResult(net, tax1, tax2, base1, base2);
    }

    /// <summary>
    /// Back-solves tax out of a gross amount (doc 04 §4). The divisor differs depending on whether
    /// tax 2 compounds, because a compounding tax 2 is charged on a base that already contains tax 1.
    /// </summary>
    private static TaxResult CalculateInclusive(
        decimal gross,
        decimal rate1,
        decimal rate2,
        bool compound,
        RoundingPolicy rounding)
    {
        var divisor = compound
            ? (1m + rate1) * (1m + rate2)
            : 1m + rate1 + rate2;

        if (divisor <= 0m)
        {
            return new TaxResult(rounding.Round(gross), 0m, 0m, 0m, 0m);
        }

        var net = rounding.Round(gross / divisor);

        var base1 = rate1 > 0m ? net : 0m;
        var tax1 = rounding.Round(base1 * rate1);

        var base2 = rate2 > 0m ? (compound ? net + tax1 : net) : 0m;
        var tax2 = rounding.Round(base2 * rate2);

        return new TaxResult(net, tax1, tax2, base1, base2);
    }
}
