using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A row of the Inventory window's browse list.</summary>
public sealed record ItemRow(int Id, string Plu, string Name, string? Department, string? Category, string? Supplier, string? Size,
    decimal OnHand, decimal OnOrder, decimal Price1, decimal LastCost, decimal AverageCost, ItemType ItemType, bool IsFlagged, bool IsDeleted,
    decimal Weight = 0, string? WeightUnit = null, int Tags = 0, string? Barcode = null)
{
    /// <summary>Average cost, or the last cost when no average is known yet.</summary>
    public decimal Cost => AverageCost > 0 ? AverageCost : LastCost;

    /// <summary>"90 g", or empty when no weight is set.</summary>
    public string WeightText => Weight > 0 ? $"{Weight:0.###} {WeightUnit}".Trim() : "";
}

public enum ItemSearchField
{
    Any,
    Plu,
    Name,
    Department,
    Supplier,
    SupplierCode,
    /// <summary>An RFID tag code: finds the item the tag belongs to.</summary>
    Epc,
    Barcode,
}

/// <summary>Inventory window operations for one location.</summary>
public sealed class InventoryService(DataContext data)
{
    public async Task<List<ItemRow>> SearchAsync(int locationId, string? text, ItemSearchField field = ItemSearchField.Any,
        bool includeDeleted = false, bool flaggedOnly = false, int take = 2000)
    {
        await using var db = data.Create();
        var q = db.Items.AsNoTracking().Where(i => i.LocationId == locationId);
        if (!includeDeleted) q = q.Where(i => !i.IsDeleted);
        if (flaggedOnly) q = q.Where(i => i.IsFlagged);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim();
            var code = t.ToUpperInvariant();
            q = field switch
            {
                ItemSearchField.Plu => q.Where(i => i.Plu.StartsWith(t)),
                ItemSearchField.Name => q.Where(i => i.Name.Contains(t)),
                ItemSearchField.Department => q.Where(i => i.Department != null && i.Department.Name.StartsWith(t)),
                ItemSearchField.Supplier => q.Where(i => i.Supplier != null && (i.Supplier.Company ?? "").StartsWith(t)),
                ItemSearchField.SupplierCode => q.Where(i => i.SupplierCode != null && i.SupplierCode.StartsWith(t)),
                ItemSearchField.Epc => q.Where(i => db.ItemTags.Any(g => g.ItemId == i.Id && g.Epc.StartsWith(code))),
                ItemSearchField.Barcode => q.Where(i => i.Barcode != null && i.Barcode.StartsWith(t)),
                _ => q.Where(i => i.Plu.StartsWith(t) || i.Name.Contains(t) || (i.SupplierCode != null && i.SupplierCode.StartsWith(t)) || (i.Brand != null && i.Brand.StartsWith(t))
                                  || (i.Barcode != null && i.Barcode.StartsWith(t))
                                  || db.ItemTags.Any(g => g.ItemId == i.Id && g.Epc == code)),
            };
        }
        return await q.OrderBy(i => i.Plu).Take(take)
            .Select(i => new ItemRow(i.Id, i.Plu, i.Name, i.Department != null ? i.Department.Name : null, i.Category != null ? i.Category.Name : null,
                i.Supplier != null ? i.Supplier.Company : null, i.Size, i.OnHand, i.OnOrder, i.Price1, i.LastCost, i.AverageCost, i.ItemType, i.IsFlagged, i.IsDeleted,
                i.Weight, i.WeightUnit, db.ItemTags.Count(g => g.ItemId == i.Id), i.Barcode))
            .ToListAsync();
    }

    public async Task<int> CountAsync(int locationId)
    {
        await using var db = data.Create();
        return await db.Items.CountAsync(i => i.LocationId == locationId && !i.IsDeleted);
    }

    /// <summary>The item with everything the Item Detail window shows.</summary>
    public async Task<Item?> GetAsync(int id)
    {
        await using var db = data.Create();
        return await db.Items.AsNoTracking().AsSplitQuery()
            .Include(i => i.Department).Include(i => i.Category).Include(i => i.Supplier)
            .Include(i => i.Suppliers.OrderBy(s => s.Rank))
            .Include(i => i.MatrixColumns.OrderBy(c => c.Ordinal))
            .Include(i => i.Variants.OrderBy(v => v.Ordinal))
            .Include(i => i.KitComponents.OrderBy(k => k.Ordinal))
            .Include(i => i.Serials)
            .SingleOrDefaultAsync(i => i.Id == id);
    }

    /// <summary>
    /// Looks up a scanned or typed code the way the POS does: stock code, then matrix line code, then the supplier's code.
    /// Returns the variant when a matrix code matched.
    /// </summary>
    public async Task<(Item? Item, ItemVariant? Variant)> FindByCodeAsync(int locationId, string code)
    {
        code = code.Trim();
        if (code.Length == 0) return (null, null);
        await using var db = data.Create();
        var items = db.Items.AsNoTracking().Include(i => i.Department).Include(i => i.Supplier)
            .Include(i => i.KitComponents).Include(i => i.Variants).Include(i => i.Serials)
            .Where(i => i.LocationId == locationId && !i.IsDeleted);
        var item = await items.SingleOrDefaultAsync(i => i.Plu == code);
        if (item is not null) return (item, null);
        var forms = Upc.Forms(code);
        item = await items.FirstOrDefaultAsync(i => i.Barcode != null && forms.Contains(i.Barcode));
        if (item is not null) return (item, null);
        var variant = await db.ItemVariants.AsNoTracking()
            .Where(v => v.Code == code && db.Items.Any(i => i.Id == v.ItemId && i.LocationId == locationId && !i.IsDeleted))
            .FirstOrDefaultAsync();
        if (variant is not null) return (await items.SingleAsync(i => i.Id == variant.ItemId), variant);
        item = await items.FirstOrDefaultAsync(i => i.SupplierCode == code);
        return (item, null);
    }

    /// <summary>The next free in-store UPC-A (number system 4), counting on from the highest one in use.</summary>
    public async Task<string> NewInStoreBarcodeAsync()
    {
        await using var db = data.Create();
        var codes = await db.Items.Where(i => i.Barcode != null && i.Barcode.Length == 12 && EF.Functions.Like(i.Barcode, "4%")).Select(i => i.Barcode!).ToListAsync();
        return Upc.InStore((codes.Select(Upc.InStoreNumber).Max() ?? 0) + 1);
    }

    /// <summary>Gives each of the items that has no barcode the next in-store UPC-A. Returns how many got one.</summary>
    public async Task<int> AssignInStoreBarcodesAsync(IEnumerable<int> itemIds)
    {
        int count = 0;
        foreach (var id in itemIds.Distinct())
        {
            await using var db = data.Create();
            if (await db.Items.Where(i => i.Id == id).Select(i => i.Barcode).SingleOrDefaultAsync() is not null) continue;
            var code = await NewInStoreBarcodeAsync();
            count += await db.Items.Where(i => i.Id == id && i.Barcode == null).ExecuteUpdateAsync(s => s.SetProperty(i => i.Barcode, code));
        }
        return count;
    }

    public async Task<bool> PluExistsAsync(int locationId, string plu, int exceptId = 0)
    {
        await using var db = data.Create();
        return await db.Items.AnyAsync(i => i.LocationId == locationId && i.Plu == plu && !i.IsDeleted && i.Id != exceptId);
    }

    /// <summary>
    /// Saves an item and its lists. Department, category and supplier are given by name and created when new.
    /// Throws <see cref="ConcurrencyConflictException"/> when another station changed the item meanwhile.
    /// </summary>
    public async Task<Item> SaveAsync(Item item, string? departmentName, string? categoryName, string? supplierName)
    {
        if (string.IsNullOrWhiteSpace(item.Plu)) throw new BusinessRuleException("Enter a stock code.");
        if (string.IsNullOrWhiteSpace(item.Name)) throw new BusinessRuleException("Enter a description.");
        item.Plu = item.Plu.Trim().ToUpperInvariant();
        if (await PluExistsAsync(item.LocationId, item.Plu, item.Id))
            throw new BusinessRuleException($"Stock code {item.Plu} is already used by another item.");

        await using var db = data.Create();
        item.Barcode = string.IsNullOrWhiteSpace(item.Barcode) ? null : item.Barcode.Trim();
        if (item.Barcode is not null)
        {
            if (!Upc.IsValid(item.Barcode))
                throw new BusinessRuleException($"Barcode {item.Barcode} is not valid. A barcode has 12 digits (UPC-A), or 13 or 8, and its last digit is a check digit.");
            var forms = Upc.Forms(item.Barcode);
            var other = await db.Items.Where(i => i.LocationId == item.LocationId && !i.IsDeleted && i.Id != item.Id && i.Barcode != null && forms.Contains(i.Barcode))
                .Select(i => i.Plu + " " + i.Name).FirstOrDefaultAsync();
            if (other is not null) throw new BusinessRuleException($"Barcode {item.Barcode} is already on {other}.");
        }
        item.DepartmentId = await LookupAsync(db.Departments, departmentName, n => new Department { Name = n }, db);
        item.CategoryId = await LookupAsync(db.Categories, categoryName, n => new Category { Name = n }, db);
        item.SupplierId = string.IsNullOrWhiteSpace(supplierName) ? null
            : (await db.Suppliers.Where(s => !s.IsDeleted && s.Company == supplierName.Trim()).Select(s => (int?)s.Id).FirstOrDefaultAsync());
        if (item.Price1 > 0) item.MarginPercent = Money.MarginPercent(item.Price1, item.AverageCost > 0 ? item.AverageCost : item.LastCost);
        item.LastUpdated = DateOnly.FromDateTime(DateTime.Today);

        var suppliers = item.Suppliers; var columns = item.MatrixColumns; var variants = item.Variants;
        var kit = item.KitComponents; var serials = item.Serials;
        item.Department = null; item.Category = null; item.Supplier = null; item.Location = null;
        item.Suppliers = []; item.MatrixColumns = []; item.Variants = []; item.KitComponents = []; item.Serials = []; item.PeriodSales = [];

        if (item.Id == 0) db.Items.Add(item);
        else db.Items.Update(item);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException($"Item {item.Plu} was changed at another station. Reload it and make your changes again.");
        }

        // Replace the child lists (they are small).
        await db.ItemSuppliers.Where(s => s.ItemId == item.Id).ExecuteDeleteAsync();
        await db.ItemMatrixColumns.Where(s => s.ItemId == item.Id).ExecuteDeleteAsync();
        await db.ItemVariants.Where(s => s.ItemId == item.Id).ExecuteDeleteAsync();
        await db.KitComponents.Where(s => s.KitItemId == item.Id).ExecuteDeleteAsync();
        await db.ItemSerials.Where(s => s.ItemId == item.Id).ExecuteDeleteAsync();
        int rank = 0;
        foreach (var s in suppliers.Where(s => !string.IsNullOrWhiteSpace(s.SupplierName)))
            db.ItemSuppliers.Add(new ItemSupplier { ItemId = item.Id, Rank = ++rank, SupplierName = s.SupplierName.Trim(), SupplierId = s.SupplierId, SupplierStockCode = s.SupplierStockCode, Cost = s.Cost, CaseQuantity = s.CaseQuantity });
        int ord = 0;
        foreach (var c in columns.Where(c => !string.IsNullOrWhiteSpace(c.Heading)))
            db.ItemMatrixColumns.Add(new ItemMatrixColumn { ItemId = item.Id, Ordinal = ++ord, Heading = c.Heading.Trim() });
        ord = 0;
        foreach (var v in variants.Where(v => !string.IsNullOrWhiteSpace(v.Value1) || !string.IsNullOrWhiteSpace(v.Code)))
            db.ItemVariants.Add(new ItemVariant { ItemId = item.Id, Ordinal = ++ord, Value1 = v.Value1, Value2 = v.Value2, Value3 = v.Value3, Code = v.Code, OnHand = v.OnHand });
        ord = 0;
        foreach (var k in kit.Where(k => !string.IsNullOrWhiteSpace(k.ComponentPlu)))
        {
            var plu = k.ComponentPlu.Trim().ToUpperInvariant();
            var componentId = await db.Items.Where(i => i.LocationId == item.LocationId && i.Plu == plu && !i.IsDeleted).Select(i => (int?)i.Id).FirstOrDefaultAsync();
            db.KitComponents.Add(new KitComponent { KitItemId = item.Id, Ordinal = ++ord, ComponentPlu = plu, ComponentItemId = componentId, Description = k.Description, Quantity = k.Quantity == 0 ? 1 : k.Quantity });
        }
        foreach (var s in serials.Where(s => !string.IsNullOrWhiteSpace(s.SerialNumber)))
            db.ItemSerials.Add(new ItemSerial { ItemId = item.Id, SerialNumber = s.SerialNumber.Trim(), Status = s.Status ?? "IN STOCK", StatusDate = s.StatusDate });
        if (item.ItemType == ItemType.Matrix && variants.Count > 0)
            item.OnHand = variants.Sum(v => v.OnHand);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task<int?> LookupAsync<T>(DbSet<T> set, string? name, Func<string, T> create, RetailPlusDbContext db) where T : class
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim().ToUpperInvariant();
        var entity = await set.FirstOrDefaultAsync(e => EF.Property<string>(e, "Name") == name);
        if (entity is null)
        {
            entity = create(name);
            set.Add(entity);
            await db.SaveChangesAsync();
        }
        return (int)db.Entry(entity).Property("Id").CurrentValue!;
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = data.Create();
        await db.Items.Where(i => i.Id == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.IsDeleted, true).SetProperty(i => i.DeletedAt, DateTime.Now));
    }

    public async Task RestoreAsync(int id)
    {
        await using var db = data.Create();
        var item = await db.Items.SingleAsync(i => i.Id == id);
        if (await db.Items.AnyAsync(i => i.LocationId == item.LocationId && i.Plu == item.Plu && !i.IsDeleted && i.Id != id))
            throw new BusinessRuleException($"Another item now uses stock code {item.Plu}.");
        item.IsDeleted = false;
        item.DeletedAt = null;
        await db.SaveChangesAsync();
    }

    public async Task SetFlagAsync(IEnumerable<int> ids, bool flagged)
    {
        var list = ids.ToList();
        await using var db = data.Create();
        await db.Items.Where(i => list.Contains(i.Id)).ExecuteUpdateAsync(s => s.SetProperty(i => i.IsFlagged, flagged));
    }

    public async Task ClearFlagsAsync(int locationId)
    {
        await using var db = data.Create();
        await db.Items.Where(i => i.LocationId == locationId && i.IsFlagged).ExecuteUpdateAsync(s => s.SetProperty(i => i.IsFlagged, false));
    }

    /// <summary>Adjust Stock: adds (or with <paramref name="setTo"/> replaces) the on-hand count and logs the change.</summary>
    public async Task<decimal> AdjustStockAsync(int itemId, decimal quantity, string? reason, string? user, bool setTo = false)
    {
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var item = await db.Items.SingleAsync(i => i.Id == itemId);
        decimal delta = setTo ? quantity - item.OnHand : quantity;
        item.OnHand += delta;
        db.StockAdjustments.Add(new StockAdjustment { ItemId = itemId, At = DateTime.Now, Quantity = delta, OnHandAfter = item.OnHand, Reason = reason, User = user });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return item.OnHand;
    }

    public async Task<List<StockAdjustment>> GetAdjustmentsAsync(int itemId)
    {
        await using var db = data.Create();
        return await db.StockAdjustments.AsNoTracking().Where(a => a.ItemId == itemId).OrderByDescending(a => a.At).ToListAsync();
    }

    public async Task<List<ItemPeriodSales>> GetPeriodSalesAsync(int itemId, int year, SalesPeriodKind kind)
    {
        await using var db = data.Create();
        return await db.ItemPeriodSales.AsNoTracking().Where(p => p.ItemId == itemId && p.Year == year && p.Kind == kind).OrderBy(p => p.Period).ToListAsync();
    }

    /// <summary>
    /// Tools / Change Prices: raises or lowers Price 1 by a percentage, or sets prices from cost and margin, for all
    /// (or flagged, or one department's) items. Returns the number of items changed.
    /// </summary>
    public async Task<int> ChangePricesAsync(int locationId, decimal percent, bool flaggedOnly, string? department, bool fromMargin = false, decimal margin = 0)
    {
        await using var db = data.Create();
        var q = db.Items.Where(i => i.LocationId == locationId && !i.IsDeleted);
        if (flaggedOnly) q = q.Where(i => i.IsFlagged);
        if (!string.IsNullOrWhiteSpace(department)) q = q.Where(i => i.Department != null && i.Department.Name == department);
        var items = await q.ToListAsync();
        foreach (var i in items)
        {
            if (fromMargin)
            {
                var cost = i.AverageCost > 0 ? i.AverageCost : i.LastCost;
                if (cost <= 0) continue;
                i.Price1 = Money.SuggestPrice(cost, margin);
            }
            else
            {
                i.Price1 = Money.Round(i.Price1 * (1 + percent / 100m));
            }
            var c = i.AverageCost > 0 ? i.AverageCost : i.LastCost;
            i.MarginPercent = Money.MarginPercent(i.Price1, c);
        }
        await db.SaveChangesAsync();
        return items.Count;
    }

    /// <summary>Copies a list of items to another location (Tools / Copy to location), skipping codes already there.</summary>
    public async Task<int> CopyToLocationAsync(IEnumerable<int> itemIds, int targetLocationId)
    {
        var ids = itemIds.ToList();
        await using var db = data.Create();
        var existing = (await db.Items.Where(i => i.LocationId == targetLocationId && !i.IsDeleted).Select(i => i.Plu).ToListAsync()).ToHashSet();
        var source = await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync();
        int n = 0;
        foreach (var s in source.Where(s => !existing.Contains(s.Plu)))
        {
            s.Id = 0; s.LocationId = targetLocationId; s.OnHand = 0; s.OnOrder = 0; s.RowVersion = [];
            db.Items.Add(s);
            n++;
        }
        await db.SaveChangesAsync();
        return n;
    }

    /// <summary>Year End: snapshot on-hand counts so last year's closing stock can be reported.</summary>
    public async Task<int> YearEndAsync(int locationId)
    {
        await using var db = data.Create();
        return await db.Items.Where(i => i.LocationId == locationId && !i.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.OnHandBeforeYearEnd, i => i.OnHand));
    }
}
