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
/// Cart-level adjustments (guide p.7). Coupons, bottle returns and subtotal discounts are
/// applied to the subtotal before tax. Gift certificates are tenders, not adjustments.
/// </summary>
public sealed class CartAdjustment : Entity
{
    private CartAdjustment()
    {
    }

    public Guid CartId { get; set; }

    public AdjustmentType Type { get; set; }

    /// <summary>Human-readable label (e.g. "Coupon:SAVE10").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Fixed amount. Mutually exclusive with Percent.</summary>
    public decimal Amount { get; set; }

    /// <summary>Percentage discount. Mutually exclusive with Amount.</summary>
    public decimal Percent { get; set; }

    /// <summary>Serial number for gift certificates.</summary>
    public string? Serial { get; set; }

    /// <summary>
    /// Records a credit against the sale. Amount and percent are mutually exclusive by convention:
    /// a coupon has a face value, a subtotal discount is usually a rate, and the pricing engine
    /// reads whichever is set.
    /// </summary>
    /// <param name="cartId">Cart the credit belongs to.</param>
    /// <param name="type">Which kind of credit.</param>
    /// <param name="label">What to print on the receipt.</param>
    /// <param name="amount">Fixed value credited.</param>
    /// <param name="percent">Percentage of the subtotal credited.</param>
    /// <param name="serial">Serial number, for gift certificates.</param>
    public static CartAdjustment Create(
        Guid cartId,
        AdjustmentType type,
        string label,
        decimal amount = 0m,
        decimal percent = 0m,
        string? serial = null) => new()
        {
            CartId = cartId,
            Type = type,
            Label = label?.Trim() ?? string.Empty,
            Amount = amount,
            Percent = percent,
            Serial = serial,
        };
}
