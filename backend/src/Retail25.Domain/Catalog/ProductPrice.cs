using Retail25.Domain.Common;

namespace Retail25.Domain.Catalog;

/// <summary>
/// Four price levels per product (guide p.34). Missing levels fall through to the regular price;
/// a level that exists but is zero is treated as "no level price" (guide p.52).
/// </summary>
public sealed class ProductPrice : Entity
{
    private ProductPrice()
    {
    }

    public long ProductId { get; private set; }

    /// <summary>Price level 1–4. Level 1 is the default; levels 2–4 are used for break points and client-assigned pricing.</summary>
    public int Level { get; private set; }

    public decimal Price { get; private set; }

    public static Result<ProductPrice> Create(long productId, int level, decimal price)
    {
        if (level is < 1 or > 4)
            return Result.Failure<ProductPrice>(new Error("price.level_out_of_range", "Price level must be between 1 and 4."));

        return Result.Success(new ProductPrice
        {
            ProductId = productId,
            Level = level,
            Price = price,
        });
    }

    public void UpdatePrice(decimal price) => Price = price;
}
