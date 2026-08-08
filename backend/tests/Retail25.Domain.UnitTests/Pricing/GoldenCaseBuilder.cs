using System.Globalization;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;
using Retail25.Domain.Sales;
using Retail25.Domain.Sales.Pricing;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.UnitTests.Pricing;

/// <summary>
/// Turns a golden JSON case into the domain objects the engine expects.
/// <para>
/// It goes through the real factories rather than reflecting fields into place, so a case that
/// describes a configuration the domain would refuse to create fails here rather than quietly
/// testing a state no store could ever be in.
/// </para>
/// </summary>
internal static class GoldenCaseBuilder
{
    public static (IReadOnlyList<LineInput> Lines, IReadOnlyList<AdjustmentInput> Adjustments, PricingContext Context) Build(GoldenCase golden)
    {
        var locationId = TestIds.Next();
        var businessDate = DateOnly.Parse(golden.Context.BusinessDate, CultureInfo.InvariantCulture);

        var tax = BuildTax(golden.Context.Tax, locationId, businessDate);
        var policy = BuildPolicy(golden.Context.Policy, locationId);
        var customer = BuildCustomer(golden.Context.Customer);
        var loyalty = BuildLoyalty(golden.Context.Loyalty, locationId);
        var rounding = BuildRounding(golden.Context.Rounding);

        var saleOverride = golden.Context.SaleTaxOverride is { } o
            ? CartTaxOverride.Create(TestIds.Next(), o.Tax1, o.Tax2, o.AppliesFromSequence, TestIds.Next(), DateTimeOffset.UnixEpoch)
            : null;

        var context = new PricingContext(
            businessDate,
            tax,
            policy,
            customer,
            saleOverride,
            loyalty,
            PricingRuleSetting.SeedDefaults(locationId),
            rounding);

        var lines = golden.Lines.Select((line, index) => BuildLine(line, index + 1, locationId)).ToList();

        var adjustments = (golden.Adjustments ?? [])
            .Select(a => new AdjustmentInput(a.Type, a.Label, a.Amount, a.Percent))
            .ToList();

        return (lines, adjustments, context);
    }

    private static TaxConfiguration BuildTax(GoldenTax golden, long locationId, DateOnly businessDate)
        => TaxConfiguration.Create(
            locationId,
            businessDate.AddYears(-1),
            golden.Tax1Enabled,
            golden.Tax1Name,
            new Percentage(golden.Tax1Rate),
            golden.Tax2Enabled,
            golden.Tax2Name,
            new Percentage(golden.Tax2Rate),
            golden.Tax2Compound,
            golden.AddOnChargeEnabled,
            golden.AddOnChargeName,
            new Percentage(golden.AddOnChargeRate),
            golden.AddOnChargeTaxable,
            golden.Inclusive ? TaxationType.Inclusive : TaxationType.Exclusive,
            null).Value;

    private static PosPolicy BuildPolicy(GoldenPolicy golden, long locationId)
    {
        var policy = PosPolicy.CreateDefault(locationId);
        policy.UpdateTaxBehaviour(golden.ApplyTax1, golden.ApplyTax2, golden.AllowTaxOverride, golden.ApplyAddOnCharge);
        policy.UpdateSellingBehaviour(false, true, false, true, golden.StaffMayDiscount, false);
        return policy;
    }

    private static CustomerPricingProfile? BuildCustomer(GoldenCustomer? golden)
    {
        if (golden is null)
        {
            return null;
        }

        var profile = CustomerPricingProfile.Create(TestIds.Next());
        profile.PriceLevel = golden.PriceLevel;
        profile.UsualDiscountPct = golden.UsualDiscountPct;
        profile.ExemptTax1 = golden.ExemptTax1;
        profile.ExemptTax2 = golden.ExemptTax2;
        profile.RewardPoints = golden.RewardPoints;
        return profile;
    }

    private static LoyaltyPolicy? BuildLoyalty(GoldenLoyalty? golden, long locationId)
        => golden is null
            ? null
            : new LoyaltyPolicy
            {
                LocationId = locationId,
                IsEnabled = golden.IsEnabled,
                PointsPerDollar = golden.PointsPerDollar,
                MinimumRequired = golden.MinimumRequired,
                PercentEnabled = golden.PercentEnabled,
                RewardPercent = golden.RewardPercent,
                FixedEnabled = golden.FixedEnabled,
                RewardFixedAmount = golden.RewardFixedAmount,
                SuppressIfSubtotalDiscountApplied = golden.SuppressIfSubtotalDiscountApplied,
            };

    private static MoneyRounding BuildRounding(GoldenRounding? golden)
        => golden is null
            ? MoneyRounding.Retail
            : new MoneyRounding(golden.Scale, MidpointRounding.AwayFromZero, golden.MinimumTender);

    private static LineInput BuildLine(GoldenLine golden, int sequence, long locationId)
    {
        var product = Product.Create(
            locationId,
            golden.StockCode,
            golden.Name,
            golden.ProductType,
            golden.RegularPrice,
            golden.Tax1Applies,
            golden.Tax2Applies).Value;

        product.UpdatePricing(golden.RegularPrice, 0m, golden.AvgCost);

        var prices = (golden.PriceLevels ?? [])
            .Select(kvp => ProductPrice.Create(product.Id, kvp.Key, kvp.Value).Value)
            .ToList();

        var breaks = (golden.PriceBreaks ?? [])
            .Select(kvp => PriceBreak.Create(product.Id, kvp.Key, kvp.Value).Value)
            .ToList();

        var bonus = golden.Bonus is { } b
            ? BonusPricing.Create(product.Id, b.BuyQty, b.FreeQty).Value
            : null;

        var sale = golden.Sale is { } s
            ? SalePricing.Create(
                product.Id,
                s.DiscountPct,
                DateOnly.Parse(s.StartsOn, CultureInfo.InvariantCulture),
                DateOnly.Parse(s.EndsOn, CultureInfo.InvariantCulture)).Value
            : null;

        return new LineInput(
            sequence,
            product,
            null,
            golden.Quantity,
            golden.ManualUnitPrice,
            golden.ManualDiscountPct,
            golden.RequestedPriceLevel,
            golden.Tax1Override,
            golden.Tax2Override,
            golden.LineType,
            golden.Source,
            golden.EmbeddedPrice,
            prices,
            breaks,
            bonus,
            sale,
            golden.AvgCost);
    }
}
