using Retail25.Domain.Common;

namespace Retail25.Domain.Catalog;

/// <summary>
/// A component of a kit product (guide p.41). When a kit is sold, it explodes into individual
/// stock movements for each component. Components can be other products or the same product.
/// </summary>
public sealed class KitComponent : Entity
{
    private KitComponent()
    {
    }

    public long KitProductId { get; private set; }

    public long ComponentProductId { get; private set; }

    /// <summary>Quantity of this component consumed per unit of the kit sold.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>If true, the component stock is reduced when the kit is sold.</summary>
    public bool ReduceStock { get; private set; } = true;

    public static Result<KitComponent> Create(long kitProductId, long componentProductId, decimal quantity)
    {
        if (quantity <= 0)
            return Result.Failure<KitComponent>(new Error("kit.quantity_invalid", "Component quantity must be greater than zero."));

        if (kitProductId == componentProductId)
            return Result.Failure<KitComponent>(new Error("kit.self_reference", "A kit cannot contain itself as a component."));

        return Result.Success(new KitComponent
        {
            KitProductId = kitProductId,
            ComponentProductId = componentProductId,
            Quantity = quantity,
        });
    }

    public void Update(decimal quantity, bool reduceStock)
    {
        Quantity = quantity;
        ReduceStock = reduceStock;
    }
}
