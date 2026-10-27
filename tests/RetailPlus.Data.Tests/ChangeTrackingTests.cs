using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Integrations;

namespace RetailPlus.Data.Tests;

/// <summary>
/// What the synchronisation relies on: deletions leave tombstones, every change moves the row's change time, edits are
/// told apart from counts the system keeps, and stock is always the sum of the stock movements.
/// </summary>
public abstract class ChangeTrackingTests(DatabaseFixture db)
{
    private static int _station = 300;

    private DataContext Data => db.Data;

    private async Task<(SessionInfo Session, Location Location)> SetUpAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var location = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Test");
        return (new SessionInfo { StationNumber = Interlocked.Increment(ref _station), LocationId = location.Id, LocationCode = location.Code, UserName = "tester", AccessLevel = 4 }, location);
    }

    private async Task<Item> ItemAsync(Guid locationId, string plu, decimal onHand, ItemType type = ItemType.Standard, List<ItemVariant>? variants = null) =>
        await new InventoryService(Data).SaveAsync(new Item
        {
            LocationId = locationId, Plu = plu, Name = "Item " + plu, Price1 = 10m, LastCost = 4m, AverageCost = 4m, OnHand = onHand, ItemType = type,
            Variants = variants ?? [],
        }, null, null, null);

    private async Task<(DateTime Updated, DateTime Edited)> StampsAsync(Guid itemId)
    {
        await using var ctx = db.CreateContext();
        var row = await ctx.Items.Where(i => i.Id == itemId)
            .Select(i => new { i.UpdatedAtUtc, Edited = EF.Property<DateTime>(i, SyncColumns.EditedAtUtc) }).SingleAsync();
        return (row.UpdatedAtUtc, row.Edited);
    }

    private static SaleDraftLine Line(Item item, decimal qty, Guid? variantId = null, SaleLineKind kind = SaleLineKind.Sale) => new()
    {
        ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = qty, UnitPrice = item.Price1,
        UnitCost = item.AverageCost, VariantId = variantId, Kind = kind, Restock = kind == SaleLineKind.Return,
    };

    private static TenderEntry Cash(decimal amount) => new("Cash", TenderKind.Cash, amount, amount);

    [SkippableFact]
    public async Task Deleting_rows_leaves_tombstones_in_the_same_save()
    {
        var (_, location) = await SetUpAsync();
        var inventory = new InventoryService(Data);
        var item = await inventory.SaveAsync(new Item
        {
            LocationId = location.Id, Plu = "TB1", Name = "Tombstoned suppliers",
            Suppliers = [new ItemSupplier { SupplierName = "FIRST" }, new ItemSupplier { SupplierName = "SECOND" }],
        }, null, null, null);
        List<Guid> before;
        await using (var ctx = db.CreateContext()) before = await ctx.ItemSuppliers.Where(s => s.ItemId == item.Id).Select(s => s.Id).ToListAsync();

        // Saving the item again replaces its supplier list: the old rows are deleted.
        var again = (await inventory.GetAsync(item.Id))!;
        again.Suppliers = [new ItemSupplier { SupplierName = "THIRD" }];
        await inventory.SaveAsync(again, null, null, null);

        await new SetupService(Data).SaveUserAsync(new UserAccount { UserName = "gone-" + location.Code, AccessLevel = 1 }, "secret");
        Guid userId;
        await using (var ctx = db.CreateContext()) userId = await ctx.UserAccounts.Where(u => u.UserName == "gone-" + location.Code).Select(u => u.Id).SingleAsync();
        await new SetupService(Data).DeleteUserAsync(userId);

        await using var check = db.CreateContext();
        var stones = await check.Tombstones.Where(t => before.Contains(t.RowId) || t.RowId == userId).ToListAsync();
        Assert.Equal(before.Order(), stones.Where(t => t.EntityName == nameof(ItemSupplier)).Select(t => t.RowId).Order());
        Assert.Null(Assert.Single(stones, t => t.EntityName == nameof(UserAccount)).StoreId);
        Assert.All(stones, t => Assert.InRange(t.DeletedAtUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1)));

        // A store's row records its store, so only that store's tills receive the deletion.
        await new SetupService(Data).SetTextAsync(DocumentTextKind.InvoiceFooter, "Thanks", location.Id);
        Guid textId;
        await using (var ctx = db.CreateContext())
        {
            var text = await ctx.DocumentTexts.SingleAsync(t => t.LocationId == location.Id);
            textId = text.Id;
            ctx.DocumentTexts.Remove(text);
            await ctx.SaveChangesAsync();
        }
        Assert.Equal(db.Station!.StoreId, (await check.Tombstones.SingleAsync(t => t.RowId == textId)).StoreId);
    }

    [SkippableFact]
    public async Task A_sale_moves_the_change_time_but_only_an_edit_moves_the_edit_time()
    {
        var (session, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "ST1", 10);
        var (updated, edited) = await StampsAsync(item.Id);
        Assert.Equal(updated, edited);

        await Task.Delay(20);
        await new SalesService(Data).PostSaleAsync(new SaleDraft { Lines = [Line(item, 1)] }, [Cash(10m)], session);
        var afterSale = await StampsAsync(item.Id);
        Assert.True(afterSale.Updated > updated, "a sale changes the item's stock, so the item is sent again");
        Assert.Equal(edited, afterSale.Edited);

        await Task.Delay(20);
        await new InventoryService(Data).SetFlagAsync([item.Id], true);
        var afterFlag = await StampsAsync(item.Id);
        Assert.True(afterFlag.Updated > afterSale.Updated);
        Assert.Equal(edited, afterFlag.Edited);

        await Task.Delay(20);
        await new InventoryService(Data).DeleteAsync(item.Id);
        var afterDelete = await StampsAsync(item.Id);
        Assert.True(afterDelete.Edited > edited, "deleting is an edit");
        Assert.Equal(afterDelete.Updated, afterDelete.Edited);

        await Task.Delay(20);
        await using (var ctx = db.CreateContext())
        {
            var row = await ctx.Items.SingleAsync(i => i.Id == item.Id);
            row.OnHand += 5;   // only a count the system keeps
            await ctx.SaveChangesAsync();
        }
        var afterCount = await StampsAsync(item.Id);
        Assert.True(afterCount.Updated > afterDelete.Updated);
        Assert.Equal(afterDelete.Edited, afterCount.Edited);
    }

    [SkippableFact]
    public async Task Stock_is_the_sum_of_the_movements_whatever_changed_it()
    {
        var (session, location) = await SetUpAsync();
        var inventory = new InventoryService(Data);
        var sales = new SalesService(Data);

        // Opening stock, a sale, a return, a void, a receipt and an adjustment.
        var plain = await ItemAsync(location.Id, "SUM1", 20);
        var sale = await sales.PostSaleAsync(new SaleDraft { Lines = [Line(plain, 3)] }, [Cash(30m)], session);
        await sales.PostSaleAsync(new SaleDraft { Lines = [Line(plain, -1, kind: SaleLineKind.Return)] }, [Cash(-10m)], session);
        await sales.VoidSaleAsync(sale.Id, session);
        var purchasing = new PurchasingService(Data);
        var po = await purchasing.CreateAsync(location.Id, OrderMethod.Blank, "Test");
        await purchasing.AddItemAsync(po.Id, plain.Plu, 6);
        await purchasing.PostOrderAsync(po.Id);
        var line = (await purchasing.GetAsync(po.Id))!.Lines.Single();
        await purchasing.PostShipmentAsync(po.Id, [new ReceivedLine(line.Id, 6, 4m)]);
        await inventory.AdjustStockAsync(plain.Id, -2, "Broken", "tester");
        await inventory.AdjustStockAsync(plain.Id, 50, "Counted", "tester", setTo: true);

        // A matrix item: lines counted in the editor, one changed, one added and one removed later, and a sale of a line.
        var matrix = await ItemAsync(location.Id, "SUM2", 0, ItemType.Matrix,
            [new ItemVariant { Value1 = "Red", OnHand = 4 }, new ItemVariant { Value1 = "Blue", OnHand = 6 }]);
        var edited = (await inventory.GetAsync(matrix.Id))!;
        var red = edited.Variants.Single(v => v.Value1 == "Red");
        var blue = edited.Variants.Single(v => v.Value1 == "Blue");
        red.OnHand = 7;
        edited.Variants = [blue, red, new ItemVariant { Value1 = "Green", OnHand = 2 }];
        await inventory.SaveAsync(edited, null, null, null);
        await sales.PostSaleAsync(new SaleDraft { Lines = [Line(matrix, 2, red.Id)] }, [Cash(20m)], session);
        edited = (await inventory.GetAsync(matrix.Id))!;
        edited.Variants = edited.Variants.Where(v => v.Value1 != "Blue").ToList();
        await inventory.SaveAsync(edited, null, null, null);
        Assert.Equal(["Red", "Green"], (await inventory.GetAsync(matrix.Id))!.Variants.OrderBy(v => v.Ordinal).Select(v => v.Value1));

        // On-hand counts from an inventory CSV file, for an existing item and a new one.
        await new CsvExchange(Data).ImportItemsAsync(location.Id, "PLU,PROD,ONHAND\nSUM1,Item SUM1,44\nSUM3,Imported,9\n");

        await using var check = db.CreateContext();
        var items = await check.Items.Where(i => i.LocationId == location.Id).Select(i => new { i.Id, i.Plu, i.OnHand }).ToListAsync();
        var sums = await check.StockMovements.GroupBy(m => m.ItemId).Select(g => new { g.Key, Sum = g.Sum(m => m.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Sum);
        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal(i.OnHand, sums.GetValueOrDefault(i.Id)));
        Assert.Equal(44m, items.Single(i => i.Plu == "SUM1").OnHand);
        Assert.Equal(7m, items.Single(i => i.Plu == "SUM2").OnHand);   // red 7 - 2 sold + green 2

        var variants = await check.ItemVariants.Where(v => v.ItemId == matrix.Id).ToListAsync();
        var variantSums = await check.StockMovements.Where(m => m.VariantId != null).GroupBy(m => m.VariantId!.Value)
            .Select(g => new { g.Key, Sum = g.Sum(m => m.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Sum);
        Assert.Equal(2, variants.Count);
        Assert.All(variants, v => Assert.Equal(v.OnHand, variantSums.GetValueOrDefault(v.Id)));
        Assert.Equal(0m, variantSums[blue.Id]);   // the removed line's stock left with it
    }

    [SkippableFact]
    public async Task Each_station_counts_its_own_sales_statistics()
    {
        var (session, location) = await SetUpAsync();
        var item = await ItemAsync(location.Id, "PS1", 50);
        var sales = new SalesService(Data);
        await sales.PostSaleAsync(new SaleDraft { Lines = [Line(item, 2)] }, [Cash(20m)], session);
        var other = new SessionInfo { StationNumber = session.StationNumber + 1000, LocationId = location.Id, UserName = "other", AccessLevel = 4 };
        await sales.PostSaleAsync(new SaleDraft { Lines = [Line(item, 3)] }, [Cash(30m)], other);

        await using (var ctx = db.CreateContext())
        {
            var rows = await ctx.ItemPeriodSales.Where(p => p.ItemId == item.Id && p.Kind == SalesPeriodKind.Month).ToListAsync();
            Assert.Equal([session.StationNumber, other.StationNumber], rows.Select(r => r.StationNumber).Order());
        }
        var month = (await new InventoryService(Data).GetPeriodSalesAsync(item.Id, DateTime.Today.Year, SalesPeriodKind.Month)).Single();
        Assert.Equal(5m, month.Units);
        Assert.Equal(50m, month.Gross);
    }
}

public sealed class ChangeTrackingTestsOnSqlite(SqliteFixture db) : ChangeTrackingTests(db), IClassFixture<SqliteFixture>;

public sealed class ChangeTrackingTestsOnSqlServer(SqlServerFixture db) : ChangeTrackingTests(db), IClassFixture<SqlServerFixture>;
