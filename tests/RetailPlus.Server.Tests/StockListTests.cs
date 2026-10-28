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
/// Transfers and stock counts are listed a page at a time with a search; the test builds its own stores and SQLite tills,
/// so the totals it checks are its own.
/// </summary>
public sealed class StockListTests(SyncWorld world) : IClassFixture<SyncWorld>
{
    private void Require() => Skip.IfNot(world.Available, world.UnavailableReason);

    [SkippableFact]
    public async Task Transfers_and_counts_are_listed_a_page_at_a_time_and_found_by_number_or_location()
    {
        Require();
        var storeA = await world.AddStoreAsync();
        var storeB = await world.AddStoreAsync();
        var storeC = await world.AddStoreAsync();
        var tillA = await world.NewJoinedTillAsync(storeA.Id);
        var tillB = await world.NewJoinedTillAsync(storeB.Id);
        var atA = await tillA.SessionAsync();
        await tillB.SyncAsync();   // store B's POS location reaches the server, then till A
        await tillA.SyncAsync();

        // Store A: a back room, an item, three transfers to store B (two shipped) and one to the back room.
        var back = await tillA.Setup.AddLocationAsync("BCK", "Back room");
        await tillA.Inventory.SaveAsync(new Item { LocationId = atA.LocationId, Plu = "T-BOX", Name = "Boxes", Price1 = 2m, OnHand = 100 }, null, null, null);
        Guid toB;
        await using (var db = tillA.Data.Create())
            toB = await db.Locations.AsNoTracking().Where(l => l.StoreId == storeB.Id && l.Code == "POS").Select(l => l.Id).SingleAsync();
        var transfers = new TransferService(tillA.Data);
        var made = new List<StockTransfer>();
        for (int i = 0; i < 3; i++)
        {
            var t = await transfers.CreateAsync(atA.LocationId, toB, "To B " + i, "tester");
            await transfers.AddItemAsync(t.Id, "T-BOX", 1 + i);
            if (i < 2) await transfers.ShipAsync(t.Id, "tester");
            made.Add(t);
        }
        var toBack = await transfers.CreateAsync(atA.LocationId, back.Id, "Shelf to back room", "tester");
        await tillA.SyncAsync();

        // Store B: three counts of its shelf.
        var atB = await tillB.SessionAsync();
        var counts = new StockCountService(tillB.Data);
        var countIds = new List<StockCount>();
        for (int i = 0; i < 3; i++) countIds.Add(await counts.StartAsync(atB.LocationId, "Count " + i, "tester"));
        await tillB.SyncAsync();

        using var super = await world.SuperAsync();
        using var adminA = await world.AdminOfAsync(storeA.Id);
        using var adminB = await world.AdminOfAsync(storeB.Id);
        using var adminC = await world.AdminOfAsync(storeC.Id);

        // A page at a time, newest first, with the total.
        var first = await GetAsync<PageOf<TransferListRow>>(adminA, "/api/transfers?pageSize=3");
        Assert.Equal((4, 1, 3, 3), (first.Total, first.Page, first.PageSize, first.Items.Count));
        var second = await GetAsync<PageOf<TransferListRow>>(adminA, "/api/transfers?pageSize=3&page=2");
        Assert.Single(second.Items);
        Assert.Equal(4, first.Items.Concat(second.Items).Select(t => t.Id).Distinct().Count());
        Assert.True(first.Items.Zip(first.Items.Skip(1)).All(p => p.First.CreatedAt >= p.Second.CreatedAt));

        // Found by number, or by a location's code or name; a status still narrows it.
        var byNumber = await GetAsync<PageOf<TransferListRow>>(adminA, $"/api/transfers?search={made[1].Number}");
        Assert.Equal(made[1].Id, Assert.Single(byNumber.Items).Id);
        var byCode = await GetAsync<PageOf<TransferListRow>>(adminA, "/api/transfers?search=BCK");
        Assert.Equal(toBack.Id, Assert.Single(byCode.Items).Id);
        Assert.Equal(toBack.Id, Assert.Single((await GetAsync<PageOf<TransferListRow>>(adminA, "/api/transfers?search=Back%20room")).Items).Id);
        Assert.Equal(3, (await GetAsync<PageOf<TransferListRow>>(adminA, $"/api/transfers?search={Uri.EscapeDataString(storeB.Name)}")).Total);
        Assert.Equal(2, (await GetAsync<PageOf<TransferListRow>>(adminA, $"/api/transfers?search={Uri.EscapeDataString(storeB.Name)}&status=shipped")).Total);
        Assert.Equal(0, (await GetAsync<PageOf<TransferListRow>>(adminA, "/api/transfers?search=nowhere")).Total);

        // Each store sees the transfers it sends or receives: B the three sent to it, C none; nobody another store's list.
        Assert.Equal(3, (await GetAsync<PageOf<TransferListRow>>(adminB, "/api/transfers")).Total);
        Assert.Equal(0, (await GetAsync<PageOf<TransferListRow>>(adminC, "/api/transfers")).Total);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminC.GetAsync($"/api/transfers?storeId={storeA.Id}")));
        Assert.Equal(4, (await GetAsync<PageOf<TransferListRow>>(super, $"/api/transfers?storeId={storeA.Id}")).Total);

        // Counts the same way.
        var countPage = await GetAsync<PageOf<CountListRow>>(adminB, "/api/stock-counts?pageSize=2&page=2");
        Assert.Equal((3, 2, 1), (countPage.Total, countPage.Page, countPage.Items.Count));
        Assert.Equal(countIds[1].Id, Assert.Single((await GetAsync<PageOf<CountListRow>>(adminB, $"/api/stock-counts?search={countIds[1].Number}")).Items).Id);
        Assert.Equal(3, (await GetAsync<PageOf<CountListRow>>(adminB, "/api/stock-counts?search=POS&status=open")).Total);
        Assert.Equal(0, (await GetAsync<PageOf<CountListRow>>(adminA, "/api/stock-counts")).Total);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.GetAsync($"/api/stock-counts?storeId={storeB.Id}")));
        Assert.Equal(3, (await GetAsync<PageOf<CountListRow>>(super, $"/api/stock-counts?storeId={storeB.Id}&pageSize=500")).Total);
    }
}
