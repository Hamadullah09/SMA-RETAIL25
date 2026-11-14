using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Core.Tests;

/// <summary>The exit gate: the tag flags, what a gate read leads to, and one pass through the gate counted once.</summary>
public sealed class GateRulesTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 10, 0, 0);

    [Fact]
    public void Tags_show_flag_1_in_stock_2_sold_and_3_unsold_at_the_gate()
    {
        Assert.Equal(1, TagFlags.Number(ItemTagStatus.InStock));
        Assert.Equal(2, TagFlags.Number(ItemTagStatus.Sold));
        Assert.Equal(3, TagFlags.Number(ItemTagStatus.Unsold));
        Assert.Null(TagFlags.Number(ItemTagStatus.Lost));
        Assert.Equal("1 · In stock, ready to sell", TagFlags.Text(ItemTagStatus.InStock));
        Assert.Equal("2 · Sold", TagFlags.Text(ItemTagStatus.Sold));
        Assert.Equal("3 · Unsold at the gate", TagFlags.Text(ItemTagStatus.Unsold));
        Assert.Equal("Lost", TagFlags.Text(ItemTagStatus.Lost));
    }

    [Fact]
    public void The_new_status_keeps_the_stored_values_of_the_others()
    {
        // Stored by name; the numbers of the existing values do not move either.
        Assert.Equal([0, 1, 2, 3], Enum.GetValues<ItemTagStatus>().Select(s => (int)s));
        Assert.Equal(["InStock", "Sold", "Lost", "Unsold"], Enum.GetNames<ItemTagStatus>());
        Assert.All(Enum.GetNames<ItemTagStatus>(), name => Assert.True(name.Length <= 8));   // the column holds 8 characters
    }

    [Fact]
    public void Unsold_pieces_are_still_the_shops_stock()
    {
        Assert.True(TagFlags.IsShopStock(ItemTagStatus.InStock));
        Assert.True(TagFlags.IsShopStock(ItemTagStatus.Unsold));
        Assert.False(TagFlags.IsShopStock(ItemTagStatus.Sold));
        Assert.False(TagFlags.IsShopStock(ItemTagStatus.Lost));
    }

    [Theory]
    [InlineData(ItemTagStatus.InStock, false, GateOutcome.NewAlarm)]
    [InlineData(ItemTagStatus.Unsold, false, GateOutcome.NewAlarm)]
    [InlineData(ItemTagStatus.Unsold, true, GateOutcome.OpenAlarm)]
    [InlineData(ItemTagStatus.InStock, true, GateOutcome.OpenAlarm)]
    [InlineData(ItemTagStatus.Sold, false, GateOutcome.Sold)]
    [InlineData(ItemTagStatus.Sold, true, GateOutcome.Sold)]
    [InlineData(ItemTagStatus.Lost, false, GateOutcome.Lost)]
    public void A_gate_read_sounds_only_for_a_piece_that_was_not_sold(ItemTagStatus status, bool openAlarm, GateOutcome expected)
    {
        Assert.Equal(expected, GateRules.Decide(status, openAlarm));
        Assert.Equal(expected is GateOutcome.NewAlarm or GateOutcome.OpenAlarm, GateRules.Sounds(expected));
    }

    [Fact]
    public void An_unknown_code_sounds_no_alarm()
    {
        Assert.Equal(GateOutcome.Unknown, GateRules.Decide(null, false));
        Assert.False(GateRules.Sounds(GateOutcome.Unknown));
        Assert.False(GateRules.Sounds(GateOutcome.Repeated));
    }

    [Fact]
    public void Resolving_puts_the_tag_back_to_flag_1_or_writes_it_off()
    {
        Assert.Equal(ItemTagStatus.InStock, GateRules.StatusAfter(GateAlarmResolution.BackInStock));
        Assert.Equal(ItemTagStatus.Lost, GateRules.StatusAfter(GateAlarmResolution.LeftStore));
        Assert.Equal(ItemTagStatus.Sold, GateRules.StatusAfter(GateAlarmResolution.SoldAfterwards));
        Assert.Equal("Left the store", GateRules.Describe(GateAlarmResolution.LeftStore));
    }

    [Fact]
    public void Reads_of_one_pass_through_the_gate_count_once()
    {
        var gate = new GateDebouncer(TimeSpan.FromSeconds(10));
        Assert.True(gate.IsNewPass("E200AA", T0));
        Assert.False(gate.IsNewPass("E200AA", T0.AddSeconds(1)));
        Assert.True(gate.IsNewPass("E200BB", T0.AddSeconds(1)));   // another tag passing together
        // A tag that stays in the field keeps being read: still the same pass.
        Assert.False(gate.IsNewPass("E200AA", T0.AddSeconds(9)));
        Assert.False(gate.IsNewPass("E200AA", T0.AddSeconds(18)));
        // Out of the field for the quiet time, then read again: a new pass.
        Assert.True(gate.IsNewPass("E200AA", T0.AddSeconds(30)));
        // A late read from before the last one is not a new pass.
        Assert.False(gate.IsNewPass("E200AA", T0.AddSeconds(25)));
    }
}
