namespace Retail25.Domain.Sales.Pricing;

/// <summary>A coupon presented against the whole sale (F3 → F3, guide p.7).</summary>
/// <param name="Description">What the customer handed over.</param>
/// <param name="Amount">Face value credited to the subtotal.</param>
public sealed record CouponCredit(string Description, decimal Amount);

/// <summary>Empty containers credited to the customer (F3 → F6, guide p.7).</summary>
/// <param name="Description">Free text describing the containers.</param>
/// <param name="Amount">Value credited.</param>
public sealed record BottleReturnCredit(string Description, decimal Amount);

/// <summary>
/// A discount applied to the subtotal rather than to individual items (F3 → F2, guide p.7).
/// Exactly one of the two forms is used.
/// </summary>
/// <param name="Percent">Percentage of the subtotal, when discounting by percentage.</param>
/// <param name="FixedAmount">A flat amount, when discounting by value.</param>
public sealed record SubtotalDiscount(decimal? Percent, decimal? FixedAmount)
{
    public bool IsPresent => (Percent is > 0m) || (FixedAmount is > 0m);

    public decimal AmountFor(decimal subtotal, RoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(rounding);

        if (Percent is > 0m)
        {
            return rounding.Round(subtotal * Percent.Value / 100m);
        }

        return FixedAmount is > 0m ? rounding.Round(FixedAmount.Value) : 0m;
    }
}

/// <summary>
/// Sale-wide credits and discounts, held once for the sale instead of being repeated on every line.
/// Ordering within the pipeline is fixed by doc 04 §4 step 3.
/// </summary>
/// <param name="Coupons">Coupons presented.</param>
/// <param name="BottleReturns">Container deposits refunded.</param>
/// <param name="SubtotalDiscount">A discount on the subtotal, if the cashier applied one.</param>
/// <param name="RedeemLoyaltyReward">
/// Whether the customer chose to spend their points on this sale. The legacy till asks rather than
/// redeeming automatically, so the answer is an input (guide p.83).
/// </param>
/// <param name="SuspendAddOnCharge">
/// Suspends the percentage add-on charge for this sale only (F11 → F6 Taxes, guide p.11).
/// </param>
public sealed record SaleAdjustments(
    IReadOnlyList<CouponCredit> Coupons,
    IReadOnlyList<BottleReturnCredit> BottleReturns,
    SubtotalDiscount? SubtotalDiscount,
    bool RedeemLoyaltyReward,
    bool SuspendAddOnCharge)
{
    public static readonly SaleAdjustments None = new([], [], null, false, false);
}
