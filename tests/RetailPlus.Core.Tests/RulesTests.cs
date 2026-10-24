using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Core.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(1.005, 1.01)]
    [InlineData(-1.005, -1.01)]
    [InlineData(2.004, 2.00)]
    public void Round_goes_half_away_from_zero(decimal amount, decimal expected) => Assert.Equal(expected, Money.Round(amount));

    [Fact]
    public void Margin_is_profit_over_price() => Assert.Equal(40m, Money.MarginPercent(10m, 6m));

    [Fact]
    public void Suggested_price_gives_the_margin() => Assert.Equal(10m, Money.SuggestPrice(6m, 40m));

    [Fact]
    public void Suggest_price_rejects_margin_of_100() => Assert.Throws<ArgumentOutOfRangeException>(() => Money.SuggestPrice(5m, 100m));

    [Theory]
    [InlineData(10.02, 0.05, 10.00)]
    [InlineData(10.03, 0.05, 10.05)]
    [InlineData(10.03, 0.01, 10.03)]
    [InlineData(-4.98, 0.05, -5.00)]
    public void Cash_rounding_to_minimum_tender(decimal amount, decimal tender, decimal expected) =>
        Assert.Equal(expected, Money.RoundToMinimumTender(amount, tender));
}

public class PricingEngineTests
{
    private static Item Item() => new()
    {
        Plu = "T1", Name = "Test", Price1 = 10m, Price2 = 9m, Price3 = 8m, Price4 = 7m,
        BreakQuantity1 = 6, BreakQuantity2 = 12, BreakQuantity3 = 24,
    };

    private static readonly DateOnly Today = new(2026, 10, 24);

    [Theory]
    [InlineData(1, 10)]
    [InlineData(6, 9)]
    [InlineData(12, 8)]
    [InlineData(30, 7)]
    public void Break_points_pick_the_price_level(decimal qty, decimal expected) =>
        Assert.Equal(expected, PricingEngine.Price(Item(), qty, Today).UnitPrice);

    [Fact]
    public void Client_price_level_beats_break_points() =>
        Assert.Equal(8m, PricingEngine.Price(Item(), 30, Today, clientPriceLevel: 3).UnitPrice);

    [Fact]
    public void Sale_discount_applies_only_between_its_dates()
    {
        var item = Item();
        item.SalePrice = 20;
        item.SaleStart = Today.AddDays(-1);
        item.SaleEnd = Today.AddDays(1);
        Assert.Equal(2m, PricingEngine.Price(item, 1, Today).DiscountAmount);
        Assert.Equal(0m, PricingEngine.Price(item, 1, Today.AddDays(5)).DiscountAmount);
    }

    [Fact]
    public void Buy_two_get_one_free()
    {
        var item = Item();
        item.BreakQuantity1 = item.BreakQuantity2 = item.BreakQuantity3 = 0;
        item.BonusPricing = true;
        item.BonusBuyQuantity = 2;
        item.BonusGetQuantity = 1;
        var r = PricingEngine.Price(item, 6, Today);
        Assert.Equal(20m, r.DiscountAmount);
        Assert.Equal(40m, r.LineTotal(6));
    }

    [Fact]
    public void Client_discount_comes_off_what_is_left()
    {
        var r = PricingEngine.Price(Item(), 1, Today, clientDiscountPercent: 10);
        Assert.Equal(9m, r.LineTotal(1));
    }
}

public class SaleCalculatorTests
{
    private static readonly TaxSetup Gst = new("GST", 5m, "PST", 7m, false, false);

    [Fact]
    public void Two_taxes_on_taxable_lines()
    {
        var t = SaleCalculator.Calculate([new CalcLine(100m, true, true), new CalcLine(50m, false, false)], Gst);
        Assert.Equal(150m, t.Subtotal);
        Assert.Equal(5m, t.Tax1);
        Assert.Equal(7m, t.Tax2);
        Assert.Equal(162m, t.Total);
    }

    [Fact]
    public void Compound_tax_includes_tax_1_in_the_base_of_tax_2()
    {
        var t = SaleCalculator.Calculate([new CalcLine(100m, true, true)], Gst with { Compound = true });
        Assert.Equal(5m, t.Tax1);
        Assert.Equal(7.35m, t.Tax2);
    }

    [Fact]
    public void Inclusive_prices_contain_the_tax()
    {
        var vat = new TaxSetup("VAT", 20m, null, 0, false, true);
        var t = SaleCalculator.Calculate([new CalcLine(120m, true, false)], vat);
        Assert.Equal(20m, t.Tax1);
        Assert.Equal(120m, t.Total);
    }

    [Fact]
    public void Subtotal_discount_lowers_the_taxes()
    {
        var t = SaleCalculator.Calculate([new CalcLine(100m, true, true)], Gst, subtotalDiscountPercent: 10);
        Assert.Equal(10m, t.SubtotalDiscount);
        Assert.Equal(4.5m, t.Tax1);
        Assert.Equal(6.3m, t.Tax2);
        Assert.Equal(100.8m, t.Total);
    }

    [Fact]
    public void Exemption_switches_a_tax_off()
    {
        var t = SaleCalculator.Calculate([new CalcLine(100m, true, true)], Gst, exemptTax2: true);
        Assert.Equal(0m, t.Tax2);
        Assert.Equal(105m, t.Total);
    }

    [Fact]
    public void Draft_skips_text_lines()
    {
        var draft = new SaleDraft
        {
            Lines =
            [
                new SaleDraftLine { Description = "Item", Quantity = 2, UnitPrice = 5m, Taxable1 = true },
                new SaleDraftLine { Kind = SaleLineKind.Text, Description = "Gift wrap please" },
            ],
        };
        var t = draft.Calculate(Gst);
        Assert.Equal(10m, t.Subtotal);
        Assert.Single(t.LineTaxes);
    }
}

public class LoyaltyAndCreditTests
{
    private static StoreSettings Points() => new()
    {
        BonusPointsEnabled = true, PointsPerDollar = 1, PointsRequired = 100, PercentDiscountReward = true, RewardPercent = 10, FixedDiscountReward = true, RewardAmount = 20,
    };

    [Fact]
    public void Points_are_not_earned_with_a_subtotal_discount()
    {
        Assert.Equal(50m, BonusPoints.Earned(Points(), 50m, false));
        Assert.Equal(0m, BonusPoints.Earned(Points(), 50m, true));
    }

    [Fact]
    public void Percent_reward_is_capped_by_the_fixed_amount()
    {
        Assert.Equal(5m, BonusPoints.RewardAmount(Points(), 50m));
        Assert.Equal(20m, BonusPoints.RewardAmount(Points(), 500m));
        Assert.True(BonusPoints.QualifiesForReward(Points(), 120, false));
        Assert.False(BonusPoints.QualifiesForReward(Points(), 80, false));
    }

    [Fact]
    public void Late_charge_after_the_grace_period()
    {
        var invoiceDate = new DateOnly(2026, 1, 1);
        Assert.Equal((0, 0m), LateCharges.Calculate(1000m, invoiceDate, null, invoiceDate.AddDays(20), 2m, 30));
        var (months, penalty) = LateCharges.Calculate(1000m, invoiceDate, null, invoiceDate.AddDays(65), 2m, 30);
        Assert.Equal(2, months);
        Assert.Equal(40m, penalty);
    }

    [Fact]
    public void Payment_goes_to_the_late_charge_first()
    {
        Assert.Equal((15m, 85m), LateCharges.SplitPayment(100m, 15m));
        Assert.Equal((10m, 0m), LateCharges.SplitPayment(10m, 15m));
    }

    [Fact]
    public void Reorder_point_method_orders_the_reorder_quantity()
    {
        var item = new Item { OnHand = 2, ReorderPoint = 5, ReorderQuantity = 12, BaseStock = 20 };
        Assert.Equal(12m, Reordering.SuggestedPieces(OrderMethod.ReorderPoints, item, 0, 0));
        item.OnHand = 6;
        Assert.Equal(0m, Reordering.SuggestedPieces(OrderMethod.ReorderPoints, item, 0, 0));
    }

    [Fact]
    public void Weekly_methods_subtract_stock_and_add_customer_orders()
    {
        var item = new Item { OnHand = 3, OnOrder = 2, CustomerOrdered = 1 };
        Assert.Equal(6m, Reordering.SuggestedPieces(OrderMethod.OneWeek, item, 10, 0));
        Assert.Equal(16m, Reordering.SuggestedPieces(OrderMethod.TwoWeeks, item, 10, 0));
    }

    [Theory]
    [InlineData(25, 12, 2)]
    [InlineData(5, 12, 1)]
    [InlineData(7, 1, 7)]
    [InlineData(0, 12, 0)]
    public void Order_quantity_rounds_to_cases(decimal pieces, decimal caseQty, decimal expected) =>
        Assert.Equal(expected, Reordering.ToOrderQuantity(pieces, caseQty));

    [Fact]
    public void Average_cost_is_weighted_by_quantity() =>
        Assert.Equal(5m, Reordering.AverageCostAfterReceipt(10, 4m, 10, 6m, 0));

    [Fact]
    public void Random_weight_barcode_carries_code_and_price()
    {
        Assert.True(RandomWeightBarcode.TryParse("212345012999", out var code, out var price));
        Assert.Equal("12345", code);
        Assert.Equal(12.99m, price);
        Assert.Equal(2.165m, RandomWeightBarcode.Quantity(12.99m, 6m));
        Assert.False(RandomWeightBarcode.TryParse("012345012999", out _, out _));
    }
}
