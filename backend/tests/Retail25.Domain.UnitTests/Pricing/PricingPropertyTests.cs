using CsCheck;
using FluentAssertions;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales;
using Retail25.Domain.Sales.Pricing;
using Retail25.Domain.ValueObjects;
using Xunit;

namespace Retail25.Domain.UnitTests.Pricing;

/// <summary>
/// Invariants that must hold for every cart, not just the ones somebody thought to write down
/// (doc 04 §8). Golden files pin known answers; these pin the relationships between them, which is
/// what catches the case nobody imagined.
/// </summary>
public sealed class PricingPropertyTests
{
    private static readonly long LocationId = TestIds.Next();

    /// <summary>Prices and quantities a real catalogue would contain, at realistic magnitudes.</summary>
    private static Gen<decimal> Price => Gen.Int[1, 500_00].Select(cents => cents / 100m);

    private static Gen<decimal> Quantity => Gen.Int[1, 20].Select(q => (decimal)q);

    [Fact]
    public void Line_taxes_always_sum_to_the_sale_taxes()
    {
        Gen.Select(Gen.Int[1, 6], Price, Quantity)
            .Sample(input =>
            {
                var (lineCount, price, quantity) = input;
                var lines = BuildLines(lineCount, price, quantity);

                var result = SalePricingEngine.Calculate(lines, [], BuildContext());

                result.Lines.Sum(l => l.Tax1Amount).Should().Be(result.Tax1Total);
                result.Lines.Sum(l => l.Tax2Amount).Should().Be(result.Tax2Total);
            }, iter: 200);
    }

    [Fact]
    public void Grand_total_is_the_discounted_subtotal_plus_charge_and_taxes()
    {
        Gen.Select(Gen.Int[1, 6], Price, Quantity)
            .Sample(input =>
            {
                var (lineCount, price, quantity) = input;
                var lines = BuildLines(lineCount, price, quantity);

                var result = SalePricingEngine.Calculate(lines, [], BuildContext());

                result.GrandTotal.Should().Be(
                    result.DiscountedSubtotal + result.AddOnCharge + result.Tax1Total + result.Tax2Total);
            }, iter: 200);
    }

    /// <summary>
    /// The proration must lose nothing and invent nothing: the shares have to add up to the discount
    /// exactly, which is the property the residue-to-largest-line rule exists to guarantee.
    /// </summary>
    [Fact]
    public void Prorated_adjustments_sum_exactly_to_the_adjustment_total()
    {
        Gen.Select(Gen.Int[2, 8], Price, Gen.Int[1, 90])
            .Sample(input =>
            {
                var (lineCount, price, discountPct) = input;
                var lines = BuildLines(lineCount, price, 1m);

                var adjustments = new[]
                {
                    new AdjustmentInput(AdjustmentType.SubtotalDiscount, "Test", 0m, discountPct),
                };

                var result = SalePricingEngine.Calculate(lines, adjustments, BuildContext());

                result.Lines.Sum(l => l.ProratedAdjustment).Should().Be(result.AdjustmentTotal);
            }, iter: 200);
    }

    [Fact]
    public void An_adjustment_never_drives_a_positive_sale_below_zero()
    {
        Gen.Select(Price, Gen.Int[1, 100_000].Select(c => c / 100m))
            .Sample(input =>
            {
                var (price, couponAmount) = input;
                var lines = BuildLines(1, price, 1m);

                var adjustments = new[] { new AdjustmentInput(AdjustmentType.Coupon, "Test", couponAmount, 0m) };

                var result = SalePricingEngine.Calculate(lines, adjustments, BuildContext());

                result.DiscountedSubtotal.Should().BeGreaterThanOrEqualTo(0m);
                result.GrandTotal.Should().BeGreaterThanOrEqualTo(0m);
            }, iter: 200);
    }

    /// <summary>
    /// Tax-inclusive pricing must return the sticker price. If it does not, the store either
    /// over-collects or eats the difference on every single sale.
    /// </summary>
    [Fact]
    public void Tax_inclusive_pricing_round_trips_to_the_sticker_price()
    {
        Gen.Select(Price, Quantity)
            .Sample(input =>
            {
                var (price, quantity) = input;
                var lines = BuildLines(1, price, quantity);

                var result = SalePricingEngine.Calculate(lines, [], BuildContext(inclusive: true));

                result.GrandTotal.Should().Be(result.Subtotal);
            }, iter: 200);
    }

    [Fact]
    public void Cash_rounding_never_moves_more_than_half_the_smallest_coin()
    {
        var rounding = new MoneyRounding(2, MidpointRounding.AwayFromZero, 0.05m);

        Gen.Int[0, 1_000_00].Select(cents => cents / 100m)
            .Sample(amount =>
            {
                var rounded = rounding.RoundCash(amount);
                Math.Abs(rounded - amount).Should().BeLessThanOrEqualTo(0.025m);
            }, iter: 500);
    }

    private static List<LineInput> BuildLines(int count, decimal price, decimal quantity)
    {
        var lines = new List<LineInput>(count);

        for (var i = 0; i < count; i++)
        {
            var product = Product.Create(LocationId, $"SKU{i:D4}", $"Item {i}", ProductType.Standard, price).Value;

            lines.Add(new LineInput(
                TestIds.Next(),
                i + 1,
                product,
                null,
                quantity,
                null,
                null,
                null,
                null,
                null,
                LineType.Sale,
                LineSource.StockCode));
        }

        return lines;
    }

    private static PricingContext BuildContext(bool inclusive = false)
    {
        var tax = TaxConfiguration.Create(
            LocationId,
            new DateOnly(2020, 1, 1),
            true, "GST", new Percentage(5m),
            true, "PST", new Percentage(7m),
            false,
            false, "Service", Percentage.Zero, false,
            inclusive ? TaxationType.Inclusive : TaxationType.Exclusive,
            null).Value;

        return new PricingContext(
            new DateOnly(2026, 7, 28),
            tax,
            PosPolicy.CreateDefault(LocationId),
            null,
            null,
            null,
            PricingRuleSetting.SeedDefaults(LocationId),
            MoneyRounding.Retail);
    }
}
