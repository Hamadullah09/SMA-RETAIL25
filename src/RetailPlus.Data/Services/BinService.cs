using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A bin of a location with the number of items kept in it.</summary>
public sealed record BinRow(Guid Id, Guid LocationId, string LocationCode, string Code, string? Zone, string? Description, bool IsActive, int Items);

/// <summary>
/// One line of a put-away, pick or count list: where (zone and bin), what, how many, and for an item kept in batches the
/// batch to take (the one that expires first) or that arrived.
/// </summary>
public sealed record BinListRow(string? Zone, string? Bin, string Plu, string Description, decimal Quantity, string? BatchNumber, DateOnly? ExpiresOn,
    string? Document, decimal OnHand = 0);

/// <summary>
/// Bins and the lists sorted by them: each item names its default bin by code (<see cref="Item.BinLocation"/>); goods
/// received are put away bin by bin, customer orders and transfers are picked bin by bin, and a zone or bin is counted on
/// its own. Items without a bin come last.
/// </summary>
public sealed class BinService(DataContext data)
{
    public async Task<List<BinRow>> ListAsync(Guid? locationId = null, Guid? store = null, bool activeOnly = false)
    {
        await using var db = data.Create();
        var q = db.Bins.AsNoTracking();
        if (locationId is Guid l) q = q.Where(b => b.LocationId == l);
        if (store is Guid s) q = q.Where(b => b.StoreId == s);
        if (activeOnly) q = q.Where(b => b.IsActive);
        var bins = await q.Select(b => new { b.Id, b.LocationId, LocationCode = b.Location!.Code, b.Code, b.Zone, b.Description, b.IsActive }).ToListAsync();
        var locations = bins.Select(b => b.LocationId).Distinct().ToList();
        var used = (await db.Items.AsNoTracking().Where(i => locations.Contains(i.LocationId) && !i.IsDeleted && i.BinLocation != null)
                .Select(i => new { i.LocationId, i.BinLocation }).ToListAsync())
            .GroupBy(i => (i.LocationId, Code: i.BinLocation!.ToUpperInvariant())).ToDictionary(g => g.Key, g => g.Count());
        return [.. bins.OrderBy(b => b.LocationCode).ThenBy(b => b.Zone ?? "￿", StringComparer.OrdinalIgnoreCase).ThenBy(b => b.Code, StringComparer.OrdinalIgnoreCase)
            .Select(b => new BinRow(b.Id, b.LocationId, b.LocationCode, b.Code, b.Zone, b.Description, b.IsActive, used.GetValueOrDefault((b.LocationId, b.Code.ToUpperInvariant()))))];
    }

    public async Task<Bin?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.Bins.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
    }

    /// <summary>
    /// Adds or changes a bin. Codes are kept in capitals and are unique per location; renaming a bin moves the items kept
    /// in it to the new code.
    /// </summary>
    public async Task<Bin> SaveAsync(Bin bin)
    {
        var code = (bin.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length is 0 or > 25) throw new BusinessRuleException("A bin needs a code of up to 25 characters.");
        await using var db = data.Create();
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == bin.LocationId) ?? throw new BusinessRuleException("That location does not exist.");
        if (await db.Bins.AnyAsync(b => b.LocationId == location.Id && b.Code == code && b.Id != bin.Id)) throw new BusinessRuleException($"Bin {code} is already at {location.Code}.");
        var row = bin.Id == Guid.Empty ? null : await db.Bins.SingleOrDefaultAsync(b => b.Id == bin.Id);
        string? oldCode = row?.Code;
        if (row is null) db.Bins.Add(row = new Bin { LocationId = location.Id, StoreId = location.StoreId });
        else if (row.LocationId != location.Id) throw new BusinessRuleException("A bin stays at its location.");
        row.Code = code;
        row.Zone = string.IsNullOrWhiteSpace(bin.Zone) ? null : Cut(bin.Zone.Trim(), 30);
        row.Description = string.IsNullOrWhiteSpace(bin.Description) ? null : Cut(bin.Description.Trim(), 100);
        row.IsActive = bin.IsActive;
        await db.SaveChangesAsync();
        if (oldCode is not null && oldCode != code)
            await db.Items.Where(i => i.LocationId == location.Id && i.BinLocation == oldCode).ExecuteUpdateAsync(s => s.SetProperty(i => i.BinLocation, code).Edited());
        return row;
    }

    /// <summary>Deletes a bin no item is kept in.</summary>
    public async Task DeleteAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.Bins.SingleOrDefaultAsync(b => b.Id == id) ?? throw new BusinessRuleException("That bin no longer exists.");
        int items = await db.Items.CountAsync(i => i.LocationId == row.LocationId && !i.IsDeleted && i.BinLocation == row.Code);
        if (items > 0) throw new BusinessRuleException($"{items} items are kept in bin {row.Code}; move them first, or make the bin inactive.");
        db.Bins.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>Makes <paramref name="binCode"/> the default bin of the items (null takes them out of any bin). Returns how many changed.</summary>
    public async Task<int> AssignAsync(IEnumerable<Guid> itemIds, string? binCode)
    {
        var ids = itemIds.Distinct().ToList();
        var code = string.IsNullOrWhiteSpace(binCode) ? null : binCode.Trim().ToUpperInvariant();
        await using var db = data.Create();
        var items = await db.Items.Where(i => ids.Contains(i.Id)).ToListAsync();
        if (code is not null)
            foreach (var location in items.Select(i => i.LocationId).Distinct())
                if (!await db.Bins.AnyAsync(b => b.LocationId == location && b.Code == code && b.IsActive))
                    throw new BusinessRuleException($"There is no active bin {code} at the item's location.");
        int n = 0;
        foreach (var item in items.Where(i => i.BinLocation != code))
        {
            item.BinLocation = code;
            n++;
        }
        await db.SaveChangesAsync();
        return n;
    }

    /// <summary>The put-away list of a received purchase order: what arrived, bin by bin, with the batches that came.</summary>
    public async Task<List<BinListRow>> PutAwayAsync(Guid purchaseOrderId)
    {
        await using var db = data.Create();
        var po = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).SingleOrDefaultAsync(p => p.Id == purchaseOrderId)
                 ?? throw new BusinessRuleException("That purchase order no longer exists.");
        var ids = po.Lines.Where(l => l.ItemId != null).Select(l => l.ItemId!.Value).ToList();
        var items = await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var zones = await ZonesAsync(db, po.LocationId);
        var batches = await db.StockMovements.AsNoTracking()
            .Where(m => m.Kind == StockMovementKind.Receipt && m.DocumentNumber == po.Number && m.BatchId != null && ids.Contains(m.ItemId))
            .Join(db.ItemBatches.AsNoTracking(), m => m.BatchId, b => b.Id, (m, b) => new { m.ItemId, b.BatchNumber, b.ExpiresOn, m.Quantity })
            .ToListAsync();
        var rows = new List<BinListRow>();
        string document = $"PO {po.Number}";
        foreach (var line in po.Lines.Where(l => l.QuantityReceived != 0))
        {
            var item = line.ItemId is Guid id ? items.GetValueOrDefault(id) : null;
            var bin = Bin(item?.BinLocation);
            var mine = batches.Where(b => item is not null && b.ItemId == item.Id).ToList();
            if (mine.Count == 0)
                rows.Add(new BinListRow(zones.GetValueOrDefault(bin ?? ""), bin, line.Plu, line.Description, line.QuantityReceived, null, null, document, item?.OnHand ?? 0));
            else
                rows.AddRange(mine.Select(b => new BinListRow(zones.GetValueOrDefault(bin ?? ""), bin, line.Plu, line.Description, b.Quantity, b.BatchNumber, b.ExpiresOn,
                    document, item!.OnHand)));
        }
        return Sort(rows);
    }

    /// <summary>
    /// The pick list of a location's open customer orders (or the ones given), bin by bin, with the batch to take first
    /// for items kept in batches.
    /// </summary>
    public async Task<List<BinListRow>> PickCustomerOrdersAsync(Guid locationId, IReadOnlyCollection<Guid>? orderIds = null)
    {
        await using var db = data.Create();
        var q = db.CustomerOrders.AsNoTracking().Where(o => o.LocationId == locationId && !o.Paid && o.ItemId != null);
        if (orderIds is { Count: > 0 }) q = q.Where(o => orderIds.Contains(o.Id));
        var orders = await q.ToListAsync();
        var wanted = orders.Select(o => (ItemId: o.ItemId!.Value, o.Plu, o.Description, o.Quantity,
            Document: $"Order {o.InvoiceNumber}" + (string.IsNullOrWhiteSpace(o.Name) ? "" : " " + o.Name))).ToList();
        return await PicksAsync(db, locationId, wanted);
    }

    /// <summary>The pick list of a transfer being sent, bin by bin; lines of one batch say so, others get the batch to take first.</summary>
    public async Task<List<BinListRow>> PickTransferAsync(Guid transferId)
    {
        await using var db = data.Create();
        var transfer = await db.StockTransfers.AsNoTracking().Include(t => t.Lines).SingleOrDefaultAsync(t => t.Id == transferId)
                       ?? throw new BusinessRuleException("That transfer no longer exists.");
        var document = $"Transfer {transfer.Number}";
        var fixedBatches = transfer.Lines.Where(l => l.BatchNumber != null).ToList();
        var rows = await PicksAsync(db, transfer.FromLocationId, transfer.Lines.Where(l => l.BatchNumber == null)
            .Select(l => (l.SourceItemId, (string?)l.Plu, l.Description, l.QuantityShipped, document)).ToList());
        if (fixedBatches.Count > 0)
        {
            var ids = fixedBatches.Select(l => l.SourceItemId).Distinct().ToList();
            var items = await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
            var zones = await ZonesAsync(db, transfer.FromLocationId);
            foreach (var l in fixedBatches)
            {
                var bin = Bin(items.GetValueOrDefault(l.SourceItemId)?.BinLocation);
                rows.Add(new BinListRow(zones.GetValueOrDefault(bin ?? ""), bin, l.Plu, l.Description, l.QuantityShipped, l.BatchNumber, l.ExpiresOn, document,
                    items.GetValueOrDefault(l.SourceItemId)?.OnHand ?? 0));
            }
        }
        return Sort(rows);
    }

    /// <summary>The count sheet of a location, zone or bin: the stocked items kept there, bin by bin, with what the system holds.</summary>
    public async Task<List<BinListRow>> CountSheetAsync(Guid locationId, string? zone = null, string? binCode = null)
    {
        await using var db = data.Create();
        var zones = await ZonesAsync(db, locationId);
        var q = db.Items.AsNoTracking().Where(i => i.LocationId == locationId && !i.IsDeleted
            && (i.ItemType == ItemType.Standard || i.ItemType == ItemType.Matrix || i.ItemType == ItemType.Serialized || i.ItemType == ItemType.Kit));
        if (!string.IsNullOrWhiteSpace(binCode))
        {
            var code = binCode.Trim().ToUpperInvariant();
            q = q.Where(i => i.BinLocation == code);
        }
        else if (!string.IsNullOrWhiteSpace(zone))
        {
            var codes = zones.Where(z => string.Equals(z.Value, zone.Trim(), StringComparison.OrdinalIgnoreCase)).Select(z => z.Key).ToList();
            q = q.Where(i => i.BinLocation != null && codes.Contains(i.BinLocation));
        }
        var items = await q.Select(i => new { i.Plu, i.Name, i.BinLocation, i.OnHand }).ToListAsync();
        return Sort([.. items.Select(i =>
        {
            var bin = Bin(i.BinLocation);
            return new BinListRow(zones.GetValueOrDefault(bin ?? ""), bin, i.Plu, i.Name, 0, null, null, null, i.OnHand);
        })]);
    }

    private static async Task<List<BinListRow>> PicksAsync(RetailPlusDbContext db, Guid locationId, List<(Guid ItemId, string? Plu, string Description, decimal Quantity, string Document)> wanted)
    {
        var ids = wanted.Select(w => w.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var zones = await ZonesAsync(db, locationId);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var rows = new List<BinListRow>();
        // What earlier lines of the list take of a batch is not offered again to later ones.
        var taken = new Dictionary<Guid, decimal>();
        foreach (var w in wanted.Where(w => w.Quantity > 0))
        {
            var item = items.GetValueOrDefault(w.ItemId);
            var bin = Bin(item?.BinLocation);
            var zone = zones.GetValueOrDefault(bin ?? "");
            if (item is { TracksBatches: true })
            {
                var stock = (await BatchService.StockOfAsync(db, item.Id)).Select(b => b with { OnHand = b.OnHand - taken.GetValueOrDefault(b.BatchId) });
                foreach (var take in Fefo.Pick(stock, w.Quantity, today, skipExpired: true))
                {
                    if (take.BatchId is Guid b) taken[b] = taken.GetValueOrDefault(b) + take.Quantity;
                    var expires = take.BatchId is Guid id ? await db.ItemBatches.AsNoTracking().Where(x => x.Id == id).Select(x => x.ExpiresOn).SingleAsync() : null;
                    rows.Add(new BinListRow(zone, bin, w.Plu ?? item.Plu, w.Description, take.Quantity, take.BatchNumber, expires, w.Document, item.OnHand));
                }
            }
            else rows.Add(new BinListRow(zone, bin, w.Plu ?? item?.Plu ?? "", w.Description, w.Quantity, null, null, w.Document, item?.OnHand ?? 0));
        }
        return Sort(rows);
    }

    /// <summary>Each bin's zone at the location, by code.</summary>
    private static async Task<Dictionary<string, string?>> ZonesAsync(RetailPlusDbContext db, Guid locationId) =>
        (await db.Bins.AsNoTracking().Where(b => b.LocationId == locationId).Select(b => new { b.Code, b.Zone }).ToListAsync())
        .ToDictionary(b => b.Code, b => b.Zone, StringComparer.OrdinalIgnoreCase);

    private static string? Bin(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    /// <summary>Zone, then bin, then stock code; rows without a bin last.</summary>
    private static List<BinListRow> Sort(List<BinListRow> rows) =>
        [.. rows.OrderBy(r => r.Bin is null).ThenBy(r => r.Zone ?? "￿", StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Bin ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Plu, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.ExpiresOn is null).ThenBy(r => r.ExpiresOn)];

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}
