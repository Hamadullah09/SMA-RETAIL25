using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Data;

namespace RetailPlus.Data.Tests;

public sealed class SchemaTests(LocalDbFixture db) : IClassFixture<LocalDbFixture>
{
    private void RequireDatabase() => Skip.IfNot(db.Available, db.UnavailableReason);

    private async Task<Location> AddLocationAsync(RetailPlusDbContext context)
    {
        var location = new Location { Code = db.NextLocationCode(), Name = "Test" };
        context.Locations.Add(location);
        await context.SaveChangesAsync();
        return location;
    }

    [SkippableFact]
    public async Task Migrations_are_complete_and_match_the_model()
    {
        RequireDatabase();
        await using var context = db.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges(), "The model has changes without a migration.");
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
    }

    [SkippableFact]
    public async Task Database_uses_the_case_insensitive_collation()
    {
        RequireDatabase();
        await using var context = db.CreateContext();
        var collation = await context.Database
            .SqlQueryRaw<string>("SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation')) AS [Value]")
            .SingleAsync();
        Assert.Equal(RetailPlusDb.Collation, collation);
    }

    [SkippableFact]
    public async Task Item_with_all_child_lists_round_trips_with_full_precision()
    {
        RequireDatabase();
        int itemId;
        await using (var context = db.CreateContext())
        {
            var location = await AddLocationAsync(context);
            var supplier = new Supplier { Company = "ACME OUTDOOR SUPPLY", Number = "S-" + location.Code };
            var item = new Item
            {
                LocationId = location.Id,
                Plu = "T100",
                Name = "TEST SHIRT",
                ItemType = ItemType.Matrix,
                Supplier = supplier,
                OnHand = 73.125m,
                LastCost = 15.375m,
                Price1 = 30.00m,
                MarginPercent = 48.750m,
                SaleStart = new DateOnly(2026, 11, 1),
                Notes = "Soft cotton shirt pique.\r\n",
                Suppliers = { new ItemSupplier { Rank = 1, Supplier = supplier, SupplierName = "ACME OUTDOOR SUPPLY", SupplierStockCode = "AX100", Cost = 45.000m, CaseQuantity = 15m } },
                MatrixColumns = { new ItemMatrixColumn { Ordinal = 1, Heading = "Color" }, new ItemMatrixColumn { Ordinal = 2, Heading = "Size" } },
                Variants = { new ItemVariant { Ordinal = 1, Value1 = "Blue", Value2 = "L", OnHand = 15m } },
                Serials = { new ItemSerial { SerialNumber = "SN-0001-A", Status = "IN STOCK", StatusDate = new DateOnly(2012, 2, 28) } },
                PeriodSales = { new ItemPeriodSales { Year = 2026, Kind = SalesPeriodKind.Month, Period = 10, Units = 2.5m, Gross = 75.00m } },
            };
            context.Items.Add(item);
            await context.SaveChangesAsync();
            itemId = item.Id;
        }

        await using (var context = db.CreateContext())
        {
            var item = await context.Items
                .Include(i => i.Suppliers).Include(i => i.MatrixColumns).Include(i => i.Variants)
                .Include(i => i.Serials).Include(i => i.PeriodSales).Include(i => i.Supplier)
                .SingleAsync(i => i.Id == itemId);
            Assert.Equal(ItemType.Matrix, item.ItemType);
            Assert.Equal(73.125m, item.OnHand);
            Assert.Equal(15.375m, item.LastCost);
            Assert.Equal(48.750m, item.MarginPercent);
            Assert.Equal(new DateOnly(2026, 11, 1), item.SaleStart);
            Assert.Equal("Soft cotton shirt pique.\r\n", item.Notes);
            Assert.Equal("ACME OUTDOOR SUPPLY", item.Supplier!.Company);
            Assert.Equal("AX100", Assert.Single(item.Suppliers).SupplierStockCode);
            Assert.Equal(2, item.MatrixColumns.Count);
            Assert.Equal(15m, Assert.Single(item.Variants).OnHand);
            Assert.Equal(new DateOnly(2012, 2, 28), Assert.Single(item.Serials).StatusDate);
            Assert.Equal(2.5m, Assert.Single(item.PeriodSales).Units);
            Assert.NotEmpty(item.RowVersion);
        }
    }

    [SkippableFact]
    public async Task Stock_code_lookups_ignore_case_like_the_original_UPPER_indexes()
    {
        RequireDatabase();
        await using var context = db.CreateContext();
        var location = await AddLocationAsync(context);
        context.Items.Add(new Item { LocationId = location.Id, Plu = "SPK001", Name = "Test item" });
        await context.SaveChangesAsync();

        Assert.True(await context.Items.AnyAsync(i => i.LocationId == location.Id && i.Plu == "spk001"));
        Assert.True(await context.Items.AnyAsync(i => i.LocationId == location.Id && i.Name.StartsWith("TEST I")));
    }

    [SkippableFact]
    public async Task Stock_codes_are_unique_among_live_items_of_a_location()
    {
        RequireDatabase();
        await using var context = db.CreateContext();
        var first = await AddLocationAsync(context);
        var second = await AddLocationAsync(context);
        context.Items.Add(new Item { LocationId = first.Id, Plu = "T400", Name = "TEST BATTERY" });
        context.Items.Add(new Item { LocationId = second.Id, Plu = "T400", Name = "Same code, other location" });
        await context.SaveChangesAsync();

        context.Items.Add(new Item { LocationId = first.Id, Plu = "T400", Name = "Duplicate" });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        // After the first item is trashed, its code can be used again (Undelete then needs a check).
        var original = await context.Items.SingleAsync(i => i.LocationId == first.Id && i.Plu == "T400");
        original.IsDeleted = true;
        original.DeletedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        context.Items.Add(new Item { LocationId = first.Id, Plu = "T400", Name = "Replacement" });
        await context.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task Customer_numbers_are_unique_among_live_clients()
    {
        RequireDatabase();
        var number = "C" + Guid.NewGuid().ToString("N")[..8];
        await using var context = db.CreateContext();
        context.Clients.Add(new Client { LastName = "Spike", Number = number });
        await context.SaveChangesAsync();
        context.Clients.Add(new Client { LastName = "Other", Number = number });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        // Clients without a number don't collide.
        context.Clients.Add(new Client { LastName = "No number 1" });
        context.Clients.Add(new Client { LastName = "No number 2" });
        await context.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task Second_save_of_a_record_edited_elsewhere_is_rejected()
    {
        RequireDatabase();
        int itemId;
        await using (var setup = db.CreateContext())
        {
            var location = await AddLocationAsync(setup);
            var item = new Item { LocationId = location.Id, Plu = "T500", Name = "TEST ROPE", Price1 = 1m };
            setup.Items.Add(item);
            await setup.SaveChangesAsync();
            itemId = item.Id;
        }

        await using var tillA = db.CreateContext();
        await using var tillB = db.CreateContext();
        var a = await tillA.Items.SingleAsync(i => i.Id == itemId);
        var b = await tillB.Items.SingleAsync(i => i.Id == itemId);
        a.Price1 = 2m;
        await tillA.SaveChangesAsync();
        b.Price1 = 3m;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => tillB.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Stock_changes_applied_as_atomic_updates_are_not_lost()
    {
        RequireDatabase();
        int itemId;
        await using (var setup = db.CreateContext())
        {
            var location = await AddLocationAsync(setup);
            var item = new Item { LocationId = location.Id, Plu = "T600", Name = "TEST BAR", OnHand = 100m };
            setup.Items.Add(item);
            await setup.SaveChangesAsync();
            itemId = item.Id;
        }

        // Ten "tills" selling at the same moment; each sale decrements stock in the database, not in memory.
        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var till = db.CreateContext();
            await till.Items.Where(i => i.Id == itemId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.OnHand, i => i.OnHand - 1.5m));
        }));

        await using var check = db.CreateContext();
        Assert.Equal(85m, (await check.Items.SingleAsync(i => i.Id == itemId)).OnHand);
    }

    [SkippableFact]
    public async Task Sequences_hand_out_increasing_numbers()
    {
        RequireDatabase();
        await using var context = db.CreateContext();
        var first = await context.NextNumberAsync(RetailPlusDbContext.Sequences.InvoiceNumbers);
        var second = await context.NextNumberAsync(RetailPlusDbContext.Sequences.InvoiceNumbers);
        Assert.Equal(first + 1, second);
        await Assert.ThrowsAsync<ArgumentException>(() => context.NextNumberAsync("Invoices; DROP TABLE Items"));
    }

    [SkippableFact]
    public async Task Store_settings_is_a_single_row()
    {
        RequireDatabase();
        await using var context = db.CreateContext();
        context.StoreSettings.Add(new StoreSettings { Id = 2, CompanyName = "Not allowed" });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task There_is_one_store_wide_text_of_each_kind()
    {
        RequireDatabase();
        await using var context = db.CreateContext();
        context.DocumentTexts.Add(new DocumentText { Kind = DocumentTextKind.PurchaseOrderHeader, Text = "Deliver to rear door." });
        await context.SaveChangesAsync();
        context.DocumentTexts.Add(new DocumentText { Kind = DocumentTextKind.PurchaseOrderHeader, Text = "Second one" });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
