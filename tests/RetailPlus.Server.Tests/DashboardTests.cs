using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Sync.Tests;
using static RetailPlus.Server.Tests.BackOffice;

namespace RetailPlus.Server.Tests;

/// <summary>
/// The dashboard's top items, with a world of its own (so its "all stores" holds only these sales): every store has its
/// own item row of a product, and across stores the product is one entry.
/// </summary>
public sealed class DashboardTests(SyncWorld world) : IClassFixture<SyncWorld>
{
    [SkippableFact]
    public async Task Top_items_of_every_store_show_a_product_once_with_its_sales_added_up_and_one_store_its_own()
    {
        Skip.IfNot(world.Available, world.UnavailableReason);
        var a = await world.AddStoreAsync();
        var b = await world.AddStoreAsync();
        var tillA = await world.NewJoinedTillAsync(a.Id);
        var tillB = await world.NewJoinedTillAsync(b.Id);
        UserAccount cashier;
        await using (var db = world.ServerData.Create())
            cashier = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.UserName == SyncWorld.AdminUser);

        // The same product at both stores (a row each), and one only store A sells.
        string rice = "RICE" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), tea = "TEA" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        Item Product(Guid location, string plu, string name, decimal price) =>
            new() { LocationId = location, Plu = plu, Name = name, Price1 = price, AverageCost = price / 2, LastCost = price / 2, OnHand = 50 };
        var locationA = (await tillA.SessionAsync()).LocationId;
        var riceA = await tillA.Inventory.SaveAsync(Product(locationA, rice, "Basmati rice 5 kg", 20m), "GROCERY", null, null);
        var teaA = await tillA.Inventory.SaveAsync(Product(locationA, tea, "Green tea", 7m), "GROCERY", null, null);
        var riceB = await tillB.Inventory.SaveAsync(Product((await tillB.SessionAsync()).LocationId, rice, "Basmati rice 5 kg", 20m), "GROCERY", null, null);
        Assert.NotEqual(riceA.Id, riceB.Id);
        await SellAsync(tillA, cashier, riceA, 3);
        await SellAsync(tillA, cashier, teaA, 1);
        await SellAsync(tillB, cashier, riceB, 2);
        await tillA.SyncAsync();
        await tillB.SyncAsync();

        using var super = await world.SuperAsync();
        var today = DateOnly.FromDateTime(DateTime.Now);
        string period = $"from={today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

        var all = await GetAsync<Dashboard>(super, $"/api/dashboard?{period}");
        var riceEverywhere = Assert.Single(all.TopItems, i => i.Plu == rice);
        Assert.Equal((5m, 100m, "Basmati rice 5 kg", (Guid?)null), (riceEverywhere.Units, riceEverywhere.NetSales, riceEverywhere.Name, riceEverywhere.ItemId));
        var teaEverywhere = Assert.Single(all.TopItems, i => i.Plu == tea);
        Assert.Equal((1m, 7m, (Guid?)teaA.Id), (teaEverywhere.Units, teaEverywhere.NetSales, teaEverywhere.ItemId));
        Assert.Equal([rice, tea], all.TopItems.Select(i => i.Plu));

        // One store: its own row, as before.
        var atA = await GetAsync<Dashboard>(super, $"/api/dashboard?{period}&storeId={a.Id}");
        var riceAtA = Assert.Single(atA.TopItems, i => i.Plu == rice);
        Assert.Equal((3m, 60m, (Guid?)riceA.Id), (riceAtA.Units, riceAtA.NetSales, riceAtA.ItemId));
        var atB = await GetAsync<Dashboard>(super, $"/api/dashboard?{period}&storeId={b.Id}");
        var riceAtB = Assert.Single(atB.TopItems);
        Assert.Equal((2m, 40m, (Guid?)riceB.Id), (riceAtB.Units, riceAtB.NetSales, riceAtB.ItemId));
    }
}
