using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Integrations;
using RetailPlus.Reports;

namespace RetailPlus.Data.Tests;

/// <summary>The application services against a real SQL Server database: sales, A/R, purchasing, drawer, reports.</summary>
public sealed class ServiceTests(LocalDbFixture db) : IClassFixture<LocalDbFixture>
{
    private static int _station = 100;

    private DataContext Data => new(db.ConnectionString);

    private async Task<(SessionInfo Session, Location Location)> SetUpAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        await new SetupService(Data).SeedAsync(1);
        var location = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Test");
        await using (var ctx = db.CreateContext())
        {
            var settings = await ctx.StoreSettings.SingleAsync();
            settings.Tax1Name = "GST"; settings.Tax1Rate = 5; settings.Tax2Name = "PST"; settings.Tax2Rate = 7; settings.MinimumTender = 0.01m;
            settings.InterestRatePerMonth = 2; settings.GracePeriodDays = 30;
            await ctx.SaveChangesAsync();
        }
        // Each test gets its own station, so drawer totals of tests sharing the database don't mix.
        return (new SessionInfo { StationNumber = Interlocked.Increment(ref _station), LocationId = location.Id, LocationCode = location.Code, UserName = "tester", AccessLevel = 4 }, location);
    }

    private async Task<Item> ItemAsync(int locationId, string plu, decimal price, decimal onHand, decimal cost = 4m) =>
        await new InventoryService(Data).SaveAsync(new Item
        {
            LocationId = locationId, Plu = plu, Name = "Item " + plu, Price1 = price, LastCost = cost, AverageCost = cost, OnHand = onHand, Taxable1 = true, Taxable2 = true,
        }, "TESTDEPT", null, null);

    private static SaleDraftLine Line(Item item, decimal qty) => new()
    {
        ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = qty, UnitPrice = item.Price1,
        Taxable1 = true, Taxable2 = true, UnitCost = item.AverageCost,
    };

    private static TenderEntry Cash(decimal amount) => new("Cash", TenderKind.Cash, amount, amount);

    [SkippableFact]
    public async Task Cash_sale_takes_stock_records_statistics_and_drawer()
    {
        var (session, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "A1", 10m, 20);
        var sales = new SalesService(Data);
        var draft = new SaleDraft { Lines = [Line(item, 3)] };

        var sale = await sales.PostSaleAsync(draft, [Cash(33.60m)], session);

        Assert.True(sale.InvoiceNumber > 0);
        Assert.Equal(30m, sale.Subtotal);
        Assert.Equal(1.5m, sale.Tax1);
        Assert.Equal(2.1m, sale.Tax2);
        Assert.Equal(33.6m, sale.Total);
        Assert.Equal(12m, sale.Cogs);
        var after = await new InventoryService(Data).GetAsync(item.Id);
        Assert.Equal(17m, after!.OnHand);
        var stats = await new InventoryService(Data).GetPeriodSalesAsync(item.Id, DateTime.Today.Year, SalesPeriodKind.Month);
        Assert.Equal(3m, stats.Single(p => p.Period == DateTime.Today.Month).Units);
        var drawer = await new DrawerService(Data).GetCurrentAsync(session);
        Assert.Equal(33.6m, drawer.Session.TotalReceipts);
        Assert.Equal(30m, drawer.Session.NetSales);
        Assert.Equal(33.6m, drawer.CashTendered);
    }

    [SkippableFact]
    public async Task Payments_must_match_the_total()
    {
        var (session, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "A2", 10m, 5);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => new SalesService(Data).PostSaleAsync(new SaleDraft { Lines = [Line(item, 1)] }, [Cash(5m)], session));
        Assert.Contains("do not match", ex.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Training_mode_saves_nothing()
    {
        var (session, location) = await SetUpAsync();
        session.AccessLevel = 0;
        var item = await ItemAsync(location.Id, "A3", 10m, 5);
        var sale = await new SalesService(Data).PostSaleAsync(new SaleDraft { Lines = [Line(item, 1)] }, [Cash(11.20m)], session);
        Assert.Equal(0, sale.Id);
        Assert.Equal(5m, (await new InventoryService(Data).GetAsync(item.Id))!.OnHand);
    }

    [SkippableFact]
    public async Task Void_puts_stock_back_and_reverses_the_drawer()
    {
        var (session, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "A4", 10m, 10);
        var sales = new SalesService(Data);
        var sale = await sales.PostSaleAsync(new SaleDraft { Lines = [Line(item, 2)] }, [Cash(22.40m)], session);
        await sales.VoidSaleAsync(sale.Id, session, "test");
        Assert.Equal(10m, (await new InventoryService(Data).GetAsync(item.Id))!.OnHand);
        Assert.Equal(SaleStatus.Voided, (await sales.GetSaleAsync(sale.Id))!.Status);
        var drawer = await new DrawerService(Data).GetCurrentAsync(session);
        Assert.Equal(0m, drawer.Session.TotalReceipts);
        await Assert.ThrowsAsync<BusinessRuleException>(() => sales.VoidSaleAsync(sale.Id, session));
    }

    [SkippableFact]
    public async Task On_account_sale_creates_an_invoice_and_payments_reduce_it()
    {
        var (session, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "A5", 100m, 10);
        var client = await new PartyService(Data).SaveClientAsync(new Client { LastName = "Tester", CreditLimit = 500 });
        Assert.False(string.IsNullOrEmpty(client.Number));
        var draft = new SaleDraft { ClientId = client.Id, ClientName = "Tester", Lines = [Line(item, 1)] };
        await new SalesService(Data).PostSaleAsync(draft, [new TenderEntry("On Account", TenderKind.OnAccount, 112m, 112m)], session);

        Assert.Equal(112m, (await new PartyService(Data).GetClientAsync(client.Id))!.Balance);
        var invoices = new InvoiceService(Data);
        var open = await invoices.ListAsync(InvoiceKind.Invoice, true, clientId: client.Id);
        Assert.Equal(112m, Assert.Single(open).Balance);

        await invoices.ApplyPaymentAsync(open[0].Id, 50m, "Cash", DateOnly.FromDateTime(DateTime.Today), session);
        Assert.Equal(62m, (await new PartyService(Data).GetClientAsync(client.Id))!.Balance);
        var left = await invoices.DistributePaymentAsync(client.Id, 62m, "Cash", DateOnly.FromDateTime(DateTime.Today), session);
        Assert.Equal(0m, left);
        Assert.Equal(0m, (await new PartyService(Data).GetClientAsync(client.Id))!.Balance);
    }

    [SkippableFact]
    public async Task Credit_limit_stops_an_on_account_sale()
    {
        var (session, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "A6", 100m, 10);
        var client = await new PartyService(Data).SaveClientAsync(new Client { LastName = "Limited", CreditLimit = 50 });
        var draft = new SaleDraft { ClientId = client.Id, Lines = [Line(item, 1)] };
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new SalesService(Data).PostSaleAsync(draft, [new TenderEntry("On Account", TenderKind.OnAccount, 112m, 112m)], session));
    }

    [SkippableFact]
    public async Task Late_charges_are_added_once()
    {
        var (session, _) = await SetUpAsync();
        var client = await new PartyService(Data).SaveClientAsync(new Client { LastName = "Late" });
        var invoices = new InvoiceService(Data);
        var invoice = await invoices.CreateAsync(client.Id, InvoiceKind.Invoice, DateOnly.FromDateTime(DateTime.Today.AddDays(-75)),
            [new InvoiceLine { Description = "Old work", Quantity = 1, UnitPrice = 1000m }], null);
        var today = DateOnly.FromDateTime(DateTime.Today);
        await invoices.ApplyLateChargesAsync(today);
        await invoices.ApplyLateChargesAsync(today);
        var after = await invoices.GetAsync(invoice.Id);
        Assert.Equal(40m, after!.Penalty);
        Assert.Equal(1040m, after.Balance);
        Assert.Equal(1040m, (await new PartyService(Data).GetClientAsync(client.Id))!.Balance);
        _ = session;
    }

    [SkippableFact]
    public async Task Purchase_order_of_flagged_items_leaves_the_others_out()
    {
        var (_, location) = await SetUpAsync();
        var flagged = await ItemAsync(location.Id, "PF1", 10m, 1);
        var other = await ItemAsync(location.Id, "PF2", 10m, 1);
        await using (var ctx = db.CreateContext())
        {
            foreach (var i in await ctx.Items.Where(x => x.Id == flagged.Id || x.Id == other.Id).ToListAsync())
            {
                i.ReorderPoint = 5;
                i.ReorderQuantity = 12;
            }
            await ctx.SaveChangesAsync();
        }
        await new InventoryService(Data).SetFlagAsync([flagged.Id], true);

        var purchasing = new PurchasingService(Data);
        var all = await purchasing.CreateAsync(location.Id, OrderMethod.ReorderPoints, "Everything");
        Assert.Equal(2, all.Lines.Count);
        var po = await purchasing.CreateAsync(location.Id, OrderMethod.ReorderPoints, "Flagged items", flaggedOnly: true);
        Assert.Equal(flagged.Id, Assert.Single(po.Lines).ItemId);
    }

    [SkippableFact]
    public async Task Purchase_order_post_and_receive_update_stock_and_cost()
    {
        var (_, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "P1", 10m, 2, cost: 4m);
        await using (var ctx = db.CreateContext())
        {
            var i = await ctx.Items.SingleAsync(x => x.Id == item.Id);
            i.ReorderPoint = 5; i.ReorderQuantity = 10;
            await ctx.SaveChangesAsync();
        }
        var purchasing = new PurchasingService(Data);
        var po = await purchasing.CreateAsync(location.Id, OrderMethod.ReorderPoints, "Test order");
        var line = Assert.Single(po.Lines);
        Assert.Equal(10m, line.OrderQuantity);

        await purchasing.PostOrderAsync(po.Id);
        Assert.Equal(10m, (await new InventoryService(Data).GetAsync(item.Id))!.OnOrder);

        await purchasing.PostShipmentAsync(po.Id, [new ReceivedLine(line.Id, 10m, 6m)]);
        var after = await new InventoryService(Data).GetAsync(item.Id);
        Assert.Equal(12m, after!.OnHand);
        Assert.Equal(0m, after.OnOrder);
        Assert.Equal(6m, after.LastCost);
        Assert.Equal(5.667m, after.AverageCost);
        Assert.Equal(PurchaseOrderStatus.Received, (await purchasing.GetAsync(po.Id))!.Status);
    }

    [SkippableFact]
    public async Task Suspended_sale_comes_back_once()
    {
        var (session, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "S1", 3m, 5);
        var sales = new SalesService(Data);
        int id = await sales.SuspendAsync(new SaleDraft { Lines = [Line(item, 4)], SubtotalDiscountPercent = 5 }, "Table 4", session.StationNumber);
        var draft = await sales.RecallAsync(id);
        Assert.NotNull(draft);
        Assert.Equal(4m, Assert.Single(draft.Lines).Quantity);
        Assert.Equal(5m, draft.SubtotalDiscountPercent);
        Assert.Null(await sales.RecallAsync(id));
    }

    [SkippableFact]
    public async Task End_of_day_closes_the_session_and_reports_expected_cash()
    {
        var (session, location) = await SetUpAsync();
        var drawer = new DrawerService(Data);
        await drawer.AddMovementAsync(session, DrawerMovementKind.OpeningFloat, 100m, "Float");
        await drawer.AddMovementAsync(session, DrawerMovementKind.PayOut, 15m, "Milk");
        var item = await ItemAsync(location.Id, "E1", 10m, 5);
        await new SalesService(Data).PostSaleAsync(new SaleDraft { Lines = [Line(item, 1)] }, [Cash(11.20m)], session);
        var report = await drawer.EndOfDayAsync(session);
        Assert.NotNull(report.Session.ClosedAt);
        Assert.Equal(96.2m, report.ExpectedCash);
        var next = await drawer.GetCurrentAsync(session);
        Assert.NotEqual(report.Session.Id, next.Session.Id);
        Assert.Contains("END OF DAY", ReceiptFormatter.Drawer(report, new StoreSettings()), StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Every_report_runs()
    {
        var (session, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "R1", 10m, 5);
        await new SalesService(Data).PostSaleAsync(new SaleDraft { Lines = [Line(item, 1)] }, [Cash(11.20m)], session);
        var reports = new ReportService(Data);
        foreach (var def in ReportService.All)
        {
            var table = await reports.RunAsync(def.Key, location.Id, DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), DateOnly.FromDateTime(DateTime.Today));
            Assert.NotEmpty(table.Columns);
            Assert.All(table.Rows, r => Assert.Equal(table.Columns.Count, r.Length));
            Assert.NotEmpty(table.ToCsv());
        }
        var daily = await reports.RunAsync("daily", location.Id, DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today));
        Assert.Equal(10m, (decimal)daily.Rows.Single()[2]!);
    }

    [SkippableFact]
    public async Task Csv_round_trip_updates_prices()
    {
        var (_, location) = await SetUpAsync();
        await ItemAsync(location.Id, "C1", 10m, 5);
        var csv = new CsvExchange(Data);
        var exported = await csv.ExportItemsAsync(location.Id);
        Assert.Contains("C1", exported, StringComparison.Ordinal);
        var result = await csv.ImportItemsAsync(location.Id, "PLU,Price1\nC1,12.50\nNEW1,3\n");
        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Skipped);
        var row = (await new InventoryService(Data).SearchAsync(location.Id, "C1", ItemSearchField.Plu)).Single();
        Assert.Equal(12.5m, row.Price1);
    }

    [SkippableFact]
    public async Task Csv_template_has_every_column_and_its_examples_are_never_imported()
    {
        var (_, location) = await SetUpAsync();
        var template = CsvExchange.ItemTemplate();
        var rows = Csv.Parse(template);
        Assert.Equal(CsvExchange.ItemColumns, rows[0]);
        Assert.Equal(["PROD", "PLU", "EPC", "DEPT", "BSIZE", "PWEIGHT", "TOLRANCE"], rows[0].Take(7));
        Assert.Equal(["SSTATUS", "RECEIPT#", "BARCODE", "BRAND", "SUPPCODE"], rows[0].TakeLast(5));
        Assert.All(rows.Skip(1), r => Assert.Equal(CsvExchange.ItemColumns.Length, r.Length));

        var csv = new CsvExchange(Data);
        var result = await csv.ImportItemsAsync(location.Id, template);
        Assert.Equal(0, result.Added);
        Assert.Equal(3, result.Skipped);   // three example rows; the notes row is not an item
        Assert.Equal(0, result.Tags);

        // The template filled in (with tag codes of our own): two items, the chocolate's two pieces, one of them sold.
        string stamp = Random.Shared.Next(0x1000000).ToString("X6", CultureInfo.InvariantCulture);
        var filled = template.Replace("EXAMPLE-", "TPL-", StringComparison.Ordinal)
            .Replace("E28011700000000000000001", "E2801170000000" + stamp + "0001", StringComparison.Ordinal)
            .Replace("E28011700000000000000002", "E2801170000000" + stamp + "0002", StringComparison.Ordinal);
        result = await csv.ImportItemsAsync(location.Id, filled);
        Assert.Equal(2, result.Added);
        Assert.Equal(2, result.Tags);
        var cola = (await new InventoryService(Data).FindByCodeAsync(location.Id, Upc.InStore(99_999))).Item;
        Assert.Equal("TPL-2", cola?.Plu);
        Assert.Equal(1.5m, cola!.Weight);
        Assert.Equal("L", cola.WeightUnit);
        var chocolate = (await new InventoryService(Data).FindByCodeAsync(location.Id, "TPL-1")).Item!;
        Assert.Equal((90m, "g", 2m, "g"), (chocolate.Weight, chocolate.WeightUnit, chocolate.WeightTolerance, chocolate.WeightToleranceUnit));
        var tags = await new RfidService(Data).GetTagsAsync(chocolate.Id);
        Assert.Equal([ItemTagStatus.InStock, ItemTagStatus.Sold], tags.OrderBy(t => t.Epc).Select(t => t.Status));
        Assert.Equal(1005, tags.Single(t => t.Status == ItemTagStatus.Sold).SoldOnInvoice);

        // A wrong check digit, or a barcode already on another item, is refused.
        result = await csv.ImportItemsAsync(location.Id, $"PLU,PROD,BARCODE\nTPL-3,Bad digit,036000291453\nTPL-4,Taken,{Upc.InStore(99_999)}\n");
        Assert.Equal(2, result.Skipped);
        Assert.Equal(0, result.Added);
    }

    [SkippableFact]
    public async Task Inventory_file_rows_are_pieces_of_an_item_and_export_writes_one_row_per_tag()
    {
        var (_, location) = await SetUpAsync();
        string stamp = Random.Shared.Next(0x1000000).ToString("X6", CultureInfo.InvariantCulture);
        string Epc(int n) => "E2801E60200" + stamp + n.ToString("X7", CultureInfo.InvariantCulture);
        var file = "PROD,PLU,EPC,DEPT,PWEIGHT,TOLRANCE,UNITPRICE1,ONHAND,ITEMTYPE,TX1,TX2,SUPPLIER,LASTUPD,SSTATUS,RECEIPT#\n"
                   + $"COTTON SHIRT,PCS-1,{Epc(1)},CLOTHING,250 GRAM,5 GRAM,30,3,STANDARD,T,F,Hill Traders,7/6/2024,0,\n"
                   + $"COTTON SHIRT,PCS-1,{Epc(2)},CLOTHING,250 GRAM,5 GRAM,30,3,STANDARD,T,F,Hill Traders,7/6/2024,1,1007\n"
                   + $",PCS-1,{Epc(3)},,,,,,,,,,,0,\n"
                   + $"OTHER NAME,PCS-1,{Epc(4)},,,,,,,,,,,0,\n"
                   + ",,EPC,,DROPDOWN,,,,,,,,,SSTATUS,\n";
        var csv = new CsvExchange(Data);
        var result = await csv.ImportItemsAsync(location.Id, file);
        Assert.Equal(1, result.Added);
        Assert.Equal(3, result.Tags);
        Assert.Equal(1, result.Skipped);   // the row that gives PCS-1 another name
        var item = (await new InventoryService(Data).FindByCodeAsync(location.Id, "PCS-1")).Item!;
        Assert.Equal(("COTTON SHIRT", 3m, 250m, "g", 5m), (item.Name, item.OnHand, item.Weight, item.WeightUnit, item.WeightTolerance));
        Assert.Equal(new DateOnly(2024, 7, 6), item.LastUpdated);
        Assert.Equal("Hill Traders", item.Supplier?.Company);
        var tags = await new RfidService(Data).GetTagsAsync(item.Id);
        Assert.Equal(3, tags.Count);
        Assert.Equal(1007, tags.Single(t => t.Epc == Epc(2)).SoldOnInvoice);

        var exported = Csv.Parse(await csv.ExportItemsAsync(location.Id));
        int epc = Array.IndexOf(exported[0], "EPC"), status = Array.IndexOf(exported[0], "SSTATUS"), receipt = Array.IndexOf(exported[0], "RECEIPT#");
        var rows = exported.Skip(1).Where(r => r[1] == "PCS-1").ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(["0", "1", "0"], rows.OrderBy(r => r[epc]).Select(r => r[status]));
        Assert.Equal("1007", rows.Single(r => r[epc] == Epc(2))[receipt]);
        Assert.All(rows, r => Assert.Equal("250 GRAM", r[Array.IndexOf(exported[0], "PWEIGHT")]));
    }

    [SkippableFact]
    public async Task Item_picture_is_stored_replaced_and_removed()
    {
        var (_, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "PIC1", 5m, 1);
        var inventory = new InventoryService(Data);
        Assert.Null(await inventory.GetPictureAsync(item.Id));
        await inventory.SetPictureAsync(item.Id, [1, 2, 3]);
        Assert.Equal([1, 2, 3], await inventory.GetPictureAsync(item.Id));
        await inventory.SetPictureAsync(item.Id, [4]);
        Assert.Equal([4], await inventory.GetPictureAsync(item.Id));
        await inventory.SetPictureAsync(item.Id, null);
        Assert.Null(await inventory.GetPictureAsync(item.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => inventory.SetPictureAsync(item.Id, new byte[3 * 1024 * 1024]));
    }

    [SkippableFact]
    public async Task Users_log_in_with_a_hashed_password()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var setup = new SetupService(Data);
        var name = "user" + Guid.NewGuid().ToString("N")[..6];
        await setup.SaveUserAsync(new UserAccount { UserName = name, AccessLevel = 3 }, "s3cret!");
        Assert.NotNull(await setup.AuthenticateAsync(name, "s3cret!"));
        Assert.Null(await setup.AuthenticateAsync(name, "wrong"));
        var stored = (await setup.GetUsersAsync()).Single(u => u.UserName == name).PasswordHash!;
        Assert.DoesNotContain("s3cret", stored, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Dashboard_shows_today_the_week_stock_and_drawer()
    {
        var (session, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "D1", 10m, 3);
        await using (var ctx = db.CreateContext())
        {
            var i = await ctx.Items.SingleAsync(x => x.Id == item.Id);
            i.ReorderPoint = 5;
            await ctx.SaveChangesAsync();
        }
        await ItemAsync(location.Id, "D2", 5m, 0);
        await ItemAsync(location.Id, "D3", 5m, 50);
        await new SalesService(Data).PostSaleAsync(new SaleDraft { Lines = [Line(item, 1)] }, [Cash(11.20m)], session);

        var d = await new DashboardService(Data).LoadAsync(session);

        Assert.Equal(11.2m, d.SalesToday);
        Assert.Equal(1, d.CustomersToday);
        Assert.Equal(7, d.Week.Count);
        Assert.Equal(11.2m, d.Week[^1].Total);
        Assert.True(d.PaymentsAreToday);
        Assert.Equal(100m, Assert.Single(d.Payments).Percent);
        Assert.Equal(3, d.ItemCount);
        Assert.Equal(1, d.ItemsLow);
        Assert.Equal(1, d.ItemsOut);
        Assert.Equal(1, d.ItemsInStock);
        Assert.Equal(["D2", "D1"], d.ToReorder.Select(r => r.Plu));
        Assert.Equal(11.2m, d.ExpectedCash);
        Assert.NotNull(d.DrawerOpenedAt);
        Assert.Equal(11.2m, Assert.Single(d.Recent).Total);
    }

    [SkippableFact]
    public async Task Backup_writes_a_file()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var folder = Path.Combine(Path.GetTempPath(), "RetailPlusBackupTest");
        var file = await new BackupService(Data).BackupAsync(folder);
        try
        {
            Assert.True(new FileInfo(file).Length > 0);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
