using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Data;
using RetailPlus.Migration.Import;

namespace RetailPlus.Migration.Tests;

/// <summary>
/// A database the importer creates and migrates itself, removed afterwards: a SQLite file in the temp folder (a till), or
/// a database on LocalDB or the server in RETAILPLUS_TEST_SQL (the server). The SQL Server tests skip when no server is
/// reachable.
/// </summary>
internal sealed class ThrowawayDatabase : IDisposable
{
    public ThrowawayDatabase(DatabaseProvider provider)
    {
        var name = "RetailPlus_ImportTest_" + Guid.NewGuid().ToString("N")[..12];
        if (provider == DatabaseProvider.Sqlite)
        {
            Connection = DatabaseConnection.SqliteFile(Path.Combine(Path.GetTempPath(), name + ".db"));
            Available = true;
            return;
        }
        var server = Environment.GetEnvironmentVariable("RETAILPLUS_TEST_SQL");
        Connection = server is { Length: > 0 }
            ? DatabaseConnection.SqlServer(new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString)
            : DatabaseConnection.LocalDb(name);
        try
        {
            using var master = new SqlConnection(new SqlConnectionStringBuilder(Connection.ConnectionString) { InitialCatalog = "master" }.ConnectionString);
            master.Open();
            Available = true;
        }
        catch (SqlException ex)
        {
            UnavailableReason = "No SQL Server for integration tests: " + ex.Message;
        }
    }

    public DatabaseConnection Connection { get; }
    public bool Available { get; }
    public string UnavailableReason { get; } = "";

    public RetailPlusDbContext CreateContext(StationIdentity? station = null) => RetailPlusDb.CreateContext(Connection, station);

    public void Dispose()
    {
        if (!Available) return;
        if (Connection.Provider == DatabaseProvider.Sqlite)
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { Connection.DatabaseName, Connection.DatabaseName + "-wal", Connection.DatabaseName + "-shm" })
                if (File.Exists(file)) File.Delete(file);
            return;
        }
        SqlConnection.ClearAllPools();
        using var db = CreateContext();
        db.Database.EnsureDeleted();
    }
}

/// <summary>The importer on both engines: into a till's SQLite file and into the server's SQL Server database.</summary>
public abstract class ImporterTests(DatabaseProvider provider) : IDisposable
{
    private readonly ThrowawayDatabase _database = new(provider);
    private readonly SyntheticDataRoot _source = new();

    public void Dispose()
    {
        _database.Dispose();
        _source.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<ImportReport> ImportAsync(string folder, bool overwrite = false) =>
        new DbfImporter(new ImportOptions { SourceFolder = folder, Database = _database.Connection, Overwrite = overwrite }).RunAsync();

    [SkippableFact]
    public async Task Synthetic_folder_imports_with_matching_control_totals()
    {
        Skip.IfNot(_database.Available, _database.UnavailableReason);
        var report = await ImportAsync(_source.Folder);

        Assert.True(report.Succeeded, report.Error);
        Assert.True(report.VerificationPassed, string.Join("; ", report.Totals.Where(t => !t.Matches)));
        await using var db = _database.CreateContext();

        var store = await db.Stores.SingleAsync();
        var location = await db.Locations.SingleAsync();
        Assert.Equal("TST", location.Code);
        Assert.Equal(store.Id, location.StoreId);
        var items = await db.Items.Include(i => i.Variants).Include(i => i.Suppliers).Include(i => i.PeriodSales)
            .Include(i => i.Serials).Include(i => i.KitComponents).ToListAsync();
        Assert.Equal(7, items.Count);
        Assert.Equal(15m, items.Where(i => !i.IsDeleted).Sum(i => i.OnHand));
        Assert.True(items.Single(i => i.Plu == "D1").IsDeleted);

        // The second item with stock code A1 keeps its data under a new, flagged code.
        var copy = items.Single(i => i.Name == "WIDGET COPY");
        Assert.Equal("A1#2", copy.Plu);
        Assert.True(copy.IsFlagged);

        var widget = items.Single(i => i.Plu == "A1");
        Assert.Equal("widget.jpg", widget.PhotoPath);
        Assert.Equal(["ACME SUPPLY", "OTHER SOURCE"], widget.Suppliers.OrderBy(s => s.Rank).Select(s => s.SupplierName));
        Assert.NotNull(widget.Suppliers.Single(s => s.Rank == 1).SupplierId);
        Assert.Null(widget.Suppliers.Single(s => s.Rank == 2).SupplierId);
        Assert.Contains(widget.PeriodSales, p => p is { Kind: SalesPeriodKind.Month, Period: 1, Units: 3m, Gross: 29.97m });
        Assert.Contains(widget.PeriodSales, p => p is { Kind: SalesPeriodKind.Week, Period: 3, Units: 2m });
        Assert.Contains(widget.PeriodSales, p => p is { Kind: SalesPeriodKind.Month, Period: 12, Units: 4m } && p.Year == widget.PeriodSales.Max(x => x.Year) - 1);

        var shirt = items.Single(i => i.Plu == "A2");
        Assert.Equal(ItemType.Matrix, shirt.ItemType);
        Assert.Equal(7m, shirt.Variants.Sum(v => v.OnHand));
        Assert.Equal(["Colour", "Size"], await db.ItemMatrixColumns.Where(c => c.ItemId == shirt.Id).OrderBy(c => c.Ordinal).Select(c => c.Heading).ToListAsync());

        var kit = items.Single(i => i.Plu == "K1");
        Assert.Equal(widget.Id, kit.KitComponents.Single(k => k.ComponentPlu == "A1").ComponentItemId);
        Assert.Null(kit.KitComponents.Single(k => k.ComponentPlu == "ZZ9").ComponentItemId);
        Assert.Equal(new DateOnly(2012, 2, 28), items.Single(i => i.Plu == "S1").Serials.Single().StatusDate);
        Assert.Equal(ItemType.Standard, items.Single(i => i.Plu == "X1").ItemType);

        // Lists completed from the inventory.
        Assert.Equal(["APPAREL", "PARTS", "TOOLS"], await db.Departments.OrderBy(d => d.Name).Select(d => d.Name).ToListAsync());
        Assert.Equal(["TOPS"], await db.Categories.Select(c => c.Name).ToListAsync());
        Assert.Equal(["ACME SUPPLY", "NEW SUPPLIER"], await db.Suppliers.OrderBy(s => s.Company).Select(s => s.Company!).ToListAsync());

        // In the order of CLIENTS.DBF.
        var record = await db.ImportSourceRows.Where(r => r.Entity == nameof(Client) && r.EntityId != null).ToDictionaryAsync(r => r.EntityId!.Value, r => r.SourceRecordNumber);
        var clients = (await db.Clients.ToListAsync()).OrderBy(c => record[c.Id]).ToList();
        Assert.Equal(3, clients.Count);
        Assert.Equal(15.75m, clients.Where(c => !c.IsDeleted).Sum(c => c.Balance));
        Assert.Null(clients[2].Number);              // duplicate CUSTNO cleared ...
        Assert.Contains("21", clients[2].Notes);     // ... and kept in the notes
        Assert.Equal(4, clients[2].PriceLevel);      // 7 clamped to the highest level
        Assert.True(clients[1].IsDeleted);

        var invoices = await db.Invoices.OrderBy(i => i.InvoiceNumber).ToListAsync();
        Assert.Equal([501L, 502L, 900_000_001L], invoices.Select(i => i.InvoiceNumber));
        Assert.Equal(InvoiceKind.Quote, invoices[1].Kind);
        Assert.Equal(clients[0].Id, invoices[0].ClientId);
        Assert.Equal("line detail", invoices[0].Notes);

        var sales = await db.Sales.Include(s => s.Lines).Include(s => s.Payments).OrderBy(s => s.InvoiceNumber).ToListAsync();
        Assert.Equal(2, sales.Count);
        Assert.Equal(29.99m, sales[0].Subtotal);
        Assert.Equal(31.49m, sales[0].Total);
        Assert.Equal(31.49m, sales[0].Payments.Single().Amount);
        Assert.Equal(SaleLineKind.Return, sales[1].Lines.Single().Kind);
        Assert.Equal(widget.Id, sales[1].Lines.Single().ItemId);

        var session = await db.DrawerSessions.Include(s => s.Tenders).SingleAsync();
        Assert.Equal(30.99m, session.Tenders.Single(t => t.TenderType == "Cash").Amount);
        Assert.Equal(2, session.CustomerCount);

        var order = await db.PurchaseOrders.Include(p => p.Lines).SingleAsync();
        Assert.Equal(8, order.Number);
        Assert.Equal(PurchaseOrderStatus.Posted, order.Status);
        Assert.Equal(12m, order.Lines.Single().CaseQuantity);

        var backorder = await db.CustomerOrders.SingleAsync();
        Assert.Equal(clients[0].Id, backorder.ClientId);
        Assert.Equal(widget.Id, backorder.ItemId);

        var keys = await db.FastKeys.OrderBy(k => k.Panel).ToListAsync();
        Assert.Equal(2, keys.Count);
        Assert.Equal((1, 2), (keys[1].Panel, keys[1].KeyNumber));

        var settings = await db.StoreSettings.SingleAsync();
        Assert.Equal(("TAX A", 5m, 0.05m, 0m), (settings.Tax1Name, settings.Tax1Rate, settings.MinimumTender, settings.GiftCardMaximum));
        Assert.Equal("***", (await db.StoreSettingExtras.SingleAsync(e => e.Name == "CCARDCHRG")).Value);
        Assert.Contains(report.Warnings, w => w.Contains("GIFTCARDMAX", StringComparison.Ordinal));

        Assert.Contains("1 Example Street", (await db.DocumentTexts.SingleAsync(t => t.Kind == DocumentTextKind.InvoiceHeader)).Text);
        Assert.Equal(location.Id, (await db.DocumentTexts.SingleAsync(t => t.Kind == DocumentTextKind.ReturnAddress)).LocationId);
        Assert.Equal(8, await db.TenderTypes.CountAsync());
        Assert.True(await db.Stations.AnyAsync(s => s.Number == 1));

        // Imported documents keep their numbers; new ones come from each station's own range. An imported number that
        // already reaches a station's range is reported.
        await using (var station1 = _database.CreateContext(new StationIdentity(store.Id, 1)))
            Assert.Equal(NumberCounter.FirstNumber(1), await station1.NextNumberAsync(RetailPlusDbContext.Sequences.InvoiceNumbers));
        Assert.Contains(report.Warnings, w => w.Contains("900000001", StringComparison.Ordinal));

        // The stock each live item starts with is its first stock movement.
        Assert.Equal(15m, await db.StockMovements.Where(m => m.Kind == StockMovementKind.Opening).SumAsync(m => m.Quantity));

        // Every source record is traced, and the batch is recorded.
        Assert.Equal(7, await db.ImportSourceRows.CountAsync(r => r.Entity == nameof(Item)));
        Assert.Equal("Succeeded", (await db.ImportBatches.SingleAsync()).Status);
    }

    [SkippableFact]
    public async Task Passwords_are_never_imported()
    {
        Skip.IfNot(_database.Available, _database.UnavailableReason);
        await ImportAsync(_source.Folder);
        await using var db = _database.CreateContext();

        var users = await db.UserAccounts.OrderBy(u => u.UserName).ToListAsync();
        Assert.Equal(["CLERK", "MANAGER"], users.Select(u => u.UserName));
        Assert.All(users, u => Assert.True(u.PasswordHash is null && u.MustChangePassword));
        Assert.Equal([1, 4], users.Select(u => u.AccessLevel));
        foreach (var secret in new[] { SyntheticDataRoot.Password1, SyntheticDataRoot.Password2 })
        {
            Assert.False(await db.ImportSourceRows.AnyAsync(r => r.RawJson.Contains(secret)));
            Assert.False(await db.ImportBatches.AnyAsync(b => b.ReportJson!.Contains(secret)));
        }
        Assert.False(await db.ImportSourceRows.AnyAsync(r => r.RawJson.Contains("P_WORD")));
    }

    [SkippableFact]
    public async Task A_database_with_data_is_only_replaced_with_overwrite()
    {
        Skip.IfNot(_database.Available, _database.UnavailableReason);
        await ImportAsync(_source.Folder);

        await Assert.ThrowsAsync<ImportRefusedException>(() => ImportAsync(_source.Folder));

        var again = await ImportAsync(_source.Folder, overwrite: true);
        Assert.True(again.VerificationPassed);
        await using var db = _database.CreateContext();
        Assert.Equal(7, await db.Items.CountAsync());
        Assert.Equal(3, await db.Clients.CountAsync());
        Assert.Equal(1, await db.ImportBatches.CountAsync());
        Assert.Equal(1, await db.Stores.CountAsync());
    }

    [SkippableFact]
    public async Task A_new_till_database_is_imported_into_its_own_store()
    {
        Skip.IfNot(_database.Available, _database.UnavailableReason);
        // What the app does on its first start: a store with settings, location POS and station 1.
        var data = new Data.Services.DataContext(_database.Connection);
        await data.MigrateAsync();
        var station = await new Data.Services.SetupService(data).SeedAsync(1);

        var report = await ImportAsync(_source.Folder);
        Assert.True(report.VerificationPassed, string.Join("; ", report.Totals.Where(t => !t.Matches)));
        await using var db = _database.CreateContext();
        Assert.Equal(station.StoreId, (await db.Stores.SingleAsync()).Id);
        Assert.Equal(["TST"], await db.Locations.Select(l => l.Code).ToListAsync());
        Assert.Equal(station.StoreId, (await db.StoreSettings.SingleAsync()).StoreId);
    }

    [SkippableFact]
    public async Task Sample_data_folders_import_with_matching_control_totals()
    {
        Skip.IfNot(_database.Available, _database.UnavailableReason);
        var folders = TestPaths.SampleFolders();
        Skip.If(folders.Count == 0, "No Retail Plus sample data (spike-data or RETAILPLUS_DBF_SAMPLES).");
        foreach (var folder in folders)
        {
            var report = await ImportAsync(folder, overwrite: true);
            Assert.True(report.Succeeded, report.Error);
            Assert.True(report.VerificationPassed, folder + ": " + string.Join("; ", report.Totals.Where(t => !t.Matches)));
            await using var db = _database.CreateContext();
            Assert.True(await db.Items.AnyAsync());
            Assert.All(await db.UserAccounts.ToListAsync(), u => Assert.Null(u.PasswordHash));
        }
    }
}

public sealed class ImporterTestsOnSqlite() : ImporterTests(DatabaseProvider.Sqlite);

public sealed class ImporterTestsOnSqlServer() : ImporterTests(DatabaseProvider.SqlServer);
