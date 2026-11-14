using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Services;

/// <summary>A location of a store as the back office lists it: whether it is the store's hold location, and how many items it keeps.</summary>
public sealed record LocationRow(Guid Id, Guid StoreId, string Code, string? Name, bool IsHold, int Items, DateTime UpdatedAtUtc);

/// <summary>
/// A store's locations, set up on the server: listed, added and renamed, and one of them marked as the store's hold
/// location, where an output inspection's failed goods go when no other location is named
/// (<see cref="QualityService.InspectOutputAsync"/>). The mark is a setting of the store (<see cref="StoreSettingExtra"/>
/// <see cref="HoldExtra"/>), and locations are master data: both reach the store's tills with their next download.
/// A location's code stays as it was made, because a till finds the location it sells from by its code (the station's
/// Location code setting); its name can change.
/// </summary>
public sealed class LocationService(DataContext data)
{
    /// <summary>The store setting that holds the id of the store's hold location.</summary>
    public const string HoldExtra = "QUALITY_HOLD";

    /// <summary>The store's locations by code, with the hold location marked.</summary>
    public async Task<List<LocationRow>> ListAsync(Guid storeId)
    {
        await using var db = data.Create();
        var hold = await HoldIdAsync(db, storeId);
        var rows = await db.Locations.AsNoTracking().Where(l => l.StoreId == storeId).OrderBy(l => l.Code)
            .Select(l => new { l.Id, l.Code, l.Name, l.UpdatedAtUtc, Items = db.Items.Count(i => i.LocationId == l.Id && !i.IsDeleted) })
            .ToListAsync();
        return [.. rows.Select(r => new LocationRow(r.Id, storeId, r.Code, r.Name, r.Id == hold, r.Items, r.UpdatedAtUtc))];
    }

    public async Task<LocationRow?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        var store = await db.Locations.AsNoTracking().Where(l => l.Id == id).Select(l => (Guid?)l.StoreId).SingleOrDefaultAsync();
        return store is Guid s ? (await ListAsync(s)).Single(l => l.Id == id) : null;
    }

    /// <summary>Adds a location to a store: a code of 1 to 3 characters not used there yet, and a name; marked as the hold location when asked.</summary>
    public async Task<LocationRow> AddAsync(Guid storeId, string? code, string? name, bool isHold = false)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        if (c.Length is < 1 or > 3) throw new BusinessRuleException("A location code has 1 to 3 characters.");
        var n = CheckName(name);
        await using (var db = data.Create())
        {
            if (!await db.Stores.AnyAsync(s => s.Id == storeId)) throw new BusinessRuleException("That store does not exist.");
            if (await db.Locations.AnyAsync(l => l.StoreId == storeId && l.Code == c)) throw new BusinessRuleException($"The store has a location {c} already.");
            var location = new Location { Id = Guid.CreateVersion7(), StoreId = storeId, Code = c, Name = n };
            db.Locations.Add(location);
            await db.SaveChangesAsync();
            if (isHold) await SetHoldAsync(storeId, location.Id);
            return (await GetAsync(location.Id))!;
        }
    }

    /// <summary>
    /// Renames a location, and marks it as the store's hold location (true) or takes the mark off it (false); null leaves
    /// the mark as it is. The code does not change.
    /// </summary>
    public async Task<LocationRow> RenameAsync(Guid id, string? name, bool? isHold = null)
    {
        var n = CheckName(name);
        Guid store;
        await using (var db = data.Create())
        {
            var location = await db.Locations.SingleOrDefaultAsync(l => l.Id == id) ?? throw new BusinessRuleException("That location no longer exists.");
            store = location.StoreId;
            if (location.Name != n)
            {
                location.Name = n;
                await db.SaveChangesAsync();
            }
        }
        if (isHold is bool hold)
        {
            var current = await HoldLocationAsync(store);
            if (hold && current != id) await SetHoldAsync(store, id);
            else if (!hold && current == id) await SetHoldAsync(store, null);
        }
        return (await GetAsync(id))!;
    }

    /// <summary>The store's hold location, if one is marked (and still one of the store's).</summary>
    public async Task<Guid?> HoldLocationAsync(Guid storeId)
    {
        await using var db = data.Create();
        return await HoldIdAsync(db, storeId);
    }

    /// <summary>
    /// Marks a location as the store's hold location, or (null) leaves the store without one. The store's only location
    /// cannot be its hold location: failed goods are moved out of the location they were made in.
    /// </summary>
    public async Task SetHoldAsync(Guid storeId, Guid? locationId)
    {
        await using var db = data.Create();
        if (locationId is Guid id)
        {
            if (!await db.Locations.AnyAsync(l => l.Id == id && l.StoreId == storeId)) throw new BusinessRuleException("The hold location must be one of the store's locations.");
            if (await db.Locations.CountAsync(l => l.StoreId == storeId) < 2)
                throw new BusinessRuleException("A store's only location cannot be its hold location; add another location for goods on hold.");
        }
        // Kept as a row whose value is emptied rather than deleted, so the change reaches the tills as any other.
        var row = await db.StoreSettingExtras.FirstOrDefaultAsync(x => x.StoreId == storeId && x.Name == HoldExtra);
        var value = locationId?.ToString("D");
        if (row is null)
        {
            if (value is null) return;
            db.StoreSettingExtras.Add(new StoreSettingExtra { StoreId = storeId, Name = HoldExtra, Value = value });
        }
        else if (row.Value == value) return;
        else row.Value = value;
        await db.SaveChangesAsync();
    }

    /// <summary>The store's hold location: the one its setting names, when that is still a location of the store.</summary>
    public static async Task<Guid?> HoldIdAsync(RetailPlusDbContext db, Guid storeId, CancellationToken cancellationToken = default)
    {
        var value = await db.StoreSettingExtras.AsNoTracking().Where(x => x.StoreId == storeId && x.Name == HoldExtra).Select(x => x.Value).FirstOrDefaultAsync(cancellationToken);
        if (!Guid.TryParse(value, out var id)) return null;
        return await db.Locations.AsNoTracking().AnyAsync(l => l.Id == id && l.StoreId == storeId, cancellationToken) ? id : null;
    }

    /// <summary>
    /// The location a store's lists and new records use when none is named: its first by code that is not the hold
    /// location (goods on hold are not sold, ordered or counted from), else its first.
    /// </summary>
    public static async Task<Guid?> MainIdAsync(RetailPlusDbContext db, Guid storeId, CancellationToken cancellationToken = default)
    {
        var hold = await HoldIdAsync(db, storeId, cancellationToken);
        return await db.Locations.AsNoTracking().Where(l => l.StoreId == storeId).OrderBy(l => l.Id == hold ? 1 : 0).ThenBy(l => l.Code)
            .Select(l => (Guid?)l.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private static string? CheckName(string? name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (n is { Length: > 50 }) throw new BusinessRuleException("A location name has at most 50 characters.");
        return n;
    }
}
