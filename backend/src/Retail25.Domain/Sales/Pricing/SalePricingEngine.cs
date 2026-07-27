using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// Full sale-level pricing pipeline (doc 04 §4). Pure function — no I/O, no clock.
/// PricingContext carries the date. Every monetary field is a snapshot.
/// </summary>
public static class SalePricingEngine
{
    public static SalePricingResult Calculate(
        IReadOnlyList<CartLinePricingInput> lines,
        PricingContext ctx)
    {
        // Step 1: Resolve each line's unit price, qty, discount, tax flags.
        var resolvedLines = new List<ResolvedLine>();
        foreach (var line in lines)
        {
            var resolution = PriceResolver.Resolve(
                line.Input, line.ProductPrices, line.PriceBreaks, line.BonusPricing, line.SalePricing);

            var lineNet = resolution.UnitPrice * resolution.ChargeableQuantity;
            var discountPct = line.Input.ManualDiscountPct ?? (line.CustomerDiscountPct > 0 ? line.CustomerDiscountPct : 0m);
            lineNet -= lineNet * discountPct / 100m;

            resolvedLines.Add(new ResolvedLine(lineNet, discountPct, resolution.Tax1Applies, resolution.Tax2Applies));
        }

        // Step 2: Subtotal
        var subtotal = resolvedLines.Sum(l => l.LineNet);

        // Step 3: Subtotal adjustments
        var adjustments = 0m;
        // Coupons (fixed amounts, guide p.7)
        adjustments += lines.Sum(l => l.CouponAmount);
        // Bottle return credits (guide p.7)
        adjustments += lines.Sum(l => l.BottleReturnCredit);
        // Subtotal discount (F3-F2, guide p.7)
        if (lines.Any(l => l.SubtotalDiscountPct > 0))
        {
            var discPct = lines.First(l => l.SubtotalDiscountPct > 0).SubtotalDiscountPct;
            adjustments += subtotal * discPct / 100m;
        }
        // Loyalty reward — ONLY if no subtotal discount (guide p.84)
        if (!lines.Any(l => l.SubtotalDiscountPct > 0) && ctx.Loyalty.IsEnabled && ctx.Customer is not null)
        {
            var customerPoints = ctx.Customer.RewardPoints;
            if (customerPoints >= ctx.Loyalty.MinimumRequired)
            {
                if (ctx.Loyalty.PercentEnabled && ctx.Loyalty.FixedEnabled)
                {
                    var pctReward = subtotal * ctx.Loyalty.RewardPercent / 100m;
                    var fixedReward = ctx.Loyalty.RewardFixedAmount;
                    adjustments += Math.Min(pctReward, fixedReward);
                }
                else if (ctx.Loyalty.PercentEnabled)
                {
                    adjustments += subtotal * ctx.Loyalty.RewardPercent / 100m;
                }
                else if (ctx.Loyalty.FixedEnabled)
                {
                    adjustments += ctx.Loyalty.RewardFixedAmount;
                }
            }
        }

        // Step 4: Discounted subtotal (floored at 0)
        var discountedSubtotal = Math.Max(0m, subtotal - adjustments);

        // Step 5: Add-on charge (guide p.76)
        var addOnCharge = 0m;
        if (ctx.Policy.ApplyAddOnCharge && ctx.Tax.AddOnChargeEnabled)
        {
            addOnCharge = discountedSubtotal * ctx.Tax.AddOnChargeRate.Rate;
        }

        // Step 6: Tax
        var taxBase1 = 0m;
        var taxBase2 = 0m;
        var tax1Total = 0m;
        var tax2Total = 0m;

        foreach (var line in resolvedLines)
        {
            var proratedDiscount = resolvedLines.Count > 1
                ? line.LineNet / resolvedLines.Sum(l => l.LineNet) * adjustments
                : adjustments;

            var taxableNet = line.LineNet - proratedDiscount;

            if (line.Tax1Applies)
            {
                taxBase1 += taxableNet;
            }
            if (line.Tax2Applies)
            {
                taxBase2 += taxableNet;
            }
        }

        if (ctx.Tax.AddOnChargeEnabled && ctx.Tax.AddOnChargeTaxable)
        {
            if (ctx.Tax.Tax1Enabled)
                taxBase1 += addOnCharge;
            if (ctx.Tax.Tax2Enabled)
                taxBase2 += addOnCharge;
        }

        if (ctx.Tax.Tax1Enabled)
        {
            tax1Total = decimal.Round(taxBase1 * ctx.Tax.Tax1Rate.Rate, 2, MidpointRounding.AwayFromZero);
        }

        if (ctx.Tax.Tax2Enabled)
        {
            var base2 = ctx.Tax.Tax2Compound ? taxBase2 + tax1Total : taxBase2;
            tax2Total = decimal.Round(base2 * ctx.Tax.Tax2Rate.Rate, 2, MidpointRounding.AwayFromZero);
        }

        // Step 7: Grand total
        var grandTotal = discountedSubtotal + addOnCharge + tax1Total + tax2Total;

        return new SalePricingResult(
            subtotal,
            adjustments,
            discountedSubtotal,
            addOnCharge,
            tax1Total,
            tax2Total,
            grandTotal,
            resolvedLines.Count);
    }
}

public sealed record ResolvedLine(
    decimal LineNet,
    decimal DiscountPct,
    bool Tax1Applies,
    bool Tax2Applies);

public sealed record CartLinePricingInput(
    Domain.Sales.Pricing.LineInput Input,
    IReadOnlyList<Domain.Catalog.ProductPrice> ProductPrices,
    IReadOnlyList<Domain.Catalog.PriceBreak> PriceBreaks,
    Domain.Catalog.BonusPricing? BonusPricing,
    Domain.Catalog.SalePricing? SalePricing,
    decimal CustomerDiscountPct,
    decimal CouponAmount,
    decimal BottleReturnCredit,
    decimal SubtotalDiscountPct);

public sealed record SalePricingResult(
    decimal Subtotal,
    decimal Adjustments,
    decimal DiscountedSubtotal,
    decimal AddOnCharge,
    decimal Tax1Total,
    decimal Tax2Total,
    decimal GrandTotal,
    int LineCount);
