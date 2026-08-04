using Retail25.Domain.Common;

namespace Retail25.Domain.Configuration;

/// <summary>
/// Loyalty / bonus points configuration (guide p.83–84). Rules are data-driven:
/// points per dollar, minimum required, % or fixed reward, and whether loyalty is
/// suppressed when a subtotal discount is already applied.
/// </summary>
public sealed class LoyaltyPolicy : AggregateRoot, IAuditable
{
    public LoyaltyPolicy()
    {
    }

    public long LocationId { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>Points earned per dollar spent (guide p.83).</summary>
    public decimal PointsPerDollar { get; set; }

    /// <summary>Minimum points required to redeem (guide p.84).</summary>
    public int MinimumRequired { get; set; }

    /// <summary>Enable percentage-based reward redemption.</summary>
    public bool PercentEnabled { get; set; }

    /// <summary>Percentage of subtotal redeemed as a discount.</summary>
    public decimal RewardPercent { get; set; }

    /// <summary>Enable fixed-amount reward redemption.</summary>
    public bool FixedEnabled { get; set; }

    /// <summary>Fixed dollar amount redeemed when FixedEnabled is true.</summary>
    public decimal RewardFixedAmount { get; set; }

    /// <summary>
    /// Legacy rule (guide p.84): no reward if a subtotal discount is already applied on this sale.
    /// </summary>
    public bool SuppressIfSubtotalDiscountApplied { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }
}
