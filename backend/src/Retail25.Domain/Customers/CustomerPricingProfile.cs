using Retail25.Domain.Common;

namespace Retail25.Domain.Customers;

/// <summary>
/// Customer-specific pricing overrides (guide p.51–52). When a customer is attached to a cart,
/// their usual discount and assigned price level are applied automatically.
/// </summary>
public sealed class CustomerPricingProfile : Entity, IAuditable
{
    public CustomerPricingProfile()
    {
    }

    public long CustomerId { get; set; }

    /// <summary>Default discount % applied to every sale for this customer (guide p.51).</summary>
    public decimal UsualDiscountPct { get; set; }

    /// <summary>Price level 1–4 assigned to this customer (guide p.52).</summary>
    public int PriceLevel { get; set; } = 1;

    public bool ExemptTax1 { get; set; }

    public bool ExemptTax2 { get; set; }

    /// <summary>Customer's reward points balance (guide p.83).</summary>
    public int RewardPoints { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static CustomerPricingProfile Create(long customerId)
    {
        return new CustomerPricingProfile
        {
            CustomerId = customerId,
        };
    }
}
