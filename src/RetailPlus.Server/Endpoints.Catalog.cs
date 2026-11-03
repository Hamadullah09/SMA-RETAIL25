using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

public sealed record ItemListRow(Guid Id, string Plu, string Name, string? Department, string? Category, decimal Price1, decimal AverageCost, decimal OnHand,
    bool IsActive, string StoreCode, string LocationCode);
public sealed record ItemStock(string StoreCode, string LocationCode, decimal OnHand);
public sealed record ItemDetail(Guid? Id, string? Version, string Plu, string Name, string? Description, Guid? DepartmentId, Guid? CategoryId, Guid? SupplierId,
    decimal Price1, decimal Price2, decimal Price3, decimal LastCost, decimal AverageCost, bool Taxable1, bool Taxable2, bool Taxable3,
    decimal ReorderPoint, decimal ReorderQuantity, decimal UnitsPerCase, bool IsActive, string? Notes, IReadOnlyList<ItemStock>? Stock,
    Guid? LocationId = null, string? Barcode = null);
/// <summary>One location's row of a stock code.</summary>
public sealed record ItemPlace(Guid ItemId, string StoreCode, string LocationCode);
/// <summary>A location an every-store change did not reach, and why.</summary>
public sealed record ItemSkip(string StoreCode, string LocationCode, string Reason);
/// <summary>Where a change at every store would go: the rows of the stock code the user may change, and the locations without it.</summary>
public sealed record ItemEveryStorePlan(IReadOnlyList<ItemPlace> Rows, IReadOnlyList<ItemSkip> Skipped);
/// <summary>
/// What <c>PUT /api/items/{id}?everyStore=true</c> did: the saved item, the fields copied, every row changed (the one
/// edited first) and the locations left alone.
/// </summary>
public sealed record ItemEveryStoreResult(ItemDetail Item, int Changed, IReadOnlyList<string> Fields, IReadOnlyList<ItemPlace> ChangedAt, IReadOnlyList<ItemSkip> Skipped);
public sealed record NamedRow(Guid Id, string Name);
public sealed record CategoryRow(Guid Id, string Name, Guid? DepartmentId);
public sealed record NameRequest(string Name);

public sealed record ClientListRow(Guid Id, string? Number, string Name, string? Company, string? Phone, string? Email, decimal Balance, decimal Points, bool IsDeleted,
    bool WhatsAppOptIn = false);
public sealed record ClientDetail(Guid? Id, string? Version, string? Number, string? FirstName, string? LastName, string? Company, string? Address1, string? Address2,
    string? City, string? Province, string? PostalCode, string? Country, string? Phone, string? Phone2, string? Email, decimal CreditLimit, decimal Balance,
    decimal Points, string? Notes, string? WhatsAppNumber = null, bool? WhatsAppOptIn = null, DateTime? WhatsAppOptInAtUtc = null, DateOnly? Birthday = null);
public sealed record SupplierListRow(Guid Id, string? Code, string? Company, string? Contact, string? Phone, string? Email, bool IsDeleted);
public sealed record SupplierDetail(Guid? Id, string? Version, string? Code, string? Company, string? Contact, string? Address1, string? Address2, string? City,
    string? Province, string? PostalCode, string? Country, string? Phone, string? Fax, string? Email, string? Notes);

/// <summary>
/// Items, clients, suppliers, departments and categories. Items belong to a location of a store (each location has its
/// own row and price for a stock code); the others are shared by every store.
/// </summary>
public static partial class Endpoints
{
    private static void MapCatalog(RouteGroupBuilder api)
    {
        api.MapGet("/items", ItemsAsync).RequireAuthorization(Need(Permissions.InventoryView));
        api.MapGet("/items/{id:guid}", ItemAsync).RequireAuthorization(Need(Permissions.InventoryView));
        api.MapPost("/items", (ItemDetail body, ApiRequest api) => SaveItemAsync(null, body, api)).RequireAuthorization(Need(Permissions.InventoryEdit));
        api.MapPut("/items/{id:guid}", (Guid id, ItemDetail body, ApiRequest api) => SaveItemAsync(id, body, api)).RequireAuthorization(Need(Permissions.InventoryEdit));
        api.MapGet("/items/{id:guid}/every-store", async (Guid id, ApiRequest api) =>
        {
            var (item, _) = await LoadItemAsync(api, id);
            return Results.Ok(await EveryStorePlanAsync(api, item.Plu));
        }).RequireAuthorization(Need(Permissions.InventoryEdit));
        api.MapDelete("/items/{id:guid}", (Guid id, ApiRequest api) => ItemTrashAsync(id, api, delete: true)).RequireAuthorization(Need(Permissions.InventoryEdit));
        api.MapPost("/items/{id:guid}/undelete", (Guid id, ApiRequest api) => ItemTrashAsync(id, api, delete: false)).RequireAuthorization(Need(Permissions.InventoryEdit));

        api.MapGet("/departments", async (ApiRequest api) =>
        {
            await using var db = api.Root.Create();
            return Results.Ok(await db.Departments.AsNoTracking().OrderBy(d => d.Name).Select(d => new NamedRow(d.Id, d.Name)).ToListAsync());
        }).RequireAuthorization(Need(Permissions.InventoryView));
        api.MapPost("/departments", async (NameRequest body, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            await new SetupService(await api.ForChangeAsync()).AddDepartmentAsync(body.Name ?? "");
            await using var db = api.Root.Create();
            var name = (body.Name ?? "").Trim().ToUpperInvariant();
            return Results.Ok(await db.Departments.AsNoTracking().Where(d => d.Name == name).Select(d => new NamedRow(d.Id, d.Name)).SingleAsync());
        }).RequireAuthorization(Need(Permissions.InventoryEdit));
        api.MapDelete("/departments/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            await using var db = api.Root.Create();
            var name = await db.Departments.Where(d => d.Id == id).Select(d => d.Name).SingleOrDefaultAsync() ?? throw ApiException.NotFound("That department");
            await new SetupService(await api.ForChangeAsync()).DeleteDepartmentAsync(name);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.InventoryEdit));

        api.MapGet("/categories", async (ApiRequest api) =>
        {
            await using var db = api.Root.Create();
            var categories = db.Categories.AsNoTracking();
            // Categories are not kept under departments; with a department, the categories its items use.
            if (api.Id("departmentId") is { } department)
                categories = categories.Where(c => db.Items.Any(i => i.DepartmentId == department && i.CategoryId == c.Id));
            return Results.Ok(await categories.OrderBy(c => c.Name).Select(c => new CategoryRow(c.Id, c.Name, null)).ToListAsync());
        }).RequireAuthorization(Need(Permissions.InventoryView));
        api.MapPost("/categories", async (NameRequest body, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            await new SetupService(await api.ForChangeAsync()).AddCategoryAsync(body.Name ?? "");
            await using var db = api.Root.Create();
            var name = (body.Name ?? "").Trim().ToUpperInvariant();
            return Results.Ok(await db.Categories.AsNoTracking().Where(d => d.Name == name).Select(d => new CategoryRow(d.Id, d.Name, null)).SingleAsync());
        }).RequireAuthorization(Need(Permissions.InventoryEdit));
        api.MapDelete("/categories/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            await using var db = api.Root.Create();
            var name = await db.Categories.Where(d => d.Id == id).Select(d => d.Name).SingleOrDefaultAsync() ?? throw ApiException.NotFound("That category");
            await new SetupService(await api.ForChangeAsync()).DeleteCategoryAsync(name);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.InventoryEdit));

        api.MapGet("/clients", ClientsAsync).RequireAuthorization(Need(Permissions.ClientsView));
        api.MapGet("/clients/{id:guid}", async (Guid id, ApiRequest api) =>
            await new PartyService(api.Root).GetClientAsync(id) is { } c ? Results.Ok(ToDetail(c)) : throw ApiException.NotFound("That client"))
            .RequireAuthorization(Need(Permissions.ClientsView));
        api.MapPost("/clients", (ClientDetail body, ApiRequest api) => SaveClientAsync(null, body, api)).RequireAuthorization(Need(Permissions.ClientsEdit));
        api.MapPut("/clients/{id:guid}", (Guid id, ClientDetail body, ApiRequest api) => SaveClientAsync(id, body, api)).RequireAuthorization(Need(Permissions.ClientsEdit));
        api.MapDelete("/clients/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            _ = await new PartyService(api.Root).GetClientAsync(id) ?? throw ApiException.NotFound("That client");
            await new PartyService(await api.ForChangeAsync()).DeleteClientAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.ClientsEdit));
        api.MapPost("/clients/{id:guid}/undelete", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            _ = await new PartyService(api.Root).GetClientAsync(id) ?? throw ApiException.NotFound("That client");
            await new PartyService(await api.ForChangeAsync()).RestoreClientAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.ClientsEdit));

        api.MapGet("/suppliers", SuppliersAsync).RequireAuthorization(Need(Permissions.InventoryView));
        api.MapGet("/suppliers/{id:guid}", async (Guid id, ApiRequest api) =>
            await new PartyService(api.Root).GetSupplierAsync(id) is { } s ? Results.Ok(ToDetail(s)) : throw ApiException.NotFound("That supplier"))
            .RequireAuthorization(Need(Permissions.InventoryView));
        api.MapPost("/suppliers", (SupplierDetail body, ApiRequest api) => SaveSupplierAsync(null, body, api)).RequireAuthorization(Need(Permissions.SuppliersEdit));
        api.MapPut("/suppliers/{id:guid}", (Guid id, SupplierDetail body, ApiRequest api) => SaveSupplierAsync(id, body, api)).RequireAuthorization(Need(Permissions.SuppliersEdit));
        api.MapDelete("/suppliers/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            _ = await new PartyService(api.Root).GetSupplierAsync(id) ?? throw ApiException.NotFound("That supplier");
            await new PartyService(await api.ForChangeAsync()).DeleteSupplierAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.SuppliersEdit));
        api.MapPost("/suppliers/{id:guid}/undelete", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            _ = await new PartyService(api.Root).GetSupplierAsync(id) ?? throw ApiException.NotFound("That supplier");
            await new PartyService(await api.ForChangeAsync()).RestoreSupplierAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.SuppliersEdit));
    }

    // Items -------------------------------------------------------------------------------------------------------------

    private static IQueryable<Item> ItemsOf(RetailPlusDbContext db, Guid? store) =>
        store is { } s ? db.Items.AsNoTracking().Where(i => db.Locations.Any(l => l.Id == i.LocationId && l.StoreId == s)) : db.Items.AsNoTracking();

    private static async Task<IResult> ItemsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var items = ItemsOf(db, store);
        items = api.Bool("deleted") == true ? items.Where(i => i.IsDeleted) : items.Where(i => !i.IsDeleted);
        if (api.Id("departmentId") is { } department) items = items.Where(i => i.DepartmentId == department);
        if (api.Id("categoryId") is { } category) items = items.Where(i => i.CategoryId == category);
        if (api.Text("search") is { } search)
            items = items.Where(i => i.Plu.StartsWith(search) || i.Name.Contains(search) || (i.Barcode != null && i.Barcode.StartsWith(search))
                                     || (i.SupplierCode != null && i.SupplierCode.StartsWith(search)));
        var rows = items.OrderBy(i => i.Plu).ThenBy(i => i.LocationId).Select(i => new
        {
            i.Id, i.Plu, i.Name, Department = i.Department != null ? i.Department.Name : null, Category = i.Category != null ? i.Category.Name : null,
            i.Price1, i.AverageCost, i.OnHand, i.IsDeleted, Location = i.Location!.Code, Store = db.Stores.Where(s => s.Id == i.Location!.StoreId).Select(s => s.Code).FirstOrDefault(),
        });
        return Results.Ok(await api.PageAsync(rows, i => new ItemListRow(i.Id, i.Plu, i.Name, i.Department, i.Category, i.Price1, i.AverageCost, i.OnHand,
            !i.IsDeleted, i.Store ?? "", i.Location)));
    }

    /// <summary>An item and the store of its location; 404 for an item of a store the user does not see.</summary>
    private static async Task<(Item Item, Guid Store)> LoadItemAsync(ApiRequest api, Guid id)
    {
        var item = await new InventoryService(api.Root).GetAsync(id) ?? throw ApiException.NotFound("That item");
        await using var db = api.Root.Create();
        var store = await db.Locations.Where(l => l.Id == item.LocationId).Select(l => l.StoreId).SingleAsync();
        api.RequireStore(store, "That item");
        return (item, store);
    }

    private static async Task<IResult> ItemAsync(Guid id, ApiRequest api)
    {
        var (item, _) = await LoadItemAsync(api, id);
        return Results.Ok(await ToDetailAsync(api, item));
    }

    private static async Task<ItemDetail> ToDetailAsync(ApiRequest api, Item item)
    {
        await using var db = api.Root.Create();
        // The same stock code at every location of the stores the user sees.
        var stock = await db.Items.AsNoTracking().Where(i => i.Plu == item.Plu && !i.IsDeleted)
            .Join(db.Locations, i => i.LocationId, l => l.Id, (i, l) => new { l.StoreId, Location = l.Code, i.OnHand })
            .Join(db.Stores, x => x.StoreId, s => s.Id, (x, s) => new { x.StoreId, Store = s.Code, x.Location, x.OnHand })
            .ToListAsync();
        return new ItemDetail(item.Id, ApiRequest.Version(item.Version), item.Plu, item.Name, item.Notes, item.DepartmentId, item.CategoryId, item.SupplierId,
            item.Price1, item.Price2, item.Price3, item.LastCost, item.AverageCost, item.Taxable1, item.Taxable2, false, item.ReorderPoint, item.ReorderQuantity,
            item.UnitsPerCase, !item.IsDeleted, item.PosMessage,
            [.. stock.Where(s => api.User.CanSee(s.StoreId)).OrderBy(s => s.Store, StringComparer.Ordinal).ThenBy(s => s.Location, StringComparer.Ordinal).Select(s => new ItemStock(s.Store, s.Location, s.OnHand))],
            item.LocationId, item.Barcode);
    }

    private static async Task<IResult> SaveItemAsync(Guid? id, ItemDetail body, ApiRequest api)
    {
        Item item;
        Guid store;
        // A change at every store copies what the user changed on this row: what it held before is kept to tell.
        ItemFields? before = null;
        string plu = "";
        bool everyStore = id is not null && api.Bool("everyStore") == true;
        if (id is { } existing)
        {
            (item, store) = await LoadItemAsync(api, existing);
            before = ItemFields.Of(item);
            plu = item.Plu;
            item.Version = ApiRequest.ParseVersion(body.Version);
        }
        else
        {
            var location = await LocationForNewItemAsync(api, body.LocationId);
            store = location.StoreId;
            item = new Item { LocationId = location.Id, OnHand = 0 };
        }
        await using (var db = api.Root.Create())
        {
            item.Plu = body.Plu ?? "";
            item.Name = body.Name ?? "";
            item.Notes = body.Description;
            item.PosMessage = body.Notes;
            item.Barcode = body.Barcode ?? item.Barcode;
            item.Price1 = body.Price1; item.Price2 = body.Price2; item.Price3 = body.Price3;
            item.LastCost = body.LastCost; item.AverageCost = body.AverageCost;
            item.Taxable1 = body.Taxable1; item.Taxable2 = body.Taxable2;
            item.ReorderPoint = body.ReorderPoint; item.ReorderQuantity = body.ReorderQuantity; item.UnitsPerCase = body.UnitsPerCase;
            string? department = body.DepartmentId is { } d ? await db.Departments.Where(x => x.Id == d).Select(x => x.Name).SingleOrDefaultAsync() ?? throw ApiException.Bad("That department does not exist.") : null;
            string? category = body.CategoryId is { } c ? await db.Categories.Where(x => x.Id == c).Select(x => x.Name).SingleOrDefaultAsync() ?? throw ApiException.Bad("That category does not exist.") : null;
            string? supplier = body.SupplierId is { } s ? await db.Suppliers.Where(x => x.Id == s).Select(x => x.Company).SingleOrDefaultAsync() ?? throw ApiException.Bad("That supplier does not exist.") : null;
            var inventory = new InventoryService(await api.ForChangeAsync(store));
            item = await inventory.SaveAsync(item, department, category, supplier);
            if (body.IsActive == item.IsDeleted)
            {
                if (body.IsActive) await inventory.RestoreAsync(item.Id); else await inventory.DeleteAsync(item.Id);
            }
        }
        var saved = (await new InventoryService(api.Root).GetAsync(item.Id))!;
        if (everyStore) return Results.Ok(await CopyToEveryStoreAsync(api, saved, plu, before!, body.IsActive));
        var detail = await ToDetailAsync(api, saved);
        return id is null ? Results.Created($"/api/items/{saved.Id}", detail) : Results.Ok(detail);
    }

    // Every store -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The fields a change at every store copies to the code's other rows. Supplier, units per case, stock and the stock
    /// code itself stay each location's own.
    /// </summary>
    private sealed record ItemFields(string Name, string? Description, string? Notes, string? Barcode, decimal Price1, decimal Price2, decimal Price3,
        decimal LastCost, decimal AverageCost, Guid? DepartmentId, Guid? CategoryId, bool Taxable1, bool Taxable2, decimal ReorderPoint, decimal ReorderQuantity,
        bool IsActive)
    {
        public static ItemFields Of(Item i) => new(i.Name, i.Notes, i.PosMessage, string.IsNullOrWhiteSpace(i.Barcode) ? null : i.Barcode.Trim(), i.Price1, i.Price2,
            i.Price3, i.LastCost, i.AverageCost, i.DepartmentId, i.CategoryId, i.Taxable1, i.Taxable2, i.ReorderPoint, i.ReorderQuantity, !i.IsDeleted);

        /// <summary>The names (as in <see cref="ItemDetail"/>) of the fields that differ.</summary>
        public static List<string> Changed(ItemFields a, ItemFields b)
        {
            var names = new List<string>();
            if (a.Name != b.Name) names.Add("name");
            if (a.Description != b.Description) names.Add("description");
            if (a.Notes != b.Notes) names.Add("notes");
            if (a.Barcode != b.Barcode) names.Add("barcode");
            if (a.Price1 != b.Price1) names.Add("price1");
            if (a.Price2 != b.Price2) names.Add("price2");
            if (a.Price3 != b.Price3) names.Add("price3");
            if (a.LastCost != b.LastCost) names.Add("lastCost");
            if (a.AverageCost != b.AverageCost) names.Add("averageCost");
            if (a.DepartmentId != b.DepartmentId) names.Add("departmentId");
            if (a.CategoryId != b.CategoryId) names.Add("categoryId");
            if (a.Taxable1 != b.Taxable1) names.Add("taxable1");
            if (a.Taxable2 != b.Taxable2) names.Add("taxable2");
            if (a.ReorderPoint != b.ReorderPoint) names.Add("reorderPoint");
            if (a.ReorderQuantity != b.ReorderQuantity) names.Add("reorderQuantity");
            if (a.IsActive != b.IsActive) names.Add("isActive");
            return names;
        }

        /// <summary>This row's fields once the named ones are taken from <paramref name="source"/>.</summary>
        public ItemFields Take(ItemFields source, List<string> fields) => new(
            fields.Contains("name") ? source.Name : Name,
            fields.Contains("description") ? source.Description : Description,
            fields.Contains("notes") ? source.Notes : Notes,
            fields.Contains("barcode") ? source.Barcode : Barcode,
            fields.Contains("price1") ? source.Price1 : Price1,
            fields.Contains("price2") ? source.Price2 : Price2,
            fields.Contains("price3") ? source.Price3 : Price3,
            fields.Contains("lastCost") ? source.LastCost : LastCost,
            fields.Contains("averageCost") ? source.AverageCost : AverageCost,
            fields.Contains("departmentId") ? source.DepartmentId : DepartmentId,
            fields.Contains("categoryId") ? source.CategoryId : CategoryId,
            fields.Contains("taxable1") ? source.Taxable1 : Taxable1,
            fields.Contains("taxable2") ? source.Taxable2 : Taxable2,
            fields.Contains("reorderPoint") ? source.ReorderPoint : ReorderPoint,
            fields.Contains("reorderQuantity") ? source.ReorderQuantity : ReorderQuantity,
            fields.Contains("isActive") ? source.IsActive : IsActive);

        /// <summary>Writes the fields onto an item (not the department, category or deletion, which the service sets).</summary>
        public void WriteTo(Item i)
        {
            i.Name = Name; i.Notes = Description; i.PosMessage = Notes; i.Barcode = Barcode;
            i.Price1 = Price1; i.Price2 = Price2; i.Price3 = Price3; i.LastCost = LastCost; i.AverageCost = AverageCost;
            i.Taxable1 = Taxable1; i.Taxable2 = Taxable2; i.ReorderPoint = ReorderPoint; i.ReorderQuantity = ReorderQuantity;
        }
    }

    /// <summary>
    /// The rows of a stock code the user may change (a store administrator: their store's locations; a super administrator:
    /// every store's), in store and location order, and the locations of those stores where the code is missing or deleted.
    /// </summary>
    private static async Task<ItemEveryStorePlan> EveryStorePlanAsync(ApiRequest api, string plu)
    {
        await using var db = api.Root.Create();
        var locations = (await db.Locations.AsNoTracking().Join(db.Stores, l => l.StoreId, s => s.Id, (l, s) => new { l.Id, l.StoreId, l.Code, Store = s.Code }).ToListAsync())
            .Where(l => api.User.CanSee(l.StoreId)).OrderBy(l => l.Store, StringComparer.Ordinal).ThenBy(l => l.Code, StringComparer.Ordinal).ToList();
        var rows = await db.Items.AsNoTracking().Where(i => i.Plu == plu).Select(i => new { i.Id, i.LocationId, i.IsDeleted }).ToListAsync();
        var places = new List<ItemPlace>();
        var skipped = new List<ItemSkip>();
        foreach (var l in locations)
        {
            var here = rows.Where(r => r.LocationId == l.Id).ToList();
            if (here.FirstOrDefault(r => !r.IsDeleted) is { } row) places.Add(new ItemPlace(row.Id, l.Store, l.Code));
            else skipped.Add(new ItemSkip(l.Store, l.Code, here.Count > 0 ? "The item is deleted here." : "No item with this stock code here."));
        }
        return new ItemEveryStorePlan(places, skipped);
    }

    /// <summary>
    /// After the row the user edited is saved (its version checked), copies the fields they changed on it to the code's
    /// other rows they may change. Each row is saved as its own store's change, so it is audited under that store and goes
    /// down to that store's tills. Nothing is created; a row that refuses the change (a barcode already used there) is
    /// listed with the reason, and a row that already holds the values is left alone.
    /// </summary>
    private static async Task<ItemEveryStoreResult> CopyToEveryStoreAsync(ApiRequest api, Item saved, string plu, ItemFields before, bool isActive)
    {
        var after = ItemFields.Of(saved) with { IsActive = isActive };
        var fields = ItemFields.Changed(before, after);
        var plan = await EveryStorePlanAsync(api, plu);
        var changedAt = new List<ItemPlace>();
        var skipped = new List<ItemSkip>(plan.Skipped);
        if (plan.Rows.FirstOrDefault(r => r.ItemId == saved.Id) is { } self) changedAt.Add(self);
        else if (await StoreAndLocationAsync(api, saved.LocationId) is { } where) changedAt.Add(new ItemPlace(saved.Id, where.Store, where.Location));

        foreach (var place in plan.Rows.Where(r => r.ItemId != saved.Id))
        {
            if (fields.Count == 0)
            {
                skipped.Add(new ItemSkip(place.StoreCode, place.LocationCode, "Nothing was changed."));
                continue;
            }
            if (await new InventoryService(api.Root).GetAsync(place.ItemId) is not { } row) continue;
            var mine = ItemFields.Of(row);
            var wanted = mine.Take(after, fields);
            if (ItemFields.Changed(mine, wanted).Count == 0)
            {
                skipped.Add(new ItemSkip(place.StoreCode, place.LocationCode, "It is already the same here."));
                continue;
            }
            wanted.WriteTo(row);
            try
            {
                var inventory = new InventoryService(await api.ForChangeAsync(await StoreOfLocationAsync(api, row.LocationId)));
                // Only the row the user edited is checked against the version they read; each other row is saved with its own.
                await inventory.SaveAsync(row, fields.Contains("departmentId") ? saved.Department?.Name : row.Department?.Name,
                    fields.Contains("categoryId") ? saved.Category?.Name : row.Category?.Name, row.Supplier?.Company);
                if (fields.Contains("isActive"))
                {
                    if (isActive) await inventory.RestoreAsync(row.Id); else await inventory.DeleteAsync(row.Id);
                }
                changedAt.Add(place);
            }
            catch (Exception ex) when (ex is BusinessRuleException or ConcurrencyConflictException)
            {
                skipped.Add(new ItemSkip(place.StoreCode, place.LocationCode, ex.Message));
            }
        }
        var detail = await ToDetailAsync(api, (await new InventoryService(api.Root).GetAsync(saved.Id))!);
        return new ItemEveryStoreResult(detail, changedAt.Count, fields, changedAt, skipped);
    }

    private static async Task<Guid> StoreOfLocationAsync(ApiRequest api, Guid locationId)
    {
        await using var db = api.Root.Create();
        return await db.Locations.Where(l => l.Id == locationId).Select(l => l.StoreId).SingleAsync();
    }

    private static async Task<(string Store, string Location)?> StoreAndLocationAsync(ApiRequest api, Guid locationId)
    {
        await using var db = api.Root.Create();
        var found = await db.Locations.AsNoTracking().Where(l => l.Id == locationId).Join(db.Stores, l => l.StoreId, s => s.Id, (l, s) => new { Store = s.Code, l.Code })
            .FirstOrDefaultAsync();
        return found is null ? null : (found.Store, found.Code);
    }

    /// <summary>
    /// Where a new item goes: the location given, else the first location of the store asked for, of the user's store, or of
    /// the only store.
    /// </summary>
    private static async Task<Location> LocationForNewItemAsync(ApiRequest api, Guid? locationId)
    {
        await using var db = api.Root.Create();
        if (locationId is { } given)
        {
            var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == given) ?? throw ApiException.Bad("That location does not exist.");
            if (!api.User.CanSee(location.StoreId)) throw ApiException.Forbidden("You may not add items to that store.");
            return location;
        }
        var store = api.AskedStore ?? api.User.StoreId;
        if (store is null && await db.Stores.CountAsync() == 1) store = await db.Stores.Select(s => s.Id).SingleAsync();
        if (store is null) throw ApiException.Bad("Say which store the item is for (storeId or locationId).");
        if (!api.User.CanSee(store.Value)) throw ApiException.Forbidden("You may not add items to that store.");
        return await db.Locations.AsNoTracking().Where(l => l.StoreId == store).OrderBy(l => l.Code).FirstOrDefaultAsync()
               ?? throw ApiException.Bad("That store has no location yet.");
    }

    private static async Task<IResult> ItemTrashAsync(Guid id, ApiRequest api, bool delete)
    {
        var (item, store) = await LoadItemAsync(api, id);
        var inventory = new InventoryService(await api.ForChangeAsync(store));
        if (delete) await inventory.DeleteAsync(item.Id);
        else await inventory.RestoreAsync(item.Id);
        return Results.NoContent();
    }

    // Clients -----------------------------------------------------------------------------------------------------------

    private static string Name(Party p) => !string.IsNullOrWhiteSpace(p.Company) && string.IsNullOrWhiteSpace(p.LastName)
        ? p.Company! : string.Join(", ", new[] { p.LastName, p.FirstName }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static async Task<IResult> ClientsAsync(ApiRequest api)
    {
        await using var db = api.Root.Create();
        var clients = db.Clients.AsNoTracking();
        clients = api.Bool("deleted") == true ? clients.Where(c => c.IsDeleted) : clients.Where(c => !c.IsDeleted);
        if (api.Text("search") is { } t)
            clients = clients.Where(c => (c.LastName ?? "").StartsWith(t) || (c.FirstName ?? "").StartsWith(t) || (c.Company ?? "").StartsWith(t)
                                         || (c.Number ?? "").StartsWith(t) || (c.AccountNumber ?? "").StartsWith(t) || (c.WorkPhone ?? "").Contains(t)
                                         || (c.HomePhone ?? "").Contains(t) || (c.Email ?? "").StartsWith(t));
        var rows = clients.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ThenBy(c => c.Company);
        return Results.Ok(await api.PageAsync(rows, c => new ClientListRow(c.Id, c.Number, Name(c), c.Company, c.WorkPhone ?? c.HomePhone, c.Email, c.Balance, c.Points, c.IsDeleted,
            c.WhatsAppOptIn)));
    }

    private static ClientDetail ToDetail(Client c) => new(c.Id, ApiRequest.Version(c.Version), c.Number, c.FirstName, c.LastName, c.Company, c.Address1, c.Address2,
        c.City, c.State, c.Zip, c.Country, c.WorkPhone, c.HomePhone, c.Email, c.CreditLimit, c.Balance, c.Points, c.Notes, c.WhatsAppNumber, c.WhatsAppOptIn,
        ApiRequest.Utc(c.WhatsAppOptInAt), c.Birthday);

    private static async Task<IResult> SaveClientAsync(Guid? id, ClientDetail body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        var parties = new PartyService(api.Root);
        Client client;
        if (id is { } existing)
        {
            client = await parties.GetClientAsync(existing) ?? throw ApiException.NotFound("That client");
            client.Version = ApiRequest.ParseVersion(body.Version);
        }
        else client = new Client();
        client.Number = string.IsNullOrWhiteSpace(body.Number) ? client.Number : body.Number.Trim();
        client.FirstName = body.FirstName; client.LastName = body.LastName; client.Company = body.Company;
        client.Address1 = body.Address1; client.Address2 = body.Address2; client.City = body.City; client.State = body.Province; client.Zip = body.PostalCode;
        client.Country = body.Country; client.WorkPhone = body.Phone; client.HomePhone = body.Phone2; client.Email = body.Email;
        client.CreditLimit = body.CreditLimit; client.Notes = body.Notes;
        // Phase E4: a body that carries the consent carries the WhatsApp number and birthday too; one without keeps them.
        if (body.WhatsAppOptIn is { } optIn)
        {
            client.WhatsAppOptIn = optIn;
            client.WhatsAppNumber = body.WhatsAppNumber;
            client.Birthday = body.Birthday;
        }
        var data = await api.ForChangeAsync();
        // A new client without a number gets one from the server's own range.
        if (string.IsNullOrWhiteSpace(client.Number) && data.Station is { } here)
            client.Number = (await NextServerNumberAsync(api, here.StoreId, RetailPlusDbContext.Sequences.CustomerNumbers)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var saved = await new PartyService(data).SaveClientAsync(client);
        var detail = ToDetail((await parties.GetClientAsync(saved.Id))!);
        return id is null ? Results.Created($"/api/clients/{saved.Id}", detail) : Results.Ok(detail);
    }

    /// <summary>The next document number of the server's own range (<see cref="ApiRequest.ServerStation"/>).</summary>
    private static async Task<long> NextServerNumberAsync(ApiRequest api, Guid store, string sequence)
    {
        await using var db = api.Root.For(new StationIdentity(store, ApiRequest.ServerStation), api.Session).Create();
        return await db.NextNumberAsync(sequence);
    }

    // Suppliers ---------------------------------------------------------------------------------------------------------

    private static async Task<IResult> SuppliersAsync(ApiRequest api)
    {
        await using var db = api.Root.Create();
        var suppliers = db.Suppliers.AsNoTracking();
        suppliers = api.Bool("deleted") == true ? suppliers.Where(c => c.IsDeleted) : suppliers.Where(c => !c.IsDeleted);
        if (api.Text("search") is { } t)
            suppliers = suppliers.Where(c => (c.Company ?? "").StartsWith(t) || (c.LastName ?? "").StartsWith(t) || (c.Number ?? "").StartsWith(t) || (c.Email ?? "").StartsWith(t));
        var rows = suppliers.OrderBy(c => c.Company).ThenBy(c => c.LastName);
        return Results.Ok(await api.PageAsync(rows, c => new SupplierListRow(c.Id, c.Number, c.Company, Contact(c), c.WorkPhone, c.Email, c.IsDeleted)));
    }

    private static string? Contact(Party p) => string.Join(" ", new[] { p.FirstName, p.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } c ? c : null;

    private static SupplierDetail ToDetail(Supplier s) => new(s.Id, ApiRequest.Version(s.Version), s.Number, s.Company, Contact(s), s.Address1, s.Address2, s.City,
        s.State, s.Zip, s.Country, s.WorkPhone, s.Fax, s.Email, s.Notes);

    private static async Task<IResult> SaveSupplierAsync(Guid? id, SupplierDetail body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        var parties = new PartyService(api.Root);
        Supplier supplier;
        if (id is { } existing)
        {
            supplier = await parties.GetSupplierAsync(existing) ?? throw ApiException.NotFound("That supplier");
            supplier.Version = ApiRequest.ParseVersion(body.Version);
        }
        else supplier = new Supplier();
        supplier.Number = body.Code; supplier.Company = body.Company;
        // The contact's first word is the first name, the rest the last name.
        var contact = (body.Contact ?? "").Trim();
        int space = contact.IndexOf(' ', StringComparison.Ordinal);
        supplier.FirstName = contact.Length == 0 ? null : space < 0 ? null : contact[..space];
        supplier.LastName = contact.Length == 0 ? null : space < 0 ? contact : contact[(space + 1)..].Trim();
        supplier.Address1 = body.Address1; supplier.Address2 = body.Address2; supplier.City = body.City; supplier.State = body.Province; supplier.Zip = body.PostalCode;
        supplier.Country = body.Country; supplier.WorkPhone = body.Phone; supplier.Fax = body.Fax; supplier.Email = body.Email; supplier.Notes = body.Notes;
        var saved = await new PartyService(await api.ForChangeAsync()).SaveSupplierAsync(supplier);
        var detail = ToDetail((await parties.GetSupplierAsync(saved.Id))!);
        return id is null ? Results.Created($"/api/suppliers/{saved.Id}", detail) : Results.Ok(detail);
    }
}
