using RetailPlus.Core.Model;

namespace RetailPlus.Core.Rules;

/// <summary>What one batch holds when a sale, transfer or count picks from it.</summary>
public sealed record BatchStock(Guid BatchId, string BatchNumber, DateOnly? ExpiresOn, decimal OnHand);

/// <summary>One part of a quantity taken out of stock: from a batch, or (null) from stock that is in no batch.</summary>
public sealed record BatchTake(Guid? BatchId, string? BatchNumber, decimal Quantity);

/// <summary>
/// First-expiry-first-out: the order stock of an item that tracks batches leaves in, at the POS, on a transfer, and when
/// a count sets uncounted stock to zero.
/// </summary>
public static class Fefo
{
    /// <summary>True when the batch's last day of sale is before <paramref name="today"/>.</summary>
    public static bool IsExpired(DateOnly? expiresOn, DateOnly today) => expiresOn is DateOnly d && d < today;

    /// <summary>
    /// Splits <paramref name="quantity"/> over the batches: the batch that expires first goes first, then the next one, and
    /// batches without an expiry date last (oldest batch first among equals). What the batches cannot cover is taken from
    /// stock in no batch (a <see cref="BatchTake"/> without a batch), so stock may still go below zero as it always could.
    /// With <paramref name="skipExpired"/>, expired batches are passed over.
    /// </summary>
    public static IReadOnlyList<BatchTake> Pick(IEnumerable<BatchStock> batches, decimal quantity, DateOnly today, bool skipExpired)
    {
        var takes = new List<BatchTake>();
        if (quantity <= 0) return takes;
        decimal left = quantity;
        foreach (var b in Order(batches))
        {
            if (left <= 0) break;
            if (b.OnHand <= 0 || (skipExpired && IsExpired(b.ExpiresOn, today))) continue;
            decimal take = Math.Min(left, b.OnHand);
            takes.Add(new BatchTake(b.BatchId, b.BatchNumber, take));
            left -= take;
        }
        if (left > 0) takes.Add(new BatchTake(null, null, left));
        return takes;
    }

    /// <summary>The batches in the order they leave: by expiry date (none last), then by batch number.</summary>
    public static IEnumerable<BatchStock> Order(IEnumerable<BatchStock> batches) =>
        batches.OrderBy(b => b.ExpiresOn is null).ThenBy(b => b.ExpiresOn).ThenBy(b => b.BatchNumber, StringComparer.OrdinalIgnoreCase);

    /// <summary>The expiry date offered when goods are received: the day made (or today) plus the shelf life, or none.</summary>
    public static DateOnly? SuggestedExpiry(int shelfLifeDays, DateOnly? madeOn, DateOnly today) =>
        shelfLifeDays > 0 ? (madeOn ?? today).AddDays(shelfLifeDays) : null;
}

/// <summary>The tolerances of the three-way match, from a store's <see cref="SupplySettings"/>.</summary>
public static class MatchTolerance
{
    /// <summary>
    /// The difference allowed on <paramref name="baseValue"/>: the larger of <paramref name="percent"/> of it and
    /// <paramref name="amount"/>.
    /// </summary>
    public static decimal Allowed(decimal baseValue, decimal percent, decimal amount) => Math.Max(Math.Abs(baseValue) * percent / 100m, amount);

    /// <summary>True when <paramref name="actual"/> differs from <paramref name="expected"/> by no more than the tolerance.</summary>
    public static bool Within(decimal expected, decimal actual, decimal percent, decimal amount) =>
        Math.Abs(actual - expected) <= Allowed(expected, percent, amount);
}

/// <summary>One received line a landed cost is spread over: its pieces, value and weight.</summary>
public sealed record LandedLine(Guid Key, decimal Quantity, decimal Value, decimal Weight);

/// <summary>Spreading landed costs (freight, duty, insurance) over received lines.</summary>
public static class LandedCosts
{
    /// <summary>
    /// Each line's share of <paramref name="total"/> by the basis, in cents, adding up to the total exactly (the cents
    /// rounding leaves go to the largest lines). Lines with nothing on the basis get nothing; when no line has anything
    /// on it (no weights entered), the total is spread by value instead.
    /// </summary>
    public static IReadOnlyDictionary<Guid, decimal> Spread(IReadOnlyList<LandedLine> lines, decimal total, LandedCostBasis basis)
    {
        var result = lines.ToDictionary(l => l.Key, _ => 0m);
        if (lines.Count == 0 || total == 0) return result;
        decimal Measure(LandedLine l, LandedCostBasis b) => Math.Max(0, b switch
        {
            LandedCostBasis.Quantity => l.Quantity,
            LandedCostBasis.Weight => l.Weight,
            _ => l.Value,
        });
        if (lines.Sum(l => Measure(l, basis)) == 0) basis = lines.Sum(l => Measure(l, LandedCostBasis.Value)) > 0 ? LandedCostBasis.Value : LandedCostBasis.Quantity;
        decimal sum = lines.Sum(l => Measure(l, basis));
        if (sum == 0) return result;
        foreach (var l in lines) result[l.Key] = Money.Round(total * Measure(l, basis) / sum);
        decimal rest = total - result.Values.Sum();
        // The cents left over by rounding go one at a time to the lines with the most on the basis.
        decimal cent = rest > 0 ? 0.01m : -0.01m;
        foreach (var l in lines.OrderByDescending(l => Measure(l, basis)).ThenBy(l => l.Key))
        {
            if (rest == 0) break;
            if (Measure(l, basis) == 0) continue;
            result[l.Key] += cent;
            rest -= cent;
        }
        return result;
    }

    /// <summary>
    /// The weight of <paramref name="pieces"/> of an item in kilograms: its shipping weight (taken as kilograms) when set,
    /// else its net weight in its unit (millilitres and litres count as grams and kilograms).
    /// </summary>
    public static decimal WeightInKilograms(decimal pieces, decimal shipWeight, decimal weight, string? unit)
    {
        if (shipWeight > 0) return pieces * shipWeight;
        decimal perPiece = Weights.Normalize(unit) switch
        {
            "mg" => weight / 1_000_000m,
            "g" or "ml" => weight / 1000m,
            "kg" or "L" => weight,
            "oz" => weight * 0.028349523125m,
            "lb" => weight * 0.45359237m,
            _ => 0,
        };
        return pieces * perPiece;
    }

    /// <summary>
    /// The part of a line's share that goes to the item's cost: the share of the pieces still on hand (at most all of the
    /// line's pieces). The rest goes to cost of goods.
    /// </summary>
    public static decimal ToInventory(decimal share, decimal linePieces, decimal onHand)
    {
        if (linePieces <= 0) return 0;
        decimal kept = Math.Clamp(onHand, 0, linePieces);
        return Money.Round(share * kept / linePieces);
    }
}

/// <summary>What the reorder suggestion of one item is worked out from.</summary>
public sealed record ReorderInputs(
    decimal UnitsSold, int WindowDays, int LeadTimeDays, int SafetyStockDays, int CoverDays, decimal OnHand, decimal OnOrder, decimal CustomerOrdered,
    decimal ReorderPoint, decimal ReorderQuantity, decimal CaseQuantity, decimal MinimumOrder);

/// <summary>The suggestion: the reorder level, the stock to order up to, and the pieces (and cases) to order.</summary>
public sealed record ReorderSuggestion(decimal AverageDailySales, decimal ReorderLevel, decimal TargetStock, decimal Available, decimal Pieces, decimal OrderQuantity);

/// <summary>Reorder suggestions from sales history.</summary>
public static class ReorderRules
{
    /// <summary>
    /// The pieces to order. The reorder level is the sales expected over the lead time and the safety days, never below the
    /// item's own reorder point. When the stock on hand and on order (less what customers ordered) is at or below it, the
    /// order brings it up to the sales of the lead time, safety and cover days (at least the reorder level plus the
    /// item's reorder quantity when it has one), at least the supplier's minimum, in whole cases.
    /// </summary>
    public static ReorderSuggestion Suggest(ReorderInputs i)
    {
        decimal daily = i.WindowDays <= 0 ? 0 : Math.Max(0, i.UnitsSold) / i.WindowDays;
        decimal level = Math.Max(Math.Ceiling(daily * (i.LeadTimeDays + i.SafetyStockDays)), i.ReorderPoint);
        decimal target = Math.Ceiling(daily * (i.LeadTimeDays + i.SafetyStockDays + i.CoverDays));
        if (i.ReorderQuantity > 0) target = Math.Max(target, level + i.ReorderQuantity);
        target = Math.Max(target, level);
        decimal available = i.OnHand + i.OnOrder - i.CustomerOrdered;
        bool needed = (daily > 0 || i.ReorderPoint > 0) && available <= level;
        decimal pieces = needed ? Math.Max(0, target - available) : 0;
        if (needed && pieces == 0) pieces = Math.Max(1, i.ReorderQuantity);
        if (pieces > 0 && i.MinimumOrder > pieces) pieces = i.MinimumOrder;
        decimal caseQty = i.CaseQuantity > 1 ? i.CaseQuantity : 1;
        decimal cases = pieces <= 0 ? 0 : Math.Ceiling(pieces / caseQty);
        return new ReorderSuggestion(Math.Round(daily, 3), level, target, available, cases * caseQty, caseQty > 1 ? cases : cases * caseQty);
    }
}
