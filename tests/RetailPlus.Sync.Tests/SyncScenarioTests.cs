using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Sync.Tests;

/// <summary>
/// Two tills and one server, end to end: the tills sell offline on their own SQLite files and synchronise over HTTP
/// with the server on SQL Server.
/// </summary>
public sealed class SyncScenarioTests(SyncWorld world) : IClassFixture<SyncWorld>
{
    private void Require() => Skip.IfNot(world.Available, world.UnavailableReason);

    private static SaleDraft Draft(Item item, decimal quantity, Guid? clientId = null) => new()
    {
        ClientId = clientId,
        Lines =
        [
            new SaleDraftLine
            {
                ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = quantity, UnitPrice = item.Price1,
                UnitCost = item.AverageCost,
            },
        ],
    };

    private static TenderEntry Cash(decimal amount) => new("Cash", TenderKind.Cash, amount, amount);

    private static async Task<Item> ItemAsync(Till till, string plu, decimal onHand, decimal price = 10m)
    {
        var session = await till.SessionAsync();
        return await till.Inventory.SaveAsync(new Item { LocationId = session.LocationId, Plu = plu, Name = "Item " + plu, Price1 = price, OnHand = onHand }, null, null, null);
    }

    /// <summary>An item's count and the sum of its movements in one database.</summary>
    private static async Task<(decimal OnHand, decimal Movements)> StockAsync(DataContext data, Guid itemId)
    {
        await using var db = data.Create();
        var onHand = await db.Items.Where(i => i.Id == itemId).Select(i => i.OnHand).SingleAsync();
        var movements = (await db.StockMovements.Where(m => m.ItemId == itemId).Select(m => m.Quantity).ToListAsync()).Sum();
        return (onHand, movements);
    }

    [SkippableFact]
    public async Task Sales_on_two_tills_reach_the_server_and_stock_is_the_sum_of_the_movements_everywhere()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        var b = await world.NewJoinedTillAsync(store.Id);
        var item = await ItemAsync(a, "SYNC1", 20);
        await a.SyncAsync();
        await b.SyncAsync();
        var atB = (await b.Inventory.GetAsync(item.Id))!;
        Assert.Equal(20m, atB.OnHand);
        Assert.Equal((await a.SessionAsync()).LocationId, (await b.SessionAsync()).LocationId);

        // Both tills sell before they hear of each other's sale.
        await a.Sales.PostSaleAsync(Draft(item, 3), [Cash(30m)], await a.SessionAsync());
        await b.Sales.PostSaleAsync(Draft(atB, 5), [Cash(50m)], await b.SessionAsync());
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        await using (var server = world.ServerData.Create())
        {
            var sales = await server.Sales.Where(s => s.StoreId == store.Id).Include(s => s.Lines).ToListAsync();
            Assert.Equal(2, sales.Count);
            Assert.Equal([a.Data.Station!.StationNumber, b.Data.Station!.StationNumber], sales.Select(s => s.StationNumber).Order());
            Assert.All(sales, s => Assert.Single(s.Lines));
        }
        Assert.Equal((12m, 12m), await StockAsync(world.ServerData, item.Id));
        Assert.Equal((12m, 12m), await StockAsync(a.Data, item.Id));
        Assert.Equal((12m, 12m), await StockAsync(b.Data, item.Id));
        await using var tillA = a.Data.Create();
        Assert.Equal(2, await tillA.Sales.CountAsync());
    }

    [SkippableFact]
    public async Task Sending_a_package_again_changes_nothing()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        await ItemAsync(a, "TWICE", 4);
        var file = (await a.Sync.CreatePackageAsync())!;
        var copy = Path.Combine(world.Folder, Path.GetFileName(file));
        File.Copy(file, copy);
        await a.SyncAsync();

        int items;
        await using (var server = world.ServerData.Create()) items = await server.Items.CountAsync();
        var client = new SyncClient(world.Server.CreateClient());
        var again = await client.UploadAsync(a.Data.Station!.StationNumber, StationKey.Parse(a.Settings.StationKey!), copy);
        Assert.Equal(nameof(SyncBatchStatus.Duplicate), again.Status);

        await using var check = world.ServerData.Create();
        Assert.Equal(items, await check.Items.CountAsync());
        Guid batch;
        await using (var stream = File.OpenRead(copy))
        using (var package = PackageReader.Open(stream))
            batch = package.Manifest.BatchId;
        Assert.Equal([SyncBatchStatus.Applied, SyncBatchStatus.Duplicate],
            (await check.SyncBatches.Where(b => b.BatchId == batch).ToListAsync()).Select(b => b.Status).Order());
    }

    [SkippableFact]
    public async Task A_changed_package_or_another_key_is_refused_and_recorded()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        await ItemAsync(a, "TAMPER", 4, price: 10m);
        var file = (await a.Sync.CreatePackageAsync())!;
        int station = a.Data.Station!.StationNumber;
        var key = StationKey.Parse(a.Settings.StationKey!);

        // The price changed in the file after the till signed it.
        var tampered = Path.Combine(world.Folder, "tampered" + SyncPackage.Extension);
        File.Copy(file, tampered);
        using (var zip = ZipFile.Open(tampered, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("tables/Item.json")!;
            string json;
            using (var reader = new StreamReader(entry.Open())) json = reader.ReadToEnd();
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("tables/Item.json").Open());
            writer.Write(json.Replace("\"Price1\":10", "\"Price1\":1", StringComparison.Ordinal));
        }
        var client = new SyncClient(world.Server.CreateClient());
        var refused = await Assert.ThrowsAsync<SyncRefusedException>(() => client.UploadAsync(station, key, tampered));
        Assert.Contains("changed", refused.Message, StringComparison.OrdinalIgnoreCase);

        // Signed with a key that is not this till's.
        await Assert.ThrowsAsync<SyncAuthException>(() => client.UploadAsync(station, StationKey.Parse(StationKey.New()), file));

        await using var server = world.ServerData.Create();
        var failed = await server.SyncBatches.Where(b => b.StationNumber == station && b.Status == SyncBatchStatus.Failed).ToListAsync();
        Assert.Equal(2, failed.Count);
        Assert.Contains(failed, b => b.Error!.Contains("changed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failed, b => b.Error!.Contains("key", StringComparison.OrdinalIgnoreCase));
        Assert.False(await server.Items.AnyAsync(i => i.Plu == "TAMPER"));
    }

    [SkippableFact]
    public async Task A_price_changed_on_the_server_reaches_the_tills_even_past_a_sale_made_before_it_arrived()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        var b = await world.NewJoinedTillAsync(store.Id);
        var item = await ItemAsync(a, "PRICE", 30, price: 10m);
        await a.SyncAsync();
        await b.SyncAsync();

        await using (var server = world.ServerData.Create())
        {
            var row = await server.Items.SingleAsync(i => i.Id == item.Id);
            row.Price1 = 25m;
            await server.SaveChangesAsync();
        }
        // Till A sells at the old price before it hears of the new one: its stock change must not undo the price.
        await Task.Delay(20);
        await a.Sales.PostSaleAsync(Draft(item, 1), [Cash(10m)], await a.SessionAsync());
        await a.SyncAsync();
        await b.SyncAsync();

        await using (var server = world.ServerData.Create()) Assert.Equal(25m, (await server.Items.SingleAsync(i => i.Id == item.Id)).Price1);
        Assert.Equal(25m, (await a.Inventory.GetAsync(item.Id))!.Price1);
        Assert.Equal(25m, (await b.Inventory.GetAsync(item.Id))!.Price1);
        Assert.Equal(29m, (await b.Inventory.GetAsync(item.Id))!.OnHand);
    }

    [SkippableFact]
    public async Task A_deletion_travels_as_a_tombstone()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        var b = await world.NewJoinedTillAsync(store.Id);
        await a.Setup.AddDepartmentAsync("TOYS");
        await a.SyncAsync();
        await b.SyncAsync();
        Assert.Contains("TOYS", await b.Setup.GetDepartmentsAsync());

        await a.Setup.DeleteDepartmentAsync("TOYS");
        await a.SyncAsync();
        // B's next package still carries its copy of the row (packages overlap); it must not bring the row back.
        await b.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        Assert.DoesNotContain("TOYS", await b.Setup.GetDepartmentsAsync());
        Assert.DoesNotContain("TOYS", await a.Setup.GetDepartmentsAsync());
        await using var server = world.ServerData.Create();
        Assert.False(await server.Departments.AnyAsync(d => d.Name == "TOYS"));
        Assert.True(await server.Tombstones.AnyAsync(t => t.EntityName == nameof(Department)));
    }

    [SkippableFact]
    public async Task The_later_edit_of_a_client_wins_whichever_till_sends_first()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        var b = await world.NewJoinedTillAsync(store.Id);
        var client = await a.Parties.SaveClientAsync(new Client { LastName = "Edited", WorkPhone = "100" });
        await a.SyncAsync();
        await b.SyncAsync();

        var atA = (await a.Parties.GetClientAsync(client.Id))!;
        atA.WorkPhone = "111";
        await a.Parties.SaveClientAsync(atA);
        await Task.Delay(20);
        var atB = (await b.Parties.GetClientAsync(client.Id))!;
        atB.WorkPhone = "222";
        await b.Parties.SaveClientAsync(atB);

        // The later edit (B) reaches the server first; the earlier one (A) arrives after it and must not win.
        await b.SyncAsync();
        await a.SyncAsync();
        await b.SyncAsync();

        await using (var server = world.ServerData.Create()) Assert.Equal("222", (await server.Clients.SingleAsync(c => c.Id == client.Id)).WorkPhone);
        Assert.Equal("222", (await a.Parties.GetClientAsync(client.Id))!.WorkPhone);
        Assert.Equal("222", (await b.Parties.GetClientAsync(client.Id))!.WorkPhone);
    }

    [SkippableFact]
    public async Task Joining_moves_a_tills_existing_data_to_the_servers_store_and_station()
    {
        Require();
        var store = await world.AddStoreAsync();
        var till = await world.NewTillAsync();
        var seeded = till.Data.Station!;
        var item = await ItemAsync(till, "OLD1", 5);
        await till.Sales.PostSaleAsync(Draft(item, 1), [Cash(10m)], await till.SessionAsync());
        await till.Setup.AddDepartmentAsync("LOCAL");

        await world.JoinAsync(till, store.Id);
        int number = till.Data.Station!.StationNumber;
        Assert.Equal(store.Id, till.Data.Station.StoreId);

        await using (var db = till.Data.Create())
        {
            // The till knows every store of the company now (transfers and gift cards point at them), but not its old one.
            var stores = await db.Stores.Select(s => s.Id).ToListAsync();
            Assert.Contains(store.Id, stores);
            Assert.DoesNotContain(seeded.StoreId, stores);
            var pos = await db.Locations.SingleAsync(l => l.StoreId == store.Id);
            await using (var server = world.ServerData.Create())
                Assert.Equal(await server.Locations.Where(l => l.StoreId == store.Id).Select(l => l.Id).SingleAsync(), pos.Id);
            Assert.Equal(pos.Id, (await db.Items.SingleAsync(i => i.Id == item.Id)).LocationId);
            var sale = await db.Sales.SingleAsync();
            Assert.Equal((store.Id, number), (sale.StoreId, sale.StationNumber));
            Assert.Equal(store.Id, (await db.StoreSettings.SingleAsync()).StoreId);
            var station = await db.Stations.SingleAsync(s => s.Number == number);
            Assert.False(await db.Stations.AnyAsync(s => s.Number == seeded.StationNumber && s.Number != number));
            Assert.Equal(store.Id, station.StoreId);
            var roles = await db.Roles.Select(r => r.Name).ToListAsync();
            Assert.Equal(roles.Count, roles.Distinct().Count());
            Assert.Equal(Permissions.DefaultRoles.Count, roles.Count);
            Assert.Equal(SetupService.DefaultTenders.Length, await db.TenderTypes.CountAsync());
            Assert.False(await db.NumberCounters.AnyAsync(c => c.StationNumber == seeded.StationNumber && seeded.StationNumber != number));
        }

        await till.SyncAsync();
        await using var check = world.ServerData.Create();
        var onServer = await check.Items.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(4m, onServer.OnHand);
        Assert.Equal(number, (await check.Sales.SingleAsync(s => s.StoreId == store.Id)).StationNumber);
        Assert.True(await check.Departments.AnyAsync(d => d.Name == "LOCAL"));
        // A new sale is numbered in the new station's range.
        var sold = await till.Sales.PostSaleAsync(Draft(item, 1), [Cash(10m)], await till.SessionAsync());
        Assert.InRange(sold.InvoiceNumber, NumberCounter.FirstNumber(number), NumberCounter.FirstNumber(number + 1) - 1);
    }

    [SkippableFact]
    public async Task The_same_stock_code_made_on_two_tills_becomes_one_item_with_both_stocks()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        var b = await world.NewJoinedTillAsync(store.Id);
        var fromA = await ItemAsync(a, "DUP", 3);
        var fromB = await ItemAsync(b, "DUP", 4);
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        await using (var server = world.ServerData.Create())
        {
            var items = await server.Items.Where(i => i.Plu == "DUP" && server.Locations.Any(l => l.Id == i.LocationId && l.StoreId == store.Id)).ToListAsync();
            Assert.Equal(fromA.Id, Assert.Single(items).Id);
            var notes = await server.SyncBatches.Where(n => n.StationNumber == b.Data.Station!.StationNumber && n.Notes != null).Select(n => n.Notes!).ToListAsync();
            Assert.Contains(notes, n => n.Contains("merged", StringComparison.Ordinal) && n.Contains("DUP", StringComparison.Ordinal));
        }
        Assert.Equal((7m, 7m), await StockAsync(world.ServerData, fromA.Id));
        Assert.Equal((7m, 7m), await StockAsync(a.Data, fromA.Id));
        Assert.Equal((7m, 7m), await StockAsync(b.Data, fromA.Id));
        await using var tillB = b.Data.Create();
        Assert.False(await tillB.Items.AnyAsync(i => i.Id == fromB.Id));
    }

    [SkippableFact]
    public async Task Points_earned_at_one_till_reach_the_other_and_the_server()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        var b = await world.NewJoinedTillAsync(store.Id);
        await using (var server = world.ServerData.Create())
        {
            var settings = await server.StoreSettings.SingleAsync(s => s.StoreId == store.Id);
            settings.BonusPointsEnabled = true;
            settings.PointsPerDollar = 1;
            await server.SaveChangesAsync();
        }
        var item = await ItemAsync(a, "PTS", 10);
        var client = await a.Parties.SaveClientAsync(new Client { LastName = "Loyal" });
        await a.SyncAsync();
        await b.SyncAsync();
        await a.Sales.PostSaleAsync(Draft(item, 2, client.Id), [Cash(20m)], await a.SessionAsync());
        await b.Sales.PostSaleAsync(Draft((await b.Inventory.GetAsync(item.Id))!, 1, client.Id), [Cash(10m)], await b.SessionAsync());
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        decimal expected = (await a.Parties.GetClientAsync(client.Id))!.Points;
        Assert.True(expected > 0);
        Assert.Equal(expected, (await b.Parties.GetClientAsync(client.Id))!.Points);
        await using var check = world.ServerData.Create();
        Assert.Equal(expected, (await check.Clients.SingleAsync(c => c.Id == client.Id)).Points);
        var earned = await check.Sales.Where(s => s.ClientId == client.Id).Select(s => s.PointsEarned).ToListAsync();
        Assert.Equal(earned.Sum(), expected);
        // Every change of points is a loyalty transaction; the points are their sum, in every database.
        var moves = await check.LoyaltyTransactions.Where(t => t.ClientId == client.Id).ToListAsync();
        Assert.Equal(expected, moves.Sum(t => t.Points));
        Assert.Equal([a.Data.Station!.StationNumber, b.Data.Station!.StationNumber], moves.Select(t => t.StationNumber).Distinct().Order());
        await using var tillB = b.Data.Create();
        Assert.Equal(moves.Count, await tillB.LoyaltyTransactions.CountAsync(t => t.ClientId == client.Id));
    }

    [SkippableFact]
    public async Task A_transfer_shipped_at_one_store_is_received_at_another_through_the_server()
    {
        Require();
        var storeA = await world.AddStoreAsync();
        var storeB = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(storeA.Id);
        var b = await world.NewJoinedTillAsync(storeB.Id);
        var item = await ItemAsync(a, "TRF1", 10);
        await a.SyncAsync();
        await b.SyncAsync();

        var transfers = new TransferService(a.Data);
        var transfer = await transfers.CreateAsync((await a.SessionAsync()).LocationId, (await b.SessionAsync()).LocationId, "To store B", "tester");
        await transfers.AddItemAsync(transfer.Id, "TRF1", 4);
        await transfers.ShipAsync(transfer.Id, "tester");
        await a.SyncAsync();
        await b.SyncAsync();

        // The receiving store's till has the shipped transfer, though it belongs to the sending store.
        var atB = (await new TransferService(b.Data).GetAsync(transfer.Id))!;
        Assert.Equal(StockTransferStatus.Shipped, atB.Status);
        var receipt = await new TransferService(b.Data).ReceiveAsync(transfer.Id, null, "tester");
        Assert.Single(receipt.CreatedItems);
        await b.SyncAsync();
        await a.SyncAsync();

        Assert.Equal(StockTransferStatus.Received, (await new TransferService(a.Data).GetAsync(transfer.Id))!.Status);
        Guid arrived;
        await using (var server = world.ServerData.Create())
        {
            Assert.Equal(StockTransferStatus.Received, (await server.StockTransfers.SingleAsync(t => t.Id == transfer.Id)).Status);
            arrived = await server.Items.Where(i => i.Plu == "TRF1" && i.LocationId == atB.ToLocationId).Select(i => i.Id).SingleAsync();
        }
        Assert.Equal((6m, 6m), await StockAsync(world.ServerData, item.Id));
        Assert.Equal((4m, 4m), await StockAsync(world.ServerData, arrived));
        Assert.Equal((6m, 6m), await StockAsync(a.Data, item.Id));
        Assert.Equal((4m, 4m), await StockAsync(b.Data, arrived));
        // Each store's tills have their own store's items only.
        await using var tillA = a.Data.Create();
        Assert.False(await tillA.Items.AnyAsync(i => i.Id == arrived));
    }

    [SkippableFact]
    public async Task A_gift_card_sold_at_one_store_is_spent_at_another()
    {
        Require();
        var storeA = await world.AddStoreAsync();
        var storeB = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(storeA.Id);
        var b = await world.NewJoinedTillAsync(storeB.Id);
        var cardItem = (await a.Inventory.SaveAsync(new Item
        {
            LocationId = (await a.SessionAsync()).LocationId, Plu = "GIFT", Name = "Gift card", ItemType = ItemType.GiftCard,
        }, null, null, null));
        var goods = await ItemAsync(b, "GOODS", 5, price: 30m);
        string number = "GC" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var line = new SaleDraftLine
        {
            ItemId = cardItem.Id, ItemType = ItemType.GiftCard, Plu = cardItem.Plu, Description = cardItem.Name, Quantity = 1, UnitPrice = 100m,
            GiftCardNumber = number, GiftCardKind = GiftCardKind.Card,
        };
        await a.Sales.PostSaleAsync(new SaleDraft { Lines = [line] }, [Cash(100m)], await a.SessionAsync());
        await a.SyncAsync();
        await b.SyncAsync();

        Assert.Equal(100m, (await new GiftCardService(b.Data).FindAsync(number))!.Balance);
        await b.Sales.PostSaleAsync(Draft(goods, 1), [new TenderEntry("Gift Card", TenderKind.GiftCard, 30m, 30m, Reference: number)], await b.SessionAsync());
        await b.SyncAsync();
        await a.SyncAsync();

        Assert.Equal(70m, (await new GiftCardService(a.Data).FindAsync(number))!.Balance);
        Assert.Equal(70m, (await new GiftCardService(b.Data).FindAsync(number))!.Balance);
        await using var server = world.ServerData.Create();
        var card = await server.GiftCards.SingleAsync(c => c.Number == number);
        Assert.Equal(storeA.Id, card.StoreId);
        var moves = await server.GiftCardTransactions.Where(t => t.GiftCardId == card.Id).ToListAsync();
        Assert.Equal(70m, moves.Sum(t => t.Amount));
        Assert.Equal([storeA.Id, storeB.Id], moves.OrderBy(t => t.Amount < 0).Select(t => t.StoreId));
    }

    [SkippableFact]
    public async Task Audit_lines_reach_the_server_with_their_user_time_and_station()
    {
        Require();
        var store = await world.AddStoreAsync();
        var other = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        var b = await world.NewJoinedTillAsync(store.Id);
        var c = await world.NewJoinedTillAsync(other.Id);
        a.Data.CurrentUser = new SessionInfo { UserId = Guid.CreateVersion7(), UserName = "auditor", StationNumber = a.Data.Station!.StationNumber };
        var item = await ItemAsync(a, "AUD1", 3);
        await a.Inventory.DeleteAsync(item.Id);
        List<AuditEntry> local;
        await using (var db = a.Data.Create()) local = await db.AuditEntries.AsNoTracking().Where(e => e.EntityId == item.Id).ToListAsync();
        Assert.Equal(2, local.Count);
        await a.SyncAsync();
        await b.SyncAsync();
        await c.SyncAsync();

        await using (var server = world.ServerData.Create())
        {
            var arrived = await server.AuditEntries.AsNoTracking().Where(e => e.EntityId == item.Id).ToListAsync();
            Assert.Equal(local.Select(e => (e.Id, e.Action, e.UserName, e.UserId, e.AtUtc, e.StationNumber, e.StoreId)).Order(),
                arrived.Select(e => (e.Id, e.Action, e.UserName, e.UserId, e.AtUtc, e.StationNumber, e.StoreId)).Order());
            // Applying the package wrote no audit lines of its own.
            Assert.False(await server.AuditEntries.AnyAsync(e => e.EntityId == item.Id && e.StationNumber != a.Data.Station.StationNumber));
        }
        await using (var tillB = b.Data.Create()) Assert.Equal(2, await tillB.AuditEntries.CountAsync(e => e.EntityId == item.Id));
        await using (var tillC = c.Data.Create()) Assert.False(await tillC.AuditEntries.AnyAsync(e => e.EntityId == item.Id));
    }

    [SkippableFact]
    public async Task A_package_on_a_stick_carries_the_database_and_a_till_can_load_the_servers_package()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        var b = await world.NewJoinedTillAsync(store.Id);
        var item = await ItemAsync(a, "USB1", 6);
        var stick = Path.Combine(world.Folder, "stick-" + Guid.NewGuid().ToString("N")[..6]);
        var file = await a.Sync.SavePackageToFileAsync(stick, includeDatabase: true);
        await using (var stream = File.OpenRead(file))
        using (var package = PackageReader.Open(stream))
        {
            package.Verify(StationKey.Parse(a.Settings.StationKey!));
            Assert.NotNull(package.Manifest.Database);
            Assert.Contains(package.Manifest.Tables, t => t.Entity == nameof(Item));
        }

        // The administrator uploads it on the web, then takes the server's package for till B to it on the stick.
        using var admin = await world.SignInAsync();
        using (var upload = new ByteArrayContent(await File.ReadAllBytesAsync(file)))
        {
            using var response = await admin.PostAsync("/api/sync/upload-file", upload);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        var forB = Path.Combine(stick, "for-b" + SyncPackage.Extension);
        await File.WriteAllBytesAsync(forB, await admin.GetByteArrayAsync($"/api/sync/download-file?station={b.Data.Station!.StationNumber}"));
        var loaded = await b.Sync.LoadPackageFromFileAsync(forB);
        Assert.Equal(SyncBatchStatus.Applied, loaded.Status);
        Assert.Equal(6m, (await b.Inventory.GetAsync(item.Id))!.OnHand);
    }
}
