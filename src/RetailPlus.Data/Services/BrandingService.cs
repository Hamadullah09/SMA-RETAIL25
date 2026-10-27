using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Services;

/// <summary>The store's logo and watermark (Setup › Branding), kept in the database for every till of the store.</summary>
public sealed class BrandingService(DataContext data)
{
    /// <summary>The largest picture kept: the app makes JPEGs of at most 800 pixels a side, well under this.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>This store's picture of the kind, or null when none is set.</summary>
    public async Task<byte[]?> GetAsync(StoreImageKind kind)
    {
        await using var db = data.Create();
        var q = db.StoreImages.AsNoTracking().Where(i => i.Kind == kind);
        if (data.Station is { } here) q = q.Where(i => i.StoreId == here.StoreId);
        return await q.Select(i => i.Data).FirstOrDefaultAsync();
    }

    /// <summary>Sets (or with null removes) this store's picture of the kind.</summary>
    public async Task SetAsync(StoreImageKind kind, byte[]? picture)
    {
        if (picture is { Length: > MaxBytes }) throw new BusinessRuleException("The picture is too large (more than 2 MB).");
        await using var db = data.Create();
        var q = db.StoreImages.Where(i => i.Kind == kind);
        if (data.Station is { } here) q = q.Where(i => i.StoreId == here.StoreId);
        var row = await q.FirstOrDefaultAsync();
        if (picture is null || picture.Length == 0)
        {
            if (row is not null) db.StoreImages.Remove(row);
        }
        else
        {
            if (row is null) db.StoreImages.Add(row = new StoreImage { Kind = kind });
            row.Data = picture;
        }
        await db.SaveChangesAsync();
    }
}
