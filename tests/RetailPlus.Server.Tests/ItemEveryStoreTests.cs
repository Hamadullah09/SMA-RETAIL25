using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Sync.Tests;
using static RetailPlus.Server.Tests.BackOffice;
using static RetailPlus.Server.Tests.WebClients;

namespace RetailPlus.Server.Tests;

/// <summary>
/// An item changed at every store that stocks its code: copied to the rows the user may change, audited under each store,
/// downloaded by the tills, refused per row where it cannot go.
/// </summary>
public sealed class ItemEveryStoreTests(SyncWorld world) : IClassFixture<SyncWorld>
{
    private void Require() => Skip.IfNot(world.Available, world.UnavailableReason);

    /// <summary>
    /// Store A and store B each stock MUG (B at a different price and with its own second price); store C does not. A till
    /// of each store sent its items.
    /// </summary>
    private async Task<(StoreInfo A, StoreInfo B, StoreInfo C, Till TillA, Till TillB, Item AtA, Item AtB)> MugsAsync()
    {
        var a = await world.AddStoreAsync();
        var b = await world.AddStoreAsync();
        var c = await world.AddStoreAsync();
        var tillA = await world.NewJoinedTillAsync(a.Id);
        var tillB = await world.NewJoinedTillAsync(b.Id);
        string plu = "MUG" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        var atA = await tillA.Inventory.SaveAsync(new Item { LocationId = (await tillA.SessionAsync()).LocationId, Plu = plu, Name = "Mug", Price1 = 5m, Price2 = 4.5m,
            AverageCost = 2m, LastCost = 2m, OnHand = 10 }, "KITCHEN", null, null);
        var atB = await tillB.Inventory.SaveAsync(new Item { LocationId = (await tillB.SessionAsync()).LocationId, Plu = plu, Name = "Mug", Price1 = 6m, Price2 = 5.5m,
            AverageCost = 2.2m, LastCost = 2.2m, OnHand = 7 }, "KITCHEN", null, null);
        await tillA.SyncAsync();
        await tillB.SyncAsync();
        return (a, b, c, tillA, tillB, atA, atB);
    }

    private static async Task<ItemEveryStoreResult> PutEveryStoreAsync(HttpClient client, ItemDetail item)
    {
        using var response = await client.PutAsJsonAsync($"/api/items/{item.Id}?everyStore=true", item);
        return await ReadAsync<ItemEveryStoreResult>(response);
    }

    [SkippableFact]
    public async Task An_item_change_is_copied_to_every_store_that_stocks_the_code_and_reaches_its_tills()
    {
        Require();
        var (a, b, c, _, tillB, atA, atB) = await MugsAsync();
        using var super = await world.SuperAsync();

        // Before: where it would go, in store order.
        var plan = await GetAsync<ItemEveryStorePlan>(super, $"/api/items/{atA.Id}/every-store");
        Assert.Equal(new[] { (atA.Id, a.Code), (atB.Id, b.Code) }.OrderBy(r => r.Code, StringComparer.Ordinal), plan.Rows.Select(r => (r.ItemId, r.StoreCode)));
        Assert.Contains(plan.Skipped, s => s.StoreCode == c.Code && s.LocationCode == "POS" && s.Reason == "No item with this stock code here.");

        // The name and the regular price change at every store; store B keeps its own second price and cost.
        var item = await GetAsync<ItemDetail>(super, $"/api/items/{atA.Id}");
        var result = await PutEveryStoreAsync(super, item with { Name = "Mug, large", Price1 = 7.5m });
        Assert.Equal(["name", "price1"], result.Fields);
        Assert.Equal(2, result.Changed);
        Assert.Equal([a.Code, b.Code], result.ChangedAt.Select(p => p.StoreCode));
        Assert.Equal(atA.Id, result.ChangedAt[0].ItemId);
        Assert.Contains(result.Skipped, s => s.StoreCode == c.Code);
        Assert.Equal(("Mug, large", 7.5m), (result.Item.Name, result.Item.Price1));

        await using (var server = world.ServerData.Create())
        {
            var rowB = await server.Items.AsNoTracking().SingleAsync(i => i.Id == atB.Id);
            Assert.Equal(("Mug, large", 7.5m, 5.5m, 2.2m, 7m), (rowB.Name, rowB.Price1, rowB.Price2, rowB.AverageCost, rowB.OnHand));
            // Each row changed is in the audit log, under its own store, as the web user at station 0.
            var lines = await server.AuditEntries.AsNoTracking().Where(e => e.Action == AuditActions.Changed && (e.EntityId == atA.Id || e.EntityId == atB.Id)).ToListAsync();
            Assert.Contains(lines, e => e.EntityId == atA.Id && e.StoreId == a.Id && e.StationNumber == 0 && e.UserName == SyncWorld.AdminUser);
            Assert.Contains(lines, e => e.EntityId == atB.Id && e.StoreId == b.Id && e.StationNumber == 0 && e.UserName == SyncWorld.AdminUser);
        }

        // Store B's till gets it with its next download, its own stock untouched.
        Assert.Equal("Mug", (await tillB.Inventory.GetAsync(atB.Id))!.Name);
        await tillB.SyncAsync();
        var atTill = (await tillB.Inventory.GetAsync(atB.Id))!;
        Assert.Equal(("Mug, large", 7.5m, 5.5m, 7m), (atTill.Name, atTill.Price1, atTill.Price2, atTill.OnHand));

        // Saving with nothing changed leaves the other rows alone.
        var again = await GetAsync<ItemDetail>(super, $"/api/items/{atA.Id}");
        var nothing = await PutEveryStoreAsync(super, again);
        Assert.Empty(nothing.Fields);
        Assert.Equal(1, nothing.Changed);
        Assert.Contains(nothing.Skipped, s => s.StoreCode == b.Code && s.Reason == "Nothing was changed.");
    }

    [SkippableFact]
    public async Task A_store_administrator_changes_only_their_store_and_the_version_is_checked_on_the_row_edited()
    {
        Require();
        var (a, b, _, _, _, atA, atB) = await MugsAsync();
        using var adminA = await world.AdminOfAsync(a.Id);

        var plan = await GetAsync<ItemEveryStorePlan>(adminA, $"/api/items/{atA.Id}/every-store");
        Assert.Equal(a.Code, Assert.Single(plan.Rows).StoreCode);
        Assert.All(plan.Skipped, s => Assert.Equal(a.Code, s.StoreCode));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminA.GetAsync($"/api/items/{atB.Id}/every-store")));

        var item = await GetAsync<ItemDetail>(adminA, $"/api/items/{atA.Id}");
        var result = await PutEveryStoreAsync(adminA, item with { Price1 = 9m });
        Assert.Equal(1, result.Changed);
        Assert.All(result.ChangedAt, p => Assert.Equal(a.Code, p.StoreCode));
        Assert.All(result.Skipped, s => Assert.Equal(a.Code, s.StoreCode));
        await using (var server = world.ServerData.Create())
            Assert.Equal(6m, (await server.Items.AsNoTracking().SingleAsync(i => i.Id == atB.Id)).Price1);

        // An old version of the row edited is refused, and nothing changes anywhere.
        using var super = await world.SuperAsync();
        using (var stale = await super.PutAsJsonAsync($"/api/items/{atA.Id}?everyStore=true", item with { Price1 = 1m }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await using (var server = world.ServerData.Create())
        {
            var prices = await server.Items.AsNoTracking().Where(i => i.Id == atA.Id || i.Id == atB.Id).ToDictionaryAsync(i => i.Id, i => i.Price1);
            Assert.Equal((9m, 6m), (prices[atA.Id], prices[atB.Id]));
        }

        // Without the edit permission there is neither the plan nor the change.
        var setup = new SetupService(world.ServerData);
        var viewRole = await setup.SaveRoleAsync(new Role { Name = "Stock look " + Guid.NewGuid().ToString("N")[..4], Level = 1 }, [Permissions.WebSignIn, Permissions.InventoryView]);
        var viewer = "view-" + Guid.NewGuid().ToString("N")[..6];
        await setup.SaveUserAsync(new UserAccount { UserName = viewer, RoleId = viewRole.Id, StoreId = a.Id, IsActive = true }, Password);
        using var looker = await world.SignInAsync(viewer, Password);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(looker.GetAsync($"/api/items/{atA.Id}/every-store")));
    }

    [SkippableFact]
    public async Task A_row_that_refuses_the_change_is_listed_with_the_reason_and_deactivating_reaches_every_store()
    {
        Require();
        var (a, b, _, _, tillB, atA, atB) = await MugsAsync();
        // At store B another item already carries the barcode the mug is about to get.
        await tillB.Inventory.SaveAsync(new Item { LocationId = (await tillB.SessionAsync()).LocationId, Plu = "CUP" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            Name = "Cup", Price1 = 3m, Barcode = "036000291452" }, null, null, null);
        await tillB.SyncAsync();
        using var super = await world.SuperAsync();

        var item = await GetAsync<ItemDetail>(super, $"/api/items/{atA.Id}");
        var result = await PutEveryStoreAsync(super, item with { Barcode = "036000291452", ReorderPoint = 3m });
        Assert.Equal(["barcode", "reorderPoint"], result.Fields);
        Assert.Equal(a.Code, Assert.Single(result.ChangedAt).StoreCode);
        var refused = Assert.Single(result.Skipped, s => s.StoreCode == b.Code);
        Assert.Contains("036000291452", refused.Reason, StringComparison.Ordinal);

        // Unticking Active deletes the item at every store that stocks it.
        var current = await GetAsync<ItemDetail>(super, $"/api/items/{atA.Id}");
        var deleted = await PutEveryStoreAsync(super, current with { Barcode = null, IsActive = false });
        Assert.Contains("isActive", deleted.Fields);
        Assert.Equal([a.Code, b.Code], deleted.ChangedAt.Select(p => p.StoreCode));
        await using var server = world.ServerData.Create();
        Assert.True(await server.Items.AsNoTracking().Where(i => i.Id == atA.Id || i.Id == atB.Id).AllAsync(i => i.IsDeleted));
    }
}
