using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Reports;

namespace RetailPlus.Data.Tests;

/// <summary>
/// Stock counts, transfers, case breaks, gift cards, loyalty points, foreign-currency tenders, the accounting export,
/// bulk price changes and undelete, against a real database.
/// </summary>
public abstract class StockAndMoneyTests(DatabaseFixture db)
{
    private static int _station = 300;

    private DataContext Data => db.Data;

    private async Task<(SessionInfo Session, Location Location)> SetUpAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var location = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Test");
        await using (var ctx = db.CreateContext())
        {
            var settings = await ctx.StoreSettings.SingleAsync();
            settings.Tax1Name = "GST"; settings.Tax1Rate = 5; settings.Tax2Name = "PST"; settings.Tax2Rate = 7; settings.MinimumTender = 0.01m;
            settings.BonusPointsEnabled = true; settings.PointsPerDollar = 1; settings.PointsRequired = 10; settings.FixedDiscountReward = true; settings.RewardAmount = 5;
            settings.GiftCardMaximum = 500;
            await ctx.SaveChangesAsync();
        }
        return (new SessionInfo { StationNumber = Interlocked.Increment(ref _station), LocationId = location.Id, LocationCode = location.Code, UserName = "tester", AccessLevel = 4 }, location);
    }

    private async Task<Item> ItemAsync(Guid locationId, string plu, decimal price, decimal onHand, decimal cost = 4m, ItemType type = ItemType.Standard, bool taxable = true,
        string? parent = null, decimal unitsPerCase = 0) =>
        await new InventoryService(Data).SaveAsync(new Item
        {
            LocationId = locationId, Plu = plu, Name = "Item " + plu, Price1 = price, LastCost = cost, AverageCost = cost, OnHand = onHand, Taxable1 = taxable, Taxable2 = taxable,
            ItemType = type, ParentCode = parent, UnitsPerCase = unitsPerCase,
        }, "TESTDEPT", null, null);

    private static SaleDraftLine Line(Item item, decimal qty, decimal? price = null) => new()
    {
        ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = qty, UnitPrice = price ?? item.Price1,
        Taxable1 = item.Taxable1, Taxable2 = item.Taxable2, UnitCost = item.AverageCost, Department = "TESTDEPT",
    };

    private static SaleDraftLine CardLine(Item card, string number, decimal amount, GiftCardKind kind = GiftCardKind.Card)
    {
        var line = Line(card, 1, amount);
        line.GiftCardNumber = number;
        line.GiftCardKind = kind;
        return line;
    }

    private static TenderEntry Cash(decimal amount) => new("Cash", TenderKind.Cash, amount, amount);

    private static TenderEntry Card(string number, decimal amount) => new("Gift Card", TenderKind.GiftCard, amount, amount, Reference: number);

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<decimal> OnHandAsync(Guid itemId) => (await new InventoryService(Data).GetAsync(itemId))!.OnHand;

    private async Task<List<StockMovement>> MovementsAsync(Guid itemId) => await new InventoryService(Data).GetStockMovementsAsync(itemId);

    // Stock counts ---------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Posting_a_count_sets_on_hand_to_the_count_and_writes_one_movement_per_changed_item()
    {
        var (session, location) = await SetUpAsync();
        var a = await ItemAsync(location.Id, "CA1", 10m, 10);
        var b = await ItemAsync(location.Id, "CB1", 10m, 5);
        var c = await ItemAsync(location.Id, "CC1", 10m, 3);
        var counts = new StockCountService(Data);
        var count = await counts.StartAsync(location.Id, "Shelf A", "tester");
        Assert.True(count.Number > 0);

        await counts.CountCodeAsync(count.Id, "ca1", 3);
        await counts.CountCodeAsync(count.Id, "CA1", 5);
        var import = await counts.ImportAsync(count.Id, [["PLU", "QTY"], ["CB1", "2"], ["CB1", "3"], ["NOPE", "1"]]);
        Assert.Equal(5m, import.Counted);
        Assert.Equal(["NOPE"], import.NotFound);

        var lines = (await counts.GetAsync(count.Id))!.Lines;
        Assert.Equal((10m, 8m, -2m, -8m), lines.Where(l => l.ItemId == a.Id).Select(l => (l.Expected, l.Counted, l.Variance, l.VarianceCost)).Single());
        Assert.Equal(0m, lines.Single(l => l.ItemId == b.Id).Variance);

        var posted = await counts.PostAsync(count.Id, zeroUncounted: false, "tester");
        Assert.Equal(1, posted.ItemsChanged);
        Assert.Equal(-8m, posted.VarianceCost);
        Assert.Equal(8m, await OnHandAsync(a.Id));
        Assert.Equal(5m, await OnHandAsync(b.Id));
        Assert.Equal(3m, await OnHandAsync(c.Id));
        var movement = Assert.Single(await MovementsAsync(a.Id), m => m.Kind == StockMovementKind.Count);
        Assert.Equal((-2m, 8m, count.Number), (movement.Quantity, movement.OnHandAfter, movement.DocumentNumber!.Value));
        Assert.DoesNotContain(await MovementsAsync(b.Id), m => m.Kind == StockMovementKind.Count);
        Assert.Equal(8m, (await MovementsAsync(a.Id)).Sum(m => m.Quantity));
        Assert.Equal(StockCountStatus.Posted, (await counts.GetAsync(count.Id))!.Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => counts.CountCodeAsync(count.Id, "CA1", 1));

        // Expected stock is taken when the item is first counted: a sale after that is not a difference.
        var second = await counts.StartAsync(location.Id, null, "tester");
        await counts.CountCodeAsync(second.Id, "CA1", 8);
        await new SalesService(Data).PostSaleAsync(new SaleDraft { Lines = [Line(a, 1)] }, [Cash(11.20m)], session);
        await counts.PostAsync(second.Id, zeroUncounted: true, "tester");
        Assert.Equal(7m, await OnHandAsync(a.Id));
        Assert.Equal(0m, await OnHandAsync(b.Id));
        Assert.Equal(0m, await OnHandAsync(c.Id));
        Assert.Equal(2, (await counts.GetAsync(second.Id))!.Lines.Count(l => l.NotCounted));
        foreach (var id in new[] { a.Id, b.Id, c.Id })
            Assert.Equal(await OnHandAsync(id), (await MovementsAsync(id)).Sum(m => m.Quantity));

        var variances = await new ReportService(Data).RunAsync("countvar", location.Id, DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today));
        Assert.Equal(3, variances.Rows.Count);
        var sheet = await new ReportService(Data).CountSheetAsync(count.Id, showOnHand: true);
        Assert.Equal(3, sheet.Rows.Count);
    }

    [SkippableFact]
    public async Task Cancelling_a_count_leaves_stock_alone()
    {
        var (_, location) = await SetUpAsync();
        var a = await ItemAsync(location.Id, "CN1", 10m, 6);
        var counts = new StockCountService(Data);
        var count = await counts.StartAsync(location.Id, null, "tester");
        await counts.CountCodeAsync(count.Id, "CN1", 2);
        await counts.CancelAsync(count.Id, "tester");

        Assert.Equal(6m, await OnHandAsync(a.Id));
        Assert.DoesNotContain(await MovementsAsync(a.Id), m => m.Kind == StockMovementKind.Count);
        Assert.Equal(StockCountStatus.Cancelled, (await counts.GetAsync(count.Id))!.Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => counts.PostAsync(count.Id, false, "tester"));
    }

    [SkippableFact]
    public async Task Rfid_tags_count_each_piece_once()
    {
        var (_, location) = await SetUpAsync();
        var a = await ItemAsync(location.Id, "CR1", 10m, 1);
        string stamp = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        string Tag(int n) => "E280" + stamp + n.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        var rfid = new RfidService(Data);
        await rfid.RegisterTagAsync(Tag(1), a.Id);
        await rfid.RegisterTagAsync(Tag(2), a.Id);
        var counts = new StockCountService(Data);
        var count = await counts.StartAsync(location.Id, null, "tester");

        var first = await counts.CountTagsAsync(count.Id, [Tag(1), Tag(2), Tag(1), "E280" + stamp + "FFFFFFFF"]);
        var again = await counts.CountTagsAsync(count.Id, [Tag(2)]);

        Assert.Equal(2m, first.Counted);
        Assert.Single(first.NotFound);
        Assert.Equal((0m, 1), (again.Counted, again.AlreadyCounted));
        Assert.Equal(2m, (await counts.GetAsync(count.Id))!.Lines.Single().Counted);
    }

    // Transfers and case breaks --------------------------------------------------------------------------------------

    private async Task<Location> OtherStoreLocationAsync()
    {
        await using var ctx = db.CreateContext();
        var store = new Store { Code = Unique("S")[..8], Name = "Second store" };
        ctx.Stores.Add(store);
        var location = new Location { StoreId = store.Id, Code = "POS", Name = "Second store" };
        ctx.Locations.Add(location);
        await ctx.SaveChangesAsync();
        return location;
    }

    [SkippableFact]
    public async Task A_transfer_to_another_store_ships_and_is_received_short_and_creates_the_missing_item()
    {
        var (_, location) = await SetUpAsync();
        var there = await OtherStoreLocationAsync();
        var shirt = await ItemAsync(location.Id, "TR1", 30m, 10, cost: 12m);
        var cap = await ItemAsync(location.Id, "TR2", 15m, 4, cost: 5m);
        // The receiving store already stocks caps; shirts are new there.
        await using (var ctx = db.CreateContext())
        {
            ctx.Items.Add(new Item { LocationId = there.Id, Plu = "TR2", Name = "Their cap", Price1 = 16m, OnHand = 1, AverageCost = 5m, LastCost = 5m });
            await ctx.SaveChangesAsync();
        }
        var transfers = new TransferService(Data);
        var transfer = await transfers.CreateAsync(location.Id, there.Id, "Weekly", "tester");
        Assert.Equal(there.StoreId, transfer.ToStoreId);
        Assert.True(StockTransfer.BelongsTo(transfer, db.Station!.StoreId));
        Assert.True(StockTransfer.BelongsTo(transfer, there.StoreId));
        await transfers.AddItemAsync(transfer.Id, "TR1", 6);
        await transfers.AddItemAsync(transfer.Id, "TR1", 4);
        await transfers.AddItemAsync(transfer.Id, "TR2", 3);
        await transfers.ShipAsync(transfer.Id, "tester");

        Assert.Equal(0m, await OnHandAsync(shirt.Id));
        Assert.Equal(1m, await OnHandAsync(cap.Id));
        Assert.Equal(-10m, Assert.Single(await MovementsAsync(shirt.Id), m => m.Kind == StockMovementKind.TransferOut).Quantity);
        var transit = await new ReportService(Data).RunAsync("transit", location.Id, DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today));
        Assert.Contains(transit.Rows, r => (string?)r[4] == "TR1" && (decimal?)r[6] == 10m);

        // The receiving store's till sees the transfer as incoming and receives 7 shirts of 10.
        var theirs = new DataContext(db.Connection, new StationIdentity(there.StoreId, 2));
        var incoming = (await new TransferService(theirs).ListAsync()).Single(t => t.Id == transfer.Id);
        Assert.True(incoming.Incoming);
        var lines = (await transfers.GetAsync(transfer.Id))!.Lines;
        var receipt = await new TransferService(theirs).ReceiveAsync(transfer.Id, new Dictionary<Guid, decimal> { [lines.Single(l => l.Plu == "TR1").Id] = 7 }, "receiver");

        Assert.Equal(["TR1 Item TR1"], receipt.CreatedItems);
        Assert.Single(receipt.ShortLines);
        Assert.Equal(10m, receipt.Received);
        var received = (await transfers.GetAsync(transfer.Id))!;
        Assert.Equal(StockTransferStatus.Received, received.Status);
        var shirtLine = received.Lines.Single(l => l.Plu == "TR1");
        Assert.Equal((10m, 7m, true), (shirtLine.QuantityShipped, shirtLine.QuantityReceived, shirtLine.CreatedAtDestination));
        var newShirt = (await new InventoryService(Data).FindByCodeAsync(there.Id, "TR1")).Item!;
        Assert.Equal((7m, 30m, 12m, "TESTDEPT"), (newShirt.OnHand, newShirt.Price1, newShirt.AverageCost, newShirt.Department?.Name));
        var theirCap = (await new InventoryService(Data).FindByCodeAsync(there.Id, "TR2")).Item!;
        Assert.Equal(4m, theirCap.OnHand);
        var arrived = Assert.Single(await MovementsAsync(newShirt.Id));
        Assert.Equal((StockMovementKind.TransferIn, 7m, there.StoreId), (arrived.Kind, arrived.Quantity, arrived.StoreId));
        await Assert.ThrowsAsync<BusinessRuleException>(() => transfers.ReceiveAsync(transfer.Id, null, "again"));
    }

    [SkippableFact]
    public async Task A_draft_transfer_can_be_cancelled_and_a_received_item_is_created_from_the_line_when_the_source_is_unknown()
    {
        var (_, location) = await SetUpAsync();
        var other = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Back room");
        await ItemAsync(location.Id, "TC1", 9m, 5);
        var transfers = new TransferService(Data);
        var draft = await transfers.CreateAsync(location.Id, other.Id, null, "tester");
        await transfers.AddItemAsync(draft.Id, "TC1", 2);
        await transfers.CancelAsync(draft.Id);
        Assert.Equal(StockTransferStatus.Cancelled, (await transfers.GetAsync(draft.Id))!.Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => transfers.ShipAsync(draft.Id, "tester"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => transfers.CreateAsync(location.Id, location.Id, null, "tester"));

        // A transfer that reached this database without its sending item (another store's item): the line's details are used.
        var shipped = await transfers.CreateAsync(location.Id, other.Id, null, "tester");
        await using (var ctx = db.CreateContext())
        {
            var t = await ctx.StockTransfers.SingleAsync(x => x.Id == shipped.Id);
            t.Status = StockTransferStatus.Shipped;
            ctx.StockTransferLines.Add(new StockTransferLine
            {
                StockTransferId = t.Id, LineNumber = 1, SourceItemId = Guid.NewGuid(), Plu = "FOREIGN1", Description = "Item from elsewhere", Department = "IMPORTED",
                ItemType = ItemType.Standard, Taxable1 = true, UnitCost = 3m, Price = 7.5m, QuantityShipped = 4,
            });
            await ctx.SaveChangesAsync();
        }
        var receipt = await transfers.ReceiveAsync(shipped.Id, null, "tester");
        Assert.Single(receipt.CreatedItems);
        var item = (await new InventoryService(Data).FindByCodeAsync(other.Id, "FOREIGN1")).Item!;
        Assert.Equal((4m, 7.5m, 3m, "IMPORTED", true, false), (item.OnHand, item.Price1, item.AverageCost, item.Department?.Name, item.Taxable1, item.Taxable2));
    }

    [SkippableFact]
    public async Task Breaking_cases_moves_stock_from_the_case_item_to_the_unit_item()
    {
        var (_, location) = await SetUpAsync();
        var box = await ItemAsync(location.Id, "CASE24", 30m, 5, cost: 24m);
        var can = await ItemAsync(location.Id, "CAN1", 1.5m, 3, cost: 1.2m, parent: "CASE24", unitsPerCase: 24);
        var inventory = new InventoryService(Data);

        var link = await inventory.FindCaseLinkAsync(box.Id);
        Assert.Equal((can.Id, 24m), (link!.UnitItemId, link.UnitsPerCase));
        var after = await inventory.BreakCaseAsync(can.Id, 2, "tester");

        Assert.Equal((3m, 51m), (after.CasesOnHand, after.UnitsOnHand));
        Assert.Equal(-2m, Assert.Single(await MovementsAsync(box.Id), m => m.Kind == StockMovementKind.CaseBreakOut).Quantity);
        Assert.Equal(48m, Assert.Single(await MovementsAsync(can.Id), m => m.Kind == StockMovementKind.CaseBreakIn).Quantity);
        Assert.Equal(1.012m, (await inventory.GetAsync(can.Id))!.AverageCost);
        await Assert.ThrowsAsync<BusinessRuleException>(() => inventory.BreakCaseAsync(box.Id, 1, "tester"));
    }

    // Gift cards -----------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Gift_cards_are_issued_reloaded_and_redeemed_and_cannot_be_overdrawn_or_overfilled()
    {
        var (session, location) = await SetUpAsync();
        var cardItem = await ItemAsync(location.Id, "GIFT", 0m, 0, cost: 0, type: ItemType.GiftCard, taxable: false);
        var goods = await ItemAsync(location.Id, "GD1", 10m, 50);
        var sales = new SalesService(Data);
        var cards = new GiftCardService(Data);
        string number = Unique("GC");

        await sales.PostSaleAsync(new SaleDraft { Lines = [CardLine(cardItem, number, 100m)] }, [Cash(100m)], session);
        var reload = await sales.PostSaleAsync(new SaleDraft { Lines = [CardLine(cardItem, number.ToLowerInvariant(), 50m)] }, [Cash(50m)], session);
        Assert.Equal(150m, (await cards.FindAsync(number))!.Balance);
        Assert.Equal(GiftCardKind.Card, (await cards.FindAsync(number))!.Card.Kind);

        // 3 items: 30 + 5% + 7% = 33.60, paid by the card.
        await sales.PostSaleAsync(new SaleDraft { Lines = [Line(goods, 3)] }, [Card(number, 33.60m)], session);
        var detail = (await cards.FindAsync(number))!;
        Assert.Equal(116.40m, detail.Balance);
        Assert.Equal([GiftCardTransactionKind.Redeem, GiftCardTransactionKind.Reload, GiftCardTransactionKind.Issue], detail.History.Select(t => t.Kind));
        Assert.Equal(116.40m, await cards.CheckPaymentAsync(number, 116.40m));

        // More than is on the card, a card this till does not know, and more than the card may hold are refused.
        await Assert.ThrowsAsync<BusinessRuleException>(() => cards.CheckPaymentAsync(number, 200m));
        var tooMuch = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            sales.PostSaleAsync(new SaleDraft { Lines = [Line(goods, 20)] }, [Card(number, 224m)], session));
        Assert.Contains("only", tooMuch.Message, StringComparison.Ordinal);
        Assert.Equal(116.40m, (await cards.FindAsync(number))!.Balance);
        await Assert.ThrowsAsync<BusinessRuleException>(() => sales.PostSaleAsync(new SaleDraft { Lines = [Line(goods, 1)] }, [Card("NO-SUCH-CARD", 11.20m)], session));
        var full = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            sales.PostSaleAsync(new SaleDraft { Lines = [CardLine(cardItem, number, 400m)] }, [Cash(400m)], session));
        Assert.Contains("at most", full.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<BusinessRuleException>(() => sales.PostSaleAsync(new SaleDraft { Lines = [CardLine(cardItem, Unique("GC"), 600m)] }, [Cash(600m)], session));

        // Voiding the reload takes its 50 off again; a certificate is not reloaded; a deactivated card cannot pay.
        await sales.VoidSaleAsync(reload.Id, session);
        Assert.Equal(66.40m, (await cards.FindAsync(number))!.Balance);
        string certificate = await cards.NewNumberAsync();
        await sales.PostSaleAsync(new SaleDraft { Lines = [CardLine(cardItem, certificate, 25m, GiftCardKind.Certificate)] }, [Cash(25m)], session);
        await Assert.ThrowsAsync<BusinessRuleException>(() => cards.CheckSaleAsync(certificate, 10m));
        await cards.SetActiveAsync(detail.Card.Id, false);
        await Assert.ThrowsAsync<BusinessRuleException>(() => cards.CheckPaymentAsync(number, 1m));
        await cards.SetActiveAsync(detail.Card.Id, true);
        await cards.AdjustAsync(detail.Card.Id, -6.40m, "Damaged card", "tester");
        Assert.Equal(60m, (await cards.FindAsync(number))!.Balance);

        var liability = await new ReportService(Data).RunAsync("giftcards", location.Id, DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today));
        Assert.Contains(liability.Rows, r => (string?)r[0] == number && (decimal?)r[6] == 60m);
        Assert.Contains(liability.Rows, r => (string?)r[0] == certificate && (decimal?)r[6] == 25m);
    }

    // Loyalty --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Points_earned_and_redeemed_are_kept_as_history_and_add_up_to_the_clients_points()
    {
        var (session, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "LY1", 10m, 50);
        var client = await new PartyService(Data).SaveClientAsync(new Client { LastName = "Loyal", Points = 4 });
        var sales = new SalesService(Data);
        var loyalty = new LoyaltyService(Data);

        await sales.PostSaleAsync(new SaleDraft { ClientId = client.Id, Lines = [Line(item, 3)] }, [Cash(33.60m)], session);
        Assert.Equal(34m, (await new PartyService(Data).GetClientAsync(client.Id))!.Points);

        // Ten points for a reward of 5 (a coupon line): 20 - 5 = 15 net, 15 points earned.
        var reward = new SaleDraftLine { Kind = SaleLineKind.Coupon, Description = "Bonus points reward", Quantity = 1, UnitPrice = -5m, Taxable1 = true, Taxable2 = true, AutoPriced = false };
        var second = await sales.PostSaleAsync(new SaleDraft { ClientId = client.Id, PointsRedeemed = 10, Lines = [Line(item, 2), reward] }, [Cash(16.80m)], session);
        Assert.Equal(39m, (await new PartyService(Data).GetClientAsync(client.Id))!.Points);
        var history = await loyalty.HistoryAsync(client.Id);
        Assert.Equal(4, history.Count);
        Assert.Equal(-10m, history.Single(h => h.Kind == LoyaltyTransactionKind.Redeem).Points);
        Assert.Equal(second.InvoiceNumber, history.Single(h => h.Kind == LoyaltyTransactionKind.Redeem).InvoiceNumber);

        await sales.VoidSaleAsync(second.Id, session);
        await loyalty.AdjustAsync(client.Id, 6, "Birthday", "tester");
        await Assert.ThrowsAsync<BusinessRuleException>(() => loyalty.AdjustAsync(client.Id, -1000, null, "tester"));
        var points = (await new PartyService(Data).GetClientAsync(client.Id))!.Points;
        Assert.Equal(40m, points);
        Assert.Equal(points, await loyalty.TotalAsync(client.Id));

        // The editor cannot change points: they only change with their history.
        var editing = (await new PartyService(Data).GetClientAsync(client.Id))!;
        editing.Points = 999;
        await new PartyService(Data).SaveClientAsync(editing);
        Assert.Equal(40m, (await new PartyService(Data).GetClientAsync(client.Id))!.Points);

        var report = await new ReportService(Data).RunAsync("loyalty", location.Id, DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today));
        var row = report.Rows.Single(r => (string?)r[0] == client.Number);
        Assert.Equal((0m, 30m, 0m, 10m, 40m), ((decimal)row[2]!, (decimal)row[3]!, (decimal)row[4]!, (decimal)row[5]!, (decimal)row[6]!));
    }

    // Foreign currency -----------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_foreign_currency_payment_keeps_both_amounts_and_gives_change_in_home_currency()
    {
        var (session, location) = await SetUpAsync();
        await using (var ctx = db.CreateContext())
        {
            if (!await ctx.TenderTypes.AnyAsync(t => t.Name == "US Dollars"))
                ctx.TenderTypes.Add(new TenderType { Name = "US Dollars", Kind = TenderKind.Cash, Currency = "USD", SortOrder = 20, OpensDrawer = true });
            await ctx.SaveChangesAsync();
        }
        var item = await ItemAsync(location.Id, "FX1", 10m, 10);
        // 1 at home buys 0.5 USD: 33.60 due is 16.80 USD; the customer gives 20 USD (40.00), so 6.40 comes back at home.
        Assert.Equal(16.80m, ForeignCurrency.ToForeign(33.60m, 0.5m));
        var tender = ForeignCurrency.Pay("US Dollars", TenderKind.Cash, "USD", 0.5m, 20m, 33.60m);
        Assert.Equal((33.60m, 40m, 6.40m), (tender.Amount, tender.Tendered, tender.Change));

        var sale = await new SalesService(Data).PostSaleAsync(new SaleDraft { Lines = [Line(item, 3)] }, [tender], session);

        var payment = Assert.Single((await new SalesService(Data).GetSaleAsync(sale.Id))!.Payments);
        Assert.Equal(("USD", 20m, 0.5m, 33.60m, 6.40m), (payment.Currency, payment.ForeignAmount, payment.ExchangeRate, payment.Amount, payment.Change));
        var drawer = await new DrawerService(Data).GetCurrentAsync(session);
        var usd = drawer.Tenders.Single(t => t.TenderType == "US Dollars");
        Assert.Equal((40m, 20m), (usd.Amount, usd.ForeignAmount));
        Assert.Equal(-6.40m, drawer.Tenders.Single(t => t.TenderType == "Cash").Amount);
        Assert.Equal(-6.40m, drawer.ExpectedCash);
        Assert.Equal(33.60m, drawer.Session.TotalReceipts);
    }

    // Accounting -----------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Every_exported_journal_balances()
    {
        var (session, location) = await SetUpAsync();
        var accounting = new AccountingService(Data);
        var table = await accounting.MappingTableAsync();
        foreach (var row in table)
        {
            row.AccountCode = row.Kind switch
            {
                AccountMappingKind.Tender when row.Key == "Cash" => "1000",
                AccountMappingKind.Department => "4100",
                AccountMappingKind.Sales => "4000",
                AccountMappingKind.Tax1 => "2200",
                AccountMappingKind.Tax2 => "2210",
                AccountMappingKind.GiftCardLiability => "2400",
                AccountMappingKind.CostOfGoods => "5000",
                AccountMappingKind.Inventory => "1300",
                AccountMappingKind.AccountsReceivable => "1100",
                AccountMappingKind.StockAdjustments => "5100",
                AccountMappingKind.PurchasesClearing => "2100",
                AccountMappingKind.GiftCardAdjustments => "5200",
                _ => "",
            };
            row.AccountName = row.AccountCode.Length > 0 ? row.Kind.ToString() : null;
        }
        await accounting.SaveMappingsAsync(table);
        Assert.Contains(await accounting.GetMappingsAsync(), m => m.Kind == AccountMappingKind.Tender && m.Key == "Cash" && m.AccountCode == "1000");

        var item = await ItemAsync(location.Id, "AC1", 10m, 40, cost: 4m);
        var cardItem = await ItemAsync(location.Id, "ACGIFT", 0m, 0, cost: 0, type: ItemType.GiftCard, taxable: false);
        var client = await new PartyService(Data).SaveClientAsync(new Client { LastName = "Ledger" });
        var sales = new SalesService(Data);
        string card = Unique("AC");
        await sales.PostSaleAsync(new SaleDraft { Lines = [Line(item, 3)] }, [Cash(33.60m)], session);
        await sales.PostSaleAsync(new SaleDraft { Lines = [Line(item, 1), CardLine(cardItem, card, 50m)], SubtotalDiscountPercent = 10 }, [Cash(55.08m)], session);
        await sales.PostSaleAsync(new SaleDraft { Lines = [Line(item, 2)] }, [new TenderEntry("Visa", TenderKind.Card, 10m, 10m), Card(card, 12.40m)], session);
        await sales.PostSaleAsync(new SaleDraft { ClientId = client.Id, Lines = [Line(item, 1)] }, [new TenderEntry("On Account", TenderKind.OnAccount, 11.20m, 11.20m)], session);
        var open = await new InvoiceService(Data).ListAsync(InvoiceKind.Invoice, true, clientId: client.Id);
        await new InvoiceService(Data).ApplyPaymentAsync(open.Single().Id, 5m, "Cash", DateOnly.FromDateTime(DateTime.Today), session);
        await new InventoryService(Data).AdjustStockAsync(item.Id, -2, "Broken", "tester");
        var purchasing = new PurchasingService(Data);
        var po = await purchasing.CreateAsync(location.Id, OrderMethod.Blank, "Restock");
        await purchasing.AddItemAsync(po.Id, "AC1", 5);
        await purchasing.PostShipmentAsync(po.Id, [new ReceivedLine((await purchasing.GetAsync(po.Id))!.Lines.Single().Id, 5m, 4.5m)]);
        var counts = new StockCountService(Data);
        var count = await counts.StartAsync(location.Id, null, "tester");
        await counts.CountCodeAsync(count.Id, "AC1", 30);
        await counts.PostAsync(count.Id, false, "tester");
        var backRoom = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Back room");
        var transfers = new TransferService(Data);
        var transfer = await transfers.CreateAsync(location.Id, backRoom.Id, null, "tester");
        await transfers.AddItemAsync(transfer.Id, "AC1", 4);
        await transfers.ShipAsync(transfer.Id, "tester");
        var box = await ItemAsync(location.Id, "ACCASE", 40m, 2, cost: 36m);
        var unit = await ItemAsync(location.Id, "ACUNIT", 2m, 0, cost: 1.5m, parent: "ACCASE", unitsPerCase: 24);
        await new InventoryService(Data).BreakCaseAsync(unit.Id, 1, "tester");
        var gift = new GiftCardService(Data);
        await gift.AdjustAsync((await gift.FindAsync(card))!.Card.Id, 5m, "Goodwill", "tester");

        var today = DateOnly.FromDateTime(DateTime.Today);
        var export = await accounting.ExportAsync(today.AddDays(-1), today);

        Assert.NotEmpty(export.Lines);
        Assert.All(export.Totals, t => Assert.Equal(t.Debit, t.Credit));
        Assert.All(export.Lines, l => Assert.True(l.Debit >= 0 && l.Credit >= 0 && (l.Debit == 0) != (l.Credit == 0)));
        Assert.Contains(export.Lines, l => l.Account == "2400" && l.Memo == "Gift cards sold" && l.Credit > 0);
        Assert.Contains(export.Lines, l => l.Account == "2100" && l.Memo == "Stock received" && l.Credit > 0);
        Assert.Contains(export.Lines, l => l.Account == "5100" && l.Memo == "Stock counts");
        Assert.Contains(export.Lines, l => l.Account == "1300" && l.Memo == "Transfers" && l.Credit > 0);
        // A case broken at its cost moves value from the case item to the units: no gain or loss.
        Assert.DoesNotContain(export.Lines, l => l.Memo == "Case breaks");
        Assert.Contains(export.Lines, l => l.Account == "1100" && l.Memo == "Payments on account" && l.Credit > 0);
        // A supervisor's correction (this test's +5 and any other test's of the day in a shared database) is posted
        // against the adjustments account; corrections that belong to a sale are not.
        var since = today.ToDateTime(TimeOnly.MinValue);
        decimal corrected;
        await using (var check = Data.Create())
            corrected = (await check.GiftCardTransactions.AsNoTracking()
                .Where(t => t.Kind == GiftCardTransactionKind.Adjust && t.SaleId == null && t.At >= since && t.StoreId == Data.Station!.StoreId)
                .Select(t => t.Amount).ToListAsync()).Sum();
        var corrections = export.Lines.Where(l => l.Date == today && l.Memo == "Gift card corrections").ToList();
        Assert.Equal(corrected, corrections.Where(l => l.Account == "5200").Sum(l => l.Debit - l.Credit));
        Assert.Equal(corrected, corrections.Where(l => l.Account == "2400").Sum(l => l.Credit - l.Debit));
        Assert.Contains("Tender Visa", export.Unmapped);
        Assert.StartsWith("Date,Journal,Store,Account,Account name,Debit,Credit,Memo", export.ToCsv(), StringComparison.Ordinal);

        var folder = Path.Combine(Path.GetTempPath(), "RetailPlusJournalTest" + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            var file = await accounting.ExportDayToFolderAsync(folder, today, session.StationNumber);
            Assert.True(File.Exists(file));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    // Prices and undelete --------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Price_changes_are_previewed_and_only_the_chosen_ones_applied()
    {
        var (_, location) = await SetUpAsync();
        var a = await ItemAsync(location.Id, "PR1", 10m, 1, cost: 5m);
        var b = await ItemAsync(location.Id, "PR2", 20m, 1, cost: 5m);
        var inventory = new InventoryService(Data);

        var preview = await inventory.PreviewPriceChangesAsync(location.Id, new PriceRule(PriceChangeMethod.Percent, 10, 0.99m), false, null);
        Assert.Equal([("PR1", 11.99m), ("PR2", 22.99m)], preview.Select(p => (p.Plu, p.NewPrice)));
        Assert.Equal(10m, (await inventory.GetAsync(a.Id))!.Price1);
        preview.Single(p => p.Plu == "PR2").Apply = false;
        preview.Single(p => p.Plu == "PR1").NewPrice = 11.49m;
        Assert.Equal(1, await inventory.ApplyPriceChangesAsync(preview));
        Assert.Equal((11.49m, 20m), ((await inventory.GetAsync(a.Id))!.Price1, (await inventory.GetAsync(b.Id))!.Price1));

        var margin = await inventory.PreviewPriceChangesAsync(location.Id, new PriceRule(PriceChangeMethod.Margin, 50), false, null);
        Assert.All(margin, p => Assert.Equal(10m, p.NewPrice));
        Assert.Equal(50m, margin[0].NewMargin);

        // Another price level changes only that price.
        await using (var edit = Data.Create())
        {
            await edit.Items.Where(i => i.Id == a.Id || i.Id == b.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.Price3, 8m).SetProperty(i => i.UpdatedAtUtc, DateTime.UtcNow));
        }
        var third = await inventory.PreviewPriceChangesAsync(location.Id, new PriceRule(PriceChangeMethod.Amount, 1.5m, Level: 3), false, null);
        Assert.Equal([("PR1", 8m, 9.5m), ("PR2", 8m, 9.5m)], third.Select(p => (p.Plu, p.OldPrice, p.NewPrice)));
        Assert.Equal(2, await inventory.ApplyPriceChangesAsync(third));
        var after = (await inventory.GetAsync(a.Id))!;
        Assert.Equal((11.49m, 9.5m), (after.Price1, after.Price3));
        await Assert.ThrowsAsync<BusinessRuleException>(() => inventory.PreviewPriceChangesAsync(location.Id, new PriceRule(PriceChangeMethod.Percent, 5, Level: 5), false, null));
    }

    [SkippableFact]
    public async Task Deleted_clients_and_suppliers_come_back_with_undelete()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var parties = new PartyService(Data);
        var client = await parties.SaveClientAsync(new Client { LastName = "Gone" });
        await parties.DeleteClientAsync(client.Id);
        Assert.DoesNotContain(await parties.SearchClientsAsync("Gone"), c => c.Id == client.Id);
        await parties.RestoreClientAsync(client.Id);
        Assert.Contains(await parties.SearchClientsAsync("Gone"), c => c.Id == client.Id);

        string number = Unique("S")[..9];
        var supplier = await parties.SaveSupplierAsync(new Supplier { Company = "Gone Supply", Number = number });
        await parties.DeleteSupplierAsync(supplier.Id);
        await parties.SaveSupplierAsync(new Supplier { Company = "Taker", Number = number });
        await Assert.ThrowsAsync<BusinessRuleException>(() => parties.RestoreSupplierAsync(supplier.Id));
        Assert.True((await parties.GetSupplierAsync(supplier.Id))!.IsDeleted);
    }
}

public sealed class StockAndMoneyTestsOnSqlite(SqliteFixture db) : StockAndMoneyTests(db), IClassFixture<SqliteFixture>;

public sealed class StockAndMoneyTestsOnSqlServer(SqlServerFixture db) : StockAndMoneyTests(db), IClassFixture<SqlServerFixture>;
