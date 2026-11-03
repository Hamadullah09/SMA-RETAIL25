using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A row of the Stock counts list.</summary>
public sealed record StockCountRow(Guid Id, long Number, string LocationCode, StockCountStatus Status, DateTime StartedAt, string? StartedBy,
    DateTime? PostedAt, string? PostedBy, int Lines, decimal VarianceCost, string? Note);

/// <summary>What adding counts did: how many pieces were counted, and the codes or tags that matched no item.</summary>
public sealed record CountResult(decimal Counted, IReadOnlyList<string> NotFound, int AlreadyCounted = 0);

/// <summary>What posting a count did.</summary>
public sealed record CountPosting(int ItemsChanged, decimal UnitsChanged, decimal VarianceCost, int SetToZero);

/// <summary>
/// Stock counts (stock takes): start a count for a location, count items by code, RFID tag or CSV file, review the
/// differences, post or cancel.
/// <para>
/// Expected stock is taken when an item is first counted, not when the count starts: a shop usually keeps selling
/// while it counts, and the shelf is counted at one moment, so the on-hand count at that moment is the one to compare
/// with. Posting then adds the difference (counted minus expected) as a <see cref="StockMovementKind.Count"/>
/// movement, which makes the on-hand count the counted quantity plus whatever was sold or received since the item was
/// counted (exactly the counted quantity when nothing moved). Because the movement is a difference, it also adds up
/// correctly with the sales other tills send to the server meanwhile.
/// </para>
/// </summary>
public sealed class StockCountService(DataContext data)
{
    /// <summary>
    /// Starts a count of a location, or of one zone or bin of it (<paramref name="zone"/>, <paramref name="binCode"/>): such
    /// a count sets to zero, when asked, only the items kept there.
    /// </summary>
    public async Task<StockCount> StartAsync(Guid locationId, string? note, string? user, string? zone = null, string? binCode = null)
    {
        await using var db = data.Create();
        if (!await db.Locations.AnyAsync(l => l.Id == locationId)) throw new BusinessRuleException("That location does not exist.");
        zone = string.IsNullOrWhiteSpace(zone) ? null : zone.Trim();
        binCode = string.IsNullOrWhiteSpace(binCode) ? null : binCode.Trim().ToUpperInvariant();
        if (binCode is not null && !await db.Bins.AnyAsync(b => b.LocationId == locationId && b.Code == binCode))
            throw new BusinessRuleException($"There is no bin {binCode} at this location.");
        if (zone is not null && !await db.Bins.AnyAsync(b => b.LocationId == locationId && b.Zone == zone))
            throw new BusinessRuleException($"No bin of this location is in zone {zone}.");
        var count = new StockCount
        {
            LocationId = locationId, Status = StockCountStatus.Open, StartedAt = DateTime.Now, StartedBy = user,
            Note = string.IsNullOrWhiteSpace(note) ? null : Trim(note.Trim(), 200), Zone = zone, BinCode = binCode,
        };
        count.Number = await db.NextNumberAsync(RetailPlusDbContext.Sequences.StockCountNumbers);
        db.StockCounts.Add(count);
        await db.SaveChangesAsync();
        return count;
    }

    public async Task<List<StockCountRow>> ListAsync(Guid? locationId = null, bool openOnly = false, int take = 500)
    {
        await using var db = data.Create();
        var q = db.StockCounts.AsNoTracking().AsQueryable();
        if (locationId is Guid l) q = q.Where(c => c.LocationId == l);
        if (openOnly) q = q.Where(c => c.Status == StockCountStatus.Open);
        var rows = await q.OrderByDescending(c => c.StartedAt).Take(take)
            .Select(c => new
            {
                c.Id, c.Number, Code = c.Location != null ? c.Location.Code : "", c.Status, c.StartedAt, c.StartedBy, c.PostedAt, c.PostedBy,
                Lines = c.Lines.Count, c.Note,
            }).ToListAsync();
        var ids = rows.Select(r => r.Id).ToList();
        // Summed in memory: SQLite cannot add up decimals.
        var costs = (await db.StockCountLines.AsNoTracking().Where(l => ids.Contains(l.StockCountId)).Select(l => new { l.StockCountId, l.VarianceCost }).ToListAsync())
            .GroupBy(l => l.StockCountId).ToDictionary(g => g.Key, g => g.Sum(l => l.VarianceCost));
        return rows.Select(r => new StockCountRow(r.Id, r.Number, r.Code, r.Status, r.StartedAt, r.StartedBy, r.PostedAt, r.PostedBy, r.Lines,
            costs.GetValueOrDefault(r.Id), r.Note)).ToList();
    }

    /// <summary>The count with its lines in stock code order.</summary>
    public async Task<StockCount?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.StockCounts.AsNoTracking().Include(c => c.Location).Include(c => c.Lines.OrderBy(l => l.Plu).ThenBy(l => l.Description))
            .SingleOrDefaultAsync(c => c.Id == id);
    }

    /// <summary>
    /// Counts an item found by a scanned or typed code (stock code, barcode, matrix line code, supplier code):
    /// adds <paramref name="quantity"/> to what was counted so far, or with <paramref name="replace"/> sets it.
    /// </summary>
    public async Task<StockCountLine> CountCodeAsync(Guid countId, string code, decimal quantity, bool replace = false, Guid? batchId = null)
    {
        await using var db = data.Create();
        var count = await OpenCountAsync(db, countId);
        var (item, variant) = await FindAsync(db, count.LocationId, code)
                              ?? throw new BusinessRuleException($"No item has the code {code.Trim()}.");
        var line = await CountAsync(db, count, item, variant, quantity, replace, await BatchOfAsync(db, item, batchId));
        await db.SaveChangesAsync();
        return line;
    }

    /// <summary>
    /// Counts an item (or one matrix line of it) picked from the inventory; for an item kept in batches, one batch of it
    /// when <paramref name="batchId"/> is given (otherwise the item as a whole).
    /// </summary>
    public async Task<StockCountLine> CountItemAsync(Guid countId, Guid itemId, Guid? variantId, decimal quantity, bool replace = false, Guid? batchId = null)
    {
        await using var db = data.Create();
        var count = await OpenCountAsync(db, countId);
        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.LocationId == count.LocationId)
                   ?? throw new BusinessRuleException("That item is not in the location being counted.");
        var variant = variantId is Guid v ? await db.ItemVariants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == v && x.ItemId == itemId) : null;
        var line = await CountAsync(db, count, item, variant, quantity, replace, await BatchOfAsync(db, item, batchId));
        await db.SaveChangesAsync();
        return line;
    }

    private static async Task<ItemBatch?> BatchOfAsync(RetailPlusDbContext db, Item item, Guid? batchId)
    {
        if (batchId is not Guid id) return null;
        if (!item.TracksBatches) throw new BusinessRuleException($"{item.Plu} is not kept in batches.");
        return await db.ItemBatches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id && b.ItemId == item.Id)
               ?? throw new BusinessRuleException("That batch is not one of the item's.");
    }

    /// <summary>
    /// Counts the pieces whose RFID tags were read: one piece per tag, each tag once per count however often it is read.
    /// Tags are matched to items as at the POS (registered tags, GS1 barcodes, item codes).
    /// </summary>
    public async Task<CountResult> CountTagsAsync(Guid countId, IEnumerable<string> epcs)
    {
        var rfid = new RfidService(data);
        await using var db = data.Create();
        var count = await OpenCountAsync(db, countId);
        var codes = epcs.Select(Epc.Normalize).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var seen = (await db.StockCountTags.Where(t => t.StockCountId == countId && codes.Contains(t.Epc)).Select(t => t.Epc).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        var notFound = new List<string>();
        decimal counted = 0;
        foreach (var epc in codes.Where(c => !seen.Contains(c)))
        {
            var found = await rfid.ResolveAsync(count.LocationId, epc);
            if (found is null)
            {
                notFound.Add(epc);
                continue;
            }
            var line = await CountAsync(db, count, found.Item, found.Variant, 1, false);
            db.StockCountTags.Add(new StockCountTag { StockCountId = countId, StockCountLineId = line.Id, Epc = epc });
            await db.SaveChangesAsync();
            counted++;
        }
        return new CountResult(counted, notFound, codes.Count(seen.Contains));
    }

    /// <summary>
    /// Counts from a CSV file's rows: a code (stock code or barcode) and a quantity per row, optionally under a header
    /// row. A code on several rows is added up (a shelf counted in two places). With <paramref name="replace"/> the
    /// file's quantities replace what was counted so far; otherwise they are added to it.
    /// </summary>
    public async Task<CountResult> ImportAsync(Guid countId, IReadOnlyList<string[]> rows, bool replace = false)
    {
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Length == 0 || row.All(string.IsNullOrWhiteSpace)) continue;
            var code = row[0].Trim();
            var text = row.Length > 1 ? row[1].Trim() : "1";
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty)
                && !decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out qty))
            {
                if (r > 0) problems.Add($"Row {r + 1}: \"{text}\" is not a quantity");
                continue;   // the header row
            }
            if (code.Length == 0)
            {
                problems.Add($"Row {r + 1}: no code");
                continue;
            }
            totals[code] = totals.GetValueOrDefault(code) + qty;
        }

        await using var db = data.Create();
        var count = await OpenCountAsync(db, countId);
        decimal counted = 0;
        foreach (var (code, qty) in totals)
        {
            var found = await FindAsync(db, count.LocationId, code);
            if (found is null)
            {
                problems.Add(code);
                continue;
            }
            await CountAsync(db, count, found.Value.Item, found.Value.Variant, qty, replace);
            await db.SaveChangesAsync();
            counted += qty;
        }
        return new CountResult(counted, problems);
    }

    /// <summary>Changes what was counted on a line (from the grid).</summary>
    public async Task SetCountedAsync(Guid lineId, decimal counted)
    {
        await using var db = data.Create();
        var line = await db.StockCountLines.SingleAsync(l => l.Id == lineId);
        await OpenCountAsync(db, line.StockCountId);
        line.Counted = counted;
        Update(line);
        await db.SaveChangesAsync();
    }

    public async Task RemoveLineAsync(Guid lineId)
    {
        await using var db = data.Create();
        var line = await db.StockCountLines.SingleAsync(l => l.Id == lineId);
        await OpenCountAsync(db, line.StockCountId);
        db.StockCountTags.RemoveRange(await db.StockCountTags.Where(t => t.StockCountLineId == lineId).ToListAsync());
        db.StockCountLines.Remove(line);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Posts the count: every line with a difference becomes a <see cref="StockMovementKind.Count"/> movement and changes
    /// the item's (and matrix line's) on-hand count by that difference. With <paramref name="zeroUncounted"/>, the
    /// location's stocked items that were not counted at all are set to zero (and get a line saying so); otherwise
    /// they are left alone.
    /// </summary>
    public async Task<CountPosting> PostAsync(Guid countId, bool zeroUncounted, string? user)
    {
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var count = await OpenCountAsync(db, countId);
        var lines = await db.StockCountLines.Where(l => l.StockCountId == countId).ToListAsync();
        var now = DateTime.Now;
        int zeroed = 0;
        if (zeroUncounted)
        {
            var counted = lines.Select(l => l.ItemId).ToHashSet();
            var bins = await BinsCountedAsync(db, count);
            var stocked = db.Items.AsNoTracking()
                .Where(i => i.LocationId == count.LocationId && !i.IsDeleted && i.OnHand != 0
                            && (i.ItemType == ItemType.Standard || i.ItemType == ItemType.Matrix || i.ItemType == ItemType.Serialized || i.ItemType == ItemType.Kit));
            // A count of a zone or bin reaches only the items kept there.
            if (bins is not null) stocked = stocked.Where(i => i.BinLocation != null && bins.Contains(i.BinLocation));
            var others = await stocked.ToListAsync();
            foreach (var item in others.Where(i => !counted.Contains(i.Id)))
            {
                var line = NewLine(count, item, null, now);
                line.NotCounted = true;
                Update(line);
                db.StockCountLines.Add(line);
                lines.Add(line);
                zeroed++;
            }
            // An item counted batch by batch: the batches not counted are set to zero too.
            foreach (var itemId in lines.Where(l => l.BatchId != null && !l.NotCounted).Select(l => l.ItemId).Distinct().ToList())
            {
                var seen = lines.Where(l => l.ItemId == itemId && l.BatchId != null).Select(l => l.BatchId!.Value).ToHashSet();
                var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
                foreach (var b in (await BatchService.StockOfAsync(db, itemId)).Where(b => b.OnHand != 0 && !seen.Contains(b.BatchId)))
                {
                    var line = NewLine(count, item, null, now);
                    line.BatchId = b.BatchId;
                    line.BatchNumber = b.BatchNumber;
                    line.Description = Trim($"{item.Name} batch {b.BatchNumber}", 60)!;
                    line.Expected = b.OnHand;
                    line.NotCounted = true;
                    Update(line);
                    db.StockCountLines.Add(line);
                    lines.Add(line);
                    zeroed++;
                }
            }
        }

        int changed = 0;
        decimal units = 0;
        string reason = $"Stock count {count.Number}";
        var batched = (await db.Items.AsNoTracking().Where(i => i.LocationId == count.LocationId && i.TracksBatches).Select(i => i.Id).ToListAsync()).ToHashSet();
        foreach (var line in lines.Where(l => l.Variance != 0))
        {
            decimal delta = line.Variance;
            await db.Items.Where(i => i.Id == line.ItemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.OnHand, i => i.OnHand + delta).Touched());
            if (line.VariantId is Guid variantId)
                await db.ItemVariants.Where(v => v.Id == variantId).ExecuteUpdateAsync(s => s.SetProperty(v => v.OnHand, v => v.OnHand + delta).Touched());
            var onHand = await db.Items.Where(i => i.Id == line.ItemId).Select(i => i.OnHand).SingleAsync();
            if (line.BatchId is not null || (batched.Contains(line.ItemId) && line.VariantId is null))
            {
                // Batches: a batch's line moves that batch; an item counted as a whole loses stock first expiry first
                // (all of it when set to zero), and stock found goes into no batch.
                var parts = line.BatchId is Guid batchId ? [new BatchTake(batchId, line.BatchNumber, -delta)]
                    : delta < 0 ? await BatchService.TakeAsync(db, line.ItemId, -delta, null, forSale: false, DateOnly.FromDateTime(now))
                    : [new BatchTake(null, null, -delta)];
                decimal running = onHand - delta;
                foreach (var part in parts)
                    InventoryService.Record(db, line.ItemId, StockMovementKind.Count, -part.Quantity, running -= part.Quantity, now, count.Number, reason, Trim(user, 50), null,
                        db.ActingUserId, line.UnitCost, part.BatchId);
            }
            else if (line.VariantId is null && line.NotCounted)
            {
                // An item set to zero: its matrix lines go to zero with it, each with a movement of its own (and one for
                // the rest of the item's change), so the lines' counts stay the sums of their movements too.
                var zeroedLines = await db.ItemVariants.Where(v => v.ItemId == line.ItemId && v.OnHand != 0).Select(v => new { v.Id, v.OnHand }).ToListAsync();
                await db.ItemVariants.Where(v => v.ItemId == line.ItemId && v.OnHand != 0).ExecuteUpdateAsync(s => s.SetProperty(v => v.OnHand, 0m).Touched());
                decimal running = onHand - delta;
                foreach (var v in zeroedLines)
                    InventoryService.Record(db, line.ItemId, StockMovementKind.Count, -v.OnHand, running -= v.OnHand, now, count.Number, reason, Trim(user, 50), v.Id, db.ActingUserId, line.UnitCost);
                decimal rest = delta + zeroedLines.Sum(v => v.OnHand);
                if (rest != 0) InventoryService.Record(db, line.ItemId, StockMovementKind.Count, rest, onHand, now, count.Number, reason, Trim(user, 50), null, db.ActingUserId, line.UnitCost);
            }
            else
                InventoryService.Record(db, line.ItemId, StockMovementKind.Count, delta, onHand, now, count.Number, reason, Trim(user, 50), line.VariantId, db.ActingUserId, line.UnitCost);

            changed++;
            units += delta;
        }

        count.Status = StockCountStatus.Posted;
        count.PostedAt = now;
        count.PostedBy = user;
        count.UncountedSetToZero = zeroUncounted;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new CountPosting(changed, units, lines.Sum(l => l.VarianceCost), zeroed);
    }

    /// <summary>Cancels an open count; stock is not touched.</summary>
    public async Task CancelAsync(Guid countId, string? user)
    {
        await using var db = data.Create();
        var count = await OpenCountAsync(db, countId);
        count.Status = StockCountStatus.Cancelled;
        count.CancelledAt = DateTime.Now;
        count.CancelledBy = user;
        await db.SaveChangesAsync();
    }

    private static async Task<StockCount> OpenCountAsync(RetailPlusDbContext db, Guid countId)
    {
        var count = await db.StockCounts.SingleOrDefaultAsync(c => c.Id == countId) ?? throw new BusinessRuleException("That stock count no longer exists.");
        if (count.Status != StockCountStatus.Open)
            throw new BusinessRuleException($"Stock count {count.Number} is {count.Status.ToString().ToLowerInvariant()}; it cannot be changed.");
        return count;
    }

    /// <summary>The item (and matrix line) a code stands for at the location, as the POS looks codes up.</summary>
    private static async Task<(Item Item, ItemVariant? Variant)?> FindAsync(RetailPlusDbContext db, Guid locationId, string code)
    {
        code = code.Trim();
        if (code.Length == 0) return null;
        var items = db.Items.AsNoTracking().Where(i => i.LocationId == locationId && !i.IsDeleted);
        var upper = code.ToUpperInvariant();
        var item = await items.FirstOrDefaultAsync(i => i.Plu == upper);
        if (item is not null) return (item, null);
        var forms = Upc.Forms(code);
        item = await items.FirstOrDefaultAsync(i => i.Barcode != null && forms.Contains(i.Barcode));
        if (item is not null) return (item, null);
        var variant = await db.ItemVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Code == code && items.Any(i => i.Id == v.ItemId));
        if (variant is not null) return (await items.SingleAsync(i => i.Id == variant.ItemId), variant);
        item = await items.FirstOrDefaultAsync(i => i.SupplierCode == code);
        return item is null ? null : (item, null);
    }

    /// <summary>
    /// Adds to (or replaces) what was counted of an item, a matrix line of it, or a batch of it. An item kept in batches is
    /// counted either as a whole or batch by batch on one count, never both, so nothing is counted twice.
    /// </summary>
    private static async Task<StockCountLine> CountAsync(RetailPlusDbContext db, StockCount count, Item item, ItemVariant? variant, decimal quantity, bool replace,
        ItemBatch? batch = null)
    {
        Guid? variantId = variant?.Id;
        Guid? batchId = batch?.Id;
        var line = db.StockCountLines.Local.FirstOrDefault(l => l.StockCountId == count.Id && l.ItemId == item.Id && l.VariantId == variantId && l.BatchId == batchId)
                   ?? await db.StockCountLines.FirstOrDefaultAsync(l => l.StockCountId == count.Id && l.ItemId == item.Id && l.VariantId == variantId && l.BatchId == batchId);
        if (line is null)
        {
            if (item.TracksBatches)
            {
                var lines = db.StockCountLines.Where(l => l.StockCountId == count.Id && l.ItemId == item.Id);
                bool other = db.StockCountLines.Local.Any(l => l.StockCountId == count.Id && l.ItemId == item.Id && (l.BatchId == null) != (batchId == null))
                             || await (batchId is null ? lines.Where(l => l.BatchId != null) : lines.Where(l => l.BatchId == null)).AnyAsync();
                if (other)
                    throw new BusinessRuleException(batchId is null
                        ? $"{item.Plu} is being counted batch by batch on this count; count it by batch."
                        : $"{item.Plu} is being counted as a whole on this count; remove that line to count it by batch.");
            }
            // The on-hand count now, when the item is first counted (see the class remarks).
            var fresh = await db.Items.AsNoTracking().Where(i => i.Id == item.Id).Select(i => new { i.OnHand, i.AverageCost, i.LastCost, i.Name }).SingleAsync();
            item.OnHand = fresh.OnHand;
            item.AverageCost = fresh.AverageCost;
            item.LastCost = fresh.LastCost;
            if (variant is not null)
                variant.OnHand = await db.ItemVariants.AsNoTracking().Where(v => v.Id == variant.Id).Select(v => v.OnHand).SingleAsync();
            line = NewLine(count, item, variant, DateTime.Now);
            if (batch is not null)
            {
                line.BatchId = batch.Id;
                line.BatchNumber = batch.BatchNumber;
                line.Expected = (await BatchService.StockOfAsync(db, item.Id)).Where(b => b.BatchId == batch.Id).Sum(b => b.OnHand);
                line.Description = Trim($"{item.Name} batch {batch.BatchNumber}", 60)!;
            }
            db.StockCountLines.Add(line);
        }
        line.Counted = replace ? quantity : line.Counted + quantity;
        line.NotCounted = false;
        Update(line);
        return line;
    }

    private static StockCountLine NewLine(StockCount count, Item item, ItemVariant? variant, DateTime now) => new()
    {
        StockCountId = count.Id, ItemId = item.Id, VariantId = variant?.Id, Plu = item.Plu,
        Description = Trim(variant is null ? item.Name : $"{item.Name} {variant.Value1} {variant.Value2} {variant.Value3}".Trim(), 60)!,
        Expected = variant?.OnHand ?? item.OnHand, UnitCost = item.AverageCost > 0 ? item.AverageCost : item.LastCost, CountedAt = now,
    };

    /// <summary>The codes of the bins a zone or bin count covers; null for a count of the whole location.</summary>
    private static async Task<List<string>?> BinsCountedAsync(RetailPlusDbContext db, StockCount count)
    {
        if (count.BinCode is { } bin) return [bin];
        if (count.Zone is { } zone)
            return await db.Bins.AsNoTracking().Where(b => b.LocationId == count.LocationId && b.Zone == zone).Select(b => b.Code).ToListAsync();
        return null;
    }

    private static void Update(StockCountLine line)
    {
        line.Variance = line.Counted - line.Expected;
        line.VarianceCost = Money.Round(line.Variance * line.UnitCost);
    }

    private static string? Trim(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
