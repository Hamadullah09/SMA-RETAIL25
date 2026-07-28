using CsCheck;
using FluentAssertions;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales.Pricing;
using Xunit;

namespace Retail25.Domain.UnitTests.Pricing;

/// <summary>
/// Properties that must hold for every cart, not just the ones someone thought to write a test for
/// (doc 04 §8). These are the invariants a rounding bug breaks first.
/// </summary>
public class PricingInvariantTests
{
    private const int Iterations = 500;

    [Fact]
    public void Sale_tax_always_equals_the_sum_of_line_tax()
    {
        Gen.Int[1, 50_000].Array[1, 8].Sample(
            cents =>
            {
                var builder = new PricingScenarioBuilder().WithStandardTaxes();

                foreach (var value in cents)
                {
                    builder.AddLine(regularPrice: value / 100m);
                }

                var result = builder.Calculate();

                result.Tax1Total.Should().Be(result.Lines.Sum(l => l.Tax1Amount));
                result.Tax2Total.Should().Be(result.Lines.Sum(l => l.Tax2Amount));
            },
            iter: Iterations);
    }

    [Fact]
    public void The_grand_total_always_reconciles_to_its_parts()
    {
        Gen.Int[1, 50_000].Array[1, 8].Sample(
            cents =>
            {
                var builder = new PricingScenarioBuilder().WithStandardTaxes();

                foreach (var value in cents)
                {
                    builder.AddLine(regularPrice: value / 100m);
                }

                var result = builder.Calculate();

                var expected = result.DiscountedSubtotal + result.AddOnCharge + result.Tax1Total + result.Tax2Total;
                result.GrandTotal.Should().Be(expected);
            },
            iter: Iterations);
    }

    [Fact]
    public void A_subtotal_discount_is_always_allocated_in_full()
    {
        // The residue-to-largest-line rule exists so this holds exactly, at any line count and any
        // discount that does not divide evenly.
        Gen.Select(Gen.Int[1, 50_000].Array[1, 8], Gen.Int[1, 90]).Sample(
            input =>
            {
                var (cents, discountPercent) = input;

                var builder = new PricingScenarioBuilder()
                    .WithStandardTaxes()
                    .WithAdjustments(new SaleAdjustments([], [], new SubtotalDiscount(discountPercent, null), false, false));

                foreach (var value in cents)
                {
                    builder.AddLine(regularPrice: value / 100m);
                }

                var result = builder.Calculate();

                result.Lines.Sum(l => l.AllocatedSubtotalAdjustment).Should().Be(result.AdjustmentTotal);
                result.Lines.Sum(l => l.TaxableAmount).Should().Be(result.DiscountedSubtotal);
            },
            iter: Iterations);
    }

    [Fact]
    public void Tax_inclusive_pricing_always_charges_exactly_the_sticker_price()
    {
        // The whole point of inclusive pricing: what is on the label is what is paid, whatever the
        // back-solved split between net and tax works out to.
        Gen.Int[1, 50_000].Array[1, 6].Sample(
            cents =>
            {
                var builder = new PricingScenarioBuilder()
                    .WithTaxes(tax1Rate: 5m, tax2Rate: 7m, taxationType: TaxationType.Inclusive);

                var sticker = 0m;
                foreach (var value in cents)
                {
                    var price = value / 100m;
                    sticker += price;
                    builder.AddLine(regularPrice: price);
                }

                builder.Calculate().GrandTotal.Should().Be(sticker);
            },
            iter: Iterations);
    }

    [Fact]
    public void Tax_is_never_charged_on_a_line_that_is_not_taxable()
    {
        Gen.Int[1, 50_000].Array[1, 6].Sample(
            cents =>
            {
                var builder = new PricingScenarioBuilder().WithStandardTaxes();

                foreach (var value in cents)
                {
                    builder.AddLine(regularPrice: value / 100m, tax1Applies: false, tax2Applies: false);
                }

                var result = builder.Calculate();

                result.Tax1Total.Should().Be(0m);
                result.Tax2Total.Should().Be(0m);
                result.GrandTotal.Should().Be(result.DiscountedSubtotal);
            },
            iter: Iterations);
    }

    [Fact]
    public void Money_is_never_created_or_destroyed_by_a_credit()
    {
        // Whatever the credits come to, the discounted subtotal plus what was credited must equal
        // the subtotal — unless the credits exceeded the sale, which is floored at zero.
        Gen.Select(Gen.Int[100, 50_000].Array[1, 6], Gen.Int[1, 10_000]).Sample(
            input =>
            {
                var (cents, couponCents) = input;

                var builder = new PricingScenarioBuilder()
                    .WithStandardTaxes()
                    .WithAdjustments(new SaleAdjustments(
                        [new CouponCredit("COUPON", couponCents / 100m)], [], null, false, false));

                foreach (var value in cents)
                {
                    builder.AddLine(regularPrice: value / 100m);
                }

                var result = builder.Calculate();

                (result.DiscountedSubtotal + result.AdjustmentTotal).Should().Be(result.Subtotal);
            },
            iter: Iterations);
    }
}
