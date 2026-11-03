using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Core.Tests;

/// <summary>The supply chain rules: first expiry first, match tolerances, spreading landed costs, reorder suggestions.</summary>
public sealed class SupplyChainRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    private static BatchStock B(string number, int? days, decimal onHand) =>
        new(Guid.NewGuid(), number, days is int d ? Today.AddDays(d) : null, onHand);

    [Fact]
    public void Fefo_takes_the_batch_that_expires_first_then_batches_without_expiry_then_stock_in_no_batch()
    {
        var takes = Fefo.Pick([B("NONE", null, 5), B("LATE", 30, 2), B("SOON", 3, 1)], 6, Today, skipExpired: true);
        Assert.Equal([("SOON", 1m), ("LATE", 2m), ("NONE", 3m)], takes.Select(t => (t.BatchNumber!, t.Quantity)));
        var more = Fefo.Pick([B("A", 3, 1)], 4, Today, skipExpired: true);
        Assert.Equal([("A", 1m), (null, 3m)], more.Select(t => (t.BatchNumber, t.Quantity)));
        Assert.Empty(Fefo.Pick([B("A", 3, 1)], 0, Today, skipExpired: true));
    }

    [Fact]
    public void Expired_batches_are_passed_over_only_when_asked()
    {
        var batches = new[] { B("OLD", -1, 2), B("NEW", 5, 2) };
        Assert.Equal(["NEW", null], Fefo.Pick(batches, 3, Today, skipExpired: true).Select(t => t.BatchNumber));
        Assert.Equal(["OLD", "NEW"], Fefo.Pick(batches, 3, Today, skipExpired: false).Select(t => t.BatchNumber));
        Assert.True(Fefo.IsExpired(Today.AddDays(-1), Today));
        Assert.False(Fefo.IsExpired(Today, Today));
        Assert.Equal(Today.AddDays(10), Fefo.SuggestedExpiry(10, null, Today));
        Assert.Equal(Today.AddDays(8), Fefo.SuggestedExpiry(10, Today.AddDays(-2), Today));
        Assert.Null(Fefo.SuggestedExpiry(0, null, Today));
    }

    [Theory]
    [InlineData(10, 10.15, 2, 0, true)]
    [InlineData(10, 10.25, 2, 0, false)]
    [InlineData(10, 10.25, 2, 0.5, true)]
    [InlineData(10, 9.7, 2, 0.5, true)]
    [InlineData(0, 0.4, 2, 0.5, true)]
    public void A_difference_passes_when_within_the_larger_of_the_percent_and_the_amount(double expected, double actual, double percent, double amount, bool within) =>
        Assert.Equal(within, MatchTolerance.Within((decimal)expected, (decimal)actual, (decimal)percent, (decimal)amount));

    [Fact]
    public void Landed_costs_are_spread_by_the_basis_to_the_cent()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        LandedLine[] lines = [new(a, 1, 100, 1), new(b, 1, 100, 0), new(c, 1, 100, 0)];
        var byValue = LandedCosts.Spread(lines, 10m, LandedCostBasis.Value);
        Assert.Equal(10m, byValue.Values.Sum());
        Assert.All(byValue.Values, v => Assert.InRange(v, 3.33m, 3.34m));
        var byWeight = LandedCosts.Spread(lines, 10m, LandedCostBasis.Weight);
        Assert.Equal((10m, 0m), (byWeight[a], byWeight[b]));
        // No weights at all: spread by value instead.
        var noWeights = LandedCosts.Spread([new(a, 2, 30, 0), new(b, 8, 10, 0)], 8m, LandedCostBasis.Weight);
        Assert.Equal((6m, 2m), (noWeights[a], noWeights[b]));
        var byQuantity = LandedCosts.Spread([new(a, 2, 30, 0), new(b, 8, 10, 0)], 8m, LandedCostBasis.Quantity);
        Assert.Equal((1.6m, 6.4m), (byQuantity[a], byQuantity[b]));
    }

    [Fact]
    public void Weights_are_kilograms_and_only_the_stock_on_hand_takes_a_share_into_its_cost()
    {
        Assert.Equal(1.5m, LandedCosts.WeightInKilograms(3, 0, 500, "g"));
        Assert.Equal(6m, LandedCosts.WeightInKilograms(3, 2, 500, "g"));   // the shipping weight wins
        Assert.Equal(2m, LandedCosts.WeightInKilograms(2, 0, 1, "L"));
        Assert.Equal(0m, LandedCosts.WeightInKilograms(2, 0, 1, null));
        Assert.Equal(5m, LandedCosts.ToInventory(10m, 10, 5));
        Assert.Equal(10m, LandedCosts.ToInventory(10m, 10, 50));
        Assert.Equal(0m, LandedCosts.ToInventory(10m, 10, -3));
    }

    [Fact]
    public void Reorder_suggestions_cover_lead_time_safety_and_cover_days_in_whole_cases()
    {
        // 3 a day; lead 3 + safety 2 = level 15; 10 available; target 3 × 8 = 24; 14 pieces, two cases of 12.
        var s = ReorderRules.Suggest(new ReorderInputs(90, 30, 3, 2, 3, 10, 0, 0, 0, 0, 12, 0));
        Assert.Equal((3m, 15m, 24m, 24m, 2m), (s.AverageDailySales, s.ReorderLevel, s.TargetStock, s.Pieces, s.OrderQuantity));
        // Enough on hand and on order: nothing.
        Assert.Equal(0m, ReorderRules.Suggest(new ReorderInputs(90, 30, 3, 2, 3, 10, 6, 0, 0, 0, 1, 0)).Pieces);
        // No sales: the reorder point is the floor, the reorder quantity the order.
        var floor = ReorderRules.Suggest(new ReorderInputs(0, 30, 3, 2, 3, 1, 0, 0, 3, 5, 1, 0));
        Assert.Equal((3m, 8m, 7m), (floor.ReorderLevel, floor.TargetStock, floor.Pieces));
        // The supplier's minimum.
        Assert.Equal(50m, ReorderRules.Suggest(new ReorderInputs(30, 30, 3, 2, 3, 0, 0, 0, 0, 0, 1, 50)).Pieces);
        // Customer orders count against what is available.
        Assert.True(ReorderRules.Suggest(new ReorderInputs(30, 30, 3, 2, 3, 20, 0, 18, 0, 0, 1, 0)).Pieces > 0);
        Assert.Equal(0m, ReorderRules.Suggest(new ReorderInputs(0, 30, 3, 2, 3, 0, 0, 0, 0, 0, 1, 0)).Pieces);
    }
}
