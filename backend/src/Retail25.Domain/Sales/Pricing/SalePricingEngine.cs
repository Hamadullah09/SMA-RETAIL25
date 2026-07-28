using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>A line handed to the engine, with the catalog pricing rows it needs.</summary>
/// <param name="Input">The line itself.</param>
/// <param name="Catalog">Price levels, break points, bonus and sale pricing for the product.</param>
/// <param name="UnitCost">Average cost frozen onto the line for cost-of-goods reporting.</param>
public sealed record PricingLineRequest(LineInput Input, ProductPricingData Catalog, decimal UnitCost);

/// <summary>
/// The sale-level pricing pipeline (doc 04 §4).
/// <para>
/// A pure function: it performs no I/O, reads no clock and touches no ambient configuration.
/// Everything it needs arrives in <see cref="PricingContext"/>, which makes the whole of the
/// money-deciding logic reproducible from stored inputs — the property the golden-file suite relies on.
/// </para>
/// <para>The order of operations is fixed and deliberate:</para>
/// <list type="number">
///   <item>Resolve each line's price, quantity, discount and tax flags.</item>
///   <item>Sum to a subtotal.</item>
///   <item>Apply sale-wide credits: coupons, bottle returns, subtotal discount, then loyalty.</item>
///   <item>Floor the discounted subtotal at zero.</item>
///   <item>Apply the percentage add-on charge.</item>
///   <item>Prorate the credits across lines and charge tax on what remains.</item>
///   <item>Total.</item>
/// </list>
/// </summary>
public static class SalePricingEngine
{
    public static SalePricingResult Calculate(
        IReadOnlyList<PricingLineRequest> requests,
        PricingContext context,
        SaleAdjustments? adjustments = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(context);

        adjustments ??= SaleAdjustments.None;
        var rounding = context.Rounding;

        // --- Step 1: resolve every line in isolation -----------------------------------------
        var resolved = new List<ResolvedLine>(requests.Count);

        foreach (var request in requests)
        {
            var price = PriceResolver.Resolve(request.Input, request.Catalog, context);
            var taxFlags = TaxResolver.Resolve(request.Input, context);

            var gross = rounding.Round(price.UnitPrice * price.ChargeableQuantity);

            var discountPct = ResolveLineDiscountPercent(request.Input, context);
            var discountAmount = rounding.Round(gross * discountPct / 100m);

            // Returns and trade-ins credit the customer, so their value — and therefore their
            // tax — is negative.
            var sign = request.Input.Type == LineType.Sale ? 1m : -1m;
            var net = (gross - discountAmount) * sign;

            resolved.Add(new ResolvedLine(request, price, taxFlags, gross, discountPct, discountAmount, net));
        }

        // --- Step 2: subtotal ------------------------------------------------------------------
        var subtotal = rounding.Round(resolved.Sum(l => l.Net));

        // --- Step 3: sale-wide credits, in the documented order --------------------------------
        var applied = new List<AppliedAdjustment>();

        foreach (var coupon in adjustments.Coupons.Where(c => c.Amount > 0m))
        {
            applied.Add(new AppliedAdjustment(AdjustmentKind.Coupon, coupon.Description, rounding.Round(coupon.Amount)));
        }

        foreach (var bottle in adjustments.BottleReturns.Where(b => b.Amount > 0m))
        {
            applied.Add(new AppliedAdjustment(AdjustmentKind.BottleReturn, bottle.Description, rounding.Round(bottle.Amount)));
        }

        var subtotalDiscountApplied = false;
        if (adjustments.SubtotalDiscount is { } discount && discount.IsPresent)
        {
            var amount = discount.AmountFor(subtotal, rounding);
            if (amount > 0m)
            {
                applied.Add(new AppliedAdjustment(AdjustmentKind.SubtotalDiscount, "Subtotal discount", amount));
                subtotalDiscountApplied = true;
            }
        }

        // Legacy rule, stated verbatim in the guide (p.84): a bonus-points reward is available only
        // when there is not already a discount on the subtotal.
        var pointsRedeemed = 0;
        if (adjustments.RedeemLoyaltyReward
            && !(subtotalDiscountApplied && context.Loyalty.SuppressIfSubtotalDiscountApplied))
        {
            var reward = CalculateLoyaltyReward(subtotal, context, rounding);
            if (reward > 0m)
            {
                applied.Add(new AppliedAdjustment(AdjustmentKind.LoyaltyReward, "Reward points", reward));
                pointsRedeemed = context.Loyalty.MinimumRequired;
            }
        }

        // Credits can reduce a sale to nothing but never below it — the balance is not carried
        // forward as store credit unless a gift certificate is issued explicitly.
        var adjustmentTotal = rounding.Round(applied.Sum(a => a.Amount));
        var creditCeiling = Math.Max(subtotal, 0m);
        if (adjustmentTotal > creditCeiling)
        {
            adjustmentTotal = creditCeiling;
        }

        // --- Step 4: discounted subtotal --------------------------------------------------------
        var discountedSubtotal = rounding.Round(subtotal - adjustmentTotal);

        // --- Step 5: percentage add-on charge ---------------------------------------------------
        var addOnApplies = context.Policy.ApplyAddOnCharge
            && context.Tax.AddOnChargeEnabled
            && !adjustments.SuspendAddOnCharge;

        var addOnCharge = addOnApplies
            ? rounding.Round(discountedSubtotal * context.Tax.AddOnChargeRate.Rate)
            : 0m;

        // --- Step 6: prorate the credits, then tax what remains ---------------------------------
        // Credits are spread across the lines that were actually bought. Weighting by positive net
        // keeps a return line from absorbing part of a coupon and inverting its tax.
        var weights = resolved.Select(l => Math.Max(l.Net, 0m)).ToList();
        var allocations = rounding.Allocate(adjustmentTotal, weights);

        var lines = new List<PricedLine>(resolved.Count);

        foreach (var (line, index) in resolved.Select((l, i) => (l, i)))
        {
            var allocated = allocations[index];
            var taxableAmount = rounding.Round(line.Net - allocated);

            var tax = TaxCalculator.Calculate(
                taxableAmount,
                line.TaxFlags.Tax1Applies,
                line.TaxFlags.Tax2Applies,
                context.Tax,
                rounding);

            lines.Add(new PricedLine(
                Sequence: line.Request.Input.Sequence,
                ProductId: line.Request.Input.Product.Id,
                VariantId: line.Request.Input.Variant?.Id,
                UnitPrice: line.Price.UnitPrice,
                PriceOrigin: line.Price.Origin,
                ResolvedPriceLevel: line.Price.ResolvedPriceLevel,
                ChargeableQuantity: line.Price.ChargeableQuantity,
                FreeQuantity: line.Price.FreeQuantity,
                StockQuantity: line.Price.StockQuantity,
                GrossAmount: line.Gross,
                LineDiscountPct: line.DiscountPct,
                LineDiscountAmount: line.DiscountAmount,
                NetAmount: line.Net,
                AllocatedSubtotalAdjustment: allocated,
                TaxableAmount: taxableAmount,
                Tax1Applies: line.TaxFlags.Tax1Applies,
                Tax2Applies: line.TaxFlags.Tax2Applies,
                Tax1Source: line.TaxFlags.Tax1Source,
                Tax2Source: line.TaxFlags.Tax2Source,
                Tax1Amount: tax.Tax1Amount,
                Tax2Amount: tax.Tax2Amount,
                LineType: line.Request.Input.Type));
        }

        // The add-on charge is taxed as a whole rather than per line, because it is a single charge
        // on the sale (guide p.77).
        var addOnTax = addOnCharge != 0m && context.Tax.AddOnChargeTaxable
            ? TaxCalculator.Calculate(
                addOnCharge,
                context.Policy.ApplyTax1,
                context.Policy.ApplyTax2,
                context.Tax,
                rounding)
            : TaxResult.Zero;

        var tax1Total = rounding.Round(lines.Sum(l => l.Tax1Amount) + addOnTax.Tax1Amount);
        var tax2Total = rounding.Round(lines.Sum(l => l.Tax2Amount) + addOnTax.Tax2Amount);

        // --- Step 7: total ----------------------------------------------------------------------
        // Under inclusive taxation the tax is already inside the line amounts, so adding it again
        // would double-charge; it is reported separately for the receipt and the tax return.
        var grandTotal = context.Tax.TaxationType == TaxationType.Inclusive
            ? rounding.Round(discountedSubtotal + addOnCharge)
            : rounding.Round(discountedSubtotal + addOnCharge + tax1Total + tax2Total);

        var pointsEarned = CalculateLoyaltyAccrual(discountedSubtotal, subtotalDiscountApplied, context);

        return new SalePricingResult(
            Lines: lines,
            Adjustments: applied,
            Subtotal: subtotal,
            AdjustmentTotal: adjustmentTotal,
            DiscountedSubtotal: discountedSubtotal,
            AddOnChargeName: context.Tax.AddOnChargeName,
            AddOnCharge: addOnCharge,
            AddOnChargeTax1: addOnTax.Tax1Amount,
            AddOnChargeTax2: addOnTax.Tax2Amount,
            Tax1Name: context.Tax.Tax1Name,
            Tax1Total: tax1Total,
            Tax2Name: context.Tax.Tax2Name,
            Tax2Total: tax2Total,
            GrandTotal: grandTotal,
            LoyaltyPointsEarned: pointsEarned,
            LoyaltyPointsRedeemed: pointsRedeemed,
            TaxationType: context.Tax.TaxationType);
    }

    /// <summary>
    /// A discount typed by staff needs both the store policy and the operator's permission; without
    /// them the request is ignored and the customer's standing discount applies instead (guide p.51, p.77).
    /// </summary>
    private static decimal ResolveLineDiscountPercent(LineInput input, PricingContext context)
    {
        if (input.ManualDiscountPct is > 0m
            && context.Permissions.CanDiscount
            && context.Policy.StaffMayDiscount)
        {
            return input.ManualDiscountPct.Value;
        }

        return context.Customer?.UsualDiscountPct ?? 0m;
    }

    /// <summary>
    /// Works out what a reward is worth (guide p.84). With both forms enabled the reward is the
    /// percentage, capped at the fixed amount — the guide's worked example is 10% up to 20 dollars.
    /// </summary>
    private static decimal CalculateLoyaltyReward(decimal subtotal, PricingContext context, RoundingPolicy rounding)
    {
        var loyalty = context.Loyalty;

        if (!loyalty.IsEnabled || context.Customer is null || subtotal <= 0m)
        {
            return 0m;
        }

        if (context.Customer.RewardPoints < loyalty.MinimumRequired)
        {
            return 0m;
        }

        var percentReward = loyalty.PercentEnabled ? rounding.Round(subtotal * loyalty.RewardPercent / 100m) : 0m;
        var fixedReward = loyalty.FixedEnabled ? rounding.Round(loyalty.RewardFixedAmount) : 0m;

        var reward = (loyalty.PercentEnabled, loyalty.FixedEnabled) switch
        {
            (true, true) => Math.Min(percentReward, fixedReward),
            (true, false) => percentReward,
            (false, true) => fixedReward,
            _ => 0m,
        };

        // A reward can never exceed the sale it is being spent on.
        return Math.Min(reward, subtotal);
    }

    /// <summary>
    /// Points are earned on the pre-tax, pre-charge value of the sale. The guide is explicit that a
    /// sale carrying a subtotal discount earns nothing, even though discounted individual items
    /// still earn (p.83).
    /// </summary>
    private static int CalculateLoyaltyAccrual(
        decimal discountedSubtotal,
        bool subtotalDiscountApplied,
        PricingContext context)
    {
        var loyalty = context.Loyalty;

        if (!loyalty.IsEnabled
            || context.Customer is null
            || subtotalDiscountApplied
            || loyalty.PointsPerDollar <= 0m
            || discountedSubtotal <= 0m)
        {
            return 0;
        }

        return (int)Math.Floor(loyalty.PointsPerDollar * discountedSubtotal);
    }

    /// <summary>Intermediate state for one line, between resolution and taxation.</summary>
    private sealed record ResolvedLine(
        PricingLineRequest Request,
        PriceResolution Price,
        LineTaxFlags TaxFlags,
        decimal Gross,
        decimal DiscountPct,
        decimal DiscountAmount,
        decimal Net);
}
