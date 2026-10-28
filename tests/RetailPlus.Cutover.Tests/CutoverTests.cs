using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Cutover.Tests;

/// <summary>
/// The cutover from the enriched Retail25 source into a new database: what it carries over, that a second run changes
/// nothing, that a run after Retail25 changed updates only that, that a dry run writes nothing, and that users keep their
/// passwords. Each test runs on a new SQLite file and on a new SQL Server database.
/// </summary>
public abstract class CutoverTests(Retail25SourceFixture source, CutoverTargets targets)
{
    private const string TstCode = "TST", NthCode = "NTH";

    private Task<CutoverReport> RunAsync(DatabaseConnection target, bool dryRun = false, string? storeCode = null) =>
        new CutoverRunner(new CutoverOptions { SourceConnectionString = source.ConnectionString, Target = target, DryRun = dryRun, StoreCode = storeCode }).RunAsync();

    private static RetailPlusDbContext Open(DatabaseConnection target) => RetailPlusDb.CreateContext(target);

    private static void AllChecksMatch(CutoverReport report)
    {
        Assert.True(report.Succeeded, report.Error);
        Assert.All(report.Checks, c => Assert.True(c.Matches, $"{c.Area} / {c.Name}: Retail25 {c.Source}, new {c.Target}"));
        Assert.NotEmpty(report.Checks);
    }

    private static async Task<Guid> LocationAsync(RetailPlusDbContext db, string code) =>
        await db.Locations.Where(l => l.Code == code).Select(l => l.Id).SingleAsync();

    [SkippableFact]
    public async Task First_run_carries_everything_over_and_every_check_matches()
    {
        Skip.IfNot(source.Available, source.UnavailableReason);
        var target = targets.Create();
        var report = await RunAsync(target);
        AllChecksMatch(report);
        Assert.Contains(report.Checks, c => c.Area == "Stock TST" && c.Name == "Units on hand" && c.Target == 5003);

        await using var db = Open(target);
        var tst = await LocationAsync(db, TstCode);
        var nth = await LocationAsync(db, NthCode);

        // Stores, stations (NTH's station 001 clashes with TST's and becomes station 2), settings, branding.
        Assert.Equal(2, await db.Stores.CountAsync());
        Assert.Equal<int>([1, 2], (await db.Stations.Select(s => s.Number).ToListAsync()).Order());
        var stores = await db.Stores.ToDictionaryAsync(s => s.Code);
        Assert.Equal("Eastern Standard Time", stores[TstCode].TimeZoneId);
        var settings = await db.StoreSettings.SingleAsync(s => s.StoreId == stores[TstCode].Id);
        Assert.Equal(("Corner Grocery Ltd", 5m, 7m, 1.5m, true), (settings.CompanyName, settings.Tax1Rate, settings.Tax2Rate, settings.InterestRatePerMonth, settings.BonusPointsEnabled));
        Assert.Equal(13m, (await db.StoreSettings.SingleAsync(s => s.StoreId == stores[NthCode].Id)).Tax1Rate);
        Assert.Equal(2, await db.StoreImages.CountAsync(i => i.StoreId == stores[TstCode].Id));
        Assert.Equal(0, await db.StoreImages.CountAsync(i => i.StoreId == stores[NthCode].Id));
        Assert.Equal("USD", (await db.TenderTypes.SingleAsync(t => t.Name == "US cash")).Currency);
        Assert.Equal(0.73m, (await db.FxRates.SingleAsync()).Rate);

        // Lists: "Grocery" of both locations is one department; long names are cut to 15 characters.
        Assert.Equal(1, await db.Departments.CountAsync(d => d.Name == "GROCERY"));
        Assert.True(await db.Departments.AnyAsync(d => d.Name == "GARDEN CENTRE S"));

        // Items: 136 demo products and 9 new ones at NTH, one gift card product at TST.
        Assert.Equal(137, await db.Items.CountAsync(i => i.LocationId == tst));
        Assert.Equal(9, await db.Items.CountAsync(i => i.LocationId == nth));
        var rice = await db.Items.Include(i => i.Suppliers).SingleAsync(i => i.LocationId == nth && i.Plu == "DEMO-0001");
        Assert.Equal((24.50m, 23m, 22m, 21m, 10m, 25m, 50m, 18.10m), (rice.Price1, rice.Price2, rice.Price3, rice.Price4, rice.BreakQuantity1, rice.BreakQuantity2, rice.BreakQuantity3, rice.AverageCost));
        Assert.Equal<string>(["Green Thumb Distributors", "Prairie Foods Wholesale"], rice.Suppliers.OrderBy(s => s.Rank).Select(s => s.SupplierName));
        Assert.Equal("GT-RICE", rice.SupplierCode);
        Assert.False(rice.Taxable2);
        var hose = await db.Items.SingleAsync(i => i.LocationId == nth && i.Plu.StartsWith("GARDEN-HOSE"));
        Assert.Equal("GARDEN-HOSE-25M-", hose.Plu);
        Assert.True(hose.IsFlagged);
        Assert.Contains("GARDEN-HOSE-25M-GREEN", hose.Notes, StringComparison.Ordinal);
        Assert.Contains("HOSE25G", hose.Notes, StringComparison.Ordinal);
        Assert.Null(hose.Barcode);
        Assert.Equal((20m, new DateOnly(2026, 10, 1), 12m), (hose.SalePrice, hose.SaleStart!.Value, hose.OnOrder));
        var juice = await db.Items.SingleAsync(i => i.LocationId == nth && i.Plu == "JUICE-1L");
        Assert.Equal((true, 2, 1, "JUICE-1L-CASE", 12m, "DEMO-0001"), (juice.BonusPricing, juice.BonusBuyQuantity, juice.BonusGetQuantity, juice.ParentCode, juice.UnitsPerCase, juice.SubstituteCode));
        var shirt = await db.Items.Include(i => i.Variants).Include(i => i.MatrixColumns).SingleAsync(i => i.Plu == "TSHIRT-CREW");
        Assert.Equal(ItemType.Matrix, shirt.ItemType);
        Assert.Equal<string>(["Colour", "Size"], shirt.MatrixColumns.OrderBy(c => c.Ordinal).Select(c => c.Heading));
        Assert.Equal(4, shirt.Variants.Count);
        Assert.Equal(11m, shirt.OnHand);   // 12 in Retail25, one on an open layaway
        Assert.Equal(2m, shirt.Variants.Single(v => v.Code == "TSC-RED-L").OnHand);
        Assert.Equal("0123456789012", shirt.Variants.Single(v => v.Value1 == "Red" && v.Value2 == "M").Code);
        Assert.True(await db.ItemPictures.AnyAsync(p => p.ItemId == shirt.Id));
        var basket = await db.Items.Include(i => i.KitComponents).SingleAsync(i => i.Plu == "GIFTBASKET");
        Assert.Equal(2, basket.KitComponents.Count(k => k.ComponentItemId != null));
        Assert.True((await db.Items.SingleAsync(i => i.Plu == "OLD-STOCK")).IsDeleted);
        var drill = await db.Items.SingleAsync(i => i.Plu == "SER-DRILL");
        Assert.Equal(5, await db.ItemSerials.CountAsync(s => s.ItemId == drill.Id));
        Assert.Equal(4, await db.ItemTags.CountAsync(t => t.ItemId == drill.Id));
        Assert.Equal(ItemTagStatus.Sold, (await db.ItemTags.SingleAsync(t => t.Epc == "3076452148003E8000000005")).Status);
        Assert.Equal(1156, await db.ItemTags.CountAsync());

        // Stock: every item's count is the sum of its movements; layaway goods are out of stock.
        foreach (var (location, units) in new[] { (tst, 5003m), (nth, 124m) })
        {
            var onHand = await db.Items.Where(i => i.LocationId == location).Select(i => i.OnHand).ToListAsync();
            Assert.Equal(units, onHand.Sum());
            var movements = await db.StockMovements.Where(m => db.Items.Any(i => i.Id == m.ItemId && i.LocationId == location)).Select(m => m.Quantity).ToListAsync();
            Assert.Equal(units, movements.Sum());
        }
        Assert.Equal(3, await db.StockMovements.CountAsync(m => m.Kind == StockMovementKind.Sale));

        // Clients: numbers that clash between the two locations get the location code; balances and points.
        var clients = await db.Clients.ToDictionaryAsync(c => c.Number!);
        Assert.Equal(6, clients.Count);
        Assert.Equal("NTH-7001", clients["NTH-1001"].AccountNumber);
        Assert.Equal(("7001", 2000m, 84.40m), (clients["1002"].AccountNumber, clients["1002"].CreditLimit, clients["1002"].Balance));
        Assert.Equal(35.93m, clients["1001"].Balance);   // the open layaway
        Assert.Equal((3, true, 5m, 640m), (clients["1003"].PriceLevel, clients["1003"].ExemptTax2, clients["1003"].UsualDiscountPercent, clients["1003"].Points));
        Assert.Equal(120m, clients["1001"].Points);
        Assert.True(clients["1004"].IsDeleted);
        Assert.Equal(760m, (await db.LoyaltyTransactions.Select(t => t.Points).ToListAsync()).Sum());
        Assert.All(await db.LoyaltyTransactions.ToListAsync(), t => Assert.Equal(LoyaltyTransactionKind.Opening, t.Kind));

        // Users and roles.
        var users = await db.UserAccounts.Include(u => u.Role).ThenInclude(r => r!.Permissions).ToDictionaryAsync(u => u.UserName);
        Assert.Equal(5, users.Count);
        Assert.Equal((Permissions.CashierRole, "C01", stores[TstCode].Id), (users["cashier1@corner.test"].Role!.Name, users["cashier1@corner.test"].StaffCode, users["cashier1@corner.test"].StoreId!.Value));
        Assert.True(SetupService.IsIdentityHash(users["cashier1@corner.test"].PasswordHash!));
        // PINs come over with their staff codes as Retail25's Argon2id hashes; a damaged one does not.
        Assert.True(SetupService.IsRetail25PinHash(users["cashier1@corner.test"].PinHash!));
        Assert.Equal("K01", users["clerk.north@corner.test"].StaffCode);
        Assert.True(SetupService.IsRetail25PinHash(users["clerk.north@corner.test"].PinHash!));
        Assert.Equal(("X99", (string?)null), (users["former@corner.test"].StaffCode, users["former@corner.test"].PinHash));
        Assert.Null(users["manager1@corner.test"].PinHash);
        var manager = users["manager1@corner.test"].Role!;
        Assert.Equal(("Store manager", false, 3), (manager.Name, manager.IsSystem, manager.Level));
        Assert.Contains(manager.Permissions, p => p.Permission == Permissions.PosVoidSale);
        Assert.Equal(Permissions.ClerkRole, users["clerk.north@corner.test"].Role!.Name);
        Assert.Equal(Permissions.AdministratorRole, users["admin@sma.rma.com"].Role!.Name);
        Assert.False(users["former@corner.test"].IsActive);
        Assert.Equal(7, await db.Roles.CountAsync(r => r.IsSystem));

        // Sales: the reversal and the practice sale stay behind; the void is on the sale itself.
        Assert.Equal(10, await db.Sales.CountAsync());
        var sales = await db.Sales.Include(s => s.Lines).Include(s => s.Payments).Where(s => s.StoreId == stores[TstCode].Id).ToDictionaryAsync(s => s.InvoiceNumber);
        Assert.Equal<long>([1, 2, 3, 4, 6, 7, 8, 9], sales.Keys.Order());
        Assert.Equal((SaleStatus.Voided, "manager1@corner.test"), (sales[4].Status, sales[4].VoidedBy));
        Assert.Equal((71.50m, -0.01m, "Cash", 8.50m), (sales[1].Total, sales[1].Rounding, sales[1].Payments.Single().TenderType, sales[1].Payments.Single().Change));
        Assert.Equal(new DateTime(2026, 10, 22, 2, 30, 0), sales[6].SoldAt);
        Assert.Equal(("USD", 0.73m, 28.60m), (sales[6].Payments.Single().Currency, sales[6].Payments.Single().ExchangeRate, sales[6].Payments.Single().ForeignAmount));
        Assert.Equal((SaleLineKind.Return, -1m, -23.95m), (sales[7].Lines.Single(l => l.LineNumber == 1).Kind, sales[7].Lines.Single(l => l.LineNumber == 1).Quantity, sales[7].Lines.Single(l => l.LineNumber == 1).LineTotal));
        Assert.Contains(sales[8].Lines, l => l.Description == "Service fee" && l.LineTotal == 1.00m);
        Assert.Equal(13.25m, sales[8].Total);
        Assert.Equal("GC10000002", sales[9].Lines.Single().GiftCardNumber);
        Assert.Equal("GC10000001", sales[2].Payments.Single(p => p.TenderType == "Gift card").Reference);
        Assert.Equal(2.82m, sales[2].SubtotalDiscount);
        Assert.Equal(0.96m, sales[1].Lines.Single(l => l.LineNumber == 1).Commission);
        Assert.True(await db.SaleLineTags.AnyAsync(t => t.Epc == "3076452148003E8000000005"));

        // Invoices, layaways, quotes.
        var invoices = await db.Invoices.Include(i => i.Payments).Include(i => i.Lines).ToListAsync();
        Assert.Equal(2, invoices.Count(i => i.Kind == InvoiceKind.Invoice));
        var open = invoices.Single(i => i.Kind == InvoiceKind.Invoice && i.Balance != 0);
        Assert.Equal((134.40m, 84.40m, 1, "Cheque", 1), (open.Total, open.Balance, open.Payments.Count, open.Payments.Single().TenderType, open.Lines.Count));
        Assert.Equal(sales[3].Id, open.SaleId);
        Assert.Equal<decimal>([35.93m, 14.99m], invoices.Where(i => i.Kind == InvoiceKind.Layaway).Select(i => i.Balance).OrderDescending());
        Assert.Equal(80m, invoices.Single(i => i.Kind == InvoiceKind.Quote).Total);

        // Gift cards: one opening transaction for what is left on each; a certificate's clashing number is told apart.
        var cards = await db.GiftCards.Include(c => c.Transactions).ToDictionaryAsync(c => c.Number);
        Assert.Equal(5, cards.Count);
        Assert.Equal(23.75m, cards["GC10000001"].Transactions.Sum(t => t.Amount));
        Assert.Equal((GiftCardKind.Certificate, 10m), (cards["GC-GC10000003"].Kind, cards["GC-GC10000003"].Transactions.Sum(t => t.Amount)));
        Assert.Empty(cards["GC10000003"].Transactions);
        Assert.False(cards["GC10000003"].IsActive);
        Assert.Equal(103.75m, (await db.GiftCardTransactions.Select(t => t.Amount).ToListAsync()).Sum());

        // Open documents, commission rules and the time clock.
        Assert.Equal(3, await db.PurchaseOrders.CountAsync());
        Assert.Equal(8m, (await db.PurchaseOrderLines.SingleAsync(l => l.Plu == "GARDEN-HOSE-25M-")).QuantityReceived);
        Assert.Equal(2m, (await db.CustomerOrders.SingleAsync()).Quantity);
        var transfer = await db.StockTransfers.Include(t => t.Lines).SingleAsync();
        Assert.Equal((StockTransferStatus.Shipped, 2, stores[NthCode].Id), (transfer.Status, transfer.Lines.Count, transfer.ToStoreId));
        var rules = await db.CommissionRules.ToListAsync();
        Assert.Equal(3, rules.Count);
        Assert.Contains(rules, r => r.Scope == CommissionScope.Item && r.Basis == CommissionBasis.AmountPerUnit && r.Rate == 1.5m);
        Assert.Equal(5, await db.TimeClockEntries.CountAsync());
        Assert.Equal(1, await db.TimeClockEntries.CountAsync(e => e.ClockOut == null));

        // The run is in the audit log once; the rows it wrote are not, one by one.
        Assert.Equal(AuditActions.Import, (await db.AuditEntries.SingleAsync()).Action);
        Assert.Equal("Succeeded", (await db.ImportBatches.SingleAsync()).Status);

        // What has no place here is in the report.
        Assert.Contains(report.NotCarriedOver, n => n.What == "drawer_sessions" && n.Rows == 1);
        Assert.Contains(report.NotCarriedOver, n => n.What.StartsWith("Staff PINs that cannot be checked", StringComparison.Ordinal) && n.Rows == 1);
        Assert.Contains(report.Checks, c => c.Name == "Staff PINs" && c.Source == 2 && c.Target == 2);
        Assert.Contains(report.Areas, a => a.Area == "Sales" && a.Skipped.ContainsKey("practice sale (training)"));
    }

    /// <summary>Row counts and the latest change time of every table the cutover writes (not the audit log or import batches).</summary>
    private static async Task<Dictionary<string, (int Count, DateTime? Latest)>> SnapshotAsync(DatabaseConnection target)
    {
        await using var db = Open(target);
        var shot = new Dictionary<string, (int, DateTime?)>();
        async Task Take<T>(DbSet<T> set) where T : class, ITracked =>
            shot[typeof(T).Name] = (await set.CountAsync(), await set.MaxAsync(e => (DateTime?)EF.Property<DateTime>(e, nameof(ITracked.UpdatedAtUtc))));
        await Take(db.Stores); await Take(db.Locations); await Take(db.Stations); await Take(db.StoreSettings); await Take(db.StoreSettingExtras);
        await Take(db.StoreImages); await Take(db.TenderTypes); await Take(db.FxRates); await Take(db.Departments); await Take(db.Categories);
        await Take(db.Roles); await Take(db.RoleGrants); await Take(db.UserAccounts); await Take(db.Suppliers); await Take(db.Items);
        await Take(db.ItemSuppliers); await Take(db.ItemMatrixColumns); await Take(db.ItemVariants); await Take(db.KitComponents);
        await Take(db.ItemSerials); await Take(db.ItemTags); await Take(db.ItemPictures); await Take(db.Clients); await Take(db.Sales);
        await Take(db.SaleLines); await Take(db.SaleLineTags); await Take(db.SalePayments); await Take(db.Invoices); await Take(db.InvoiceLines);
        await Take(db.InvoicePayments); await Take(db.StockMovements); await Take(db.GiftCards); await Take(db.GiftCardTransactions);
        await Take(db.LoyaltyTransactions); await Take(db.PurchaseOrders); await Take(db.PurchaseOrderLines); await Take(db.CustomerOrders);
        await Take(db.StockTransfers); await Take(db.StockTransferLines); await Take(db.CommissionRules); await Take(db.TimeClockEntries);
        return shot;
    }

    [SkippableFact]
    public async Task A_second_run_changes_nothing()
    {
        Skip.IfNot(source.Available, source.UnavailableReason);
        var target = targets.Create();
        AllChecksMatch(await RunAsync(target));
        var before = await SnapshotAsync(target);

        var again = await RunAsync(target);
        AllChecksMatch(again);
        Assert.Equal((0, 0, 0), (again.Added, again.Changed, again.Removed));
        Assert.Equal(before, await SnapshotAsync(target));
        await using var db = Open(target);
        Assert.Equal(2, await db.AuditEntries.CountAsync(a => a.Action == AuditActions.Import));
        Assert.Equal(2, await db.ImportBatches.CountAsync());
    }

    /// <summary>Changes made in Retail25 between two runs, and how to undo them (the source is shared by the tests).</summary>
    private const string Change = """
        UPDATE products SET regular_price = 25.95 WHERE id = 1;
        UPDATE gift_cards SET remaining_value = 20.00 WHERE id = 1;
        DELETE FROM product_suppliers WHERE id = 2;
        SET IDENTITY_INSERT customers ON;
        INSERT customers (id, location_id, customer_number, first_name, last_name, is_deleted, created_at, row_version)
        VALUES (7, 1, 1007, 'Gita', 'New', 0, '2026-10-26T12:00:00+00:00', 0);
        SET IDENTITY_INSERT customers OFF;
        """;

    private const string Undo = """
        UPDATE products SET regular_price = 23.95 WHERE id = 1;
        UPDATE gift_cards SET remaining_value = 23.75 WHERE id = 1;
        SET IDENTITY_INSERT product_suppliers ON;
        INSERT product_suppliers (id, product_id, supplier_id, rank, cost, reorder_number, case_qty, minimum_order_qty, created_at, row_version)
        VALUES (2, 201, 1, 2, 18.200, 'PF-0001', 12, 12, '2026-01-10T12:00:00+00:00', 0);
        SET IDENTITY_INSERT product_suppliers OFF;
        DELETE FROM customers WHERE id = 7;
        """;

    [SkippableFact]
    public async Task A_run_after_Retail25_changed_updates_what_changed()
    {
        Skip.IfNot(source.Available, source.UnavailableReason);
        var target = targets.Create();
        AllChecksMatch(await RunAsync(target));
        await source.ExecuteAsync(Change);
        try
        {
            var report = await RunAsync(target);
            AllChecksMatch(report);
            int Changed(string area) => report.Areas.Single(a => a.Area == area).Changed;
            Assert.Equal(1, report.Areas.Single(a => a.Area == "Clients").Added);
            Assert.Equal(1, report.Areas.Single(a => a.Area == "Item suppliers").Removed);
            Assert.Equal((1, 1, 1), (Changed("Items"), Changed("Gift card balances"), Changed("Transfer lines")));
            // Nothing else: the new client, the price (also on the transfer in transit), the card, the supplier link.
            Assert.Equal((1, 3, 1), (report.Added, report.Changed, report.Removed));

            await using var db = Open(target);
            var tst = await LocationAsync(db, TstCode);
            Assert.Equal(25.95m, (await db.Items.SingleAsync(i => i.LocationId == tst && i.Plu == "DEMO-0001")).Price1);
            Assert.True(await db.Clients.AnyAsync(c => c.Number == "1007"));
            Assert.Equal(20m, (await db.GiftCardTransactions.Where(t => db.GiftCards.Any(c => c.Id == t.GiftCardId && c.Number == "GC10000001")).Select(t => t.Amount).ToListAsync()).Sum());
            Assert.Single(await db.ItemSuppliers.Where(s => db.Items.Any(i => i.Id == s.ItemId && i.Plu == "DEMO-0001" && i.LocationId != tst)).ToListAsync());
        }
        finally
        {
            await source.ExecuteAsync(Undo);
        }
    }

    [SkippableFact]
    public async Task A_dry_run_writes_nothing()
    {
        Skip.IfNot(source.Available, source.UnavailableReason);

        // Into a database that does not exist yet: it is not created, and the report is made all the same.
        var absent = targets.Create();
        var report = await RunAsync(absent, dryRun: true);
        AllChecksMatch(report);
        Assert.True(report.DryRun);
        Assert.NotNull(report.StagedIn);
        Assert.False(File.Exists(report.StagedIn));
        Assert.True(report.Added > 3000);
        Assert.False(await targets.ExistsAsync(absent));

        // Into a database that has data: Retail25's changes are found, and none is written.
        var target = targets.Create();
        AllChecksMatch(await RunAsync(target));
        var before = await SnapshotAsync(target);
        await source.ExecuteAsync(Change);
        try
        {
            var dry = await RunAsync(target, dryRun: true);
            AllChecksMatch(dry);
            Assert.Null(dry.StagedIn);
            Assert.Equal(1, dry.Areas.Single(a => a.Area == "Clients").Added);
            Assert.Equal(1, dry.Areas.Single(a => a.Area == "Items").Changed);
            Assert.Equal(before, await SnapshotAsync(target));
            await using var db = Open(target);
            Assert.Equal(1, await db.AuditEntries.CountAsync());
            Assert.Equal(1, await db.ImportBatches.CountAsync());
            Assert.False(await db.Clients.AnyAsync(c => c.Number == "1007"));
        }
        finally
        {
            await source.ExecuteAsync(Undo);
        }
    }

    [SkippableFact]
    public async Task A_carried_over_user_signs_in_with_the_old_password_and_is_rehashed()
    {
        Skip.IfNot(source.Available, source.UnavailableReason);
        var target = targets.Create();
        AllChecksMatch(await RunAsync(target));
        var setup = new SetupService(new DataContext(target));

        Assert.Null((await setup.SignInAsync("manager1@corner.test", "Cashier-2026")).User);
        var signedIn = await setup.SignInAsync("manager1@corner.test", "Manager-2026");
        Assert.NotNull(signedIn.User);
        Assert.Equal("Store manager", signedIn.User.Role!.Name);
        Assert.True(SetupService.PermissionsOf(signedIn.User).Contains(Permissions.PosVoidSale));

        async Task<string> HashAsync()
        {
            await using var db = Open(target);
            return (await db.UserAccounts.SingleAsync(u => u.UserName == "manager1@corner.test")).PasswordHash!;
        }
        var upgraded = await HashAsync();
        Assert.StartsWith("pbkdf2$", upgraded, StringComparison.Ordinal);

        // Another run keeps the password set here, and it still signs in.
        var again = await RunAsync(target);
        AllChecksMatch(again);
        Assert.Equal(upgraded, await HashAsync());
        Assert.NotNull((await setup.SignInAsync("manager1@corner.test", "Manager-2026")).User);
        Assert.NotNull((await setup.SignInAsync("clerk.north@corner.test", "Clerk-2026")).User);
    }

    [SkippableFact]
    public async Task A_carried_over_user_signs_in_with_the_old_PIN_and_is_rehashed()
    {
        Skip.IfNot(source.Available, source.UnavailableReason);
        var target = targets.Create();
        AllChecksMatch(await RunAsync(target));
        var setup = new SetupService(new DataContext(target));
        async Task<(string? Code, string? Pin)> PinAsync(string userName)
        {
            await using var db = Open(target);
            var user = await db.UserAccounts.SingleAsync(u => u.UserName == userName);
            return (user.StaffCode, user.PinHash);
        }
        var retail25 = await PinAsync("clerk.north@corner.test");

        Assert.Null((await setup.SignInWithPinAsync("K01", "1234")).User);
        Assert.Equal(retail25, await PinAsync("clerk.north@corner.test"));
        var signedIn = await setup.SignInWithPinAsync("K01", "1357");
        Assert.Equal("clerk.north@corner.test", signedIn.User?.UserName);
        var upgraded = await PinAsync("clerk.north@corner.test");
        Assert.Equal("K01", upgraded.Code);
        Assert.StartsWith("pbkdf2$", upgraded.Pin, StringComparison.Ordinal);

        // An approval by PIN proves it too (a cashier may not approve, but the PIN was right).
        var (approver, _) = await setup.ApproveAsync("C01", "2468", pin: true, Permissions.PosVoidSale, "Void sale");
        Assert.Null(approver);
        Assert.StartsWith("pbkdf2$", (await PinAsync("cashier1@corner.test")).Pin, StringComparison.Ordinal);

        // Another run keeps the PINs stored here, and they still sign in.
        AllChecksMatch(await RunAsync(target));
        Assert.Equal(upgraded, await PinAsync("clerk.north@corner.test"));
        Assert.NotNull((await setup.SignInWithPinAsync("K01", "1357")).User);
        Assert.NotNull((await setup.SignInWithPinAsync("C01", "2468")).User);
    }

    [SkippableFact]
    public async Task One_store_can_be_carried_over_on_its_own()
    {
        Skip.IfNot(source.Available, source.UnavailableReason);
        var target = targets.Create();
        var report = await RunAsync(target, storeCode: "nth");
        AllChecksMatch(report);
        await using var db = Open(target);
        Assert.Equal(NthCode, (await db.Stores.SingleAsync()).Code);
        Assert.Equal(1, (await db.Stations.SingleAsync()).Number);
        Assert.Equal(9, await db.Items.CountAsync());
        Assert.Equal(2, await db.Sales.CountAsync());
        Assert.Equal<string>(["clerk.north@corner.test"], await db.UserAccounts.Select(u => u.UserName).ToListAsync());
        Assert.Equal<string>(["1001", "1002"], (await db.Clients.Select(c => c.Number!).ToListAsync()).Order());
        Assert.Contains(report.Areas.Single(a => a.Area == "Items").Skipped, s => s.Key == "store not carried over" && s.Value == 137);
    }
}

[Collection(Retail25SourceGroup.Name)]
public sealed class CutoverTestsOnSqlite(Retail25SourceFixture source, SqliteTargets targets) : CutoverTests(source, targets), IClassFixture<SqliteTargets>;

[Collection(Retail25SourceGroup.Name)]
public sealed class CutoverTestsOnSqlServer(Retail25SourceFixture source, SqlServerTargets targets) : CutoverTests(source, targets), IClassFixture<SqlServerTargets>;
