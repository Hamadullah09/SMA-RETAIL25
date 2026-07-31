using FluentAssertions;
using Retail25.Domain.Staff;
using Xunit;

namespace Retail25.Domain.UnitTests.Staff;

/// <summary>
/// What a line pays. Rule precedence and the three payment shapes are the part people argue about at
/// payroll, so every case here is a figure worked out by hand.
/// </summary>
public sealed class CommissionCalculatorTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid Widget = Guid.NewGuid();
    private static readonly Guid Gadget = Guid.NewGuid();
    private static readonly Guid Hardware = Guid.NewGuid();

    private static CommissionRule Rule(
        CommissionType type, decimal value, Guid? productId = null, Guid? departmentId = null, decimal? max = null)
        => CommissionRule.Create(Staff, type, value, productId, departmentId, max).Value;

    private static CommissionableLine Line(decimal net = 100m, decimal cost = 60m, decimal quantity = 1m)
        => new(Widget, Hardware, quantity, net, cost);

    /* ---------------------------------------------------------------------------------------------
     * The three shapes
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public void A_percentage_pays_a_share_of_the_takings()
        => CommissionCalculator.Award(Rule(CommissionType.Percentage, 5m), Line(net: 100m))!
            .Amount.Should().Be(5.00m);

    /// <summary>Per unit, not per line — three of an item pays three times.</summary>
    [Fact]
    public void A_fixed_rate_pays_per_unit()
        => CommissionCalculator.Award(Rule(CommissionType.Fixed, 2.50m), Line(quantity: 3m))!
            .Amount.Should().Be(7.50m);

    [Fact]
    public void Percent_of_profit_pays_on_the_margin()
        => CommissionCalculator.Award(Rule(CommissionType.PercentOfProfit, 20m), Line(net: 100m, cost: 60m))!
            .Amount.Should().Be(8.00m, because: "40 of margin at 20% is 8");

    /// <summary>
    /// Paying nothing on a line sold at or below cost is the point of paying on margin. Otherwise a
    /// discount to clear old stock would pay commission out of a loss.
    /// </summary>
    [Fact]
    public void Percent_of_profit_pays_nothing_on_a_line_sold_at_a_loss()
        => CommissionCalculator.Award(Rule(CommissionType.PercentOfProfit, 20m), Line(net: 50m, cost: 60m))
            .Should().BeNull();

    [Fact]
    public void Percent_of_profit_pays_nothing_at_exactly_cost()
        => CommissionCalculator.Award(Rule(CommissionType.PercentOfProfit, 20m), Line(net: 60m, cost: 60m))
            .Should().BeNull();

    [Fact]
    public void An_award_is_rounded_to_the_penny()
        => CommissionCalculator.Award(Rule(CommissionType.Percentage, 3.33m), Line(net: 99.99m))!
            .Amount.Should().Be(3.33m, because: "3.329667 rounds to 3.33");

    [Fact]
    public void A_rate_that_works_out_to_nothing_writes_nothing()
        => CommissionCalculator.Award(Rule(CommissionType.Percentage, 0.001m), Line(net: 1m))
            .Should().BeNull();

    /* ---------------------------------------------------------------------------------------------
     * The cap
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public void The_cap_cuts_a_large_award_down()
    {
        var award = CommissionCalculator.Award(Rule(CommissionType.Percentage, 10m, max: 5m), Line(net: 200m))!;

        award.Amount.Should().Be(5m);
        award.WasCapped.Should().BeTrue();
    }

    [Fact]
    public void An_award_under_the_cap_is_not_marked_capped()
    {
        var award = CommissionCalculator.Award(Rule(CommissionType.Percentage, 10m, max: 500m), Line(net: 200m))!;

        award.Amount.Should().Be(20m);
        award.WasCapped.Should().BeFalse();
    }

    /// <summary>
    /// The cap is a ceiling on what is earned, so it bounds the magnitude. Capping only the positive
    /// side would let a return claw back more than the sale ever paid.
    /// </summary>
    [Fact]
    public void The_cap_bounds_a_clawback_as_well_as_a_payment()
    {
        var award = CommissionCalculator.Award(Rule(CommissionType.Percentage, 10m, max: 5m), Line(net: -200m))!;

        award.Amount.Should().Be(-5m);
        award.WasCapped.Should().BeTrue();
    }

    [Fact]
    public void A_return_pays_a_negative_award()
        => CommissionCalculator.Award(Rule(CommissionType.Percentage, 5m), Line(net: -100m))!
            .Amount.Should().Be(-5.00m);

    /// <summary>A returned unit takes back exactly what selling it paid.</summary>
    [Fact]
    public void A_returned_unit_takes_back_what_it_paid_on_a_fixed_rate()
        => CommissionCalculator.Award(Rule(CommissionType.Fixed, 2.50m), Line(net: -20m, quantity: -1m))!
            .Amount.Should().Be(-2.50m);

    /* ---------------------------------------------------------------------------------------------
     * Precedence
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public void An_item_rule_beats_a_department_rule()
    {
        var rules = new[]
        {
            Rule(CommissionType.Percentage, 10m, departmentId: Hardware),
            Rule(CommissionType.Percentage, 2m, productId: Widget),
        };

        CommissionCalculator.Resolve(rules, Line())!.ProductId.Should().Be(Widget);
    }

    [Fact]
    public void A_department_rule_beats_the_staff_wide_rate()
    {
        var rules = new[]
        {
            Rule(CommissionType.Percentage, 10m),
            Rule(CommissionType.Percentage, 2m, departmentId: Hardware),
        };

        CommissionCalculator.Resolve(rules, Line())!.DepartmentId.Should().Be(Hardware);
    }

    [Fact]
    public void The_staff_wide_rate_applies_when_nothing_more_specific_does()
    {
        var rules = new[]
        {
            Rule(CommissionType.Percentage, 4m),
            Rule(CommissionType.Percentage, 10m, productId: Gadget),
        };

        var resolved = CommissionCalculator.Resolve(rules, Line())!;

        resolved.ProductId.Should().BeNull();
        resolved.DepartmentId.Should().BeNull();
    }

    [Fact]
    public void A_rule_for_another_department_does_not_apply()
    {
        var rules = new[] { Rule(CommissionType.Percentage, 10m, departmentId: Guid.NewGuid()) };

        CommissionCalculator.Resolve(rules, Line()).Should().BeNull();
    }

    [Fact]
    public void An_item_with_no_department_still_matches_the_staff_wide_rate()
    {
        var rules = new[] { Rule(CommissionType.Percentage, 4m) };
        var unfiled = new CommissionableLine(Widget, null, 1m, 100m, 60m);

        CommissionCalculator.Resolve(rules, unfiled).Should().NotBeNull();
    }

    [Fact]
    public void An_inactive_rule_is_ignored()
    {
        var rule = Rule(CommissionType.Percentage, 10m, productId: Widget);
        rule.Update(CommissionType.Percentage, 10m, null, isActive: false);

        CommissionCalculator.Resolve([rule], Line()).Should().BeNull();
    }

    /// <summary>
    /// Where two rules tie on specificity the more generous wins. Paying the smaller of two rates
    /// that both apply is the kind of thing found months later.
    /// </summary>
    [Fact]
    public void A_tie_on_specificity_goes_to_the_more_generous_rule()
    {
        var rules = new[]
        {
            Rule(CommissionType.Percentage, 3m, productId: Widget),
            Rule(CommissionType.Percentage, 7m, productId: Widget),
        };

        CommissionCalculator.Resolve(rules, Line())!.Value.Should().Be(7m);
    }

    [Fact]
    public void No_rules_at_all_pays_nothing()
        => CommissionCalculator.Award(Array.Empty<CommissionRule>(), Line()).Should().BeNull();

    /* ---------------------------------------------------------------------------------------------
     * The rule itself
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public void A_rule_cannot_target_an_item_and_a_department_at_once()
        => CommissionRule.Create(Staff, CommissionType.Percentage, 5m, Widget, Hardware)
            .Error.Should().Be(CommissionRule.TooSpecific);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_rule_has_to_pay_something(decimal value)
        => CommissionRule.Create(Staff, CommissionType.Percentage, value).Error.Should().Be(CommissionRule.ValueRequired);

    /// <summary>A rate meant as 5 typed as 500 pays out more than the sale brought in.</summary>
    [Theory]
    [InlineData(CommissionType.Percentage)]
    [InlineData(CommissionType.PercentOfProfit)]
    public void A_percentage_over_a_hundred_is_refused(CommissionType type)
        => CommissionRule.Create(Staff, type, 500m).Error.Code
            .Should().Be(CommissionRule.PercentageOutOfRange.Code);

    /// <summary>A fixed rate of £500 an item is unusual but not nonsense — a jeweller's, say.</summary>
    [Fact]
    public void A_large_fixed_rate_is_allowed()
        => CommissionRule.Create(Staff, CommissionType.Fixed, 500m).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData(null, null, 0)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 2)]
    public void Specificity_reads_off_what_the_rule_targets(bool? hasProduct, bool? hasDepartment, int expected)
    {
        var rule = Rule(
            CommissionType.Percentage,
            5m,
            hasProduct == true ? Widget : null,
            hasDepartment == true ? Hardware : null);

        rule.Specificity.Should().Be(expected);
    }
}
