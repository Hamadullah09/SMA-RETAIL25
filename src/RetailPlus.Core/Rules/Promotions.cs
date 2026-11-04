using System.Globalization;
using RetailPlus.Core.Model;

namespace RetailPlus.Core.Rules;

/// <summary>
/// Where and when promotions are worked out: the store, its local time, the coupon codes entered on the sale, and how
/// often each promotion's coupon was used already (by promotion id).
/// </summary>
public sealed record PromotionContext(Guid StoreId, DateTime Now, IReadOnlyCollection<string>? CouponCodes = null, IReadOnlyDictionary<Guid, int>? CouponUses = null);

/// <summary>
/// A sale line as the engine sees it: what it is (stock code, department, category), how many and what it comes to
/// before any promotion (<paramref name="Net"/>). Lines that may take no promotion (a price changed by hand) still count
/// towards a spend; they have <paramref name="Eligible"/> false.
/// </summary>
public sealed record PromotionLine(int Index, string? Plu, Guid? DepartmentId, Guid? CategoryId, decimal Quantity, decimal Net,
    bool Taxable1 = true, bool Taxable2 = true, bool Eligible = true);

/// <summary>A promotion's discount on one line.</summary>
public sealed record LinePromotion(int Index, Guid PromotionId, string Name, decimal Discount);

/// <summary>A sale-wide promotion (spend over an amount): a line of its own, taxed like most of what it was counted on.</summary>
public sealed record SalePromotion(Guid PromotionId, string Name, decimal Discount, bool Taxable1, bool Taxable2);

/// <summary>What the promotions give a sale, and what the cashier should hear about the coupons entered.</summary>
public sealed record PromotionOutcome(IReadOnlyList<LinePromotion> Lines, SalePromotion? Sale, IReadOnlyList<string> Notes)
{
    public static readonly PromotionOutcome None = new([], null, []);

    public decimal Total => Lines.Sum(l => l.Discount) + (Sale?.Discount ?? 0);

    /// <summary>Every promotion used, once each.</summary>
    public IReadOnlyList<Guid> Applied => [.. Lines.Select(l => l.PromotionId).Append(Sale?.PromotionId ?? Guid.Empty).Where(id => id != Guid.Empty).Distinct()];
}

/// <summary>
/// Promotions at the till: percent or amount off, buy X get Y, bundle prices, spend over an amount, coupon codes and happy
/// hours, each with dates, stores, a priority and whether it combines with others.
/// <list type="bullet">
/// <item>A promotion runs when it is on, today is within its dates and days, the time within its happy hour, the store one
/// of its stores, and, for a coupon, its code was entered and it is not used up.</item>
/// <item>The best price for the customer wins: the engine tries every promotion that may not combine on its own, and all
/// the combinable ones together, and keeps the set that takes the most off; a higher priority wins a tie.</item>
/// <item>Within a set the biggest discount goes first. A line takes one promotion: a buy X get Y or a bundle takes every
/// line it used (bought and given), a percent or amount off the lines it reduces. A spend over an amount is worked out
/// last, on what the lines then come to, and becomes a line of its own.</item>
/// </list>
/// </summary>
public static class PromotionEngine
{
    /// <summary>The bit of a day in <see cref="Promotion.Days"/>.</summary>
    public static int DayBit(DayOfWeek day) => 1 << (int)day;

    /// <summary>True when the promotion runs at the store at this time (coupons aside).</summary>
    public static bool RunsAt(Promotion p, Guid storeId, DateTime now)
    {
        if (!p.IsActive) return false;
        var today = DateOnly.FromDateTime(now);
        if (p.StartsOn is { } start && today < start) return false;
        if (p.EndsOn is { } end && today > end) return false;
        if (!p.AllStores && !p.Stores.Any(s => s.StoreId == storeId)) return false;
        if (p.Days != 0 && (p.Days & DayBit(now.DayOfWeek)) == 0) return false;
        if (p.StartTime is { } from && p.EndTime is { } to && from != to)
        {
            var time = TimeOnly.FromDateTime(now);
            bool inside = from < to ? time >= from && time < to : time >= from || time < to;
            if (!inside) return false;
        }
        return true;
    }

    /// <summary>A coupon code as it is kept and compared: trimmed, upper case.</summary>
    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static bool HasCoupon(Promotion p) => !string.IsNullOrWhiteSpace(p.CouponCode);

    private static bool Entered(Promotion p, PromotionContext context) =>
        context.CouponCodes?.Any(c => NormalizeCode(c) == NormalizeCode(p.CouponCode!)) == true;

    private static bool UsedUp(Promotion p, PromotionContext context) =>
        p.CouponMaxUses > 0 && (context.CouponUses?.GetValueOrDefault(p.Id) ?? 0) >= p.CouponMaxUses;

    /// <summary>The promotions that may apply to this sale: running now, and for coupons, entered and not used up.</summary>
    public static List<Promotion> Usable(IEnumerable<Promotion> promotions, PromotionContext context) =>
        [.. promotions.Where(p => RunsAt(p, context.StoreId, context.Now) && (!HasCoupon(p) || (Entered(p, context) && !UsedUp(p, context))))];

    // Evaluating ------------------------------------------------------------------------------------------------------

    /// <summary>What the promotions give these lines.</summary>
    public static PromotionOutcome Evaluate(IReadOnlyList<PromotionLine> lines, IReadOnlyList<Promotion> promotions, PromotionContext context)
    {
        var usable = Usable(promotions, context);
        var sets = new List<List<Promotion>>();
        var combinable = usable.Where(p => p.Combinable).ToList();
        if (combinable.Count > 0) sets.Add(combinable);
        sets.AddRange(usable.Where(p => !p.Combinable).Select(p => new List<Promotion> { p }));

        PromotionOutcome best = PromotionOutcome.None;
        int bestPriority = int.MinValue;
        foreach (var set in sets)
        {
            var outcome = EvaluateSet(lines, set);
            if (outcome.Total <= 0) continue;
            int priority = set.Where(p => outcome.Applied.Contains(p.Id)).Max(p => p.Priority);
            if (outcome.Total > best.Total || (outcome.Total == best.Total && priority > bestPriority))
            {
                best = outcome;
                bestPriority = priority;
            }
        }
        return best with { Notes = CouponNotes(promotions, context, best) };
    }

    private static List<string> CouponNotes(IReadOnlyList<Promotion> promotions, PromotionContext context, PromotionOutcome outcome)
    {
        var notes = new List<string>();
        foreach (var code in (context.CouponCodes ?? []).Select(NormalizeCode).Distinct())
        {
            var withCode = promotions.Where(p => HasCoupon(p) && NormalizeCode(p.CouponCode!) == code).ToList();
            if (withCode.Count == 0) notes.Add($"Coupon {code} is not known.");
            else if (!withCode.Any(p => RunsAt(p, context.StoreId, context.Now))) notes.Add($"Coupon {code} is not valid now.");
            else if (withCode.All(p => UsedUp(p, context))) notes.Add($"Coupon {code} has been used up.");
            else if (!withCode.Any(p => outcome.Applied.Contains(p.Id))) notes.Add($"Coupon {code} does not apply to this sale.");
        }
        return notes;
    }

    /// <summary>A candidate step of the greedy evaluation: a promotion's discounts on some lines.</summary>
    private sealed record Step(Promotion Promotion, IReadOnlyList<(int Index, decimal Discount)> Discounts, IReadOnlyList<int> Claims)
    {
        public decimal Total => Discounts.Sum(d => d.Discount);
    }

    private static bool PerLine(Promotion p) => p.Kind is PromotionKind.PercentOff or PromotionKind.AmountOff;

    private static PromotionOutcome EvaluateSet(IReadOnlyList<PromotionLine> lines, IReadOnlyList<Promotion> set)
    {
        var claimed = new HashSet<int>();
        var used = new HashSet<Guid>();
        var results = new List<LinePromotion>();
        var itemPromotions = set.Where(p => p.Kind != PromotionKind.SpendOver).ToList();
        while (true)
        {
            Step? best = null;
            var free = lines.Where(l => l.Eligible && l.Net > 0 && l.Quantity > 0 && !claimed.Contains(l.Index)).ToList();
            foreach (var p in itemPromotions)
            {
                if (PerLine(p))
                {
                    foreach (var line in free.Where(l => Matches(p, PromotionTargetRole.Qualifies, l)))
                    {
                        decimal d = LineDiscount(p, line);
                        if (d > 0) best = Better(best, new Step(p, [(line.Index, d)], [line.Index]));
                    }
                }
                else if (!used.Contains(p.Id) && (p.Kind == PromotionKind.BuyXGetY ? BuyXGetY(p, free) : Bundle(p, free)) is { } step)
                    best = Better(best, step);
            }
            if (best is null) break;
            foreach (var index in best.Claims) claimed.Add(index);
            if (!PerLine(best.Promotion)) used.Add(best.Promotion.Id);
            var shares = best.Discounts.ToDictionary(d => d.Index, d => d.Discount);
            results.AddRange(best.Claims.Select(i => new LinePromotion(i, best.Promotion.Id, best.Promotion.Name, shares.GetValueOrDefault(i))));
        }

        SalePromotion? sale = null;
        var after = results.GroupBy(r => r.Index).ToDictionary(g => g.Key, g => g.Sum(r => r.Discount));
        foreach (var p in set.Where(p => p.Kind == PromotionKind.SpendOver).OrderByDescending(p => p.Priority))
            if (Spend(p, lines, after) is { } candidate && candidate.Discount > (sale?.Discount ?? 0)) sale = candidate;
        return new PromotionOutcome(results, sale, []);
    }

    private static Step Better(Step? current, Step candidate)
    {
        if (current is null) return candidate;
        if (candidate.Total != current.Total) return candidate.Total > current.Total ? candidate : current;
        if (candidate.Promotion.Priority != current.Promotion.Priority) return candidate.Promotion.Priority > current.Promotion.Priority ? candidate : current;
        return string.CompareOrdinal(candidate.Promotion.Name, current.Promotion.Name) < 0 ? candidate : current;
    }

    /// <summary>True when the line is one of the items a target role of the promotion names.</summary>
    public static bool Matches(Promotion p, PromotionTargetRole role, PromotionLine line)
    {
        if (role == PromotionTargetRole.Qualifies && p.Scope == PromotionScope.AllItems) return true;
        var targets = p.Targets.Where(t => t.Role == role);
        if (role == PromotionTargetRole.Qualifies)
            targets = p.Scope switch
            {
                PromotionScope.Departments => targets.Where(t => t.DepartmentId is not null),
                PromotionScope.Categories => targets.Where(t => t.CategoryId is not null),
                _ => targets.Where(t => !string.IsNullOrWhiteSpace(t.Plu)),
            };
        return targets.Any(t => Matches(t, line));
    }

    private static bool Matches(PromotionTarget t, PromotionLine line) =>
        (t.DepartmentId is { } d && line.DepartmentId == d)
        || (t.CategoryId is { } c && line.CategoryId == c)
        || (!string.IsNullOrWhiteSpace(t.Plu) && line.Plu is not null && string.Equals(t.Plu.Trim(), line.Plu.Trim(), StringComparison.OrdinalIgnoreCase));

    private static decimal LineDiscount(Promotion p, PromotionLine line) => p.Kind switch
    {
        PromotionKind.PercentOff when p.Percent > 0 => Math.Min(line.Net, Money.Round(line.Net * Math.Min(p.Percent, 100) / 100m)),
        PromotionKind.AmountOff when p.Amount > 0 => Math.Min(line.Net, Money.Round(p.Amount * line.Quantity)),
        _ => 0,
    };

    /// <summary>The whole units of the lines, each with its price (the line's net amount shared over its quantity).</summary>
    private static List<(int Index, decimal Price)> Units(IEnumerable<PromotionLine> lines) =>
        [.. lines.SelectMany(l => Enumerable.Repeat((l.Index, l.Net / l.Quantity), (int)Math.Floor(l.Quantity)))];

    private static Step? BuyXGetY(Promotion p, IReadOnlyList<PromotionLine> free)
    {
        if (p.BuyQuantity <= 0 || p.GetQuantity <= 0 || p.GetPercent <= 0) return null;
        bool sameItems = !p.Targets.Any(t => t.Role == PromotionTargetRole.Reward);
        var bought = free.Where(l => Matches(p, PromotionTargetRole.Qualifies, l) && (sameItems || !Matches(p, PromotionTargetRole.Reward, l))).ToList();
        List<(int Index, decimal Price)> given;
        List<int> claims;
        if (sameItems)
        {
            var units = Units(bought);
            int groups = units.Count / (p.BuyQuantity + p.GetQuantity);
            given = [.. units.OrderBy(u => u.Price).Take(groups * p.GetQuantity)];
            claims = [.. bought.Select(l => l.Index)];
        }
        else
        {
            int groups = Units(bought).Count / p.BuyQuantity;
            var rewards = free.Where(l => Matches(p, PromotionTargetRole.Reward, l)).ToList();
            given = [.. Units(rewards).OrderBy(u => u.Price).Take(groups * p.GetQuantity)];
            claims = [.. bought.Select(l => l.Index).Concat(given.Select(g => g.Index).Distinct())];
        }
        if (given.Count == 0) return null;
        decimal share = Math.Min(p.GetPercent, 100) / 100m;
        var discounts = given.GroupBy(g => g.Index).Select(g => (g.Key, Money.Round(g.Sum(u => u.Price) * share))).Where(d => d.Item2 > 0).ToList();
        return discounts.Count == 0 ? null : new Step(p, discounts, claims);
    }

    private static Step? Bundle(Promotion p, IReadOnlyList<PromotionLine> free)
    {
        var parts = p.Targets.Where(t => t.Role == PromotionTargetRole.BundlePart).ToList();
        if (parts.Count == 0 || p.BundlePrice < 0) return null;
        var units = Units(free).OrderByDescending(u => u.Price).ToList();
        var taken = new bool[units.Count];
        var byLine = new Dictionary<int, (decimal Value, decimal Discount)>();
        decimal total = 0;
        while (true)
        {
            var bundle = new List<int>();
            foreach (var part in parts)
            {
                int need = Math.Max(1, (int)Math.Floor(part.Quantity));
                for (int u = 0; u < units.Count && need > 0; u++)
                {
                    if (taken[u] || bundle.Contains(u) || !Matches(part, free.First(l => l.Index == units[u].Index))) continue;
                    bundle.Add(u);
                    need--;
                }
                if (need > 0) { bundle.Clear(); break; }
            }
            if (bundle.Count == 0) break;
            decimal regular = bundle.Sum(u => units[u].Price);
            decimal discount = Money.Round(regular - p.BundlePrice);
            if (discount <= 0) break;
            foreach (var u in bundle) taken[u] = true;
            // The bundle's saving is shared over its units by their price; the last unit takes what rounding left.
            decimal left = discount;
            for (int i = 0; i < bundle.Count; i++)
            {
                var unit = units[bundle[i]];
                decimal part = i == bundle.Count - 1 ? left : Money.Round(discount * unit.Price / regular);
                left -= part;
                var was = byLine.GetValueOrDefault(unit.Index);
                byLine[unit.Index] = (was.Value + unit.Price, was.Discount + part);
            }
            total += discount;
        }
        if (total <= 0) return null;
        return new Step(p, [.. byLine.Select(kv => (kv.Key, kv.Value.Discount))], [.. byLine.Keys]);
    }

    private static SalePromotion? Spend(Promotion p, IReadOnlyList<PromotionLine> lines, IReadOnlyDictionary<int, decimal> discounts)
    {
        if (p.SpendThreshold <= 0) return null;
        var counted = lines.Where(l => l.Quantity > 0 && l.Net > 0 && Matches(p, PromotionTargetRole.Qualifies, l))
            .Select(l => (Line: l, Amount: l.Net - discounts.GetValueOrDefault(l.Index))).ToList();
        decimal spend = counted.Sum(c => c.Amount);
        if (spend < p.SpendThreshold) return null;
        decimal discount = p.Percent > 0 ? Money.Round(spend * Math.Min(p.Percent, 100) / 100m) : Math.Min(spend, Money.Round(p.Amount));
        if (discount <= 0) return null;
        // Taxed like the larger part (by value) of what was counted.
        bool tax1 = counted.Where(c => c.Line.Taxable1).Sum(c => c.Amount) * 2 >= spend;
        bool tax2 = counted.Where(c => c.Line.Taxable2).Sum(c => c.Amount) * 2 >= spend;
        return new SalePromotion(p.Id, p.Name, discount, tax1, tax2);
    }

    // The sale draft --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Works the promotions out again for a sale being rung up: clears what they gave before, sets each line's promotion
    /// and discount, and adds the coupon line of a sale-wide promotion. Sale lines of goods (not gift cards) whose price
    /// came from the pricing rules may take a promotion; returns, coupons and text lines never do.
    /// </summary>
    public static PromotionOutcome Apply(SaleDraft draft, IReadOnlyList<Promotion> promotions, PromotionContext context)
    {
        draft.Lines.RemoveAll(l => l.IsPromotionLine);
        foreach (var line in draft.Lines)
        {
            line.PromotionId = null;
            line.PromotionName = null;
            line.PromotionDiscount = 0;
        }
        var lines = draft.Lines.Select((l, i) => (Line: l, Index: i))
            .Where(x => x.Line.Kind == SaleLineKind.Sale && x.Line.ItemType != ItemType.GiftCard && (x.Line.ItemId is not null || x.Line.Plu is not null))
            .Select(x => new PromotionLine(x.Index, x.Line.Plu, x.Line.DepartmentId, x.Line.CategoryId, x.Line.Quantity,
                Money.Round(x.Line.UnitPrice * x.Line.Quantity - x.Line.DiscountAmount), x.Line.Taxable1, x.Line.Taxable2, Eligible: x.Line.AutoPriced))
            .ToList();
        var outcome = promotions.Count == 0 && (context.CouponCodes?.Count ?? 0) == 0 ? PromotionOutcome.None : Evaluate(lines, promotions, context);
        foreach (var applied in outcome.Lines)
        {
            var line = draft.Lines[applied.Index];
            line.PromotionId = applied.PromotionId;
            line.PromotionName = applied.Name;
            line.PromotionDiscount = applied.Discount;
        }
        if (outcome.Sale is { } sale)
            draft.Lines.Add(new SaleDraftLine
            {
                Kind = SaleLineKind.Coupon, Description = sale.Name, Quantity = 1, UnitPrice = -sale.Discount, Taxable1 = sale.Taxable1, Taxable2 = sale.Taxable2,
                AutoPriced = false, ItemType = ItemType.NonStock, PromotionId = sale.PromotionId, PromotionName = sale.Name,
            });
        return outcome;
    }

    // Describing ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The promotion in a few words for lists: "10% off department FOOD, with coupon SAVE10, Mon Tue 17:00–19:00".
    /// <paramref name="targetName"/> names a department or category target (stock codes name themselves).
    /// </summary>
    public static string Describe(Promotion p, Func<PromotionTarget, string?>? targetName = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        string N(decimal d) => d.ToString("#,0.##", culture);
        string Name(PromotionTarget t) => t.Plu ?? targetName?.Invoke(t) ?? "?";
        string Of(PromotionTargetRole role)
        {
            var names = p.Targets.Where(t => t.Role == role).Select(Name).ToList();
            if (role == PromotionTargetRole.Qualifies)
                return p.Scope switch
                {
                    PromotionScope.AllItems => "all items",
                    PromotionScope.Departments => (names.Count == 1 ? "department " : "departments ") + string.Join(", ", names),
                    PromotionScope.Categories => (names.Count == 1 ? "category " : "categories ") + string.Join(", ", names),
                    _ => string.Join(", ", names),
                };
            return string.Join(", ", names);
        }
        string what = p.Kind switch
        {
            PromotionKind.PercentOff => $"{N(p.Percent)}% off {Of(PromotionTargetRole.Qualifies)}",
            PromotionKind.AmountOff => $"{N(p.Amount)} off each unit of {Of(PromotionTargetRole.Qualifies)}",
            PromotionKind.BuyXGetY => $"Buy {p.BuyQuantity} of {Of(PromotionTargetRole.Qualifies)}, get {p.GetQuantity}"
                                      + (p.Targets.Any(t => t.Role == PromotionTargetRole.Reward) ? " of " + Of(PromotionTargetRole.Reward) : "")
                                      + (p.GetPercent >= 100 ? " free" : $" at {N(p.GetPercent)}% off"),
            PromotionKind.BundlePrice => string.Join(" + ", p.Targets.Where(t => t.Role == PromotionTargetRole.BundlePart)
                .Select(t => $"{N(Math.Max(1, Math.Floor(t.Quantity)))} × {Name(t)}")) + $" for {N(p.BundlePrice)}",
            PromotionKind.SpendOver => $"Spend {N(p.SpendThreshold)}" + (p.Scope == PromotionScope.AllItems ? "" : " on " + Of(PromotionTargetRole.Qualifies))
                                       + ", get " + (p.Percent > 0 ? $"{N(p.Percent)}% off" : $"{N(p.Amount)} off"),
            _ => p.Kind.ToString(),
        };
        var parts = new List<string> { what };
        if (!string.IsNullOrWhiteSpace(p.CouponCode)) parts.Add("with coupon " + p.CouponCode);
        var when = new List<string>();
        if (p.Days != 0)
            when.Add(string.Join(" ", Enum.GetValues<DayOfWeek>().Where(d => (p.Days & DayBit(d)) != 0).Select(d => culture.DateTimeFormat.GetAbbreviatedDayName(d))));
        if (p.StartTime is { } from && p.EndTime is { } to && from != to) when.Add($"{from:HH\\:mm}–{to:HH\\:mm}");
        if (when.Count > 0) parts.Add(string.Join(" ", when));
        return string.Join(", ", parts);
    }
}
