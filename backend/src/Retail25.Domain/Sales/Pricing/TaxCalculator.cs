using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// Tax calculation result for a single line (doc 04 §3, §4).
/// </summary>
public sealed record TaxLineResult(
    decimal Tax1Amount,
    decimal Tax2Amount,
    decimal TaxableBase1,
    decimal TaxableBase2);

/// <summary>
/// Pure tax calculator implementing the exact legacy pipeline (doc 04 §4).
/// Tax-inclusive mode backs out tax from gross prices.
/// Tax2 compound means Tax2 is calculated on (TaxableBase + Tax1).
/// </summary>
public static class TaxCalculator
{
    /// <summary>
    /// Calculates tax for a single line given the resolved unit price, quantity, discount
    /// and the effective tax configuration.
    /// </summary>
    public static TaxLineResult CalculateLine(
        decimal unitPrice,
        decimal quantity,
        decimal discountPct,
        bool tax1Applies,
        bool tax2Applies,
        TaxConfiguration tax)
    {
        var grossAmount = unitPrice * quantity;
        var discountAmount = grossAmount * discountPct / 100m;
        var netAmount = grossAmount - discountAmount;

        decimal tax1 = 0m;
        decimal tax2 = 0m;
        decimal taxableBase1 = 0m;
        decimal taxableBase2 = 0m;

        if (tax.TaxationType == TaxationType.Inclusive)
        {
            // Tax-inclusive: prices already contain tax (doc 04 §4).
            // Back-solve: netOfTax = gross / (1 + r1 + r2) or gross / ((1 + r1) * (1 + r2))
            var r1 = tax1Applies ? tax.Tax1Rate.Rate : 0m;
            var r2 = tax2Applies ? tax.Tax2Rate.Rate : 0m;

            decimal divisor;
            if (tax.Tax2Compound)
            {
                divisor = (1m + r1) * (1m + r2);
            }
            else
            {
                divisor = 1m + r1 + r2;
            }

            if (divisor > 0m)
            {
                var netOfTax = netAmount / divisor;
                tax1 = tax1Applies ? RoundTax(netOfTax * r1) : 0m;
                tax2 = tax2Applies
                    ? RoundTax(tax.Tax2Compound ? (netOfTax + tax1) * r2 : netOfTax * r2)
                    : 0m;
                taxableBase1 = tax1Applies ? netOfTax : 0m;
                taxableBase2 = tax2Applies ? (tax.Tax2Compound ? netOfTax + tax1 : netOfTax) : 0m;
            }
        }
        else
        {
            // Tax-exclusive: tax is added on top (doc 04 §4).
            taxableBase1 = tax1Applies ? netAmount : 0m;
            tax1 = tax1Applies ? RoundTax(netAmount * tax.Tax1Rate.Rate) : 0m;

            taxableBase2 = tax2Applies ? netAmount : 0m;
            if (tax.Tax2Compound && tax2Applies)
            {
                taxableBase2 = netAmount + tax1;
                tax2 = RoundTax(taxableBase2 * tax.Tax2Rate.Rate);
            }
            else if (tax2Applies)
            {
                tax2 = RoundTax(netAmount * tax.Tax2Rate.Rate);
            }
        }

        return new TaxLineResult(tax1, tax2, taxableBase1, taxableBase2);
    }

    /// <summary>
    /// Rounds a tax amount using AwayFromZero at 2dp (retail convention, doc 04 §4).
    /// Applied once per line per tax, never on the running total.
    /// </summary>
    private static decimal RoundTax(decimal amount)
        => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
