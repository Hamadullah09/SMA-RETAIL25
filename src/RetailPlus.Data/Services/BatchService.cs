using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A batch with its stock, for the batch lists and reports. <see cref="DaysLeft"/> is negative once expired.</summary>
public sealed record BatchRow(Guid Id, Guid ItemId, string Plu, string ItemName, Guid StoreId, string LocationCode, string BatchNumber, DateOnly? ExpiresOn,
    DateOnly? MadeOn, string? Supplier, DateOnly? ReceivedOn, long? PurchaseOrderNumber, decimal OnHand, decimal UnitCost, decimal Value, int? DaysLeft,
    string? Bin, string? Note);

/// <summary>What arrived of one batch on a purchase order line.</summary>
public sealed record ReceivedBatch(string BatchNumber, decimal Pieces, DateOnly? ExpiresOn = null, DateOnly? MadeOn = null);

/// <summary>An item's stock that may be sold today, and what of it has expired.</summary>
public sealed record SellableStock(decimal OnHand, decimal Sellable, decimal Expired, bool ExpiredBlocked);

/// <summary>
/// What a sale of an item kept in batches would take, worked out before it is paid so that the POS can say which batch
/// goes and warn about expired goods: the batches in the order they leave (first expiry first), the expired batches that
/// still hold stock, whether the store stops them from being sold, and how much may be sold.
/// </summary>
public sealed record SalePick(IReadOnlyList<BatchTake> Takes, IReadOnlyList<BatchStock> Batches, IReadOnlyList<BatchStock> ExpiredBatches, bool ExpiredBlocked,
    decimal Sellable)
{
    public decimal Expired => ExpiredBatches.Sum(b => b.OnHand);

    /// <summary>The expiry date of one of the item's batches (none for stock in no batch).</summary>
    public DateOnly? ExpiryOf(Guid? batchId) => Batches.FirstOrDefault(b => b.BatchId == batchId)?.ExpiresOn;
}

/// <summary>An item kept in batches on a purchase order, with the shelf life that offers the expiry date at receiving.</summary>
public sealed record BatchItem(Guid ItemId, int ShelfLifeDays);

/// <summary>
/// Batches and expiry: an item that tracks batches (<see cref="Item.TracksBatches"/>) is received in batches, each with a
/// number and an expiry date; its stock per batch is the sum of the stock movements that name the batch. The POS sells the
/// batch that expires first (<see cref="Fefo"/>), and a store can stop expired stock from being sold
/// (<see cref="SupplySettings.BlockExpiredSales"/>).
/// </summary>
public sealed class BatchService(DataContext data)
{
    /// <summary>The batches of an item with their stock, in the order they leave (first expiry first).</summary>
    public async Task<List<BatchRow>> OfItemAsync(Guid itemId, bool inStockOnly = false)
    {
        await using var db = data.Create();
        var rows = await RowsAsync(db, db.ItemBatches.AsNoTracking().Where(b => b.ItemId == itemId), DateOnly.FromDateTime(DateTime.Today));
        return [.. rows.Where(r => !inStockOnly || r.OnHand != 0).OrderBy(r => r.ExpiresOn is null).ThenBy(r => r.ExpiresOn)
            .ThenBy(r => r.BatchNumber, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Stock by batch: every batch of the location (or of the store, or of every store on the server) with stock, or all of
    /// them, optionally only those whose item code, name or batch number starts with (or contains) the text.
    /// </summary>
    public async Task<List<BatchRow>> ListAsync(Guid? locationId = null, Guid? store = null, string? search = null, bool inStockOnly = true, DateOnly? today = null)
    {
        await using var db = data.Create();
        var rows = await RowsAsync(db, Filter(db, locationId, store, search), today ?? DateOnly.FromDateTime(DateTime.Today));
        return [.. rows.Where(r => !inStockOnly || r.OnHand != 0).OrderBy(r => r.Plu, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.ExpiresOn is null).ThenBy(r => r.ExpiresOn)];
    }

    /// <summary>Batches with stock that expire from today to <paramref name="days"/> days ahead, soonest first.</summary>
    public async Task<List<BatchRow>> ExpiringAsync(int days, Guid? locationId = null, Guid? store = null, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        var last = day.AddDays(Math.Max(0, days));
        await using var db = data.Create();
        var q = Filter(db, locationId, store, null).Where(b => b.ExpiresOn != null && b.ExpiresOn >= day && b.ExpiresOn <= last);
        var rows = await RowsAsync(db, q, day);
        return [.. rows.Where(r => r.OnHand > 0).OrderBy(r => r.ExpiresOn).ThenBy(r => r.Plu, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Expired batches that still have stock, with its value at the item's cost, oldest expiry first.</summary>
    public async Task<List<BatchRow>> ExpiredAsync(Guid? locationId = null, Guid? store = null, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        await using var db = data.Create();
        var rows = await RowsAsync(db, Filter(db, locationId, store, null).Where(b => b.ExpiresOn != null && b.ExpiresOn < day), day);
        return [.. rows.Where(r => r.OnHand > 0).OrderBy(r => r.ExpiresOn).ThenBy(r => r.Plu, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<ItemBatch?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.ItemBatches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
    }

    /// <summary>
    /// Adds a batch to an item by hand, or changes a batch's expiry, made-on date, supplier and note. The batch number of a
    /// batch with movements stays as it is.
    /// </summary>
    public async Task<ItemBatch> SaveAsync(ItemBatch batch)
    {
        var number = (batch.BatchNumber ?? "").Trim().ToUpperInvariant();
        if (number.Length is 0 or > 30) throw new BusinessRuleException("A batch needs a number of up to 30 characters.");
        if (batch.MadeOn is DateOnly made && batch.ExpiresOn is DateOnly expires && expires < made)
            throw new BusinessRuleException("A batch cannot expire before it was made.");
        await using var db = data.Create();
        var item = await db.Items.AsNoTracking().Include(i => i.Location).SingleOrDefaultAsync(i => i.Id == batch.ItemId)
                   ?? throw new BusinessRuleException("That item no longer exists.");
        if (await db.ItemBatches.AnyAsync(b => b.ItemId == item.Id && b.BatchNumber == number && b.Id != batch.Id))
            throw new BusinessRuleException($"{item.Plu} already has batch {number}.");
        var row = batch.Id == Guid.Empty ? null : await db.ItemBatches.SingleOrDefaultAsync(b => b.Id == batch.Id);
        if (row is null)
        {
            db.ItemBatches.Add(row = new ItemBatch { ItemId = item.Id, StoreId = item.Location!.StoreId, ReceivedOn = DateOnly.FromDateTime(DateTime.Today) });
            row.BatchNumber = number;
        }
        else if (row.BatchNumber != number)
        {
            if (await db.StockMovements.AnyAsync(m => m.BatchId == row.Id)) throw new BusinessRuleException($"Batch {row.BatchNumber} has stock movements; its number stays.");
            row.BatchNumber = number;
        }
        row.ExpiresOn = batch.ExpiresOn;
        row.MadeOn = batch.MadeOn;
        row.SupplierId = batch.SupplierId;
        row.Note = string.IsNullOrWhiteSpace(batch.Note) ? null : Cut(batch.Note.Trim(), 200);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Deletes a batch that never moved.</summary>
    public async Task DeleteAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.ItemBatches.SingleOrDefaultAsync(b => b.Id == id) ?? throw new BusinessRuleException("That batch no longer exists.");
        if (await db.StockMovements.AnyAsync(m => m.BatchId == id)) throw new BusinessRuleException($"Batch {row.BatchNumber} has stock movements, so it stays.");
        db.ItemBatches.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// What of an item may be sold today: its stock less the expired batches' when the store stops expired stock from being
    /// sold. Items that do not track batches can sell all they have.
    /// </summary>
    public async Task<SellableStock> SellableAsync(Guid itemId, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        await using var db = data.Create();
        var item = await db.Items.AsNoTracking().Where(i => i.Id == itemId).Select(i => new { i.OnHand, i.TracksBatches, i.Location!.StoreId }).SingleOrDefaultAsync();
        if (item is null) return new SellableStock(0, 0, 0, false);
        if (!item.TracksBatches) return new SellableStock(item.OnHand, item.OnHand, 0, false);
        bool block = (await SupplySettingsService.ForStoreAsync(db, item.StoreId)).BlockExpiredSales;
        decimal expired = (await StockOfAsync(db, itemId)).Where(b => Fefo.IsExpired(b.ExpiresOn, day) && b.OnHand > 0).Sum(b => b.OnHand);
        return new SellableStock(item.OnHand, block ? item.OnHand - expired : item.OnHand, expired, block && expired > 0);
    }

    /// <summary>
    /// The batches a sale of <paramref name="quantity"/> would take (as <see cref="TakeAsync"/> takes them when the sale is
    /// saved), and the item's expired stock. Null for an item that is not kept in batches.
    /// </summary>
    public async Task<SalePick?> PickForSaleAsync(Guid itemId, decimal quantity, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        await using var db = data.Create();
        var item = await db.Items.AsNoTracking().Where(i => i.Id == itemId).Select(i => new { i.OnHand, i.TracksBatches, i.Location!.StoreId }).SingleOrDefaultAsync();
        if (item is null || !item.TracksBatches) return null;
        bool block = (await SupplySettingsService.ForStoreAsync(db, item.StoreId)).BlockExpiredSales;
        var stock = await StockOfAsync(db, itemId);
        var expired = Fefo.Order(stock.Where(b => Fefo.IsExpired(b.ExpiresOn, day) && b.OnHand > 0)).ToList();
        decimal sellable = block ? item.OnHand - expired.Sum(b => b.OnHand) : item.OnHand;
        return new SalePick(Fefo.Pick(stock, quantity, day, skipExpired: block), stock, expired, block, sellable);
    }

    /// <summary>Which of these items are kept in batches, with their shelf life: receiving asks for their batches.</summary>
    public async Task<List<BatchItem>> KeptInBatchesAsync(IEnumerable<Guid> itemIds)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        await using var db = data.Create();
        return await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id) && i.TracksBatches).Select(i => new BatchItem(i.Id, i.ShelfLifeDays)).ToListAsync();
    }

    /// <summary>
    /// Adjusts a batch's stock (adds <paramref name="quantity"/>, or with <paramref name="setTo"/> makes it that), writing an
    /// adjustment movement of the batch. Expired goods thrown away are adjusted out this way.
    /// </summary>
    public async Task<decimal> AdjustAsync(Guid batchId, decimal quantity, string? reason, bool setTo = false)
    {
        await using var db = data.Create();
        db.SuppressAudit = true;
        await using var tx = await db.Database.BeginTransactionAsync();
        var batch = await db.ItemBatches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == batchId) ?? throw new BusinessRuleException("That batch no longer exists.");
        var item = await db.Items.SingleAsync(i => i.Id == batch.ItemId);
        decimal held = (await StockOfAsync(db, item.Id)).Where(b => b.BatchId == batchId).Sum(b => b.OnHand);
        decimal delta = setTo ? quantity - held : quantity;
        if (delta == 0) return held;
        item.OnHand += delta;
        var why = string.IsNullOrWhiteSpace(reason) ? $"Batch {batch.BatchNumber}" : Cut($"Batch {batch.BatchNumber}: {reason.Trim()}", 100);
        InventoryService.Record(db, item.Id, StockMovementKind.Adjustment, delta, item.OnHand, reason: why, unitCost: item.AverageCost > 0 ? item.AverageCost : item.LastCost,
            batchId: batchId);
        db.Audit(AuditActions.BatchAdjusted, $"{item.Plu} batch {batch.BatchNumber}: {delta:+0.###;-0.###}" + (string.IsNullOrWhiteSpace(reason) ? "" : " " + reason.Trim()),
            nameof(ItemBatch), batchId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return held + delta;
    }

    // Used by the other services, inside their own transaction -------------------------------------------------------

    /// <summary>The stock of each of an item's batches (from its movements), every batch listed even when empty.</summary>
    internal static async Task<List<BatchStock>> StockOfAsync(RetailPlusDbContext db, Guid itemId)
    {
        var batches = await db.ItemBatches.AsNoTracking().Where(b => b.ItemId == itemId).Select(b => new { b.Id, b.BatchNumber, b.ExpiresOn }).ToListAsync();
        if (batches.Count == 0) return [];
        // Added up here: SQLite keeps decimals as text.
        var moves = await db.StockMovements.AsNoTracking().Where(m => m.ItemId == itemId && m.BatchId != null).Select(m => new { m.BatchId, m.Quantity }).ToListAsync();
        var added = db.StockMovements.Local.Where(m => m.ItemId == itemId && m.BatchId != null && db.Entry(m).State == EntityState.Added)
            .Select(m => new { m.BatchId, m.Quantity });
        var sums = moves.Concat(added).GroupBy(m => m.BatchId!.Value).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
        return [.. batches.Select(b => new BatchStock(b.Id, b.BatchNumber, b.ExpiresOn, sums.GetValueOrDefault(b.Id)))];
    }

    /// <summary>
    /// Takes <paramref name="quantity"/> of an item out of stock by batch: the batch given, or first expiry first. With the
    /// store's <see cref="SupplySettings.BlockExpiredSales"/> (and <paramref name="forSale"/>), expired batches are not
    /// taken, and a sale that only expired stock could cover is refused.
    /// </summary>
    internal static async Task<IReadOnlyList<BatchTake>> TakeAsync(RetailPlusDbContext db, Guid itemId, decimal quantity, Guid? batchId, bool forSale, DateOnly today)
    {
        var stock = await StockOfAsync(db, itemId);
        bool block = false;
        if (forSale)
        {
            var store = await db.Items.AsNoTracking().Where(i => i.Id == itemId).Select(i => i.Location!.StoreId).SingleAsync();
            block = (await SupplySettingsService.ForStoreAsync(db, store)).BlockExpiredSales;
        }
        if (batchId is Guid chosen)
        {
            var batch = stock.FirstOrDefault(b => b.BatchId == chosen) ?? throw new BusinessRuleException("That batch is not one of the item's.");
            if (block && Fefo.IsExpired(batch.ExpiresOn, today)) throw new BusinessRuleException($"Batch {batch.BatchNumber} expired on {batch.ExpiresOn:d}; it cannot be sold.");
            return [new BatchTake(chosen, batch.BatchNumber, quantity)];
        }
        var takes = Fefo.Pick(stock, quantity, today, skipExpired: block);
        if (block && takes.Count > 0 && takes[^1] is { BatchId: null } rest)
        {
            decimal expired = stock.Where(b => Fefo.IsExpired(b.ExpiresOn, today) && b.OnHand > 0).Sum(b => b.OnHand);
            decimal loose = await LooseStockAsync(db, itemId, stock);
            if (expired > 0 && rest.Quantity > Math.Max(0, loose))
            {
                var name = await db.Items.AsNoTracking().Where(i => i.Id == itemId).Select(i => i.Plu + " " + i.Name).SingleAsync();
                throw new BusinessRuleException($"{name}: only {quantity - rest.Quantity + Math.Max(0, loose):0.###} can be sold; {expired:0.###} have expired.");
            }
        }
        return takes;
    }

    /// <summary>An item's stock that is in no batch: its on-hand count less what its batches hold.</summary>
    private static async Task<decimal> LooseStockAsync(RetailPlusDbContext db, Guid itemId, List<BatchStock> stock)
    {
        decimal onHand = await db.Items.AsNoTracking().Where(i => i.Id == itemId).Select(i => i.OnHand).SingleAsync();
        return onHand - stock.Sum(b => b.OnHand);
    }

    /// <summary>
    /// The batch of an item with this number, made when the item has none yet (dates and supplier from what arrived). An
    /// existing batch keeps its dates, except that a date it lacked is filled in.
    /// </summary>
    internal static async Task<ItemBatch> FindOrMakeAsync(RetailPlusDbContext db, Guid itemId, Guid storeId, string batchNumber, DateOnly? expiresOn, DateOnly? madeOn,
        Guid? supplierId, long? purchaseOrderNumber, DateOnly today)
    {
        var number = batchNumber.Trim().ToUpperInvariant();
        if (number.Length is 0 or > 30) throw new BusinessRuleException("A batch needs a number of up to 30 characters.");
        var batch = db.ItemBatches.Local.FirstOrDefault(b => b.ItemId == itemId && b.BatchNumber == number)
                    ?? await db.ItemBatches.FirstOrDefaultAsync(b => b.ItemId == itemId && b.BatchNumber == number);
        if (batch is null)
        {
            batch = new ItemBatch
            {
                ItemId = itemId, StoreId = storeId, BatchNumber = number, ExpiresOn = expiresOn, MadeOn = madeOn, SupplierId = supplierId, ReceivedOn = today,
                PurchaseOrderNumber = purchaseOrderNumber,
            };
            batch.Id = Guid.CreateVersion7();
            db.ItemBatches.Add(batch);
        }
        else
        {
            batch.ExpiresOn ??= expiresOn;
            batch.MadeOn ??= madeOn;
            batch.SupplierId ??= supplierId;
        }
        return batch;
    }

    // Reading helpers -----------------------------------------------------------------------------------------------

    private static IQueryable<ItemBatch> Filter(RetailPlusDbContext db, Guid? locationId, Guid? store, string? search)
    {
        var q = db.ItemBatches.AsNoTracking();
        if (store is Guid s) q = q.Where(b => b.StoreId == s);
        if (locationId is Guid l) q = q.Where(b => b.Item!.LocationId == l);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim();
            q = q.Where(b => b.BatchNumber.StartsWith(t) || b.Item!.Plu.StartsWith(t) || b.Item.Name.Contains(t));
        }
        return q;
    }

    private static async Task<List<BatchRow>> RowsAsync(RetailPlusDbContext db, IQueryable<ItemBatch> batches, DateOnly today)
    {
        var rows = await batches.Select(b => new
        {
            b.Id, b.ItemId, b.Item!.Plu, ItemName = b.Item.Name, b.StoreId, LocationCode = b.Item.Location!.Code, b.BatchNumber, b.ExpiresOn, b.MadeOn,
            Supplier = b.Supplier != null ? b.Supplier.Company : null, b.ReceivedOn, b.PurchaseOrderNumber, b.Item.AverageCost, b.Item.LastCost,
            b.Item.BinLocation, b.Note,
        }).ToListAsync();
        if (rows.Count == 0) return [];
        var ids = rows.Select(r => r.Id).ToList();
        var sums = new Dictionary<Guid, decimal>();
        foreach (var chunk in ids.Chunk(500))
        {
            var part = chunk.Select(id => (Guid?)id).ToList();
            foreach (var m in await db.StockMovements.AsNoTracking().Where(m => part.Contains(m.BatchId)).Select(m => new { m.BatchId, m.Quantity }).ToListAsync())
                sums[m.BatchId!.Value] = sums.GetValueOrDefault(m.BatchId!.Value) + m.Quantity;
        }
        return [.. rows.Select(r =>
        {
            decimal onHand = sums.GetValueOrDefault(r.Id);
            decimal cost = r.AverageCost > 0 ? r.AverageCost : r.LastCost;
            return new BatchRow(r.Id, r.ItemId, r.Plu, r.ItemName, r.StoreId, r.LocationCode, r.BatchNumber, r.ExpiresOn, r.MadeOn, r.Supplier, r.ReceivedOn,
                r.PurchaseOrderNumber, onHand, cost, Money.Round(onHand * cost), r.ExpiresOn is DateOnly d ? d.DayNumber - today.DayNumber : null, r.BinLocation, r.Note);
        })];
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}
