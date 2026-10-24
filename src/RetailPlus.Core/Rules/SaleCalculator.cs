namespace RetailPlus.Core.Rules;

/// <summary>Tax setup used for one sale (the store default, or one of the out-of-state configurations).</summary>
public sealed record TaxSetup(string? Tax1Name, decimal Tax1Rate, string? Tax2Name, decimal Tax2Rate, bool Compound, bool Inclusive)
{
    public static readonly TaxSetup None = new(null, 0, null, 0, false, false);
}

/// <summary>A line as the calculator sees it.</summary>
public sealed record CalcLine(decimal LineTotal, bool Taxable1, bool Taxable2);

/// <summary>Totals of a sale.</summary>
public sealed record SaleTotals(
    decimal Subtotal,
    decimal SubtotalDiscount,
    decimal Tax1,
    decimal Tax2,
    decimal Total,
    IReadOnlyList<(decimal Tax1, decimal Tax2)> LineTaxes)
{
    public decimal NetBeforeTax => Subtotal - SubtotalDiscount;
}

/// <summary>
/// Sale totals: line totals, the subtotal discount (spread over the lines so taxes stay right), and two sales taxes,
/// optionally compound (Tax 1 included in the base of Tax 2) and optionally included in the prices (VAT style).
/// Client tax exemptions switch a tax off for the whole sale.
/// </summary>
public static class SaleCalculator
{
    public static SaleTotals Calculate(IReadOnlyList<CalcLine> lines, TaxSetup tax, decimal subtotalDiscountPercent = 0,
        bool exemptTax1 = false, bool exemptTax2 = false)
    {
        decimal subtotal = lines.Sum(l => l.LineTotal);
        decimal discount = Money.Round(subtotal * subtotalDiscountPercent / 100m);
        decimal factor = subtotal == 0 ? 1 : (subtotal - discount) / subtotal;
        decimal r1 = exemptTax1 ? 0 : tax.Tax1Rate / 100m;
        decimal r2 = exemptTax2 ? 0 : tax.Tax2Rate / 100m;

        decimal tax1 = 0, tax2 = 0;
        var perLine = new List<(decimal, decimal)>(lines.Count);
        foreach (var line in lines)
        {
            decimal amount = line.LineTotal * factor;
            decimal lr1 = line.Taxable1 ? r1 : 0;
            decimal lr2 = line.Taxable2 ? r2 : 0;
            decimal t1, t2;
            if (tax.Inclusive)
            {
                // The price contains the taxes: split it into base + taxes.
                decimal divisor = 1 + lr1 + lr2 + (tax.Compound ? lr1 * lr2 : 0);
                decimal net = amount / divisor;
                t1 = net * lr1;
                t2 = (net + (tax.Compound ? t1 : 0)) * lr2;
            }
            else
            {
                t1 = amount * lr1;
                t2 = (amount + (tax.Compound ? t1 : 0)) * lr2;
            }
            tax1 += t1;
            tax2 += t2;
            perLine.Add((Money.Round(t1), Money.Round(t2)));
        }

        tax1 = Money.Round(tax1);
        tax2 = Money.Round(tax2);
        decimal total = tax.Inclusive ? subtotal - discount : subtotal - discount + tax1 + tax2;
        return new SaleTotals(subtotal, discount, tax1, tax2, Money.Round(total), perLine);
    }
}
