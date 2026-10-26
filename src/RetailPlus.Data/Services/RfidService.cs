using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>How a tag was matched to an item.</summary>
public enum TagMatch
{
    Registered,
    Gtin,
    ItemCode,
}

/// <summary>The item (and matrix line) an RFID tag stands for.</summary>
public sealed record TagLookup(Item Item, ItemVariant? Variant, ItemTag? Tag, TagMatch Match);

/// <summary>An antenna with its reader, for the assignment screen.</summary>
public sealed record AntennaRow(int AntennaId, int ReaderId, string ReaderName, string Endpoint, int Port, string? Name, bool IsEnabled,
    int PowerDbm, RfidAntennaPurpose Purpose, int? StationNumber);

/// <summary>RFID readers, their antennas, which station each antenna serves, and the tags put on items.</summary>
public sealed class RfidService(DataContext data)
{
    // Readers ---------------------------------------------------------------------------------------------------

    public async Task<List<RfidReader>> GetReadersAsync(string? hubKey = null, CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var q = db.RfidReaders.AsNoTracking().Include(r => r.Antennas.OrderBy(a => a.Port)).AsQueryable();
        if (!string.IsNullOrWhiteSpace(hubKey)) q = q.Where(r => r.HubKey == hubKey);
        return await q.OrderBy(r => r.Name).ToListAsync(cancellationToken);
    }

    public async Task<RfidReader?> GetReaderAsync(int id)
    {
        await using var db = data.Create();
        return await db.RfidReaders.AsNoTracking().Include(r => r.Antennas.OrderBy(a => a.Port)).SingleOrDefaultAsync(r => r.Id == id);
    }

    /// <summary>A new reader with the vendor's defaults and its four antenna ports.</summary>
    public static RfidReader NewReader(string name) => new()
    {
        Name = name,
        Antennas = Enumerable.Range(1, 4).Select(p => new RfidAntenna { Port = p, Name = $"Antenna {p}", IsEnabled = p == 1 }).ToList(),
    };

    public async Task<RfidReader> SaveReaderAsync(RfidReader reader)
    {
        reader.Name = reader.Name.Trim();
        if (reader.Name.Length == 0) throw new BusinessRuleException("Give the reader a name.");
        if (reader.Name.Length > 40) throw new BusinessRuleException("A reader name can have at most 40 characters.");
        if (reader.Location?.Length > 60) throw new BusinessRuleException("The location can have at most 60 characters.");
        if (reader.Transport == RfidTransport.Tcp)
        {
            if (string.IsNullOrWhiteSpace(reader.Host)) throw new BusinessRuleException("Enter the reader's IP address.");
            if (reader.Port is < 1 or > 65535) throw new BusinessRuleException("The port must be between 1 and 65535.");
        }
        else if (string.IsNullOrWhiteSpace(reader.SerialPort)) throw new BusinessRuleException("Choose the serial port of the reader.");
        if (reader.Address is < 0 or > 255) throw new BusinessRuleException("The RS-485 address must be between 0 and 255 (FF).");
        if (reader.RssiThresholdDbm is < -100 or > -20) throw new BusinessRuleException("The signal threshold must be between -100 and -20 dBm.");
        reader.HubKey = string.IsNullOrWhiteSpace(reader.HubKey) ? "main" : reader.HubKey.Trim();
        for (int port = 1; port <= 4; port++)
            if (reader.Antennas.All(a => a.Port != port)) reader.Antennas.Add(new RfidAntenna { Port = port, Name = $"Antenna {port}", IsEnabled = false });
        foreach (var a in reader.Antennas)
        {
            if (a.Port is < 1 or > 4) throw new BusinessRuleException("Antenna ports are 1 to 4.");
            if (a.PowerDbm is < 0 or > 33) throw new BusinessRuleException($"Antenna {a.Port}: power must be 0 to 33 dBm.");
            if (a.StationNumber is < 1 or > 999) throw new BusinessRuleException($"Antenna {a.Port}: station numbers are 1 to 999.");
        }

        await using var db = data.Create();
        if (await db.RfidReaders.AnyAsync(r => r.Id != reader.Id && r.Name == reader.Name))
            throw new BusinessRuleException($"There is already a reader called {reader.Name}.");
        if (reader.Transport == RfidTransport.Tcp && !string.Equals(reader.Host, "sim", StringComparison.OrdinalIgnoreCase)
            && await db.RfidReaders.AnyAsync(r => r.Id != reader.Id && r.Transport == RfidTransport.Tcp && r.Host == reader.Host && r.Port == reader.Port))
            throw new BusinessRuleException($"Another reader already uses {reader.Host}:{reader.Port}. Every reader needs its own IP address.");

        if (reader.Id == 0)
        {
            foreach (var a in reader.Antennas) a.Id = 0;
            db.RfidReaders.Add(reader);
        }
        else
        {
            var existing = await db.RfidReaders.Include(r => r.Antennas).SingleOrDefaultAsync(r => r.Id == reader.Id)
                           ?? throw new BusinessRuleException("This reader was deleted at another station.");
            var antennas = reader.Antennas;
            db.Entry(existing).CurrentValues.SetValues(reader);
            db.Entry(existing).Property(r => r.RowVersion).OriginalValue = reader.RowVersion;
            foreach (var a in antennas)
            {
                var target = existing.Antennas.FirstOrDefault(x => x.Port == a.Port);
                if (target is null) existing.Antennas.Add(target = new RfidAntenna { Port = a.Port });
                target.Name = a.Name;
                target.IsEnabled = a.IsEnabled;
                target.PowerDbm = a.PowerDbm;
                target.Purpose = a.Purpose;
                target.StationNumber = a.StationNumber;
            }
            reader = existing;
        }
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("This reader was changed at another station. Open it again and repeat your changes.");
        }
        return reader;
    }

    public async Task DeleteReaderAsync(int id)
    {
        await using var db = data.Create();
        await db.RfidReaders.Where(r => r.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>Records that the settings were written to the reader (and its firmware version, when known).</summary>
    public async Task MarkAppliedAsync(int readerId, string? firmware)
    {
        await using var db = data.Create();
        var now = DateTime.Now;
        await db.RfidReaders.Where(r => r.Id == readerId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.SettingsAppliedAt, now)
            .SetProperty(r => r.FirmwareVersion, r => firmware ?? r.FirmwareVersion));
    }

    // Antennas and stations ----------------------------------------------------------------------------------

    public async Task<List<AntennaRow>> GetAntennasAsync(int? stationNumber = null)
    {
        await using var db = data.Create();
        var q = db.RfidAntennas.AsNoTracking().Join(db.RfidReaders.AsNoTracking(), a => a.ReaderId, r => r.Id, (a, r) => new { a, r });
        if (stationNumber is int s) q = q.Where(x => x.a.StationNumber == s);
        var rows = await q.OrderBy(x => x.r.Name).ThenBy(x => x.a.Port).ToListAsync();
        return rows.Select(x => new AntennaRow(x.a.Id, x.r.Id, x.r.Name, x.r.Endpoint, x.a.Port, x.a.Name, x.a.IsEnabled && x.r.IsEnabled,
            x.a.PowerDbm, x.a.Purpose, x.a.StationNumber)).ToList();
    }

    /// <summary>Saves the station assignment grid (station, purpose, name, enabled and power of each antenna).</summary>
    public async Task SaveAssignmentsAsync(IEnumerable<AntennaRow> rows)
    {
        var list = rows.ToList();
        if (list.FirstOrDefault(r => r.StationNumber is < 1 or > 999) is { } bad)
            throw new BusinessRuleException($"{bad.ReaderName} antenna {bad.Port}: station numbers are 1 to 999.");
        if (list.FirstOrDefault(r => r.PowerDbm is < 0 or > 33) is { } loud)
            throw new BusinessRuleException($"{loud.ReaderName} antenna {loud.Port}: power must be 0 to 33 dBm.");
        await using var db = data.Create();
        var ids = list.Select(r => r.AntennaId).ToList();
        var antennas = await db.RfidAntennas.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        foreach (var r in list)
        {
            if (!antennas.TryGetValue(r.AntennaId, out var a)) continue;
            a.StationNumber = r.StationNumber;
            a.Purpose = r.Purpose;
            a.Name = r.Name;
            a.IsEnabled = r.IsEnabled;
            a.PowerDbm = r.PowerDbm;
        }
        await db.SaveChangesAsync();
    }

    // Tags --------------------------------------------------------------------------------------------------

    public async Task<List<ItemTag>> GetTagsAsync(int itemId)
    {
        await using var db = data.Create();
        return await db.ItemTags.AsNoTracking().Where(t => t.ItemId == itemId).OrderBy(t => t.RegisteredAt).ToListAsync();
    }

    public async Task<int> CountTagsAsync(int itemId, ItemTagStatus status)
    {
        await using var db = data.Create();
        return await db.ItemTags.CountAsync(t => t.ItemId == itemId && t.Status == status);
    }

    /// <summary>
    /// Ties a tag to an item. A tag already on another item is refused unless <paramref name="move"/> is set
    /// (relabelling).
    /// </summary>
    public async Task<ItemTag> RegisterTagAsync(string epc, int itemId, int? variantId = null, string? serialNumber = null, bool move = false)
    {
        var code = Epc.Normalize(epc) ?? throw new BusinessRuleException("A tag code is hexadecimal (0-9, A-F) with an even number of digits.");
        await using var db = data.Create();
        var existing = await db.ItemTags.SingleOrDefaultAsync(t => t.Epc == code);
        if (existing is not null)
        {
            if (existing.ItemId == itemId) return existing;
            if (!move)
            {
                var other = await db.Items.Where(i => i.Id == existing.ItemId).Select(i => i.Plu + " " + i.Name).SingleOrDefaultAsync();
                throw new BusinessRuleException($"Tag {code} already belongs to {other}.");
            }
            existing.ItemId = itemId;
            existing.VariantId = variantId;
            existing.SerialNumber = serialNumber;
            existing.Status = ItemTagStatus.InStock;
            existing.RegisteredAt = DateTime.Now;
            await db.SaveChangesAsync();
            return existing;
        }
        var tag = new ItemTag { Epc = code, ItemId = itemId, VariantId = variantId, SerialNumber = serialNumber, Status = ItemTagStatus.InStock, RegisteredAt = DateTime.Now };
        db.ItemTags.Add(tag);
        await db.SaveChangesAsync();
        return tag;
    }

    public async Task RemoveTagAsync(int tagId)
    {
        await using var db = data.Create();
        await db.ItemTags.Where(t => t.Id == tagId).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Finds the item a tag stands for at a location: a registered tag first, then a GS1 SGTIN-96 tag by its GTIN
    /// (the product barcode), then a tag holding the item code as text, then the hex code itself as a stock code.
    /// </summary>
    public async Task<TagLookup?> ResolveAsync(int locationId, string epc)
    {
        var code = Epc.Normalize(epc);
        if (code is null) return null;
        await using var db = data.Create();
        var items = db.Items.AsNoTracking().Include(i => i.Department).Include(i => i.Supplier).Include(i => i.KitComponents)
            .Include(i => i.Variants).Include(i => i.Serials).Where(i => !i.IsDeleted && i.LocationId == locationId);

        var tag = await db.ItemTags.AsNoTracking().SingleOrDefaultAsync(t => t.Epc == code);
        if (tag is not null)
        {
            var item = await items.SingleOrDefaultAsync(i => i.Id == tag.ItemId);
            if (item is null)
            {
                // Tagged at another location: sell the item with the same stock code here.
                var plu = await db.Items.Where(i => i.Id == tag.ItemId).Select(i => i.Plu).SingleOrDefaultAsync();
                item = plu is null ? null : await items.FirstOrDefaultAsync(i => i.Plu == plu);
            }
            if (item is not null) return new TagLookup(item, item.Variants.FirstOrDefault(v => v.Id == tag.VariantId), tag, TagMatch.Registered);
        }

        if (Epc.TryDecodeSgtin96(code, out var gtin, out _))
        {
            var forms = Epc.BarcodeForms(gtin).ToList();
            var item = await items.FirstOrDefaultAsync(i => forms.Contains(i.Plu) || (i.SupplierCode != null && forms.Contains(i.SupplierCode)));
            if (item is not null) return new TagLookup(item, null, null, TagMatch.Gtin);
            var variant = await db.ItemVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Code != null && forms.Contains(v.Code));
            if (variant is not null && await items.FirstOrDefaultAsync(i => i.Id == variant.ItemId) is { } owner)
                return new TagLookup(owner, owner.Variants.First(v => v.Id == variant.Id), null, TagMatch.Gtin);
        }

        var candidates = new List<string> { code };
        if (Epc.TryAscii(code, out var text)) candidates.Insert(0, text.ToUpperInvariant());
        foreach (var candidate in candidates)
        {
            var item = await items.FirstOrDefaultAsync(i => i.Plu == candidate);
            if (item is not null) return new TagLookup(item, null, null, TagMatch.ItemCode);
        }
        return null;
    }
}
