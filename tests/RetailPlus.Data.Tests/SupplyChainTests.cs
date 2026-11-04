using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>
/// Supply chain (phase E2) against a real database: batches and expiry through receiving, the POS (first expiry first,
/// expired stock refused, voids), counts, transfers and adjustments; bins and their lists; requisitions and their approval;
/// the three-way match with its ledger postings; landed costs; reorder suggestions; and the supplier performance report.
/// </summary>
public abstract class SupplyChainTests(DatabaseFixture db)
{
    private static int _station = 700;

    private DataContext Data => db.Data;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private async Task<(SessionInfo Session, Location Location)> SetUpAsync(bool blockExpired = true)
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var location = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Supply");
        await using (var ctx = db.CreateContext())
        {
            var settings = await ctx.StoreSettings.SingleAsync();
            settings.Tax1Rate = 0; settings.Tax2Rate = 0; settings.MinimumTender = 0.01m;
            await ctx.SaveChangesAsync();
        }
        var supply = await new SupplySettingsService(Data).GetAsync(db.Station!.StoreId);
        supply.BlockExpiredSales = blockExpired;
        await new SupplySettingsService(Data).SaveAsync(db.Station.StoreId, supply);
        return (new SessionInfo { StationNumber = Interlocked.Increment(ref _station), LocationId = location.Id, LocationCode = location.Code, UserName = "tester", AccessLevel = 4 },
            location);
    }

    private async Task<Item> ItemAsync(Guid locationId, string plu, decimal price = 10m, decimal onHand = 0, decimal cost = 4m, bool batches = false, int shelfLife = 0,
        string? bin = null, Guid? supplierId = null, decimal reorderPoint = 0, decimal pack = 0, decimal weight = 0, string? weightUnit = null)
    {
        var item = new Item
        {
            LocationId = locationId, Plu = plu, Name = "Item " + plu, Price1 = price, LastCost = cost, AverageCost = cost, OnHand = onHand, TracksBatches = batches,
            ShelfLifeDays = shelfLife, BinLocation = bin, SupplierId = supplierId, ReorderPoint = reorderPoint, PackQuantity = (int)pack, Weight = weight, WeightUnit = weightUnit,
        };
        var saved = await new InventoryService(Data).SaveAsync(item, "SUPPLY", null, null);
        if (supplierId is Guid id)
            await using (var ctx = db.CreateContext())
            {
                var row = await ctx.Items.SingleAsync(i => i.Id == saved.Id);
                row.SupplierId = id;
                await ctx.SaveChangesAsync();
                saved.SupplierId = id;
            }
        return saved;
    }

    private async Task<Supplier> SupplierAsync(string name, int leadTime = 0) =>
        await new PartyService(Data).SaveSupplierAsync(new Supplier { Company = name + " " + Guid.NewGuid().ToString("N")[..6], LeadTimeDays = leadTime });

    private static SaleDraftLine Line(Item item, decimal qty, Guid? batch = null) => new()
    {
        ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = qty, UnitPrice = item.Price1, UnitCost = item.AverageCost,
        BatchId = batch, Kind = qty < 0 ? SaleLineKind.Return : SaleLineKind.Sale,
    };

    private async Task<Sale> SellAsync(SessionInfo session, params SaleDraftLine[] lines)
    {
        var draft = new SaleDraft { Lines = [.. lines] };
        decimal total = draft.Calculate(draft.EffectiveTax(new StoreSettings())).Total;
        return await new SalesService(Data).PostSaleAsync(draft, [new TenderEntry("Cash", TenderKind.Cash, total, total)], session);
    }

    /// <summary>A posted order of the items (pieces each) at their cost, received as given.</summary>
    private async Task<PurchaseOrder> ReceivedOrderAsync(Guid locationId, Guid? supplierId, IReadOnlyList<(Item Item, decimal Pieces, decimal Cost, ReceivedBatch[]? Batches)> lines,
        decimal? receivedFactor = null, DateOnly? expected = null)
    {
        var purchasing = new PurchasingService(Data);
        var po = await purchasing.CreateFromItemsAsync(locationId, supplierId, "Test order", [.. lines.Select(l => new OrderItem(l.Item.Id, l.Pieces, l.Cost))], expected);
        await purchasing.PostOrderAsync(po.Id);
        po = (await purchasing.GetAsync(po.Id))!;
        var received = po.Lines.Select(l =>
        {
            var wanted = lines.Single(x => x.Item.Id == l.ItemId);
            decimal pieces = Math.Round(wanted.Pieces * (receivedFactor ?? 1), 3);
            return new ReceivedLine(l.Id, pieces, wanted.Cost, wanted.Batches);
        }).ToList();
        await purchasing.PostShipmentAsync(po.Id, received);
        return (await purchasing.GetAsync(po.Id))!;
    }

    private async Task<Dictionary<string, decimal>> BatchStockAsync(Guid itemId) =>
        (await new BatchService(Data).OfItemAsync(itemId)).ToDictionary(b => b.BatchNumber, b => b.OnHand);

    private async Task<decimal> OnHandAsync(Guid itemId) => (await new InventoryService(Data).GetAsync(itemId))!.OnHand;

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // Batches and expiry -------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Receiving_asks_for_batches_and_keeps_stock_per_batch_from_the_movements()
    {
        var (_, location) = await SetUpAsync();
        var milk = await ItemAsync(location.Id, Unique("MLK"), batches: true, shelfLife: 10);
        var purchasing = new PurchasingService(Data);
        var po = await purchasing.CreateFromItemsAsync(location.Id, null, "Milk", [new OrderItem(milk.Id, 12)]);
        await purchasing.PostOrderAsync(po.Id);
        var line = (await purchasing.GetAsync(po.Id))!.Lines.Single();

        // An item kept in batches is not received without them, nor with batches that do not add up.
        await Assert.ThrowsAsync<BusinessRuleException>(() => purchasing.PostShipmentAsync(po.Id, [new ReceivedLine(line.Id, 12, 4m)]));
        await Assert.ThrowsAsync<BusinessRuleException>(() => purchasing.PostShipmentAsync(po.Id, [new ReceivedLine(line.Id, 12, 4m, [new ReceivedBatch("L1", 5)])]));

        var made = Today.AddDays(-2);
        await purchasing.PostShipmentAsync(po.Id, [new ReceivedLine(line.Id, 12, 4m, [new ReceivedBatch("l1", 5, Today.AddDays(20)), new ReceivedBatch("L2", 7, null, made)])]);
        var batches = await new BatchService(Data).OfItemAsync(milk.Id);
        Assert.Equal(["L2", "L1"], batches.Select(b => b.BatchNumber));   // first expiry first: L2 from its shelf life
        Assert.Equal((made.AddDays(10), 7m), (batches[0].ExpiresOn, batches[0].OnHand));
        Assert.Equal((Today.AddDays(20), 5m, po.Number), (batches[1].ExpiresOn, batches[1].OnHand, batches[1].PurchaseOrderNumber));
        Assert.Equal(12m, await OnHandAsync(milk.Id));
        var moves = await new InventoryService(Data).GetStockMovementsAsync(milk.Id);
        Assert.All(moves.Where(m => m.Kind == StockMovementKind.Receipt), m => Assert.NotNull(m.BatchId));
        Assert.Equal(12m, moves.Sum(m => m.Quantity));
        var ordered = (await purchasing.GetAsync(po.Id))!.Lines.Single();
        Assert.Equal(4m, ordered.OrderedCost);
    }

    [SkippableFact]
    public async Task The_pos_sells_the_batch_that_expires_first_splits_lines_and_a_void_puts_the_batches_back()
    {
        var (session, location) = await SetUpAsync();
        var yogurt = await ItemAsync(location.Id, Unique("YOG"), batches: true);
        await ReceivedOrderAsync(location.Id, null, [(yogurt, 10, 3m, [new ReceivedBatch("LATE", 6, Today.AddDays(30)), new ReceivedBatch("SOON", 4, Today.AddDays(3))])]);

        var sale = await SellAsync(session, Line(yogurt, 6));
        Assert.Equal(new Dictionary<string, decimal> { ["SOON"] = 0, ["LATE"] = 4 }, await BatchStockAsync(yogurt.Id));
        var soldMoves = (await new InventoryService(Data).GetStockMovementsAsync(yogurt.Id)).Where(m => m.Kind == StockMovementKind.Sale).ToList();
        Assert.Equal(2, soldMoves.Count);
        Assert.Equal(-6m, soldMoves.Sum(m => m.Quantity));

        // A batch chosen by hand is taken as it is.
        var late = (await new BatchService(Data).OfItemAsync(yogurt.Id)).Single(b => b.BatchNumber == "LATE");
        await SellAsync(session, Line(yogurt, 1, late.Id));
        Assert.Equal(3m, (await BatchStockAsync(yogurt.Id))["LATE"]);

        // Voiding the first sale puts back what it took of each batch.
        await new SalesService(Data).VoidSaleAsync(sale.Id, session);
        Assert.Equal(new Dictionary<string, decimal> { ["SOON"] = 4, ["LATE"] = 5 }, await BatchStockAsync(yogurt.Id));
        Assert.Equal(9m, await OnHandAsync(yogurt.Id));

        // A return without a batch goes back into no batch.
        await SellAsync(session, Line(yogurt, -1));
        Assert.Equal(10m, await OnHandAsync(yogurt.Id));
        Assert.Equal(9m, (await BatchStockAsync(yogurt.Id)).Values.Sum());
    }

    [SkippableFact]
    public async Task Expired_stock_is_refused_at_the_pos_when_the_store_says_so_and_reported_with_its_value()
    {
        var (session, location) = await SetUpAsync(blockExpired: true);
        var bread = await ItemAsync(location.Id, Unique("BRD"), cost: 2m, batches: true);
        await ReceivedOrderAsync(location.Id, null, [(bread, 5, 2m, [new ReceivedBatch("OLD", 3, Today.AddDays(-1)), new ReceivedBatch("NEW", 2, Today.AddDays(5))])]);

        var sellable = await new BatchService(Data).SellableAsync(bread.Id);
        Assert.Equal((5m, 2m, 3m, true), (sellable.OnHand, sellable.Sellable, sellable.Expired, sellable.ExpiredBlocked));
        await SellAsync(session, Line(bread, 2));   // the fresh batch, past the expired one
        Assert.Equal(new Dictionary<string, decimal> { ["OLD"] = 3, ["NEW"] = 0 }, await BatchStockAsync(bread.Id));
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => SellAsync(session, Line(bread, 1)));
        Assert.Contains("expired", refused.Message, StringComparison.OrdinalIgnoreCase);
        var old = (await new BatchService(Data).OfItemAsync(bread.Id)).Single(b => b.BatchNumber == "OLD");
        await Assert.ThrowsAsync<BusinessRuleException>(() => SellAsync(session, Line(bread, 1, old.Id)));
        Assert.Equal(3m, await OnHandAsync(bread.Id));

        var expired = await new BatchService(Data).ExpiredAsync(location.Id);
        var row = Assert.Single(expired);
        Assert.Equal(("OLD", 3m, 6m, -1), (row.BatchNumber, row.OnHand, row.Value, row.DaysLeft));
        Assert.Empty(await new BatchService(Data).ExpiringAsync(30, location.Id));   // NEW is empty
        Assert.Single(await new BatchService(Data).ListAsync(location.Id));

        // With the setting off, the expired batch sells first like any other.
        var supply = await new SupplySettingsService(Data).GetAsync(db.Station!.StoreId);
        supply.BlockExpiredSales = false;
        await new SupplySettingsService(Data).SaveAsync(db.Station.StoreId, supply);
        await SellAsync(session, Line(bread, 1));
        Assert.Equal(2m, (await BatchStockAsync(bread.Id))["OLD"]);

        // Thrown away: adjusted out of the batch.
        Assert.Equal(0m, await new BatchService(Data).AdjustAsync(old.Id, 0, "Thrown away", setTo: true));
        Assert.Equal(0m, await OnHandAsync(bread.Id));
        Assert.Empty(await new BatchService(Data).ExpiredAsync(location.Id));
    }

    [SkippableFact]
    public async Task The_pos_learns_before_payment_which_batch_a_sale_takes_and_what_has_expired()
    {
        var (_, location) = await SetUpAsync(blockExpired: true);
        var milk = await ItemAsync(location.Id, Unique("MLK"), cost: 2m, batches: true, shelfLife: 7);
        var salt = await ItemAsync(location.Id, Unique("SLT"), onHand: 4);
        await ReceivedOrderAsync(location.Id, null, [(milk, 6, 2m, [new ReceivedBatch("OLD", 2, Today.AddDays(-2)), new ReceivedBatch("NEW", 4, Today.AddDays(6))])]);
        var batches = new BatchService(Data);

        // Receiving: only the items kept in batches are asked about, with their shelf life.
        Assert.Equal([new BatchItem(milk.Id, 7)], await batches.KeptInBatchesAsync([milk.Id, salt.Id, milk.Id]));
        Assert.Null(await batches.PickForSaleAsync(salt.Id, 1));

        // Expired stock is stopped: the sale takes the good batch, and no more than it holds.
        var pick = (await batches.PickForSaleAsync(milk.Id, 3))!;
        Assert.Equal([("NEW", 3m)], pick.Takes.Select(t => (t.BatchNumber!, t.Quantity)));
        Assert.Equal((2m, true, 4m, "OLD"), (pick.Expired, pick.ExpiredBlocked, pick.Sellable, pick.ExpiredBatches[0].BatchNumber));
        Assert.Equal(Today.AddDays(6), pick.ExpiryOf(pick.Takes[0].BatchId));

        // With the setting off, the expired batch leaves first, as the sale will take it.
        var supply = await new SupplySettingsService(Data).GetAsync(db.Station!.StoreId);
        supply.BlockExpiredSales = false;
        await new SupplySettingsService(Data).SaveAsync(db.Station.StoreId, supply);
        pick = (await batches.PickForSaleAsync(milk.Id, 3))!;
        Assert.Equal([("OLD", 2m), ("NEW", 1m)], pick.Takes.Select(t => (t.BatchNumber!, t.Quantity)));
        Assert.Equal((false, 6m), (pick.ExpiredBlocked, pick.Sellable));
    }

    [SkippableFact]
    public async Task Expiring_lists_batches_within_the_days_asked()
    {
        var (_, location) = await SetUpAsync();
        var cheese = await ItemAsync(location.Id, Unique("CHS"), batches: true);
        await ReceivedOrderAsync(location.Id, null, [(cheese, 9, 5m,
            [new ReceivedBatch("A", 3, Today.AddDays(2)), new ReceivedBatch("B", 3, Today.AddDays(12)), new ReceivedBatch("C", 3, Today.AddDays(40))])]);
        var batches = new BatchService(Data);
        Assert.Equal(["A"], (await batches.ExpiringAsync(7, location.Id)).Select(b => b.BatchNumber));
        Assert.Equal(["A", "B"], (await batches.ExpiringAsync(30, location.Id)).Select(b => b.BatchNumber));
        Assert.Equal(3, (await batches.ListAsync(location.Id, search: cheese.Plu)).Count);
        Assert.Equal(2, (await batches.ExpiringAsync(30, store: db.Station!.StoreId)).Count(b => b.ItemId == cheese.Id));
    }

    [SkippableFact]
    public async Task Counts_work_per_batch_and_an_item_counted_whole_loses_its_oldest_batches_first()
    {
        var (_, location) = await SetUpAsync();
        var juice = await ItemAsync(location.Id, Unique("JUC"), batches: true);
        var water = await ItemAsync(location.Id, Unique("WTR"), batches: true);
        await ReceivedOrderAsync(location.Id, null, [
            (juice, 10, 2m, [new ReceivedBatch("J1", 4, Today.AddDays(5)), new ReceivedBatch("J2", 6, Today.AddDays(50))]),
            (water, 8, 1m, [new ReceivedBatch("W1", 5, Today.AddDays(5)), new ReceivedBatch("W2", 3, Today.AddDays(50))]),
        ]);
        var j1 = (await new BatchService(Data).OfItemAsync(juice.Id)).Single(b => b.BatchNumber == "J1");
        var counts = new StockCountService(Data);
        var count = await counts.StartAsync(location.Id, "Batches", "tester");
        var line = await counts.CountItemAsync(count.Id, juice.Id, null, 3, batchId: j1.Id);
        Assert.Equal((4m, -1m, "J1"), (line.Expected, line.Variance, line.BatchNumber));
        // Juice is counted by batch here, so not as a whole too.
        await Assert.ThrowsAsync<BusinessRuleException>(() => counts.CountCodeAsync(count.Id, juice.Plu, 1));
        await counts.CountCodeAsync(count.Id, water.Plu, 6);   // water as a whole: 2 short
        var posted = await counts.PostAsync(count.Id, zeroUncounted: true, "tester");
        Assert.True(posted.SetToZero >= 1);

        // J1 counted 3; J2 not counted on a batch count, so set to zero. Water lost 2 from W1, which expires first.
        Assert.Equal(new Dictionary<string, decimal> { ["J1"] = 3, ["J2"] = 0 }, await BatchStockAsync(juice.Id));
        Assert.Equal(3m, await OnHandAsync(juice.Id));
        Assert.Equal(new Dictionary<string, decimal> { ["W1"] = 3, ["W2"] = 3 }, await BatchStockAsync(water.Id));
        Assert.Equal(6m, await OnHandAsync(water.Id));
    }

    [SkippableFact]
    public async Task Transfers_send_the_batches_that_expire_first_and_the_receiver_gets_the_same_batches()
    {
        var (_, location) = await SetUpAsync();
        var back = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Back");
        var soup = await ItemAsync(location.Id, Unique("SUP"), batches: true);
        await ReceivedOrderAsync(location.Id, null, [(soup, 10, 2m, [new ReceivedBatch("S1", 4, Today.AddDays(5)), new ReceivedBatch("S2", 6, Today.AddDays(60))])]);
        var transfers = new TransferService(Data);
        var transfer = await transfers.CreateAsync(location.Id, back.Id, null, "tester");
        await transfers.AddItemAsync(transfer.Id, soup.Plu, 7);
        await transfers.ShipAsync(transfer.Id, "tester");
        var shipped = (await transfers.GetAsync(transfer.Id))!;
        Assert.Equal([("S1", 4m), ("S2", 3m)], shipped.Lines.OrderBy(l => l.LineNumber).Select(l => (l.BatchNumber!, l.QuantityShipped)));
        Assert.Equal(new Dictionary<string, decimal> { ["S1"] = 0, ["S2"] = 3 }, await BatchStockAsync(soup.Id));

        await transfers.ReceiveAsync(transfer.Id, null, "tester");
        var there = (await new InventoryService(Data).SearchAsync(back.Id, soup.Plu)).Single();
        var arrived = await new BatchService(Data).OfItemAsync(there.Id);
        Assert.Equal([("S1", 4m, Today.AddDays(5)), ("S2", 3m, Today.AddDays(60))], arrived.Select(b => (b.BatchNumber, b.OnHand, b.ExpiresOn!.Value)));

        // One batch picked by hand stays one line.
        var s2 = (await new BatchService(Data).OfItemAsync(soup.Id)).Single(b => b.BatchNumber == "S2");
        var second = await transfers.CreateAsync(location.Id, back.Id, null, "tester");
        await transfers.AddItemAsync(second.Id, soup.Plu, 2, s2.Id);
        await transfers.ShipAsync(second.Id, "tester");
        Assert.Equal(1m, (await BatchStockAsync(soup.Id))["S2"]);
    }

    [SkippableFact]
    public async Task Adjusting_an_item_kept_in_batches_takes_from_the_oldest_batch()
    {
        var (_, location) = await SetUpAsync();
        var eggs = await ItemAsync(location.Id, Unique("EGG"), batches: true);
        await ReceivedOrderAsync(location.Id, null, [(eggs, 6, 1m, [new ReceivedBatch("E1", 2, Today.AddDays(3)), new ReceivedBatch("E2", 4, Today.AddDays(9))])]);
        await new InventoryService(Data).AdjustStockAsync(eggs.Id, -3, "Broken", "tester");
        Assert.Equal(new Dictionary<string, decimal> { ["E1"] = 0, ["E2"] = 3 }, await BatchStockAsync(eggs.Id));
        var e2 = (await new BatchService(Data).OfItemAsync(eggs.Id)).Single(b => b.BatchNumber == "E2");
        await new InventoryService(Data).AdjustStockAsync(eggs.Id, 2, "Found", "tester", batchId: e2.Id);
        Assert.Equal(5m, (await BatchStockAsync(eggs.Id))["E2"]);
        Assert.Equal(5m, await OnHandAsync(eggs.Id));

        // Batches can be added and changed by hand; a batch number of one item is unique.
        var batches = new BatchService(Data);
        var extra = await batches.SaveAsync(new ItemBatch { ItemId = eggs.Id, BatchNumber = "e3", ExpiresOn = Today.AddDays(20) });
        Assert.Equal("E3", extra.BatchNumber);
        await Assert.ThrowsAsync<BusinessRuleException>(() => batches.SaveAsync(new ItemBatch { ItemId = eggs.Id, BatchNumber = "E3" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => batches.SaveAsync(new ItemBatch { ItemId = eggs.Id, BatchNumber = "E4", MadeOn = Today, ExpiresOn = Today.AddDays(-1) }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => batches.DeleteAsync(e2.Id));
        await batches.DeleteAsync(extra.Id);
        Assert.Equal(2, (await batches.OfItemAsync(eggs.Id)).Count);
    }

    // Bins ---------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Bins_sort_the_put_away_pick_and_count_lists()
    {
        var (_, location) = await SetUpAsync();
        var bins = new BinService(Data);
        var a = await bins.SaveAsync(new Bin { LocationId = location.Id, Code = "a-01", Zone = "Aisle 1" });
        var b = await bins.SaveAsync(new Bin { LocationId = location.Id, Code = "B-02", Zone = "Aisle 2" });
        await Assert.ThrowsAsync<BusinessRuleException>(() => bins.SaveAsync(new Bin { LocationId = location.Id, Code = "A-01" }));
        Assert.Equal("A-01", a.Code);

        var tins = await ItemAsync(location.Id, Unique("TIN"), bin: "B-02");
        var jars = await ItemAsync(location.Id, Unique("JAR"), bin: "A-01");
        var loose = await ItemAsync(location.Id, Unique("LSE"));
        var rice = await ItemAsync(location.Id, Unique("RCE"), batches: true);
        await bins.AssignAsync([rice.Id], "a-01");
        await Assert.ThrowsAsync<BusinessRuleException>(() => bins.AssignAsync([loose.Id], "Z-99"));
        var po = await ReceivedOrderAsync(location.Id, null, [(tins, 5, 1m, null), (jars, 3, 1m, null), (loose, 2, 1m, null),
            (rice, 4, 1m, [new ReceivedBatch("R1", 1, Today.AddDays(9)), new ReceivedBatch("R2", 3, Today.AddDays(90))])]);

        var putAway = await bins.PutAwayAsync(po.Id);
        Assert.Equal(["A-01", "A-01", "A-01", "B-02", null], putAway.Select(r => r.Bin));
        Assert.Equal("Aisle 1", putAway[0].Zone);
        Assert.Equal(["R1", "R2"], putAway.Where(r => r.Plu == rice.Plu).Select(r => r.BatchNumber));
        Assert.Equal(loose.Plu, putAway[^1].Plu);

        // Customer orders are picked bin by bin, with the batch to take first.
        var sales = new SalesService(Data);
        await sales.SaveCustomerOrderAsync(new CustomerOrder { LocationId = location.Id, ItemId = tins.Id, Plu = tins.Plu, Description = tins.Name, Quantity = 2, Name = "Ayesha" });
        await sales.SaveCustomerOrderAsync(new CustomerOrder { LocationId = location.Id, ItemId = rice.Id, Plu = rice.Plu, Description = rice.Name, Quantity = 2, Name = "Bilal" });
        var picks = await bins.PickCustomerOrdersAsync(location.Id);
        Assert.Equal([(rice.Plu, "R1", 1m), (rice.Plu, "R2", 1m), (tins.Plu, (string?)null, 2m)], picks.Select(p => (p.Plu, p.BatchNumber, p.Quantity)));

        var back = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Back");
        var transfers = new TransferService(Data);
        var transfer = await transfers.CreateAsync(location.Id, back.Id, null, "tester");
        await transfers.AddItemAsync(transfer.Id, tins.Plu, 1);
        await transfers.AddItemAsync(transfer.Id, jars.Plu, 1);
        Assert.Equal([jars.Plu, tins.Plu], (await bins.PickTransferAsync(transfer.Id)).Select(p => p.Plu));

        // A count of one zone lists, and sets to zero, only what is kept there.
        Assert.Equal(new[] { jars.Plu, rice.Plu }.Order(StringComparer.OrdinalIgnoreCase), (await bins.CountSheetAsync(location.Id, zone: "Aisle 1")).Select(r => r.Plu));
        var counts = new StockCountService(Data);
        var count = await counts.StartAsync(location.Id, null, "tester", zone: "Aisle 1");
        await counts.CountCodeAsync(count.Id, jars.Plu, 3);
        await counts.PostAsync(count.Id, zeroUncounted: true, "tester");
        Assert.Equal(0m, await OnHandAsync(rice.Id));
        Assert.Equal(5m, await OnHandAsync(tins.Id));
        Assert.Equal(2m, await OnHandAsync(loose.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => counts.StartAsync(location.Id, null, "tester", binCode: "NOPE"));

        // Renaming a bin moves its items; a bin in use is not deleted.
        a.Code = "A-1";
        await bins.SaveAsync(a);
        Assert.Equal("A-1", (await new InventoryService(Data).GetAsync(jars.Id))!.BinLocation);
        await Assert.ThrowsAsync<BusinessRuleException>(() => bins.DeleteAsync(a.Id));
        Assert.Equal(2, (await bins.ListAsync(location.Id)).Single(x => x.Code == "A-1").Items);
        await bins.AssignAsync([tins.Id], null);
        await bins.DeleteAsync(b.Id);
        Assert.Single(await bins.ListAsync(location.Id));
    }

    // Requisitions -----------------------------------------------------------------------------------------------------

    private async Task<DataContext> AsUserAsync(string role, decimal? limit = null)
    {
        var setup = new SetupService(Data);
        var r = (await setup.GetRolesAsync()).Single(x => x.Name == role);
        if (limit is not null) await new SupplySettingsService(Data).SetLimitAsync(r.Id, limit);
        var user = await setup.SaveUserAsync(new UserAccount { UserName = Unique("u"), RoleId = r.Id, StoreId = db.Station!.StoreId, IsActive = true }, "Password-1");
        var session = new SessionInfo { UserId = user.Id, UserName = user.UserName, Permissions = r.Permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal) };
        return Data.For(db.Station, session);
    }

    [SkippableFact]
    public async Task Requisitions_go_from_draft_to_approved_within_the_limit_and_become_one_order_per_supplier()
    {
        var (_, location) = await SetUpAsync();
        var north = await SupplierAsync("North");
        var south = await SupplierAsync("South");
        var flour = await ItemAsync(location.Id, Unique("FLR"), cost: 5m, supplierId: north.Id);
        var sugar = await ItemAsync(location.Id, Unique("SGR"), cost: 3m, supplierId: north.Id);
        var salt = await ItemAsync(location.Id, Unique("SLT"), cost: 1m, supplierId: south.Id);

        var clerk = new RequisitionService(await AsUserAsync(Permissions.ClerkRole));
        var requisition = await clerk.CreateAsync(location.Id, Today.AddDays(7), "Weekly", [new RequisitionLineInput(flour.Id, null, 10)]);
        Assert.Equal(RequisitionStatus.Draft, requisition.Status);
        requisition = await clerk.SaveAsync(requisition.Id, requisition.Version, Today.AddDays(7), "Weekly",
            [new RequisitionLineInput(flour.Id, null, 10), new RequisitionLineInput(null, sugar.Plu.ToLowerInvariant(), 20), new RequisitionLineInput(salt.Id, null, 5, Today.AddDays(3))]);
        Assert.Equal(115m, RequisitionService.Total(requisition));   // 10×5 + 20×3 + 5×1
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => clerk.SubmitAsync(requisition.Id, Guid.NewGuid()));
        requisition = await clerk.SubmitAsync(requisition.Id, requisition.Version);
        await Assert.ThrowsAsync<BusinessRuleException>(() => clerk.ApproveAsync(requisition.Id));   // a clerk may not approve

        // A manager limited to 100 may not approve 115; one limited to 500 may.
        var small = new RequisitionService(await AsUserAsync(Permissions.SupervisorRole));
        await Assert.ThrowsAsync<BusinessRuleException>(() => small.ApproveAsync(requisition.Id));   // no purchasing.approve
        var limited = await AsUserAsync(Permissions.ManagerRole, limit: 100m);
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => new RequisitionService(limited).ApproveAsync(requisition.Id));
        Assert.Contains("100.00", refused.Message, StringComparison.Ordinal);
        await new SupplySettingsService(Data).SetLimitAsync((await new SetupService(Data).GetRolesAsync()).Single(r => r.Name == Permissions.ManagerRole).Id, 500m);
        var approved = await new RequisitionService(limited).ApproveAsync(requisition.Id, null, "Fine");
        Assert.Equal((RequisitionStatus.Approved, "Fine"), (approved.Status, approved.DecisionNote));
        Assert.NotNull(approved.DecidedBy);

        var made = await new RequisitionService(limited).OrderAsync(requisition.Id);
        Assert.Equal(2, made.Orders.Count);
        var purchasing = new PurchasingService(Data);
        var orders = new List<PurchaseOrder>();
        foreach (var (id, _, _) in made.Orders) orders.Add((await purchasing.GetAsync(id))!);
        var toNorth = orders.Single(o => o.SupplierId == north.Id);
        Assert.Equal(2, toNorth.Lines.Count);
        Assert.All(toNorth.Lines, l => Assert.Equal(north.Id, l.SupplierId));
        Assert.Equal(Today.AddDays(3), orders.Single(o => o.SupplierId == south.Id).ExpectedOn);
        var ordered = (await clerk.GetAsync(requisition.Id))!;
        Assert.Equal(RequisitionStatus.Ordered, ordered.Status);
        Assert.All(ordered.Lines, l => Assert.NotNull(l.PurchaseOrderLineId));
        await Assert.ThrowsAsync<BusinessRuleException>(() => clerk.DeleteAsync(requisition.Id));
        Assert.Contains((await clerk.ListAsync(db.Station!.StoreId, location.Id)), r => r.Id == requisition.Id && r.Status == RequisitionStatus.Ordered && r.Total == 115m);
    }

    [SkippableFact]
    public async Task A_rejected_requisition_can_be_changed_and_sent_again_and_the_store_may_ask_for_a_second_person()
    {
        var (_, location) = await SetUpAsync();
        var oil = await ItemAsync(location.Id, Unique("OIL"), cost: 8m);
        var manager = new RequisitionService(await AsUserAsync(Permissions.ManagerRole));
        var requisition = await manager.CreateAsync(location.Id, null, null, [new RequisitionLineInput(oil.Id, null, 4)]);
        await manager.SubmitAsync(requisition.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => manager.RejectAsync(requisition.Id, " "));
        var rejected = await manager.RejectAsync(requisition.Id, "Too many");
        Assert.Equal((RequisitionStatus.Rejected, "Too many"), (rejected.Status, rejected.DecisionNote));
        var again = await manager.SaveAsync(requisition.Id, rejected.Version, null, null, [new RequisitionLineInput(oil.Id, null, 2)]);
        Assert.Equal(RequisitionStatus.Draft, again.Status);
        await manager.SubmitAsync(requisition.Id);

        var store = db.Station!.StoreId;
        var supply = await new SupplySettingsService(Data).GetAsync(store);
        supply.ApproverMustDiffer = true;
        await new SupplySettingsService(Data).SaveAsync(store, supply);
        try
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => manager.ApproveAsync(requisition.Id));
            var other = new RequisitionService(await AsUserAsync(Permissions.AdministratorRole));
            Assert.Equal(RequisitionStatus.Approved, (await other.ApproveAsync(requisition.Id)).Status);
        }
        finally
        {
            supply.ApproverMustDiffer = false;
            await new SupplySettingsService(Data).SaveAsync(store, supply);
        }
    }

    // Three-way match --------------------------------------------------------------------------------------------------

    private static BillLineInfo Bill(PurchaseOrder po, PurchaseOrderLine line, decimal quantity, decimal cost, string number = "B-1", Guid? billId = null) =>
        new(billId ?? Guid.NewGuid(), Guid.NewGuid(), number, Today, line.SupplierId, po.Id, line.Id, quantity, cost);

    [SkippableFact]
    public async Task Bills_within_tolerance_match_others_are_held_and_accepted_price_differences_go_to_stock_and_cost_of_goods()
    {
        var (session, location) = await SetUpAsync();
        await new LedgerService(Data).SeedAsync();
        var store = db.Station!.StoreId;
        var supply = await new SupplySettingsService(Data).GetAsync(store);
        (supply.PriceTolerancePercent, supply.PriceToleranceAmount, supply.QuantityTolerancePercent, supply.QuantityToleranceUnits) = (2, 0, 0, 0);
        await new SupplySettingsService(Data).SaveAsync(store, supply);
        var vendor = await SupplierAsync("Vendor");
        var tea = await ItemAsync(location.Id, Unique("TEA"), cost: 10m, supplierId: vendor.Id);
        var cups = await ItemAsync(location.Id, Unique("CUP"), cost: 5m, supplierId: vendor.Id);
        var po = await ReceivedOrderAsync(location.Id, vendor.Id, [(tea, 10, 10m, null), (cups, 4, 5m, null)]);
        var teaLine = po.Lines.Single(l => l.ItemId == tea.Id);
        var cupLine = po.Lines.Single(l => l.ItemId == cups.Id);
        await SellAsync(session, Line(tea, 4));   // 6 tea left in stock

        var bills = new InMemoryBillLineSource();
        var billId = Guid.NewGuid();
        var teaBill = Bill(po, teaLine, 10, 10.15m, billId: billId);   // within 2%
        var cupBill = Bill(po, cupLine, 6, 5m, billId: billId);         // 6 billed, 4 received
        bills.Add(teaBill);
        bills.Add(cupBill);
        var match = new PurchaseMatchService(Data, bills);
        var run = await match.MatchBillAsync(billId);
        Assert.Equal((2, 1, 1), (run.Lines, run.Matched, run.Held));
        var rows = await match.ListAsync(store, purchaseOrderId: po.Id);
        var held = rows.Single(r => r.Status == BillMatchStatus.Held);
        Assert.Equal(cupLine.Id, held.PurchaseOrderLineId);
        Assert.Contains("6 billed", held.Reason, StringComparison.Ordinal);
        Assert.False(await match.IsPayableAsync(billId));

        // Tea matched by itself: 10 × 0.15 = 1.50 more than received; 6 of 10 are still in stock.
        await using (var ctx = db.CreateContext())
        {
            var variance = await ctx.PriceVariances.AsNoTracking().SingleAsync(v => v.PurchaseOrderLineId == teaLine.Id);
            Assert.Equal((1.50m, 0.90m, 0.60m), (variance.Difference, variance.ToInventory, variance.ToCostOfGoods));
            var entry = await ctx.JournalEntries.AsNoTracking().Include(e => e.Lines).ThenInclude(l => l.Account).SingleAsync(e => e.Source == $"match:{teaLine.Id}");
            Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
            Assert.Equal(0.90m, entry.Lines.Single(l => l.Account!.Role == LedgerRoles.Inventory).Debit);
            Assert.Equal(0.60m, entry.Lines.Single(l => l.Account!.Role == LedgerRoles.CostOfGoods).Debit);
            Assert.Equal(1.50m, entry.Lines.Single(l => l.Account!.Role == LedgerRoles.PurchasesClearing).Credit);
        }
        Assert.Equal(10.15m, (await new InventoryService(Data).GetAsync(tea.Id))!.AverageCost);   // 10 + 0.90 / 6

        // The cups: only someone who may approve purchases decides; the accepted line has no price difference to post.
        var clerk = new PurchaseMatchService(await AsUserAsync(Permissions.ClerkRole), bills);
        await Assert.ThrowsAsync<BusinessRuleException>(() => clerk.AcceptAsync(held.Id));
        var manager = new PurchaseMatchService(await AsUserAsync(Permissions.ManagerRole), bills);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => manager.AcceptAsync(held.Id, Guid.NewGuid()));
        var accepted = await manager.AcceptAsync(held.Id, held.Version, "Two more come next week");
        Assert.Equal(BillMatchStatus.Accepted, accepted.Status);
        Assert.True(await match.IsPayableAsync(billId));
        await Assert.ThrowsAsync<BusinessRuleException>(() => manager.RejectAsync(held.Id, "Late"));   // no longer held

        // Matching again keeps the decision; the reports show what is billed but not received, and the other way round.
        await match.MatchOrderAsync(po.Id);
        Assert.Equal(BillMatchStatus.Accepted, (await match.GetAsync(held.Id))!.Status);
        var billedNotReceived = await match.BilledNotReceivedAsync(store);
        Assert.Equal(2m, billedNotReceived.Single(r => r.PurchaseOrderLineId == cupLine.Id).Billed - 4m);
        Assert.DoesNotContain(await match.ReceivedNotBilledAsync(store), r => r.PurchaseOrderId == po.Id);

        // A price far off is held; rejecting it blocks the bill.
        var po2 = await ReceivedOrderAsync(location.Id, vendor.Id, [(tea, 5, 10m, null)]);
        var dear = Bill(po2, po2.Lines.Single(), 5, 12m, "B-2");
        bills.Add(dear);
        await match.MatchBillAsync(dear.BillId);
        var dearRow = (await match.ListAsync(store, BillMatchStatus.Held)).Single(r => r.BillLineId == dear.BillLineId);
        Assert.Contains("12.00", dearRow.Reason, StringComparison.Ordinal);
        await manager.RejectAsync(dearRow.Id, "Wrong price");
        Assert.Single(await match.BlockedLinesAsync(dear.BillId));
        await using (var ctx = db.CreateContext())
            Assert.False(await ctx.JournalEntries.AnyAsync(e => e.Source == $"match:{po2.Lines.Single().Id}"));

        // An order received and not billed shows in that report.
        var po3 = await ReceivedOrderAsync(location.Id, vendor.Id, [(cups, 2, 5m, null)]);
        Assert.Contains(await match.ReceivedNotBilledAsync(store), r => r.PurchaseOrderId == po3.Id && r.Received == 2 && r.Billed == 0 && r.Value == 10m);

        // The first bill is voided: the source no longer gives its lines, matching the bill again takes them out, the tea's
        // price difference comes back out of its cost and its ledger entry is reversed.
        bills.Remove(teaBill.BillLineId);
        bills.Remove(cupBill.BillLineId);
        await match.MatchBillAsync(billId);
        Assert.Empty(await match.ListAsync(store, purchaseOrderId: po.Id));
        Assert.Equal(10m, (await new InventoryService(Data).GetAsync(tea.Id))!.AverageCost);   // 10.082 after PO 2, less 0.90 over 11
        await using (var ctx = db.CreateContext())
        {
            var variance = await ctx.PriceVariances.AsNoTracking().SingleAsync(v => v.PurchaseOrderLineId == teaLine.Id);
            Assert.Equal((0m, 0m, 0m), (variance.Difference, variance.ToInventory, variance.ToCostOfGoods));
            Assert.Equal(JournalStatus.Reversed, (await ctx.JournalEntries.AsNoTracking().SingleAsync(e => e.Source == $"match:{teaLine.Id}")).Status);
        }

        // Billed again at the higher price: posted under the line's next source.
        var again = Bill(po, teaLine, 10, 10.15m, "B-3");
        bills.Add(again);
        await match.MatchBillAsync(again.BillId);
        await using (var ctx = db.CreateContext())
        {
            var entry = await ctx.JournalEntries.AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.Source == $"match:{teaLine.Id}/2");
            Assert.Equal((JournalStatus.Posted, 1.50m), (entry.Status, entry.Lines.Sum(l => l.Debit)));
        }
    }

    // Landed costs -----------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Landed_costs_are_spread_by_value_raise_the_cost_of_stock_on_hand_and_post_the_rest_to_cost_of_goods()
    {
        var (session, location) = await SetUpAsync();
        await new LedgerService(Data).SeedAsync();
        var store = db.Station!.StoreId;
        var shoes = await ItemAsync(location.Id, Unique("SHO"), price: 50m, cost: 20m, weight: 2, weightUnit: "kg");
        var socks = await ItemAsync(location.Id, Unique("SOK"), price: 5m, cost: 2m, weight: 100, weightUnit: "g");
        var first = await ReceivedOrderAsync(location.Id, null, [(shoes, 10, 20m, null)]);
        var second = await ReceivedOrderAsync(location.Id, null, [(socks, 50, 2m, null)]);
        await SellAsync(session, Line(shoes, 5));   // half the shoes are gone

        var landed = new LandedCostService(Data);
        Assert.Contains(await landed.ReceiptsAsync(store), r => r.PurchaseOrderId == first.Id && r.Value == 200m);
        var open = await new PurchasingService(Data).CreateFromItemsAsync(location.Id, null, "Open", [new OrderItem(shoes.Id, 1)]);
        await Assert.ThrowsAsync<BusinessRuleException>(() => landed.SaveAsync(null, null, store, Today, LandedCostBasis.Value, null, null,
            [new LandedChargeInput(LandedCostChargeKind.Freight, null, 30)], [open.Id]));
        var voucher = await landed.SaveAsync(null, null, store, Today, LandedCostBasis.Value, "FR-9", null,
            [new LandedChargeInput(LandedCostChargeKind.Freight, "Truck", 24), new LandedChargeInput(LandedCostChargeKind.Duty, null, 6)], [first.Id, second.Id]);
        Assert.True(voucher.Number > 0);

        // By value: shoes 200 of 300 take 20, socks 100 of 300 take 10.
        var preview = await landed.PreviewAsync(voucher.Id);
        Assert.Equal(20m, preview.Single(a => a.ItemId == shoes.Id).Share);
        Assert.Equal(10m, preview.Single(a => a.ItemId == socks.Id).Share);
        // By weight: shoes 20 kg, socks 5 kg.
        voucher = await landed.SaveAsync(voucher.Id, voucher.Version, store, Today, LandedCostBasis.Weight, "FR-9", null,
            [new LandedChargeInput(LandedCostChargeKind.Freight, "Truck", 24), new LandedChargeInput(LandedCostChargeKind.Duty, null, 6)], [first.Id, second.Id]);
        Assert.Equal(24m, (await landed.PreviewAsync(voucher.Id)).Single(a => a.ItemId == shoes.Id).Share);
        voucher = await landed.SaveAsync(voucher.Id, voucher.Version, store, Today, LandedCostBasis.Value, "FR-9", null,
            [new LandedChargeInput(LandedCostChargeKind.Freight, "Truck", 24), new LandedChargeInput(LandedCostChargeKind.Duty, null, 6)], [first.Id, second.Id]);

        var posted = await landed.PostAsync(voucher.Id, voucher.Version);
        Assert.Equal(LandedCostStatus.Posted, posted.Status);
        var shoeShare = posted.Allocations.Single(a => a.ItemId == shoes.Id);
        Assert.Equal((10m, 10m), (shoeShare.ToInventory, shoeShare.ToCostOfGoods));   // 5 of 10 still on hand
        Assert.Equal(22m, (await new InventoryService(Data).GetAsync(shoes.Id))!.AverageCost);   // 20 + 10 / 5
        Assert.Equal(2.2m, (await new InventoryService(Data).GetAsync(socks.Id))!.AverageCost);  // 2 + 10 / 50
        await using (var ctx = db.CreateContext())
        {
            var entry = await ctx.JournalEntries.AsNoTracking().Include(e => e.Lines).ThenInclude(l => l.Account).SingleAsync(e => e.Source == $"landed:{voucher.Id}");
            Assert.Equal(20m, entry.Lines.Single(l => l.Account!.Role == LedgerRoles.Inventory).Debit);
            Assert.Equal(10m, entry.Lines.Single(l => l.Account!.Role == LedgerRoles.CostOfGoods).Debit);
            Assert.Equal(30m, entry.Lines.Single(l => l.Account!.Role == LedgerRoles.LandedCostsClearing).Credit);
        }
        await Assert.ThrowsAsync<BusinessRuleException>(() => landed.PostAsync(voucher.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => landed.DeleteAsync(voucher.Id));
        Assert.Contains(await landed.ListAsync(store, LandedCostStatus.Posted), r => r.Id == voucher.Id && r.Total == 30m && r.Receipts == 2);
    }

    // Reorder suggestions and supplier performance -----------------------------------------------------------------------

    [SkippableFact]
    public async Task Reorder_suggestions_come_from_sales_lead_time_and_safety_days_in_whole_cases_with_the_reorder_point_as_a_floor()
    {
        var (session, location) = await SetUpAsync();
        var store = db.Station!.StoreId;
        var supply = await new SupplySettingsService(Data).GetAsync(store);
        (supply.SalesWindowDays, supply.SafetyStockDays, supply.OrderCoverDays, supply.DefaultLeadTimeDays) = (10, 2, 3, 5);
        await new SupplySettingsService(Data).SaveAsync(store, supply);
        var fast = await SupplierAsync("Fast", leadTime: 3);
        var soap = await ItemAsync(location.Id, Unique("SOP"), onHand: 40, cost: 1m, supplierId: fast.Id, pack: 12);
        var mop = await ItemAsync(location.Id, Unique("MOP"), onHand: 1, cost: 6m, reorderPoint: 3);
        var idle = await ItemAsync(location.Id, Unique("IDL"), onHand: 50, cost: 1m);
        await SellAsync(session, Line(soap, 30));   // 30 in the window: 3 a day

        var reorder = new ReorderService(Data);
        var rows = await reorder.SuggestAsync(location.Id, today: Today.AddDays(1));
        var soapRow = rows.Single(r => r.ItemId == soap.Id);
        // Level 3 × (3 + 2) = 15 ≥ 10 on hand; target 3 × (3 + 2 + 3) = 24; 14 pieces, two cases of 12.
        Assert.Equal((3m, 3, 15m, 24m, 24m, 2m), (soapRow.AverageDailySales, soapRow.LeadTimeDays, soapRow.ReorderLevel, soapRow.TargetStock, soapRow.SuggestedPieces, soapRow.OrderQuantity));
        var mopRow = rows.Single(r => r.ItemId == mop.Id);
        Assert.Equal((5, 3m, 2m), (mopRow.LeadTimeDays, mopRow.ReorderLevel, mopRow.SuggestedPieces));   // no sales: the reorder point
        Assert.DoesNotContain(rows, r => r.ItemId == idle.Id);
        Assert.Single(await reorder.SuggestAsync(location.Id, fast.Id, today: Today.AddDays(1)));

        var orders = await reorder.ToPurchaseOrdersAsync(location.Id, [new ReorderChoice(soap.Id, 24, fast.Id), new ReorderChoice(mop.Id, 2, null)]);
        Assert.Equal(2, orders.Count);
        var soapLine = orders.Single(o => o.SupplierId == fast.Id).Lines.Single();
        Assert.Equal((12m, 2m), (soapLine.CaseQuantity, soapLine.OrderQuantity));
        var requisition = await reorder.ToRequisitionAsync(location.Id, [new ReorderChoice(mop.Id, 2, null)], submit: true);
        Assert.Equal(RequisitionStatus.Submitted, requisition.Status);
    }

    [SkippableFact]
    public async Task Supplier_performance_counts_on_time_orders_fill_rate_price_changes_returns_and_lead_time()
    {
        var (_, location) = await SetUpAsync();
        var store = db.Station!.StoreId;
        var acme = await SupplierAsync("Acme", leadTime: 2);
        var pen = await ItemAsync(location.Id, Unique("PEN"), cost: 1m, supplierId: acme.Id);
        // On time (expected tomorrow), all of it; then late (expected yesterday), 8 of 10 at a higher price.
        await ReceivedOrderAsync(location.Id, acme.Id, [(pen, 10, 1m, null)], expected: Today.AddDays(1));
        await ReceivedOrderAsync(location.Id, acme.Id, [(pen, 10, 1.1m, null)], receivedFactor: 0.8m, expected: Today.AddDays(-1));
        await new SalesService(Data).SaveRmaAsync(new Rma
        {
            IsSupplierRma = true, Supplier = acme.Company, Plu = pen.Plu, Quantity = 3, UnitPrice = 1m, SupplierRmaDate = Today, IsActive = true,
        });

        var report = await new SupplierPerformanceService(Data).ReportAsync(Today.AddDays(-1), Today, store);
        var row = report.Single(r => r.SupplierId == acme.Id);
        Assert.Equal((2, 1, 50m), (row.Orders, row.OnTime, row.OnTimePercent));
        Assert.Equal((20m, 18m, 90m), (row.OrderedPieces, row.ReceivedPieces, row.FillRatePercent));
        Assert.Equal((1, 0, 10m), (row.PriceIncreases, row.PriceDecreases, row.AveragePriceChangePercent));
        Assert.Equal((1, 3m, 3m), (row.Returns, row.ReturnedPieces, row.ReturnedValue));
        Assert.Equal(0m, row.AverageLeadDays);
        Assert.DoesNotContain(await new SupplierPerformanceService(Data).ReportAsync(Today.AddDays(-30), Today.AddDays(-10), store), r => r.SupplierId == acme.Id);
    }

    [SkippableFact]
    public async Task Settings_are_checked_and_kept_per_store_and_limits_per_role()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var service = new SupplySettingsService(Data);
        var store = db.Station!.StoreId;
        var settings = await service.GetAsync(store);
        settings.SalesWindowDays = 0;
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveAsync(store, settings));
        settings.SalesWindowDays = 90;
        settings.PriceTolerancePercent = -1;
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveAsync(store, settings));
        settings.PriceTolerancePercent = 5;
        await service.SaveAsync(store, settings);
        Assert.Equal((90, 5m), ((await service.GetAsync(store)).SalesWindowDays, (await service.GetAsync(store)).PriceTolerancePercent));

        var roles = await service.GetLimitsAsync();
        var manager = roles.Single(r => r.Role == Permissions.ManagerRole);
        Assert.True(manager.MayApprove);
        Assert.False(roles.Single(r => r.Role == Permissions.CashierRole).MayApprove);
        await service.SetLimitAsync(manager.RoleId, 2500m);
        Assert.Equal(2500m, (await service.GetLimitsAsync()).Single(r => r.RoleId == manager.RoleId).MaxAmount);
        await service.SetLimitAsync(manager.RoleId, null);
        Assert.Null((await service.GetLimitsAsync()).Single(r => r.RoleId == manager.RoleId).MaxAmount);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SetLimitAsync(manager.RoleId, -1));
    }
}

public sealed class SupplyChainTestsOnSqlite(SqliteFixture db) : SupplyChainTests(db), IClassFixture<SqliteFixture>;

public sealed class SupplyChainTestsOnSqlServer(SqlServerFixture db) : SupplyChainTests(db), IClassFixture<SqlServerFixture>;
