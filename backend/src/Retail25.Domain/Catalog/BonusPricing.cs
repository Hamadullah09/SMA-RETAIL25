using Retail25.Domain.Common;

namespace Retail25.Domain.Catalog;

/// <summary>
/// Buy-X-Get-Y bonus pricing (guide p.35). When a customer buys <see cref="BuyQty"/> or more,
/// <see cref="FreeQty"/> items are free. The engine calculates chargeable quantity as:
/// chargeable = totalQty - floor(totalQty / BuyQty) * FreeQty.
/// </summary>
public sealed class BonusPricing : Entity
{
    private BonusPricing()
    {
    }

    public Guid ProductId { get; private set; }

    public decimal BuyQty { get; private set; }

    public decimal FreeQty { get; private set; }

    public static Result<BonusPricing> Create(Guid productId, decimal buyQty, decimal freeQty)
    {
        if (buyQty <= 0)
            return Result.Failure<BonusPricing>(new Error("bonus.buy_qty_invalid", "Buy quantity must be greater than zero."));

        if (freeQty <= 0)
            return Result.Failure<BonusPricing>(new Error("bonus.free_qty_invalid", "Free quantity must be greater than zero."));

        if (freeQty >= buyQty)
            return Result.Failure<BonusPricing>(new Error("bonus.free_exceeds_buy", "Free quantity must be less than buy quantity."));

        return Result.Success(new BonusPricing
        {
            ProductId = productId,
            BuyQty = buyQty,
            FreeQty = freeQty,
        });
    }

    public void Update(decimal buyQty, decimal freeQty)
    {
        BuyQty = buyQty;
        FreeQty = freeQty;
    }
}
