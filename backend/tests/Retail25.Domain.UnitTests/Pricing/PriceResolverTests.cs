using FluentAssertions;
using Retail25.Domain.Sales;
using Retail25.Domain.Sales.Pricing;
using Xunit;

namespace Retail25.Domain.UnitTests.Pricing;

/// <summary>
/// The precedence ladder from doc 04 §2. Each test pins one rung and the conditions under which it
/// yields to the one below.
/// </summary>
public class PriceResolverTests
{
    [Fact]
    public void Falls_back_to_the_regular_price_when_no_rule_applies()
    {
        var result = new PricingScenarioBuilder()
            .AddLine(regularPrice: 12.50m)
            .Calculate();

        result.Lines[0].UnitPrice.Should().Be(12.50m);
        result.Lines[0].PriceOrigin.Should().Be(PriceOrigin.Regular);
    }

    [Fact]
    public void Honours_a_manual_price_when_the_operator_may_override()
    {
        var result = new PricingScenarioBuilder()
            .WithPermissions(PricingPermissions.All)
            .AddLine(regularPrice: 12.50m, manualUnitPrice: 9m)
            .Calculate();

        result.Lines[0].UnitPrice.Should().Be(9m);
        result.Lines[0].PriceOrigin.Should().Be(PriceOrigin.Manual);
    }

    [Fact]
    public void Ignores_a_manual_price_when_the_operator_may_not_override()
    {
        // A till where the key does nothing, rather than an error the cashier cannot act on.
        var result = new PricingScenarioBuilder()
            .WithPermissions(PricingPermissions.None)
            .AddLine(regularPrice: 12.50m, manualUnitPrice: 9m)
            .Calculate();

        result.Lines[0].UnitPrice.Should().Be(12.50m);
        result.Lines[0].PriceOrigin.Should().Be(PriceOrigin.Regular);
    }

    [Fact]
    public void Applies_the_highest_qualifying_volume_break()
    {
        var result = new PricingScenarioBuilder()
            .AddLine(
                regularPrice: 10m,
                quantity: 25m,
                priceLevels: [(2, 9m), (3, 8m)],
                priceBreaks: [(2, 10m), (3, 20m)])
            .Calculate();

        result.Lines[0].UnitPrice.Should().Be(8m);
        result.Lines[0].PriceOrigin.Should().Be(PriceOrigin.Break);
        result.Lines[0].ResolvedPriceLevel.Should().Be(3);
    }

    [Fact]
    public void Ignores_a_break_whose_price_level_is_not_set_on_the_item()
    {
        // Guide p.52: a missing level falls through to the regular price rather than erroring.
        var result = new PricingScenarioBuilder()
            .AddLine(regularPrice: 10m, quantity: 25m, priceBreaks: [(3, 20m)])
            .Calculate();

        result.Lines[0].UnitPrice.Should().Be(10m);
        result.Lines[0].PriceOrigin.Should().Be(PriceOrigin.Regular);
    }

    [Fact]
    public void Uses_the_customers_assigned_price_level()
    {
        var result = new PricingScenarioBuilder()
            .WithCustomer(priceLevel: 3)
            .AddLine(regularPrice: 10m, priceLevels: [(3, 7.25m)])
            .Calculate();

        result.Lines[0].UnitPrice.Should().Be(7.25m);
        result.Lines[0].PriceOrigin.Should().Be(PriceOrigin.ClientLevel);
    }

    [Fact]
    public void Falls_through_to_regular_when_the_customers_level_is_not_priced_on_the_item()
    {
        var result = new PricingScenarioBuilder()
            .WithCustomer(priceLevel: 4)
            .AddLine(regularPrice: 10m, priceLevels: [(2, 9m)])
            .Calculate();

        result.Lines[0].UnitPrice.Should().Be(10m);
        result.Lines[0].PriceOrigin.Should().Be(PriceOrigin.Regular);
    }

    [Fact]
    public void Applies_a_sale_price_inside_its_window()
    {
        var result = new PricingScenarioBuilder()
            .OnDate(new DateOnly(2026, 7, 27))
            .AddLine(
                regularPrice: 20m,
                salePricing: (25m, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 31)))
            .Calculate();

        result.Lines[0].UnitPrice.Should().Be(15m);
        result.Lines[0].PriceOrigin.Should().Be(PriceOrigin.Sale);
    }

    [Fact]
    public void Ignores_a_sale_price_outside_its_window()
    {
        var result = new PricingScenarioBuilder()
            .OnDate(new DateOnly(2026, 8, 1))
            .AddLine(
                regularPrice: 20m,
                salePricing: (25m, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 31)))
            .Calculate();

        result.Lines[0].UnitPrice.Should().Be(20m);
        result.Lines[0].PriceOrigin.Should().Be(PriceOrigin.Regular);
    }

    [Fact]
    public void Break_points_outrank_an_active_sale_price()
    {
        // Decision P1: the guide says a sale price applies "unless one of the other pricing
        // features applies" (p.35).
        var result = new PricingScenarioBuilder()
            .OnDate(new DateOnly(2026, 7, 27))
            .AddLine(
                regularPrice: 20m,
                quantity: 12m,
                priceLevels: [(2, 18m)],
                priceBreaks: [(2, 10m)],
                salePricing: (50m, new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)))
            .Calculate();

        result.Lines[0].UnitPrice.Should().Be(18m);
        result.Lines[0].PriceOrigin.Should().Be(PriceOrigin.Break);
    }

    [Fact]
    public void Bonus_pricing_charges_for_fewer_units_than_it_delivers()
    {
        // Buy 3 get 1 free on a quantity of 6: two free, four charged, six leave the shelf.
        var result = new PricingScenarioBuilder()
            .AddLine(regularPrice: 5m, quantity: 6m, bonus: (3m, 1m))
            .Calculate();

        var line = result.Lines[0];
        line.PriceOrigin.Should().Be(PriceOrigin.Bonus);
        line.ChargeableQuantity.Should().Be(4m);
        line.FreeQuantity.Should().Be(2m);
        line.StockQuantity.Should().Be(6m);
        line.NetAmount.Should().Be(20m);
    }

    [Fact]
    public void Random_weight_barcode_derives_quantity_from_the_embedded_price()
    {
        // Guide p.98: quantity = embedded price / unit price. 7.50 of something at 3.00/kg is 2.5 kg.
        var result = new PricingScenarioBuilder()
            .AddLine(
                regularPrice: 3m,
                quantity: 1m,
                embeddedUnitPrice: 7.50m,
                source: PriceSource.RandomWeight)
            .Calculate();

        var line = result.Lines[0];
        line.PriceOrigin.Should().Be(PriceOrigin.RandomWeight);
        line.ChargeableQuantity.Should().Be(2.5m);
        line.NetAmount.Should().Be(7.50m);
    }

    [Fact]
    public void Random_weight_barcode_sells_one_package_when_the_item_has_no_unit_price()
    {
        // Guide p.98: "If the Price 1 is left blank or is zero, a quantity of 1 will be subtracted
        // from the Quantity On Hand for each package sold."
        var result = new PricingScenarioBuilder()
            .AddLine(
                regularPrice: 0m,
                embeddedUnitPrice: 4.99m,
                source: PriceSource.RandomWeight)
            .Calculate();

        result.Lines[0].ChargeableQuantity.Should().Be(1m);
        result.Lines[0].NetAmount.Should().Be(4.99m);
    }

    [Fact]
    public void A_manual_price_on_a_weighed_item_is_treated_as_a_price_per_unit_of_weight()
    {
        // Guide p.98: the override is per pound or kilo, multiplied by the weight the tag implies.
        // 7.50 embedded at a 3.00 shelf price is 2.5 kg; at 2.00/kg that is 3.75 kg for 7.50.
        var result = new PricingScenarioBuilder()
            .WithPermissions(PricingPermissions.All)
            .AddLine(
                regularPrice: 3m,
                embeddedUnitPrice: 7.50m,
                manualUnitPrice: 2m,
                source: PriceSource.RandomWeight)
            .Calculate();

        result.Lines[0].UnitPrice.Should().Be(2m);
        result.Lines[0].ChargeableQuantity.Should().Be(3.75m);
        result.Lines[0].NetAmount.Should().Be(7.50m);
    }
}
