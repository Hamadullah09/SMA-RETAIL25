using Retail25.Domain.Common;

namespace Retail25.Domain.Sales;

public enum AdjustmentType
{
    SubtotalDiscount = 0,
    Coupon = 1,
    BottleReturn = 2,
    GiftCertificate = 3,
    LoyaltyReward = 4,
}

/// <summary>
/// A cart-level adjustment from the Credits menu (guide p.7). Coupons, bottle credits and the
/// subtotal discount reduce the taxable base before tax is computed.
/// <para>
/// A gift certificate is stored here for traceability but is settled as a <b>tender</b>, not as a
/// discount (doc 04 §4 step 3f) — redeeming one must not reduce the tax the customer pays.
/// </para>
/// </summary>
public sealed class CartAdjustment : Entity
{
    public static readonly Error AmountRequired = new("adjustment.amount_required", "An adjustment needs either an amount or a percentage.");
    public static readonly Error PercentOutOfRange = new("adjustment.percent_out_of_range", "A percentage adjustment must be between 0 and 100.");

    public CartAdjustment()
    {
    }

    public long CartId { get; set; }

    public AdjustmentType Type { get; set; }

    /// <summary>Human-readable label shown on the totals panel and printed on the receipt.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Fixed amount. Mutually exclusive with <see cref="Percent"/>.</summary>
    public decimal Amount { get; set; }

    /// <summary>Percentage of the subtotal. Mutually exclusive with <see cref="Amount"/>.</summary>
    public decimal Percent { get; set; }

    /// <summary>Serial number for a gift certificate, or the coupon code.</summary>
    public string? Serial { get; set; }

    public long AppliedByStaffId { get; set; }

    public DateTimeOffset AppliedAt { get; set; }

    public static Result<CartAdjustment> Create(
        long cartId,
        AdjustmentType type,
        string label,
        decimal amount,
        decimal percent,
        long staffId,
        DateTimeOffset now,
        string? serial = null)
    {
        if (amount <= 0m && percent <= 0m)
        {
            return Result.Failure<CartAdjustment>(AmountRequired);
        }

        if (percent is < 0m or > 100m)
        {
            return Result.Failure<CartAdjustment>(PercentOutOfRange.With("value", percent));
        }

        return Result.Success(new CartAdjustment
        {
            CartId = cartId,
            Type = type,
            Label = string.IsNullOrWhiteSpace(label) ? type.ToString() : label.Trim(),
            Amount = amount,
            Percent = percent,
            Serial = serial?.Trim(),
            AppliedByStaffId = staffId,
            AppliedAt = now,
        });
    }
}
