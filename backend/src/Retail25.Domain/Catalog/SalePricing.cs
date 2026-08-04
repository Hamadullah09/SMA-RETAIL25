using Retail25.Domain.Common;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.Catalog;

/// <summary>
/// Date-ranged promotional pricing (guide p.35). During the window, the product sells at
/// RegularPrice * (1 - DiscountPct). Break points and explicit level selection outrank
/// sale pricing (decision P1).
/// </summary>
public sealed class SalePricing : Entity
{
    private SalePricing()
    {
    }

    public long ProductId { get; private set; }

    public decimal DiscountPct { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly EndsOn { get; private set; }

    public bool IsActive(DateOnly businessDate) => businessDate >= StartsOn && businessDate <= EndsOn;

    public static Result<SalePricing> Create(long productId, decimal discountPct, DateOnly startsOn, DateOnly endsOn)
    {
        if (discountPct < 0 || discountPct > 100)
            return Result.Failure<SalePricing>(new Error("sale.discount_invalid", "Sale discount must be between 0 and 100 percent."));

        if (endsOn < startsOn)
            return Result.Failure<SalePricing>(new Error("sale.date_range_invalid", "Sale end date cannot precede start date."));

        return Result.Success(new SalePricing
        {
            ProductId = productId,
            DiscountPct = discountPct,
            StartsOn = startsOn,
            EndsOn = endsOn,
        });
    }

    public void Update(decimal discountPct, DateOnly startsOn, DateOnly endsOn)
    {
        DiscountPct = discountPct;
        StartsOn = startsOn;
        EndsOn = endsOn;
    }
}
