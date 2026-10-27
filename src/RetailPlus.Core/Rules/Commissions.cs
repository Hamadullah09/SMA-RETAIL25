using RetailPlus.Core.Model;

namespace RetailPlus.Core.Rules;

/// <summary>
/// A sales line as commission sees it: who sold it, on which day, what it was and how much it came to.
/// <see cref="StoredCommission"/> is what the line earned when it was sold, from the item's own commission rate.
/// </summary>
public sealed record CommissionLine(
    Guid? StaffId, string? Staff, DateOnly SoldOn, Guid? ItemId, Guid? DepartmentId, Guid? CategoryId,
    decimal Quantity, decimal NetAmount, decimal StoredCommission);

/// <summary>
/// Works out commission from sales lines and commission rules. For each line the rule that fits best wins:
/// <list type="number">
/// <item>the narrowest scope: one item, then a category, then a department, then all items;</item>
/// <item>then a rule for the salesperson over a rule for everyone;</item>
/// <item>then the rule that started most recently.</item>
/// </list>
/// Only active rules whose dates include the day of the sale count. A line no rule covers keeps the commission it was
/// sold with (the item's commission percent), as Retail Plus did.
/// </summary>
public static class Commissions
{
    /// <summary>The rule that decides the commission on a line, or null when none covers it.</summary>
    public static CommissionRule? RuleFor(IEnumerable<CommissionRule> rules, CommissionLine line) =>
        rules.Where(r => Covers(r, line))
            .OrderByDescending(r => Specificity(r.Scope))
            .ThenByDescending(r => r.UserId is not null)
            .ThenByDescending(r => r.EffectiveFrom ?? DateOnly.MinValue)
            .ThenByDescending(r => r.CreatedAtUtc)
            .FirstOrDefault();

    /// <summary>The commission a line earns: by its best rule, else what it was sold with.</summary>
    public static decimal For(IEnumerable<CommissionRule> rules, CommissionLine line) => Apply(RuleFor(rules, line), line);

    /// <summary>The commission a line earns under <paramref name="rule"/> (its stored commission when there is no rule).</summary>
    public static decimal Apply(CommissionRule? rule, CommissionLine line) => rule?.Basis switch
    {
        null => line.StoredCommission,
        CommissionBasis.PercentOfNetSales => Money.Round(line.NetAmount * rule.Rate / 100m),
        _ => Money.Round(line.Quantity * rule.Rate),
    };

    /// <summary>True when the rule is in force on the day of the line and covers its salesperson and goods.</summary>
    public static bool Covers(CommissionRule rule, CommissionLine line)
    {
        if (!rule.IsActive) return false;
        if (rule.EffectiveFrom is { } from && line.SoldOn < from) return false;
        if (rule.EffectiveTo is { } to && line.SoldOn > to) return false;
        if (rule.UserId is { } user && user != line.StaffId) return false;
        return rule.Scope switch
        {
            CommissionScope.Item => rule.ItemId is not null && rule.ItemId == line.ItemId,
            CommissionScope.Category => rule.CategoryId is not null && rule.CategoryId == line.CategoryId,
            CommissionScope.Department => rule.DepartmentId is not null && rule.DepartmentId == line.DepartmentId,
            _ => true,
        };
    }

    private static int Specificity(CommissionScope scope) => scope switch
    {
        CommissionScope.Item => 3,
        CommissionScope.Category => 2,
        CommissionScope.Department => 1,
        _ => 0,
    };
}
