using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// The sale-level pipeline, run in the strict order documented at doc 04 §4. Pure: no I/O, no clock,
/// no randomness. Given the same context and lines it returns the same money forever, which is what
/// makes the golden-file suite meaningful.
/// </summary>
public static class SalePricingEngine
{
    public static SalePricingResult Calculate(
        IReadOnlyList<LineInput> lines,
        IReadOnlyList<AdjustmentInput> adjustments,
        PricingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lines ??= [];
        adjustments ??= [];

        var rounding = context.Rounding;
        var tax = context.Tax;

        // 1 — resolve unit price, quantity, discount and tax flags per line (§2, §3).
        var priced = new List<PricedLine>(lines.Count);
        foreach (var line in lines)
        {
            priced.Add(PriceLine(line, context));
        }

        // 2 — subtotal.
        var subtotal = rounding.Round(priced.Sum(p => p.LineNet));

        // 3 — sale-level adjustments, in the documented order.
        var applied = ApplyAdjustments(adjustments, subtotal, context, out var adjustmentTotal);

        // 4 — an adjustment can take the sale down to zero but never below it: a coupon larger than
        // the basket is a discount, not a payout. The cap is on the adjustment rather than on the
        // subtotal, because a refund-only sale has a legitimately negative subtotal and flooring
        // that would turn a refund into a zero-value transaction.
        adjustmentTotal = Math.Min(adjustmentTotal, Math.Max(0m, subtotal));
        var discountedSubtotal = rounding.Round(subtotal - adjustmentTotal);

        // 5 — percentage add-on charge on the discounted subtotal (guide p.76).
        var addOnCharge = 0m;
        if (context.Policy.ApplyAddOnCharge && tax.AddOnChargeEnabled)
        {
            addOnCharge = rounding.Round(discountedSubtotal * tax.AddOnChargeRate.Rate);
        }

        // 6 — prorate the adjustments across lines by net contribution, then tax each line once.
        var prorated = Prorate(priced, adjustmentTotal, rounding);
        var resolved = new List<ResolvedLine>(priced.Count);
        var tax1Total = 0m;
        var tax2Total = 0m;

        for (var i = 0; i < priced.Count; i++)
        {
            var line = priced[i];
            var share = prorated[i];
            var taxableNet = line.LineNet - share;

            var amounts = TaxCalculator.Calculate(taxableNet, line.Tax1Applies, line.Tax2Applies, tax, rounding);
            tax1Total += amounts.Tax1;
            tax2Total += amounts.Tax2;

            resolved.Add(new ResolvedLine(
                line.Input.LineId,
                line.Input.Sequence,
                line.Input.Product.Id,
                line.Input.Variant?.Id,
                line.Input.Product.StockCode,
                line.Input.Product.Name,
                line.EffectiveQuantity,
                line.ChargeableQuantity,
                line.UnitPrice,
                line.Origin,
                line.DiscountPct,
                line.LineGross,
                line.LineNet,
                share,
                taxableNet,
                line.Tax1Applies,
                line.Tax2Applies,
                amounts.Tax1,
                amounts.Tax2,
                line.Input.UnitCost,
                line.Input.Type));
        }

        // The add-on charge is taxed once at sale level, not smeared across lines (guide p.77).
        if (addOnCharge != 0m && tax.AddOnChargeTaxable)
        {
            var chargeTax = TaxCalculator.Calculate(addOnCharge, tax.Tax1Enabled, tax.Tax2Enabled, tax, rounding);
            tax1Total += chargeTax.Tax1;
            tax2Total += chargeTax.Tax2;
        }

        // 7 — grand total. Under inclusive pricing the tax is already inside the subtotal.
        var inclusive = tax.TaxationType == TaxationType.Inclusive;
        var grandTotal = inclusive
            ? rounding.Round(discountedSubtotal + addOnCharge)
            : rounding.Round(discountedSubtotal + addOnCharge + tax1Total + tax2Total);

        // 10 — loyalty accrual on the pre-tax, pre-charge subtotal (guide p.83).
        var pointsEarned = 0;
        var pointsRedeemed = 0;
        if (context.Loyalty is { IsEnabled: true } loyalty && context.Customer is not null)
        {
            pointsEarned = (int)Math.Floor(loyalty.PointsPerDollar * discountedSubtotal);
            if (applied.Any(a => a.Type == AdjustmentType.LoyaltyReward))
            {
                pointsRedeemed = loyalty.MinimumRequired;
            }
        }

        return new SalePricingResult(
            resolved,
            applied,
            subtotal,
            adjustmentTotal,
            discountedSubtotal,
            addOnCharge,
            rounding.Round(tax1Total),
            rounding.Round(tax2Total),
            grandTotal,
            resolved.Sum(r => r.CostOfGoods),
            pointsEarned,
            pointsRedeemed,
            tax.Tax1Name,
            tax.Tax2Name,
            tax.AddOnChargeName,
            inclusive);
    }

    private static PricedLine PriceLine(LineInput line, PricingContext context)
    {
        var resolution = PriceResolver.Resolve(line, context);
        var flags = TaxFlagResolver.Resolve(line, context);

        // A staff discount needs permission; the caller strips ManualDiscountPct when it is absent,
        // so by the time the engine sees it the decision has already been made (doc 04 §2).
        var discountPct = line.ManualDiscountPct ?? context.Customer?.UsualDiscountPct ?? 0m;

        var gross = context.Rounding.Round(resolution.UnitPrice * resolution.ChargeableQuantity);
        var net = context.Rounding.Round(gross - (gross * discountPct / 100m));

        // Returns and trade-ins are credits: the line net and, downstream, its tax go negative.
        if (line.IsCredit)
        {
            gross = -gross;
            net = -net;
        }

        return new PricedLine(
            line,
            resolution.UnitPrice,
            resolution.Origin,
            resolution.ChargeableQuantity,
            resolution.EffectiveQuantity,
            discountPct,
            gross,
            net,
            flags.Tax1Applies,
            flags.Tax2Applies);
    }

    /// <summary>
    /// Applies coupons, bottle credits, the subtotal discount and the loyalty reward in the order
    /// the guide sets out (p.7, p.84). The loyalty reward is suppressed when a subtotal discount has
    /// already landed — a verbatim legacy rule, and configurable per location.
    /// </summary>
    private static IReadOnlyList<AppliedAdjustment> ApplyAdjustments(
        IReadOnlyList<AdjustmentInput> adjustments,
        decimal subtotal,
        PricingContext context,
        out decimal total)
    {
        var rounding = context.Rounding;
        var applied = new List<AppliedAdjustment>(adjustments.Count);
        total = 0m;

        foreach (var coupon in adjustments.Where(a => a.Type == AdjustmentType.Coupon))
        {
            var amount = rounding.Round(coupon.Percent > 0m ? subtotal * coupon.Percent / 100m : coupon.Amount);
            applied.Add(new AppliedAdjustment(AdjustmentType.Coupon, coupon.Label, amount));
            total += amount;
        }

        foreach (var bottle in adjustments.Where(a => a.Type == AdjustmentType.BottleReturn))
        {
            var amount = rounding.Round(bottle.Amount);
            applied.Add(new AppliedAdjustment(AdjustmentType.BottleReturn, bottle.Label, amount));
            total += amount;
        }

        var subtotalDiscount = adjustments.FirstOrDefault(a => a.Type == AdjustmentType.SubtotalDiscount);
        if (subtotalDiscount is not null)
        {
            var amount = rounding.Round(
                subtotalDiscount.Percent > 0m
                    ? subtotal * subtotalDiscount.Percent / 100m
                    : subtotalDiscount.Amount);
            applied.Add(new AppliedAdjustment(AdjustmentType.SubtotalDiscount, subtotalDiscount.Label, amount));
            total += amount;
        }

        var reward = ResolveLoyaltyReward(adjustments, subtotal, subtotalDiscount is not null, context);
        if (reward is not null)
        {
            applied.Add(reward);
            total += reward.Amount;
        }

        total = rounding.Round(total);
        return applied;
    }

    private static AppliedAdjustment? ResolveLoyaltyReward(
        IReadOnlyList<AdjustmentInput> adjustments,
        decimal subtotal,
        bool subtotalDiscountApplied,
        PricingContext context)
    {
        var requested = adjustments.FirstOrDefault(a => a.Type == AdjustmentType.LoyaltyReward);
        if (requested is null || context.Loyalty is not { IsEnabled: true } loyalty || context.Customer is not { } customer)
        {
            return null;
        }

        if (subtotalDiscountApplied && loyalty.SuppressIfSubtotalDiscountApplied)
        {
            return null;
        }

        if (customer.RewardPoints < loyalty.MinimumRequired)
        {
            return null;
        }

        var percentReward = loyalty.PercentEnabled ? subtotal * loyalty.RewardPercent / 100m : (decimal?)null;
        var fixedReward = loyalty.FixedEnabled ? loyalty.RewardFixedAmount : (decimal?)null;

        var amount = (percentReward, fixedReward) switch
        {
            ({ } pct, { } fix) => Math.Min(pct, fix),
            ({ } pct, null) => pct,
            (null, { } fix) => fix,
            _ => 0m,
        };

        return amount <= 0m
            ? null
            : new AppliedAdjustment(AdjustmentType.LoyaltyReward, requested.Label, context.Rounding.Round(amount));
    }

    /// <summary>
    /// Splits the sale-level adjustment across lines by net contribution so a tax-exempt line never
    /// subsidises a taxable one (doc 04 §4). The rounding residue goes to the largest line, which
    /// makes the split deterministic and lets a property test assert the parts sum exactly.
    /// </summary>
    private static decimal[] Prorate(IReadOnlyList<PricedLine> lines, decimal adjustmentTotal, MoneyRounding rounding)
    {
        var shares = new decimal[lines.Count];
        if (lines.Count == 0 || adjustmentTotal == 0m)
        {
            return shares;
        }

        // Credits (returns, trade-ins) are not discounted by a sale-level adjustment; only the
        // positive-value lines carry it.
        var weights = lines.Select(l => Math.Max(0m, l.LineNet)).ToArray();
        var totalWeight = weights.Sum();

        if (totalWeight <= 0m)
        {
            shares[0] = adjustmentTotal;
            return shares;
        }

        var running = 0m;
        var largest = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            shares[i] = rounding.Round(adjustmentTotal * weights[i] / totalWeight);
            running += shares[i];

            if (weights[i] > weights[largest])
            {
                largest = i;
            }
        }

        var residue = adjustmentTotal - running;
        if (residue != 0m)
        {
            shares[largest] += residue;
        }

        return shares;
    }

    private sealed record PricedLine(
        LineInput Input,
        decimal UnitPrice,
        PriceOrigin Origin,
        decimal ChargeableQuantity,
        decimal EffectiveQuantity,
        decimal DiscountPct,
        decimal LineGross,
        decimal LineNet,
        bool Tax1Applies,
        bool Tax2Applies);
}
