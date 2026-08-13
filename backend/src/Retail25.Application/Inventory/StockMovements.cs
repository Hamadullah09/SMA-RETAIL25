using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Inventory;

namespace Retail25.Application.Inventory;

/// <summary>
/// Moving stock: the ledger entry, the level, and the snapshot on the product — together.
/// <para>
/// Eight commands wrote those three things by hand before this existed, and a ninth forgot. The
/// RFID import commissioned tagged units into stock through the state machine and recorded no stock
/// at all, so a shop that imported two hundred tagged garments had two hundred units the till could
/// sell and an inventory screen that said it owned none of them. The first sale of each one took
/// its on-hand figure to −1.
/// </para>
/// <para>
/// <c>Product.OnHand</c> is a derived snapshot of the ledger (doc: ledgers, not mutable rows), so
/// writing one without the other is not a small inconsistency — it is a number that cannot be
/// rebuilt from the history it claims to summarise.
/// </para>
/// </summary>
public static class StockMovements
{
    /// <summary>
    /// Records <paramref name="quantity"/> moving, signed: positive arrives, negative leaves.
    /// </summary>
    public static async Task ApplyAsync(
        IApplicationDbContext db,
        long productId,
        long? variantId,
        long locationId,
        decimal quantity,
        decimal unitCost,
        MovementType movementType,
        string reason,
        DateTimeOffset occurredAt,
        long? staffId,
        string? referenceType = null,
        long? referenceId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (quantity == 0m)
        {
            return;
        }

        db.StockLedgerEntries.Add(new StockLedgerEntry
        {
            ProductId = productId,
            VariantId = variantId,
            LocationId = locationId,
            MovementType = movementType,
            Quantity = quantity,
            UnitCost = unitCost,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Reason = reason,
            OccurredAt = occurredAt,
            StaffId = staffId,
        });

        var level = await db.StockLevels.FirstOrDefaultAsync(
            s => s.ProductId == productId && s.VariantId == variantId && s.LocationId == locationId,
            ct);

        if (level is null)
        {
            level = StockLevel.Create(productId, variantId, locationId);
            db.StockLevels.Add(level);
        }

        level.OnHand += quantity;

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        product?.UpdateStockLevels(product.OnHand + quantity, product.OnOrder);
    }
}
