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
    public async Task<StockCount> StartAsync(Guid locationId, string? note, string? user)
    {
        await using var db = data.Create();
        if (!await db.Locations.AnyAsync(l => l.Id == locationId)) throw new BusinessRuleException("That location does not exist.");
        var count = new StockCount
        {
            LocationId = locationId, Status = StockCountStatus.Open, StartedAt = DateTime.Now, StartedBy = user,
            Note = string.IsNullOrWhiteSpace(note) ? null : Trim(note.Trim(), 200),
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
    public async Task<StockCountLine> CountCodeAsync(Guid countId, string code, decimal quantity, bool replace = false)
    {
        await using var db = data.Create();
        var count = await OpenCountAsync(db, countId);
        var (item, variant) = await FindAsync(db, count.LocationId, code)
                              ?? throw new BusinessRuleException($"No item has the code {code.Trim()}.");
        var line = await CountAsync(db, count, item, variant, quantity, replace);
        await db.SaveChangesAsync();
        return line;
    }

    /// <summary>Counts an item (or one matrix line of it) picked from the inventory.</summary>
    public async Task<StockCountLine> CountItemAsync(Guid countId, Guid itemId, Guid? variantId, decimal quantity, bool replace = false)
    {
        await using var db = data.Create();
        var count = await OpenCountAsync(db, countId);
        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.LocationId == count.LocationId)
                   ?? throw new BusinessRuleException("That item is not in the location being counted.");
        var variant = variantId is Guid v ? await db.ItemVariants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == v && x.ItemId == itemId) : null;
        var line = await CountAsync(db, count, item, variant, quantity, replace);
        await db.SaveChangesAsync();
        return line;
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
            var others = await db.Items.AsNoTracking()
                .Where(i => i.LocationId == count.LocationId && !i.IsDeleted && i.OnHand != 0
                            && (i.ItemType == ItemType.Standard || i.ItemType == ItemType.Matrix || i.ItemType == ItemType.Serialized || i.ItemType == ItemType.Kit))
                .ToListAsync();
            foreach (var item in others.Where(i => !counted.Contains(i.Id)))
            {
                var line = NewLine(count, item, null, now);
                line.NotCounted = true;
                Update(line);
                db.StockCountLines.Add(line);
                lines.Add(line);
                zeroed++;
            }
        }

        int changed = 0;
        decimal units = 0;
        string reason = $"Stock count {count.Number}";
        foreach (var line in lines.Where(l => l.Variance != 0))
        {
            decimal delta = line.Variance;
            await db.Items.Where(i => i.Id == line.ItemId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.OnHand, i => i.OnHand + delta).SetProperty(i => i.UpdatedAtUtc, DateTime.UtcNow));
            if (line.VariantId is Guid variantId)
                await db.ItemVariants.Where(v => v.Id == variantId).ExecuteUpdateAsync(s => s
                    .SetProperty(v => v.OnHand, v => v.OnHand + delta).SetProperty(v => v.UpdatedAtUtc, DateTime.UtcNow));
            else if (line.NotCounted)
                // An item set to zero: its matrix lines go to zero with it.
                await db.ItemVariants.Where(v => v.ItemId == line.ItemId && v.OnHand != 0).ExecuteUpdateAsync(s => s
                    .SetProperty(v => v.OnHand, 0m).SetProperty(v => v.UpdatedAtUtc, DateTime.UtcNow));
            var onHand = await db.Items.Where(i => i.Id == line.ItemId).Select(i => i.OnHand).SingleAsync();
            InventoryService.Record(db, line.ItemId, StockMovementKind.Count, delta, onHand, now, count.Number, reason, Trim(user, 15), line.VariantId, line.UnitCost);
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

    private static async Task<StockCountLine> CountAsync(RetailPlusDbContext db, StockCount count, Item item, ItemVariant? variant, decimal quantity, bool replace)
    {
        Guid? variantId = variant?.Id;
        var line = db.StockCountLines.Local.FirstOrDefault(l => l.StockCountId == count.Id && l.ItemId == item.Id && l.VariantId == variantId)
                   ?? await db.StockCountLines.FirstOrDefaultAsync(l => l.StockCountId == count.Id && l.ItemId == item.Id && l.VariantId == variantId);
        if (line is null)
        {
            // The on-hand count now, when the item is first counted (see the class remarks).
            var fresh = await db.Items.AsNoTracking().Where(i => i.Id == item.Id).Select(i => new { i.OnHand, i.AverageCost, i.LastCost, i.Name }).SingleAsync();
            item.OnHand = fresh.OnHand;
            item.AverageCost = fresh.AverageCost;
            item.LastCost = fresh.LastCost;
            if (variant is not null)
                variant.OnHand = await db.ItemVariants.AsNoTracking().Where(v => v.Id == variant.Id).Select(v => v.OnHand).SingleAsync();
            line = NewLine(count, item, variant, DateTime.Now);
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

    private static void Update(StockCountLine line)
    {
        line.Variance = line.Counted - line.Expected;
        line.VarianceCost = Money.Round(line.Variance * line.UnitCost);
    }

    private static string? Trim(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
