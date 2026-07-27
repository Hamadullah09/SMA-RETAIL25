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
}
