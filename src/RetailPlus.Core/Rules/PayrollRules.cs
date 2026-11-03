using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace RetailPlus.Core.Rules;

/// <summary>One income tax slab: above <see cref="IncomeFrom"/>, <see cref="FixedTax"/> plus <see cref="Rate"/> percent of the excess.</summary>
public sealed record TaxSlab(decimal IncomeFrom, decimal? IncomeTo, decimal FixedTax, decimal Rate);

/// <summary>A month's income tax by the annualised projection, with what it was worked out from.</summary>
public sealed record MonthlyTax(decimal Tax, decimal ProjectedAnnualTaxable, decimal AnnualTax);

/// <summary>
/// The arithmetic of payroll that does not depend on a country: tax by slabs, the projection method spreading the year's
/// tax over the months still to pay, tax years, service, gratuity years, and the format checks of identity numbers and
/// IBANs (the patterns themselves come from the country's rule set).
/// </summary>
public static class PayrollRules
{
    /// <summary>
    /// The tax on a year's taxable income: the slab with the highest start below the income gives its fixed tax plus its
    /// rate on the excess; above <paramref name="surchargeThreshold"/> the tax carries <paramref name="surchargeRate"/>
    /// percent on top.
    /// </summary>
    public static decimal AnnualTax(decimal taxable, IEnumerable<TaxSlab> slabs, decimal surchargeThreshold = 0, decimal surchargeRate = 0)
    {
        if (taxable <= 0) return 0;
        var slab = slabs.Where(s => taxable > s.IncomeFrom).OrderByDescending(s => s.IncomeFrom).FirstOrDefault();
        if (slab is null) return 0;
        decimal tax = slab.FixedTax + (taxable - slab.IncomeFrom) * slab.Rate / 100m;
        if (surchargeThreshold > 0 && taxable > surchargeThreshold) tax += tax * surchargeRate / 100m;
        return Money.Round(tax);
    }

    /// <summary>
    /// The annualised projection method: the year's taxable income is what was paid before (earlier payslips and a
    /// previous employer), this payslip, and a regular month for every month still to come; the tax on that, less the tax
    /// already deducted, is spread evenly over this month and the months still to come.
    /// </summary>
    public static MonthlyTax ProjectedMonthlyTax(decimal taxableBefore, decimal taxBefore, decimal taxableNow, decimal regularMonthly, int monthsAfter,
        IReadOnlyList<TaxSlab> slabs, decimal surchargeThreshold = 0, decimal surchargeRate = 0)
    {
        monthsAfter = Math.Max(0, monthsAfter);
        decimal projected = taxableBefore + taxableNow + Math.Max(0, regularMonthly) * monthsAfter;
        decimal annual = AnnualTax(projected, slabs, surchargeThreshold, surchargeRate);
        decimal tax = Money.Round(Math.Max(0, (annual - taxBefore) / (monthsAfter + 1)));
        return new MonthlyTax(tax, Money.Round(projected), annual);
    }

    /// <summary>The first day of the tax year holding <paramref name="day"/>.</summary>
    public static DateOnly TaxYearStart(DateOnly day, int startMonth) =>
        new(day.Month >= startMonth ? day.Year : day.Year - 1, startMonth, 1);

    /// <summary>The tax year as people name it: 2026-27 for a year from July 2026, or 2026 for a calendar year.</summary>
    public static string TaxYearName(DateOnly start) =>
        start.Month == 1 ? start.Year.ToString(CultureInfo.InvariantCulture) : $"{start.Year}-{(start.Year + 1) % 100:00}";

    /// <summary>
    /// The months of the tax year still to be paid after the month of <paramref name="periodEnd"/>: none for someone who
    /// leaves in this month or before the year ends (up to their leaving month).
    /// </summary>
    public static int MonthsAfter(DateOnly periodEnd, int taxYearStartMonth, DateOnly? leavingDate)
    {
        var yearEnd = TaxYearStart(periodEnd, taxYearStartMonth).AddYears(1).AddDays(-1);
        var last = leavingDate is { } left && left < yearEnd ? left : yearEnd;
        int months = (last.Year - periodEnd.Year) * 12 + last.Month - periodEnd.Month;
        return Math.Max(0, months);
    }

    /// <summary>Whole months from one day to another (1 March to 31 March is one month; to 30 March is none).</summary>
    public static int CompletedMonths(DateOnly from, DateOnly to)
    {
        if (to < from) return 0;
        var end = to.AddDays(1);
        int months = (end.Year - from.Year) * 12 + end.Month - from.Month;
        if (end.Day < from.Day) months--;
        return Math.Max(0, months);
    }

    /// <summary>
    /// The years of service gratuity is paid for: none before <paramref name="minimumMonths"/>, else the completed years and
    /// one more for a part year longer than <paramref name="partYearMonths"/> months.
    /// </summary>
    public static int GratuityYears(DateOnly joined, DateOnly left, int minimumMonths, int partYearMonths)
    {
        int months = CompletedMonths(joined, left);
        if (months < minimumMonths) return 0;
        return months / 12 + (months % 12 > partYearMonths ? 1 : 0);
    }

    // Formats -----------------------------------------------------------------------------------------------------------

    /// <summary>True when <paramref name="value"/> matches the rule set's pattern (no pattern accepts everything).</summary>
    public static bool Matches(string value, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>True when the pattern is a regular expression that can be used.</summary>
    public static bool IsValidPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>An IBAN as it is stored: capitals, no spaces.</summary>
    public static string NormalizeIban(string iban) => new([.. iban.Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant)]);

    /// <summary>The IBAN check digits (ISO 13616, mod 97), the same in every country.</summary>
    public static bool IbanChecksumValid(string iban)
    {
        var text = NormalizeIban(iban);
        if (text.Length < 15 || text.Length > 34 || !text.All(char.IsAsciiLetterOrDigit)) return false;
        var rearranged = text[4..] + text[..4];
        var digits = new System.Text.StringBuilder();
        foreach (var c in rearranged)
            digits.Append(char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString(CultureInfo.InvariantCulture));
        return BigInteger.Parse(digits.ToString(), CultureInfo.InvariantCulture) % 97 == 1;
    }
}
