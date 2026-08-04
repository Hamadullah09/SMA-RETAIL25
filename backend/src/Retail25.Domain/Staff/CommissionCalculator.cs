namespace Retail25.Domain.Staff;

/// <summary>One line, reduced to what a commission rule needs to know about it.</summary>
/// <param name="LineNet">Net of discounts and before tax — what the line actually brought in.</param>
/// <param name="LineCost">Extended cost, for percent-of-profit.</param>
public sealed record CommissionableLine(
    long ProductId,
    long? DepartmentId,
    decimal Quantity,
    decimal LineNet,
    decimal LineCost);

/// <summary>What a rule produced for one line.</summary>
public sealed record CommissionAward(
    CommissionRule Rule,
    decimal Amount,
    bool WasCapped);

/// <summary>
/// Works out what a line pays (guide p.33, p.76).
/// <para>
/// Pure and static on purpose: rule precedence and the three payment shapes are the part people
/// argue about at payroll, and being able to check them without a database or a sale is worth more
/// than the convenience of reading rules from inside.
/// </para>
/// </summary>
public static class CommissionCalculator
{
    /// <summary>
    /// The rule that applies to a line, or null if none does.
    /// <para>
    /// Most specific wins: item, then department, then the staff-wide rate. Where two rules tie —
    /// two item rules for the same item, which the UI does not offer but the table permits — the
    /// more generous one is taken. Paying someone the smaller of two rates that both apply is the
    /// kind of thing that gets found months later.
    /// </para>
    /// </summary>
    public static CommissionRule? Resolve(IEnumerable<CommissionRule> rules, CommissionableLine line)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(line);

        CommissionRule? best = null;

        foreach (var rule in rules)
        {
            if (!rule.IsActive || !Applies(rule, line))
            {
                continue;
            }

            if (best is null
                || rule.Specificity > best.Specificity
                || (rule.Specificity == best.Specificity && rule.Value > best.Value))
            {
                best = rule;
            }
        }

        return best;
    }

    private static bool Applies(CommissionRule rule, CommissionableLine line)
    {
        if (rule.ProductId is { } productId)
        {
            return productId == line.ProductId;
        }

        if (rule.DepartmentId is { } departmentId)
        {
            return line.DepartmentId == departmentId;
        }

        return true;
    }

    /// <summary>
    /// What one line pays, or null when no rule applies or the rule works out to nothing.
    /// <para>
    /// A return has a negative <c>LineNet</c> and produces a negative award, which is what takes the
    /// commission back off the person who sold it. That only reaches the staff member who processed
    /// the return, though — see the note on voids in <c>CompleteSaleHandler</c>.
    /// </para>
    /// </summary>
    public static CommissionAward? Award(IEnumerable<CommissionRule> rules, CommissionableLine line)
    {
        var rule = Resolve(rules, line);

        return rule is null ? null : Award(rule, line);
    }

    public static CommissionAward? Award(CommissionRule rule, CommissionableLine line)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(line);

        var raw = rule.CommissionType switch
        {
            CommissionType.Percentage => line.LineNet * rule.Value / 100m,

            // Per unit, so three of an item pays three times. Signed by quantity so a return of two
            // takes back what selling two paid.
            CommissionType.Fixed => rule.Value * line.Quantity,

            // Nothing is owed on a line sold at or below cost — that is the point of paying on
            // margin rather than on revenue.
            CommissionType.PercentOfProfit => Math.Max(0m, line.LineNet - line.LineCost) * rule.Value / 100m,

            _ => 0m,
        };

        var rounded = decimal.Round(raw, 2, MidpointRounding.AwayFromZero);

        if (rounded == 0m)
        {
            return null;
        }

        // The cap is a ceiling on what is earned, so it applies to the magnitude. Capping only the
        // positive side would mean a return clawed back more than the sale ever paid.
        var capped = false;

        if (rule.MaxCommission is { } max && max > 0m && Math.Abs(rounded) > max)
        {
            rounded = rounded < 0m ? -max : max;
            capped = true;
        }

        return new CommissionAward(rule, rounded, capped);
    }
}
