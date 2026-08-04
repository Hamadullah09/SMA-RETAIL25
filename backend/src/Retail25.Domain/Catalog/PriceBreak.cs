using Retail25.Domain.Common;

namespace Retail25.Domain.Catalog;

/// <summary>
/// Volume break points (guide p.34). When quantity meets or exceeds a break threshold,
/// the corresponding price level is applied. Break points outrank sale pricing (decision P1).
/// </summary>
public sealed class PriceBreak : Entity
{
    private PriceBreak()
    {
    }

    public long ProductId { get; private set; }

    /// <summary>Which price level this break applies to (2–4; level 1 is always the base).</summary>
    public int Level { get; private set; }

    /// <summary>Minimum quantity to trigger this price level.</summary>
    public decimal MinQuantity { get; private set; }

    public static Result<PriceBreak> Create(long productId, int level, decimal minQuantity)
    {
        if (level is < 2 or > 4)
            return Result.Failure<PriceBreak>(new Error("break.level_out_of_range", "Break point levels must be between 2 and 4."));

        if (minQuantity <= 0)
            return Result.Failure<PriceBreak>(new Error("break.min_quantity_invalid", "Break point minimum quantity must be greater than zero."));

        return Result.Success(new PriceBreak
        {
            ProductId = productId,
            Level = level,
            MinQuantity = minQuantity,
        });
    }

    public void Update(decimal minQuantity, int level)
    {
        MinQuantity = minQuantity;
        Level = level;
    }
}
