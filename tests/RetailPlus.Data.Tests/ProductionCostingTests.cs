using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>
/// The costing postings check: production posts through the general ledger, and every entry balances. A loaf is made of
/// dough (made itself, from flour and salt) and a bag, so the bill has two levels; both bills make a yield of more than one
/// and lose some of their material (scrap allowances), and the floor throws some material away and an inspection scraps
/// some output. Material issued goes to work in progress, labour, machine and overhead are absorbed into it, the output and
/// its by-product come out of it into stock, and what is left at the close is the variance. A class of its own (its own
/// database), because one test locks the month.
/// </summary>
public abstract class ProductionCostingTests(DatabaseFixture db)
{
    private static readonly string[] FourSteps = ["close", "conversion", "issue", "receipt"];
    private static readonly string[] IssueAndTwoChanges = ["issue", "issue (change)", "issue (change)"];

    private DataContext Data => db.Data;

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<Item> ItemAsync(Guid locationId, string plu, decimal onHand, decimal cost) =>
        await new InventoryService(Data).SaveAsync(new Item { LocationId = locationId, Plu = plu, Name = "Item " + plu, OnHand = onHand, LastCost = cost, AverageCost = cost },
            "COSTTEST", null, null);

    private async Task<decimal> OnHandAsync(Guid itemId) => (await new InventoryService(Data).GetAsync(itemId))!.OnHand;

    private async Task<Dictionary<string, Guid>> AccountsAsync()
    {
        await using var ctx = db.CreateContext();
        return await ctx.LedgerAccounts.AsNoTracking().Where(a => a.Role != null).ToDictionaryAsync(a => a.Role!, a => a.Id);
    }

    private async Task<List<JournalEntry>> EntriesAsync(Guid orderId)
    {
        await using var ctx = db.CreateContext();
        return await ctx.JournalEntries.AsNoTracking().Include(e => e.Lines).Where(e => e.Source!.StartsWith($"prod:{orderId:N}:")).OrderBy(e => e.Number).ToListAsync();
    }

    private static decimal Net(IEnumerable<JournalEntry> entries, Guid account) => entries.SelectMany(e => e.Lines).Where(l => l.AccountId == account).Sum(l => l.Debit - l.Credit);

    /// <summary>Every entry balances, is for the store, and holds a debit and a credit of the same amount on the step's accounts.</summary>
    private static void AllBalance(IEnumerable<JournalEntry> entries, Guid store, Dictionary<string, Guid> accounts)
    {
        var expected = new Dictionary<string, (string Debit, string Credit)>
        {
            ["issue"] = (LedgerRoles.WorkInProgress, LedgerRoles.Inventory),
            ["conversion"] = (LedgerRoles.WorkInProgress, LedgerRoles.ManufacturingOverhead),
            ["receipt"] = (LedgerRoles.Inventory, LedgerRoles.WorkInProgress),
            ["close"] = (LedgerRoles.ProductionVariance, LedgerRoles.WorkInProgress),
        };
        foreach (var e in entries)
        {
            Assert.Equal(e.Lines.Sum(l => l.Debit), e.Lines.Sum(l => l.Credit));
            Assert.True(e.Lines.Sum(l => l.Debit) > 0, $"Entry {e.Number} is of nothing.");
            Assert.All(e.Lines, l => Assert.Equal(store, l.StoreId));
            Assert.Equal((JournalStatus.Posted, (Guid?)store), (e.Status, e.StoreId));
            if (e.Kind != JournalKind.Production) continue;
            var step = e.Source!.Split(':')[2];
            var (debit, credit) = expected[step];
            Assert.Equal(2, e.Lines.Count);
            Assert.Equal(accounts[debit], e.Lines.Single(l => l.Debit > 0).AccountId);
            Assert.Equal(accounts[credit], e.Lines.Single(l => l.Credit > 0).AccountId);
        }
    }

    private static decimal Step(IEnumerable<JournalEntry> entries, string step) =>
        entries.Where(e => e.Source!.Split(':')[2] == step).SelectMany(e => e.Lines).Sum(l => l.Debit);

    [SkippableFact]
    public async Task A_two_level_bill_with_yield_and_scrap_posts_balanced_entries_and_leaves_no_work_in_progress()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var loc = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Bakery");
        await new LedgerService(Data).SeedAsync();
        var accounts = await AccountsAsync();
        var flour = await ItemAsync(loc.Id, Unique("FL"), 100, 2m);
        var salt = await ItemAsync(loc.Id, Unique("SA"), 10, 1.5m);
        var bag = await ItemAsync(loc.Id, Unique("BG"), 100, 0.25m);
        var dough = await ItemAsync(loc.Id, Unique("DO"), 0, 0);
        var loaf = await ItemAsync(loc.Id, Unique("LF"), 0, 0);
        var crumb = await ItemAsync(loc.Id, Unique("CR"), 0, 0);
        var manufacturing = new ManufacturingService(Data);
        var mixer = await manufacturing.SaveWorkCentreAsync(new WorkCentre
        {
            LocationId = loc.Id, Code = Unique("MX"), Name = "Mixer", LabourCostPerHour = 60, MachineCostPerHour = 30, OverheadPerHour = 12,
        });
        var oven = await manufacturing.SaveWorkCentreAsync(new WorkCentre
        {
            LocationId = loc.Id, Code = Unique("OV"), Name = "Oven", LabourCostPerHour = 40, MachineCostPerHour = 20, OverheadPerHour = 10,
        });
        // Level 2: 10 kg of dough from 6 kg of flour (5 % lost) and 0.2 kg of salt, with 10 % overhead on material.
        await manufacturing.SaveAsync(new BillOfMaterials
        {
            ProductCode = dough.Plu, YieldQuantity = 10, OverheadPercent = 10,
            Lines = [new BomLine { ComponentCode = flour.Plu, Quantity = 6, ScrapPercent = 5, OperationSequence = 10 }, new BomLine { ComponentCode = salt.Plu, Quantity = 0.2m }],
            Operations = [new RoutingOperation { Sequence = 10, WorkCentreId = mixer.Id, Name = "Mix", SetupMinutes = 30, RunMinutesPerUnit = 3 }],
        });
        // Level 1: 20 loaves from 10 kg of dough (10 % lost) and 20 bags taken at the end, with a bag of crumbs taking 5 % of the cost.
        await manufacturing.SaveAsync(new BillOfMaterials
        {
            ProductCode = loaf.Plu, YieldQuantity = 20,
            Lines = [new BomLine { ComponentCode = dough.Plu, Quantity = 10, ScrapPercent = 10 }, new BomLine { ComponentCode = bag.Plu, Quantity = 20, IssueMethod = IssueMethod.Backflush }],
            ByProducts = [new BomByProduct { StockCode = crumb.Plu, Quantity = 1, CostSharePercent = 5 }],
            Operations = [new RoutingOperation { Sequence = 10, WorkCentreId = oven.Id, Name = "Bake", SetupMinutes = 20, RunMinutesPerUnit = 2 }],
        });
        var production = new ProductionService(Data);
        var costing = new ProductionCostingService(Data);

        // The dough: 11 kg for the loaves' 10 x 1.1. Flour 7 issued and 0.5 spilt, salt 0.22; 65 minutes at the mixer.
        var doughOrder = (await production.ReleaseAsync((await production.CreateAsync(new NewProductionOrder(dough.Plu, 11, loc.Id, loc.Id))).Id)).Order;
        Assert.Equal((6.93m, 0.22m), (doughOrder.Lines.Single(l => l.StockCode == flour.Plu).QuantityPlanned, doughOrder.Lines.Single(l => l.StockCode == salt.Plu).QuantityPlanned));
        await production.IssueAsync(doughOrder.Id, flour.Plu, 7);
        await production.ScrapMaterialAsync(doughOrder.Id, flour.Plu, 0.5m, "Spilt");
        await production.IssueAsync(doughOrder.Id, salt.Plu, 0.22m);
        await production.ReportAsync(doughOrder.Id, 10, 65, 11, 0);
        await production.ReceiveAsync(doughOrder.Id, 11, complete: true);
        await production.CloseAsync(doughOrder.Id);
        await costing.PostAsync(doughOrder.Id);
        var doughEntries = await EntriesAsync(doughOrder.Id);
        AllBalance(doughEntries, loc.StoreId, accounts);
        // Material 7 x 2.00 + 0.5 x 2.00 + 0.22 x 1.50 = 15.33; time 65 + 32.50 + 13 and 10 % of the material: 112.03; all of it received.
        Assert.Equal((15.33m, 112.03m, 127.36m), (Step(doughEntries, "issue"), Step(doughEntries, "conversion"), Step(doughEntries, "receipt")));
        Assert.DoesNotContain(doughEntries, e => e.Source!.EndsWith(":close", StringComparison.Ordinal)); // nothing was left to close
        Assert.Equal(0m, Net(doughEntries, accounts[LedgerRoles.WorkInProgress]));
        Assert.Equal(11.578m, (await new InventoryService(Data).GetAsync(dough.Id))!.AverageCost);

        // The loaves: their standard cost rolls the dough up from its own bill; 10.6 kg of dough issued and 0.4 dropped.
        var loafOrder = (await production.ReleaseAsync((await production.CreateAsync(new NewProductionOrder(loaf.Plu, 20, loc.Id, loc.Id))).Id)).Order;
        Assert.Equal((11m, 20m, 1m), (loafOrder.Lines.Single(l => l.StockCode == dough.Plu).QuantityPlanned, loafOrder.Lines.Single(l => l.StockCode == bag.Plu).QuantityPlanned,
            loafOrder.Lines.Single(l => l.Kind == ProductionLineKind.ByProduct).QuantityPlanned));
        Assert.Equal(11.619m, loafOrder.Lines.Single(l => l.StockCode == dough.Plu).StandardUnitCost);
        Assert.Equal(10.141m, loafOrder.StandardUnitCost);
        await production.IssueAsync(loafOrder.Id, dough.Plu, 10.6m);
        await production.ScrapMaterialAsync(loafOrder.Id, dough.Plu, 0.4m, "Dropped");
        await production.ReportAsync(loafOrder.Id, 10, 60, 20, 0);
        // Ten first, at the standard cost of the loaves' 95 %; posted as the server does after every change.
        await production.ReceiveAsync(loafOrder.Id, 10, complete: false);
        await costing.PostAsync(loafOrder.Id);
        var partway = await EntriesAsync(loafOrder.Id);
        AllBalance(partway, loc.StoreId, accounts);
        Assert.Equal((127.36m, 70m, 96.34m), (Step(partway, "issue"), Step(partway, "conversion"), Step(partway, "receipt")));
        Assert.Equal(127.36m + 70m - 96.34m, Net(partway, accounts[LedgerRoles.WorkInProgress]));

        // The rest: the bags are taken, the crumbs take 5 % of everything charged and the loaves the rest; two fail and are scrapped.
        await production.ReceiveAsync(loafOrder.Id, 10, complete: true, [new ByProductQuantity(crumb.Plu, 1)]);
        await new QualityService(Data).InspectOutputAsync(loafOrder.Id, new InspectionRequest(20, 2, []) { Disposition = InspectionDisposition.Scrap });
        await costing.PostAsync(loafOrder.Id);
        await production.CloseAsync(loafOrder.Id);
        await costing.PostAsync(loafOrder.Id);
        var loafEntries = await EntriesAsync(loafOrder.Id);
        AllBalance(loafEntries, loc.StoreId, accounts);
        // One entry per step: posting again replaced the month's entries rather than adding to them.
        Assert.Equal(4, loafEntries.Count);
        Assert.Equal(partway.Single(e => e.Source!.Contains(":issue:", StringComparison.Ordinal)).Id, loafEntries.Single(e => e.Source!.Contains(":issue:", StringComparison.Ordinal)).Id);
        // Issued: dough 11 x 11.578 + 20 bags x 0.25 + the 2 scrapped loaves back at 9.612 = 151.58. Received: 96.34 + 95.90 + 10.12 (crumbs).
        Assert.Equal((151.58m, 70m, 202.36m, 19.22m), (Step(loafEntries, "issue"), Step(loafEntries, "conversion"), Step(loafEntries, "receipt"), Step(loafEntries, "close")));
        Assert.Equal(0m, Net(loafEntries, accounts[LedgerRoles.WorkInProgress]));

        // Both levels together: the books move by what the stock moved, the overhead absorbed is what was applied, the variance the scrapped loaves.
        var all = doughEntries.Concat(loafEntries).ToList();
        Assert.Equal(all.SelectMany(e => e.Lines).Sum(l => l.Debit), all.SelectMany(e => e.Lines).Sum(l => l.Credit));
        Assert.Equal(-15.33m + 127.36m - 151.58m + 202.36m, Net(all, accounts[LedgerRoles.Inventory]));
        Assert.Equal(-(112.03m + 70m), Net(all, accounts[LedgerRoles.ManufacturingOverhead]));
        Assert.Equal(19.22m, Net(all, accounts[LedgerRoles.ProductionVariance]));
        await using (var ctx = db.CreateContext())
        {
            var moved = await ctx.StockMovements.AsNoTracking().Where(m => m.ProductionOrderId == doughOrder.Id || m.ProductionOrderId == loafOrder.Id)
                .Select(m => new { m.ProductionOrderId, m.Quantity, m.UnitCost }).ToListAsync();
            decimal stockValue = moved.GroupBy(m => m.ProductionOrderId).Sum(g => Money.Round(g.Sum(m => m.Quantity * m.UnitCost)));
            Assert.Equal(stockValue, Net(all, accounts[LedgerRoles.Inventory]));
        }
        Assert.Equal((92.5m, 0m, 80m, 18m, 1m), (await OnHandAsync(flour.Id), await OnHandAsync(dough.Id), await OnHandAsync(bag.Id), await OnHandAsync(loaf.Id), await OnHandAsync(crumb.Id)));

        // The order's summary lists the four entries by step, with what each moved.
        var summary = (await costing.SummaryAsync(loafOrder.Id))!;
        Assert.Equal(FourSteps, summary.Postings.Select(p => p.Step).Order().ToArray());
        Assert.Equal(19.22m, summary.Postings.Single(p => p.Step == "close").Amount);
    }

    [SkippableFact]
    public async Task A_change_in_a_locked_month_posts_only_the_difference_in_the_next_open_month_and_still_balances()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var loc = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Workshop");
        var ledger = new LedgerService(Data);
        await ledger.SeedAsync();
        var accounts = await AccountsAsync();
        var part = await ItemAsync(loc.Id, Unique("PT"), 100, 1m);
        var widget = await ItemAsync(loc.Id, Unique("WG"), 0, 0);
        await new ManufacturingService(Data).SaveAsync(new BillOfMaterials { ProductCode = widget.Plu, Lines = [new BomLine { ComponentCode = part.Plu, Quantity = 1 }] });
        var production = new ProductionService(Data);
        var costing = new ProductionCostingService(Data);
        var first = (await production.ReleaseAsync((await production.CreateAsync(new NewProductionOrder(widget.Plu, 10, loc.Id, loc.Id))).Id)).Order;
        await production.IssueAsync(first.Id, part.Plu, 10);
        await costing.PostAsync(first.Id);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        await ledger.EnsureYearAsync(today);
        var period = (await ledger.GetYearsAsync()).SelectMany(y => y.Periods).Single(p => p.StartDate <= today && p.EndDate >= today);
        await ledger.SetPeriodStatusAsync(period.Id, FiscalStatus.Locked);
        try
        {
            // Two more after the month was locked: the difference, in the first open month.
            await production.IssueAsync(first.Id, part.Plu, 2);
            await costing.PostAsync(first.Id);
            var entries = await EntriesAsync(first.Id);
            var change = Assert.Single(entries, e => e.Kind == JournalKind.Adjustment);
            Assert.Equal((nextMonth, 2m), (change.Date, change.Lines.Sum(l => l.Debit)));
            Assert.Equal(10m, entries.Single(e => e.Kind == JournalKind.Production).Lines.Sum(l => l.Debit));
            Assert.Equal(12m, Net(entries, accounts[LedgerRoles.WorkInProgress]));

            // All of it given back: the month comes to nothing, and so does the order's work in progress.
            await production.IssueAsync(first.Id, part.Plu, -12);
            await costing.PostAsync(first.Id);
            entries = await EntriesAsync(first.Id);
            Assert.Equal(2, entries.Count(e => e.Kind == JournalKind.Adjustment));
            Assert.Equal(0m, Net(entries, accounts[LedgerRoles.WorkInProgress]));
            Assert.Equal(0m, Net(entries, accounts[LedgerRoles.Inventory]));
            AllBalance(entries, loc.StoreId, accounts);
            Assert.Equal(IssueAndTwoChanges, (await costing.SummaryAsync(first.Id))!.Postings.Select(p => p.Step).ToArray());

            // An order first worked while the month is locked goes into the next open month.
            var second = (await production.ReleaseAsync((await production.CreateAsync(new NewProductionOrder(widget.Plu, 3, loc.Id, loc.Id))).Id)).Order;
            await production.IssueAsync(second.Id, part.Plu, 3);
            await costing.PostAsync(second.Id);
            var late = Assert.Single(await EntriesAsync(second.Id));
            Assert.Equal((JournalKind.Production, nextMonth, 3m), (late.Kind, late.Date, late.Lines.Sum(l => l.Debit)));
            AllBalance([late], loc.StoreId, accounts);
        }
        finally
        {
            await ledger.SetPeriodStatusAsync(period.Id, FiscalStatus.Open);
        }

        // In an open month, material all given back leaves an entry whose lines cancel out, in place of the one posted.
        var third = (await production.ReleaseAsync((await production.CreateAsync(new NewProductionOrder(widget.Plu, 5, loc.Id, loc.Id))).Id)).Order;
        await production.IssueAsync(third.Id, part.Plu, 5);
        await costing.PostAsync(third.Id);
        await production.IssueAsync(third.Id, part.Plu, -5);
        await costing.PostAsync(third.Id);
        var undone = Assert.Single(await EntriesAsync(third.Id));
        Assert.Equal((0m, 0m), (Net([undone], accounts[LedgerRoles.WorkInProgress]), Net([undone], accounts[LedgerRoles.Inventory])));
        Assert.Equal(undone.Lines.Sum(l => l.Debit), undone.Lines.Sum(l => l.Credit));
        Assert.Equal(0m, Assert.Single((await costing.SummaryAsync(third.Id))!.Postings).Amount);
        Assert.Equal(97m, await OnHandAsync(part.Id));
    }
}

public sealed class ProductionCostingTestsOnSqlite(SqliteFixture db) : ProductionCostingTests(db), IClassFixture<SqliteFixture>;

public sealed class ProductionCostingTestsOnSqlServer(SqlServerFixture db) : ProductionCostingTests(db), IClassFixture<SqlServerFixture>;
