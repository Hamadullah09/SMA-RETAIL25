using System.Globalization;

namespace RetailPlus.Core.Rules;

/// <summary>
/// The rules of a customer segment. Every rule is optional; a client is a member when they meet all the rules that are
/// set. Spend (what the client paid, returns taken off) and visits (completed sales) count the last
/// <see cref="PeriodDays"/> days, else <see cref="From"/> to <see cref="To"/>, else all time; with <see cref="StoreId"/>
/// only that store's sales count, for the last purchase too.
/// </summary>
public sealed record SegmentRules
{
    public int? PeriodDays { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public decimal? MinSpend { get; init; }
    public decimal? MaxSpend { get; init; }
    public int? MinVisits { get; init; }
    public int? MaxVisits { get; init; }
    /// <summary>Lapsed: the last purchase was before this day (a client who never bought counts as lapsed).</summary>
    public DateOnly? LastPurchaseBefore { get; init; }
    /// <summary>Recent: the last purchase was on or after this day.</summary>
    public DateOnly? LastPurchaseAfter { get; init; }
    /// <summary>Lapsed: nothing bought for this many days.</summary>
    public int? NoPurchaseForDays { get; init; }
    public Guid? StoreId { get; init; }
    /// <summary>Bought something of this department in the period.</summary>
    public Guid? DepartmentId { get; init; }
    public int? BirthdayMonth { get; init; }
    public decimal? MinPoints { get; init; }
    public decimal? MaxPoints { get; init; }
    /// <summary>A balance owing of at least this much.</summary>
    public decimal? MinBalance { get; init; }
    /// <summary>True: agreed to WhatsApp messages and has a number; false: has not.</summary>
    public bool? WhatsAppConsent { get; init; }

    /// <summary>True when spend, visits or a department are asked about, which need the sales of the period.</summary>
    public bool UsesPeriodSales => MinSpend is not null || MaxSpend is not null || MinVisits is not null || MaxVisits is not null || DepartmentId is not null;

    /// <summary>The first and last day of the period, given today; null ends for all time.</summary>
    public (DateOnly? From, DateOnly? To) Period(DateOnly today) =>
        PeriodDays is { } days && days > 0 ? (today.AddDays(1 - days), today) : (From, To);

    /// <summary>A problem with the rules for the person who wrote them, or null.</summary>
    public string? Problem()
    {
        if (PeriodDays is < 1 or > 3660) return "The period is 1 to 3660 days.";
        if (From is { } f && To is { } t && f > t) return "The period ends before it starts.";
        if (BirthdayMonth is < 1 or > 12) return "The birthday month is 1 to 12.";
        if (MinSpend is { } a && MaxSpend is { } b && a > b) return "The lowest spend is above the highest.";
        if (MinVisits is { } c && MaxVisits is { } d && c > d) return "The fewest visits are above the most.";
        if (MinVisits is < 0 || MaxVisits is < 0) return "Visits cannot be below zero.";
        if (MinPoints is { } e && MaxPoints is { } g && e > g) return "The lowest points are above the highest.";
        if (NoPurchaseForDays is < 1) return "Days without a purchase start at 1.";
        return null;
    }

    /// <summary>
    /// The rules in words: "spent at least 1,000 in the last 90 days, birthday in March, agreed to WhatsApp".
    /// <paramref name="names"/> names a store or department.
    /// </summary>
    public string Describe(Func<Guid, string?>? names = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        string N(decimal d) => d.ToString("#,0.##", culture);
        string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string period = PeriodDays is > 0 ? $"in the last {PeriodDays} days"
            : From is { } f && To is { } t ? $"from {D(f)} to {D(t)}" : From is { } f2 ? $"since {D(f2)}" : To is { } t2 ? $"up to {D(t2)}" : "ever";
        var parts = new List<string>();
        if (MinSpend is { } min && MaxSpend is { } max) parts.Add($"spent {N(min)} to {N(max)} {period}");
        else if (MinSpend is { } min2) parts.Add($"spent at least {N(min2)} {period}");
        else if (MaxSpend is { } max2) parts.Add($"spent at most {N(max2)} {period}");
        if (MinVisits is { } v1 && MaxVisits is { } v2) parts.Add($"{v1} to {v2} visits {period}");
        else if (MinVisits is { } v3) parts.Add($"at least {v3} visits {period}");
        else if (MaxVisits is { } v4) parts.Add($"at most {v4} visits {period}");
        if (DepartmentId is { } dept) parts.Add($"bought {names?.Invoke(dept) ?? "the department"} {period}");
        if (LastPurchaseBefore is { } before) parts.Add($"last bought before {D(before)}");
        if (LastPurchaseAfter is { } after) parts.Add($"bought since {D(after)}");
        if (NoPurchaseForDays is { } idle) parts.Add($"nothing bought for {idle} days");
        if (StoreId is { } store) parts.Add($"at {names?.Invoke(store) ?? "one store"}");
        if (BirthdayMonth is { } month) parts.Add("birthday in " + CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month));
        if (MinPoints is { } p1 && MaxPoints is { } p2) parts.Add($"{N(p1)} to {N(p2)} points");
        else if (MinPoints is { } p3) parts.Add($"at least {N(p3)} points");
        else if (MaxPoints is { } p4) parts.Add($"at most {N(p4)} points");
        if (MinBalance is { } owing) parts.Add($"owes at least {N(owing)}");
        if (WhatsAppConsent is { } consent) parts.Add(consent ? "agreed to WhatsApp" : "no WhatsApp consent");
        return parts.Count == 0 ? "Every client" : string.Join(", ", parts);
    }
}

/// <summary>What is known of one client when a segment is worked out.</summary>
/// <param name="Spend">What they paid in the period (at the store, when the rules name one).</param>
/// <param name="Visits">Completed sales in the period.</param>
/// <param name="LastPurchase">The day of their last completed sale (at the store, when the rules name one).</param>
/// <param name="BoughtDepartment">Bought something of the rules' department in the period.</param>
public sealed record SegmentFacts(decimal Spend, int Visits, DateOnly? LastPurchase, bool BoughtDepartment, DateOnly? Birthday, decimal Points,
    decimal Balance, bool WhatsAppOptIn, string? WhatsAppNumber);

public static class Segments
{
    /// <summary>True when the client meets every rule that is set.</summary>
    public static bool Matches(SegmentRules r, SegmentFacts c, DateOnly today)
    {
        if (r.MinSpend is { } minSpend && c.Spend < minSpend) return false;
        if (r.MaxSpend is { } maxSpend && c.Spend > maxSpend) return false;
        if (r.MinVisits is { } minVisits && c.Visits < minVisits) return false;
        if (r.MaxVisits is { } maxVisits && c.Visits > maxVisits) return false;
        if (r.DepartmentId is not null && !c.BoughtDepartment) return false;
        if (r.LastPurchaseBefore is { } before && c.LastPurchase is { } last && last >= before) return false;
        if (r.LastPurchaseAfter is { } after && (c.LastPurchase is not { } recent || recent < after)) return false;
        if (r.NoPurchaseForDays is { } idle && c.LastPurchase is { } lastDay && lastDay > today.AddDays(-idle)) return false;
        if (r.BirthdayMonth is { } month && c.Birthday?.Month != month) return false;
        if (r.MinPoints is { } minPoints && c.Points < minPoints) return false;
        if (r.MaxPoints is { } maxPoints && c.Points > maxPoints) return false;
        if (r.MinBalance is { } owing && c.Balance < owing) return false;
        if (r.WhatsAppConsent is { } consent && consent != (c.WhatsAppOptIn && !string.IsNullOrWhiteSpace(c.WhatsAppNumber))) return false;
        return true;
    }
}
