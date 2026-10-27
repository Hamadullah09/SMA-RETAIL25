using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Cutover;

public sealed partial class CutoverRunner
{
    /// <summary>The cost a unit of stock is valued at: the average cost, or the last cost when there is none.</summary>
    private static decimal ValuationCost(decimal averageCost, decimal lastCost) => averageCost != 0 ? averageCost : lastCost;

    /// <summary>
    /// Stock: one opening movement per Retail25 stock level (an item's, or a matrix line's), so that every item's count is
    /// the sum of its movements from the first day; and one movement out per line of an open layaway, as a layaway is a
    /// sale here. Each item's count is then made the sum of its movements, whatever else the database holds.
    /// </summary>
    private async Task StockAsync(CancellationToken ct)
    {
        Log("Opening stock");
        var area = _report.Area("Stock movements", "stock_levels, layaway_lines");
        var movements = new List<StockMovement>();
        var running = new Dictionary<Guid, decimal>();
        foreach (var level in _src["stock_levels"].Rows.OrderBy(l => l.Long("product_id")).ThenBy(l => l.LongOrNull("variant_id") ?? 0).ThenBy(l => l.Id))
        {
            area.Read++;
            if (ItemOf(level.Long("product_id")) is not { } item) { area.Skip("item not carried over"); continue; }
            if (level.Long("location_id") != item.Row.Long("location_id")) { area.Skip("a level at another location than the product's"); continue; }
            decimal quantity = Three(level.Dec("on_hand"), "StockMovement.Quantity");
            if (quantity == 0) { area.Skip("nothing in stock"); continue; }
            Guid? variant = level.LongOrNull("variant_id") is long v ? _variants.GetValueOrDefault(v).Id : null;
            if (variant == Guid.Empty) variant = null;
            running[item.Id] = running.GetValueOrDefault(item.Id) + quantity;
            movements.Add(From(new StockMovement
            {
                Id = StableIds.For("opening-stock", level.Id), StoreId = item.Store.StoreId, ItemId = item.Id, VariantId = variant, Kind = StockMovementKind.Opening,
                At = Local(item.Store, DateTimeOffset.UtcNow), Quantity = quantity, OnHandAfter = running[item.Id],
                UnitCost = Three(ValuationCost(item.Row.Dec("avg_cost"), item.Row.Dec("last_cost")), "StockMovement.UnitCost"),
                Reason = "Carried over from Retail25",
            }, level));
        }
        var layaways = _src["layaways"];
        foreach (var line in OpenLayawayStockLines().OrderBy(l => l.Id))
        {
            area.Read++;
            var item = ItemOf(line.Long("product_id"))!;
            var layaway = layaways.Find(line.Long("layaway_id"))!;
            decimal quantity = Three(line.Dec("quantity"), "StockMovement.Quantity");
            running[item.Id] = running.GetValueOrDefault(item.Id) - quantity;
            Guid? variant = line.LongOrNull("variant_id") is long v && _variants.TryGetValue(v, out var found) ? found.Id : null;
            movements.Add(From(new StockMovement
            {
                Id = StableIds.For("layaway-stock", line.Id), StoreId = item.Store.StoreId, ItemId = item.Id, VariantId = variant, Kind = StockMovementKind.Sale,
                At = Local(item.Store, layaway.Instant("created_at") ?? DateTimeOffset.UtcNow), Quantity = -quantity, OnHandAfter = running[item.Id],
                UnitCost = Three(ValuationCost(item.Row.Dec("avg_cost"), item.Row.Dec("last_cost")), "StockMovement.UnitCost"),
                DocumentNumber = layaway.Long("layaway_number"), Reason = $"On layaway {layaway.Long("layaway_number")} (Retail25)",
            }, line));
        }
        await _w.UpsertAsync(area, movements, ct, merge: (stored, mapped) => mapped.At = stored.At);
        var itemIds = _items.Values.Select(i => i.Id).ToList();
        await _w.RemoveMissingAsync<StockMovement>(area, db => db.StockMovements.Where(m => itemIds.Contains(m.ItemId)), movements.Select(m => m.Id).ToHashSet(), ct);

        // On hand is the sum of the movements (it already is, unless the database holds movements of its own).
        var ledgerArea = _report.Area("On hand from the movements", "stock_levels");
        await using var ctx = _w.NewContext();
        var sums = new Dictionary<Guid, decimal>();
        var variantSums = new Dictionary<Guid, decimal>();
        foreach (var chunk in itemIds.Chunk(2000))
            foreach (var m in await ctx.StockMovements.Where(m => chunk.Contains(m.ItemId)).Select(m => new { m.ItemId, m.VariantId, m.Quantity }).ToListAsync(ct))
            {
                sums[m.ItemId] = sums.GetValueOrDefault(m.ItemId) + m.Quantity;
                if (m.VariantId is Guid v) variantSums[v] = variantSums.GetValueOrDefault(v) + m.Quantity;
            }
        foreach (var chunk in itemIds.Chunk(2000))
            foreach (var item in await ctx.Items.Where(i => chunk.Contains(i.Id)).ToListAsync(ct))
            {
                ledgerArea.Read++;
                decimal sum = sums.GetValueOrDefault(item.Id);
                if (item.OnHand == sum) { ledgerArea.Unchanged++; continue; }
                Warn($"Item {item.Plu} at {item.LocationId}: on hand {item.OnHand:0.###} made {sum:0.###}, the sum of its stock movements in the new database");
                item.OnHand = sum;
                ledgerArea.Changed++;
            }
        var variantIds = _variants.Values.Select(v => v.Id).ToList();
        foreach (var chunk in variantIds.Chunk(2000))
            foreach (var variant in await ctx.ItemVariants.Where(v => chunk.Contains(v.Id)).ToListAsync(ct))
            {
                decimal sum = variantSums.GetValueOrDefault(variant.Id);
                if (variant.OnHand == sum) continue;
                Warn($"Matrix line {variant.Code ?? variant.Value1} of item {variant.ItemId}: on hand {variant.OnHand:0.###} made {sum:0.###}, the sum of its stock movements");
                variant.OnHand = sum;
            }
        await ctx.SaveChangesAsync(ct);
    }
}
