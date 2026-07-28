using FluentAssertions;
using Retail25.Domain.Sales;
using Retail25.Domain.Sales.Pricing;
using Xunit;

namespace Retail25.Domain.UnitTests.Pricing;

/// <summary>
/// The sale-level pipeline (doc 04 §4): subtotal, sale-wide credits in their fixed order,
/// proration onto the tax base, the add-on charge, and the total.
/// </summary>
public class SalePricingEngineTests
{
    [Fact]
    public void An_empty_cart_totals_to_nothing()
    {
        var result = new PricingScenarioBuilder().WithStandardTaxes().Calculate();

        result.Lines.Should().BeEmpty();
        result.Subtotal.Should().Be(0m);
        result.GrandTotal.Should().Be(0m);
    }

    [Fact]
    public void A_line_discount_reduces_both_the_line_and_its_tax()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithPolicy(p => p.UpdateSellingBehaviour(false, true, false, false, staffMayDiscount: true, false))
            .AddLine(regularPrice: 100m, manualDiscountPct: 10m)
            .Calculate();

        result.Lines[0].LineDiscountAmount.Should().Be(10m);
        result.Lines[0].NetAmount.Should().Be(90m);
        result.Tax1Total.Should().Be(4.50m);
        result.GrandTotal.Should().Be(100.80m);
    }

    [Fact]
    public void A_staff_discount_is_ignored_when_the_store_forbids_it()
    {
        // Guide p.77: "Staff May Discount". With it off, the request has no effect.
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithPolicy(p => p.UpdateSellingBehaviour(false, true, false, false, staffMayDiscount: false, false))
            .AddLine(regularPrice: 100m, manualDiscountPct: 10m)
            .Calculate();

        result.Lines[0].LineDiscountAmount.Should().Be(0m);
        result.Subtotal.Should().Be(100m);
    }

    [Fact]
    public void A_customers_usual_discount_applies_without_anyone_typing_it()
    {
        // Guide p.51.
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithCustomer(usualDiscountPct: 15m)
            .AddLine(regularPrice: 200m)
            .Calculate();

        result.Lines[0].LineDiscountPct.Should().Be(15m);
        result.Lines[0].NetAmount.Should().Be(170m);
    }

    [Fact]
    public void A_subtotal_discount_is_spread_across_lines_before_tax_is_charged()
    {
        // Without proration a tax-exempt line would subsidise a taxable one. 20.00 off a 100.00
        // subtotal split evenly across two 50.00 lines leaves 40.00 taxable on each.
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithAdjustments(new SaleAdjustments([], [], new SubtotalDiscount(20m, null), false, false))
            .AddLine(regularPrice: 50m)
            .AddLine(regularPrice: 50m)
            .Calculate();

        result.AdjustmentTotal.Should().Be(20m);
        result.DiscountedSubtotal.Should().Be(80m);
        result.Lines.Should().AllSatisfy(l => l.TaxableAmount.Should().Be(40m));
        result.Tax1Total.Should().Be(4m);
        result.GrandTotal.Should().Be(89.60m);
    }

    [Fact]
    public void Proration_never_loses_or_invents_a_penny()
    {
        // 10.00 across three equal lines cannot divide evenly. The parts must still sum exactly.
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithAdjustments(new SaleAdjustments([], [], new SubtotalDiscount(null, 10m), false, false))
            .AddLine(regularPrice: 10m)
            .AddLine(regularPrice: 10m)
            .AddLine(regularPrice: 10m)
            .Calculate();

        result.Lines.Sum(l => l.AllocatedSubtotalAdjustment).Should().Be(10m);
        result.Lines.Sum(l => l.TaxableAmount).Should().Be(result.DiscountedSubtotal);
    }

    [Fact]
    public void A_coupon_reduces_the_subtotal_and_the_tax_base()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithAdjustments(new SaleAdjustments(
                [new CouponCredit("SAVE5", 5m)], [], null, false, false))
            .AddLine(regularPrice: 100m)
            .Calculate();

        result.AdjustmentTotal.Should().Be(5m);
        result.DiscountedSubtotal.Should().Be(95m);
        result.Tax1Total.Should().Be(4.75m);
        result.GrandTotal.Should().Be(106.40m);
    }

    [Fact]
    public void Credits_cannot_push_a_sale_below_zero()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithAdjustments(new SaleAdjustments(
                [new CouponCredit("TOO BIG", 500m)], [], null, false, false))
            .AddLine(regularPrice: 20m)
            .Calculate();

        result.DiscountedSubtotal.Should().Be(0m);
        result.GrandTotal.Should().Be(0m);
    }

    [Fact]
    public void A_return_line_credits_the_customer_and_reverses_its_tax()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .AddLine(regularPrice: 100m)
            .AddLine(regularPrice: 30m, lineType: LineType.Return)
            .Calculate();

        result.Lines[1].NetAmount.Should().Be(-30m);
        result.Lines[1].Tax1Amount.Should().Be(-1.50m);
        result.Subtotal.Should().Be(70m);
        result.GrandTotal.Should().Be(78.40m);
    }

    [Fact]
    public void A_credit_is_not_absorbed_by_a_return_line()
    {
        // Weighting proration by a negative line would invert its tax. The coupon belongs to the
        // 100.00 that was actually bought.
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithAdjustments(new SaleAdjustments([new CouponCredit("SAVE10", 10m)], [], null, false, false))
            .AddLine(regularPrice: 100m)
            .AddLine(regularPrice: 40m, lineType: LineType.Return)
            .Calculate();

        result.Lines[0].AllocatedSubtotalAdjustment.Should().Be(10m);
        result.Lines[1].AllocatedSubtotalAdjustment.Should().Be(0m);
        result.Lines[1].TaxableAmount.Should().Be(-40m);
    }

    [Fact]
    public void Loyalty_points_accrue_on_the_pre_tax_value_of_the_sale()
    {
        // Guide p.83: one point per dollar spent, before taxes and charges.
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithCustomer(rewardPoints: 0)
            .WithLoyalty(pointsPerDollar: 1m, minimumRequired: 500)
            .AddLine(regularPrice: 149.99m)
            .Calculate();

        result.LoyaltyPointsEarned.Should().Be(149);
    }

    [Fact]
    public void A_reward_is_available_once_the_customer_has_enough_points()
    {
        // Guide p.84's worked example: 10% up to a maximum of 20.00. On 300.00 the 30.00
        // percentage reward is capped at 20.00.
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithCustomer(rewardPoints: 600)
            .WithLoyalty(minimumRequired: 500, percentEnabled: true, rewardPercent: 10m, fixedEnabled: true, rewardFixedAmount: 20m)
            .WithAdjustments(new SaleAdjustments([], [], null, RedeemLoyaltyReward: true, false))
            .AddLine(regularPrice: 300m)
            .Calculate();

        result.AdjustmentTotal.Should().Be(20m);
        result.LoyaltyPointsRedeemed.Should().Be(500);
    }

    [Fact]
    public void A_reward_is_refused_when_the_customer_is_short_of_points()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithCustomer(rewardPoints: 100)
            .WithLoyalty(minimumRequired: 500, percentEnabled: true, rewardPercent: 10m)
            .WithAdjustments(new SaleAdjustments([], [], null, RedeemLoyaltyReward: true, false))
            .AddLine(regularPrice: 300m)
            .Calculate();

        result.AdjustmentTotal.Should().Be(0m);
        result.LoyaltyPointsRedeemed.Should().Be(0);
    }

    [Fact]
    public void A_reward_is_refused_when_a_subtotal_discount_is_already_applied()
    {
        // Guide p.84, verbatim: a reward requires that "there cannot already be a discount applied
        // to the subtotal of the sale".
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithCustomer(rewardPoints: 600)
            .WithLoyalty(minimumRequired: 500, percentEnabled: true, rewardPercent: 10m)
            .WithAdjustments(new SaleAdjustments([], [], new SubtotalDiscount(5m, null), RedeemLoyaltyReward: true, false))
            .AddLine(regularPrice: 300m)
            .Calculate();

        result.LoyaltyPointsRedeemed.Should().Be(0);
        result.AdjustmentTotal.Should().Be(15m);
    }

    [Fact]
    public void No_points_are_earned_on_a_sale_carrying_a_subtotal_discount()
    {
        // Guide p.83: points are still awarded on discounted items, but not where the discount is
        // on the subtotal.
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithCustomer(rewardPoints: 0)
            .WithLoyalty(pointsPerDollar: 1m)
            .WithAdjustments(new SaleAdjustments([], [], new SubtotalDiscount(10m, null), false, false))
            .AddLine(regularPrice: 200m)
            .Calculate();

        result.LoyaltyPointsEarned.Should().Be(0);
    }

    [Fact]
    public void The_add_on_charge_can_be_suspended_for_one_sale()
    {
        // Guide p.11: F11 → F6 Taxes suspends charges for the current sale only.
        var result = new PricingScenarioBuilder()
            .WithTaxes(tax1Rate: 5m, tax2Rate: 0m, addOnEnabled: true, addOnRate: 10m)
            .WithPolicy(p => p.UpdateTaxBehaviour(true, false, true, applyAddOnCharge: true))
            .WithAdjustments(new SaleAdjustments([], [], null, false, SuspendAddOnCharge: true))
            .AddLine(regularPrice: 100m)
            .Calculate();

        result.AddOnCharge.Should().Be(0m);
        result.GrandTotal.Should().Be(105m);
    }

    [Fact]
    public void Tax_is_rounded_once_per_line_rather_than_on_the_running_total()
    {
        // Three lines of 0.33 at 5%: each rounds to 0.02, so the tax is 0.06 — not the 0.05 that
        // rounding a 0.0495 running total would give. This is the penny-drift case.
        var result = new PricingScenarioBuilder()
            .WithTaxes(tax1Rate: 5m, tax2Rate: 0m)
            .AddLine(regularPrice: 0.33m)
            .AddLine(regularPrice: 0.33m)
            .AddLine(regularPrice: 0.33m)
            .Calculate();

        result.Lines.Should().AllSatisfy(l => l.Tax1Amount.Should().Be(0.02m));
        result.Tax1Total.Should().Be(0.06m);
        result.GrandTotal.Should().Be(1.05m);
    }

    [Fact]
    public void A_zero_decimal_currency_rounds_to_whole_units()
    {
        // Currencies with no minor unit are a configuration change, not a code change.
        var result = new PricingScenarioBuilder()
            .WithTaxes(tax1Rate: 10m, tax2Rate: 0m)
            .WithCurrencyScale(scale: 0, minimumTender: 1m)
            .AddLine(regularPrice: 1250m)
            .Calculate();

        result.Tax1Total.Should().Be(125m);
        result.GrandTotal.Should().Be(1375m);
    }

    [Fact]
    public void Totals_are_the_sum_of_their_lines()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .AddLine(regularPrice: 19.99m, quantity: 3m)
            .AddLine(regularPrice: 4.25m, quantity: 7m)
            .AddLine(regularPrice: 100m, tax2Applies: false)
            .Calculate();

        result.Subtotal.Should().Be(result.Lines.Sum(l => l.NetAmount));
        result.Tax1Total.Should().Be(result.Lines.Sum(l => l.Tax1Amount));
        result.Tax2Total.Should().Be(result.Lines.Sum(l => l.Tax2Amount));
        result.GrandTotal.Should().Be(result.DiscountedSubtotal + result.Tax1Total + result.Tax2Total);
    }
}
