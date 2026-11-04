using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Reports;

namespace RetailPlus.Data.Tests;

/// <summary>
/// Manufacturing against a real database: bills of materials (versions, copies, comparison, loops, explosion and the cost
/// roll-up), production orders from release to close with their stock movements, costs, variances and ledger postings,
/// planning (MRP) with firming and capacity, and quality inspections. Every test uses stock codes and a location of its
/// own, because they share one database.
/// </summary>
public abstract class ManufacturingTests(DatabaseFixture db)
{
    private DataContext Data => db.Data;

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<Location> LocationAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        return await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Production");
    }

    private async Task<Item> ItemAsync(Guid locationId, string plu, decimal onHand, decimal cost, decimal price = 0, string? barcode = null, Guid? supplierId = null)
    {
        var item = await new InventoryService(Data).SaveAsync(new Item
        {
            LocationId = locationId, Plu = plu, Name = "Item " + plu, OnHand = onHand, LastCost = cost, AverageCost = cost, Price1 = price, Barcode = barcode,
        }, "PRODTEST", null, null);
        if (supplierId is Guid s)
        {
            await using var ctx = db.CreateContext();
            await ctx.Items.Where(i => i.Id == item.Id).ExecuteUpdateAsync(x => x.SetProperty(i => i.SupplierId, s).Touched());
        }
        return item;
    }

    private async Task<Item> ReloadAsync(Guid itemId) => (await new InventoryService(Data).GetAsync(itemId))!;

    private async Task<WorkCentre> CentreAsync(Guid locationId, string code, decimal labour, decimal machine, decimal overhead, decimal hoursPerDay = 8) =>
        await new ManufacturingService(Data).SaveWorkCentreAsync(new WorkCentre
        {
            LocationId = locationId, Code = code, Name = "Centre " + code, LabourCostPerHour = labour, MachineCostPerHour = machine, OverheadPerHour = overhead,
            CapacityHoursPerDay = hoursPerDay, DaysPerWeek = 5,
        });

    /// <summary>
    /// A cake bakery: flour (2.00), sugar (5.00, 10 % lost), boxes (0.50, backflushed) make 10 cakes and 1 crumb bag (10 % of the
    /// cost), at a mixer of 60 + 30 + 12 an hour (setup 30 min, 6 min a cake), with 5 % overhead on material.
    /// </summary>
    private async Task<(Location Location, BillOfMaterials Bom, Item Flour, Item Sugar, Item Box, Item Cake, Item Crumb)> BakeryAsync(decimal sugarOnHand = 3)
    {
        var loc = await LocationAsync();
        var flour = await ItemAsync(loc.Id, Unique("FL"), 100, 2m, barcode: Upc.InStore(Random.Shared.NextInt64(10_000, 99_999)));
        var sugar = await ItemAsync(loc.Id, Unique("SU"), sugarOnHand, 5m);
        var box = await ItemAsync(loc.Id, Unique("BX"), 100, 0.5m);
        var cake = await ItemAsync(loc.Id, Unique("CK"), 0, 0, price: 30);
        var crumb = await ItemAsync(loc.Id, Unique("CR"), 0, 0);
        var mixer = await CentreAsync(loc.Id, Unique("M"), 60, 30, 12);
        var bom = await new ManufacturingService(Data).SaveAsync(new BillOfMaterials
        {
            ProductCode = cake.Plu, YieldQuantity = 10, OverheadPercent = 5,
            Lines =
            [
                new BomLine { ComponentCode = flour.Plu, Quantity = 5, OperationSequence = 10 },
                new BomLine { ComponentCode = sugar.Plu, Quantity = 2, ScrapPercent = 10 },
                new BomLine { ComponentCode = box.Plu, Quantity = 10, IssueMethod = IssueMethod.Backflush },
            ],
            ByProducts = [new BomByProduct { StockCode = crumb.Plu, Quantity = 1, CostSharePercent = 10 }],
            Operations = [new RoutingOperation { Sequence = 10, WorkCentreId = mixer.Id, Name = "Mix and bake", SetupMinutes = 30, RunMinutesPerUnit = 6 }],
        });
        return (loc, bom, flour, sugar, box, cake, crumb);
    }

    // Bills of materials -------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_bill_is_versioned_copied_compared_and_refuses_loops_and_bad_lines()
    {
        var loc = await LocationAsync();
        var a = await ItemAsync(loc.Id, Unique("A"), 0, 1);
        var b = await ItemAsync(loc.Id, Unique("B"), 0, 1);
        var c = await ItemAsync(loc.Id, Unique("C"), 0, 1);
        var service = new ManufacturingService(Data);
        var first = await service.SaveAsync(new BillOfMaterials { ProductCode = a.Plu.ToLowerInvariant(), Lines = [new BomLine { ComponentCode = b.Plu, Quantity = 2 }] });
        Assert.Equal((a.Plu, 1, a.Name, true), (first.ProductCode, first.Revision, first.ProductName, first.IsActive));
        Assert.Equal(b.Name, first.Lines.Single().Description);

        var copy = await service.CopyAsync(first.Id);
        Assert.Equal((2, false), (copy.Revision, copy.IsActive));
        copy.Lines.Single().Quantity = 3;
        copy.Lines.Add(new BomLine { ComponentCode = c.Plu, Quantity = 1 });
        copy.IsActive = true;
        copy = await service.SaveAsync(copy);
        var diff = await service.CompareAsync(first.Id, copy.Id);
        Assert.Contains(diff, d => d.Part == "Component" && d.Key == b.Plu && d.Change == "Changed");
        Assert.Contains(diff, d => d.Part == "Component" && d.Key == c.Plu && d.Change == "Added");
        Assert.DoesNotContain(diff, d => d.Part == "Bill");
        // The highest active version in force is the one used.
        Assert.Equal(copy.Id, (await service.ActiveForAsync(a.Plu, DateOnly.FromDateTime(DateTime.Today)))!.Id);

        // B made from A while A is made from B: a loop.
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveAsync(new BillOfMaterials { ProductCode = b.Plu, Lines = [new BomLine { ComponentCode = a.Plu, Quantity = 1 }] }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveAsync(new BillOfMaterials { ProductCode = c.Plu, Lines = [new BomLine { ComponentCode = c.Plu, Quantity = 1 }] }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveAsync(new BillOfMaterials { ProductCode = c.Plu, YieldQuantity = 0, Lines = [new BomLine { ComponentCode = b.Plu, Quantity = 1 }] }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveAsync(new BillOfMaterials
        {
            ProductCode = c.Plu, Lines = [new BomLine { ComponentCode = b.Plu, Quantity = 1 }],
            ByProducts = [new BomByProduct { StockCode = a.Plu, Quantity = 1, CostSharePercent = 100 }],
        }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveAsync(new BillOfMaterials
        {
            ProductCode = c.Plu, Lines = [new BomLine { ComponentCode = b.Plu, Quantity = 1, OperationSequence = 20 }],
        }));
        Assert.Equal(2, (await service.ListAsync(a.Plu)).Count);
    }

    [SkippableFact]
    public async Task A_bill_explodes_over_its_levels_and_rolls_up_material_labour_machine_and_overhead()
    {
        var (loc, bom, flour, sugar, box, cake, _) = await BakeryAsync();
        var service = new ManufacturingService(Data);
        var roll = await service.RollUpAsync(bom.Id, locationId: loc.Id);
        // Material: 5 x 2.00 + 2.2 x 5.00 + 10 x 0.50 = 26.00. Time: 30 + 60 minutes = 1.5 h at 60, 30 and 12 an hour.
        Assert.Equal((26.00m, 90.00m, 45.00m, 18.00m + 1.30m), (roll.Material, roll.Labour, roll.Machine, roll.Overhead));
        Assert.Equal(180.30m, roll.Total);
        Assert.Equal(90m, roll.ProductSharePercent);
        Assert.Equal(16.227m, roll.UnitCost);
        // A larger lot spreads the setup: 20 cakes cost less each.
        Assert.True((await service.RollUpAsync(bom.Id, 20, loc.Id)).UnitCost < roll.UnitCost);

        // A second level: a gift pack of 2 cakes and a ribbon; the cakes explode into their own components.
        var ribbon = await ItemAsync(loc.Id, Unique("RB"), 10, 1m);
        var pack = await ItemAsync(loc.Id, Unique("GP"), 0, 0);
        var packBom = await service.SaveAsync(new BillOfMaterials
        {
            ProductCode = pack.Plu, Lines = [new BomLine { ComponentCode = cake.Plu, Quantity = 2 }, new BomLine { ComponentCode = ribbon.Plu, Quantity = 1 }],
        });
        var tree = await service.ExplodeAsync(packBom.Id, locationId: loc.Id);
        var cakeLine = tree.Single(l => l.StockCode == cake.Plu);
        Assert.Equal((1, "1", true, 16.227m), (cakeLine.Level, cakeLine.Path, cakeLine.IsMade, cakeLine.UnitCost));
        Assert.Equal(1.0m, tree.Single(l => l.StockCode == flour.Plu).QuantityPerUnit); // 5 / 10 x 2 cakes
        Assert.Equal(0.44m, tree.Single(l => l.StockCode == sugar.Plu).QuantityPerUnit); // 2 / 10 x 1.1 x 2
        Assert.Equal(("1.3", 2), (tree.Single(l => l.StockCode == box.Plu).Path, tree.Single(l => l.StockCode == box.Plu).Level));
        Assert.Equal(Money.Round(2 * 16.227m + 1m), (await service.RollUpAsync(packBom.Id, locationId: loc.Id)).Total);
    }

    [SkippableFact]
    public async Task Work_centres_have_unique_codes_and_one_in_a_routing_is_not_deleted()
    {
        var loc = await LocationAsync();
        var code = Unique("W");
        var service = new ManufacturingService(Data);
        var centre = await CentreAsync(loc.Id, code.ToLowerInvariant(), 10, 0, 0);
        Assert.Equal(code, centre.Code);
        await Assert.ThrowsAsync<BusinessRuleException>(() => CentreAsync(loc.Id, code, 1, 1, 1));
        var item = await ItemAsync(loc.Id, Unique("P"), 0, 0);
        var part = await ItemAsync(loc.Id, Unique("Q"), 0, 1);
        await service.SaveAsync(new BillOfMaterials
        {
            ProductCode = item.Plu, Lines = [new BomLine { ComponentCode = part.Plu, Quantity = 1 }],
            Operations = [new RoutingOperation { Sequence = 10, WorkCentreId = centre.Id, Name = "Cut" }],
        });
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeleteWorkCentreAsync(centre.Id));
        Assert.Contains(await service.WorkCentresAsync(loc.StoreId), w => w.Id == centre.Id);
    }

    // Production orders ---------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task An_order_is_released_worked_received_at_cost_backflushed_and_closed_with_its_variance()
    {
        var (loc, bom, flour, sugar, box, cake, crumb) = await BakeryAsync();
        var production = new ProductionService(Data);
        var order = await production.CreateAsync(new NewProductionOrder(cake.Plu, 20, loc.Id, loc.Id) { PlannedStart = DateOnly.FromDateTime(DateTime.Today) });
        Assert.Equal((ProductionOrderStatus.Planned, bom.Id, 1), (order.Status, order.BillOfMaterialsId, order.BomRevision));
        Assert.True(order.Number > db.Station!.StationNumber * NumberCounter.NumbersPerStation);
        await Assert.ThrowsAsync<BusinessRuleException>(() => production.IssueAsync(order.Id, flour.Plu, 1));

        // Release: the bill copied onto the order for 20, the standard cost rolled up, sugar short (3 of 4.4) and suggested to buy.
        var released = await production.ReleaseAsync(order.Id);
        order = released.Order;
        Assert.Equal(ProductionOrderStatus.Released, order.Status);
        var lines = order.Lines.Where(l => l.Kind == ProductionLineKind.Component).ToDictionary(l => l.StockCode);
        Assert.Equal((10m, 4.4m, 20m), (lines[flour.Plu].QuantityPlanned, lines[sugar.Plu].QuantityPlanned, lines[box.Plu].QuantityPlanned));
        Assert.Equal((3m, IssueMethod.Backflush), (lines[sugar.Plu].QuantityReserved, lines[box.Plu].IssueMethod));
        Assert.Equal(2m, order.Lines.Single(l => l.Kind == ProductionLineKind.ByProduct).QuantityPlanned);
        Assert.Equal((2.6m, 7.5m, 3.75m, 1.63m), (order.StandardMaterialCost, order.StandardLabourCost, order.StandardMachineCost, order.StandardOverheadCost));
        var shortage = Assert.Single(released.Shortages);
        Assert.Equal((sugar.Plu, 1.4m), (shortage.StockCode, shortage.Missing));
        var suggestion = Assert.Single(await new MrpService(Data).SuggestionsAsync(loc.StoreId), s => s.StockCode == sugar.Plu);
        Assert.Equal((SuggestionKind.Buy, 1.4m, (Guid?)null), (suggestion.Kind, suggestion.Quantity, suggestion.RunId));

        // The floor: flour by its barcode, one back, sugar in full; the operation's time and quantities.
        await production.IssueAsync(order.Id, "0" + flour.Barcode, 11);
        await production.IssueAsync(order.Id, flour.Plu, -1);
        await Assert.ThrowsAsync<BusinessRuleException>(() => production.IssueAsync(order.Id, flour.Plu, -50));
        await production.IssueAsync(order.Id, sugar.Plu, 4.4m);
        await production.StartOperationAsync(order.Id, 10);
        var report = await production.ReportAsync(order.Id, 10, 150, 19, 1, operatorName: "baker");
        Assert.Equal((150m, 75m, 30m), (report.LabourCost, report.MachineCost, report.OverheadCost));
        Assert.Equal(90m, (await ReloadAsync(flour.Id)).OnHand);
        Assert.Equal(-1.4m, (await ReloadAsync(sugar.Id)).OnHand);

        // Ten made before completion: valued at the standard cost of the product's share (15.48 x 90 %).
        var progress = await production.ReceiveAsync(order.Id, 10, complete: false);
        Assert.Equal(ProductionOrderStatus.InProgress, progress.Order.Status);
        Assert.Equal(139.32m, progress.Costs.Received);
        Assert.Equal((10m, 13.932m), ((await ReloadAsync(cake.Id)).OnHand, (await ReloadAsync(cake.Id)).AverageCost));
        await Assert.ThrowsAsync<BusinessRuleException>(() => production.ReceiveAsync(order.Id, 1, complete: false, [new ByProductQuantity(crumb.Plu, 1)]));

        // Completion: boxes backflushed for the 19 made, crumbs take 10 % of everything charged, the cakes the rest.
        progress = await production.ReceiveAsync(order.Id, 9, complete: true, [new ByProductQuantity(crumb.Plu, 2)]);
        Assert.Equal(ProductionOrderStatus.Completed, progress.Order.Status);
        Assert.Equal(81m, (await ReloadAsync(box.Id)).OnHand);
        Assert.Equal(51.50m, progress.Costs.Material);
        Assert.Equal(309.08m, progress.Costs.Charged);
        Assert.Equal(0m, progress.Costs.WorkInProgress);
        Assert.Equal((19m, 14.641m), ((await ReloadAsync(cake.Id)).OnHand, (await ReloadAsync(cake.Id)).AverageCost));
        Assert.Equal((2m, 15.455m), ((await ReloadAsync(crumb.Id)).OnHand, (await ReloadAsync(crumb.Id)).AverageCost));
        Assert.All(progress.Operations, o => Assert.Equal(OperationStatus.Done, o.Status));
        Assert.Equal(new[] { StockMovementKind.ProductionIssue, StockMovementKind.ProductionReceipt },
            progress.Movements.Select(m => m.Kind).Distinct().Order().ToArray());
        await Assert.ThrowsAsync<BusinessRuleException>(() => production.ReportAsync(order.Id, 10, 5, 0, 0));

        // A late issue after completion stays in work in progress, and closing makes it the variance.
        await production.IssueAsync(order.Id, flour.Plu, 1);
        var costing = new ProductionCostingService(Data);
        var summary = (await costing.SummaryAsync(order.Id))!;
        Assert.Equal(2.10m, summary.Costs.WorkInProgress);
        var v = summary.Variances;
        Assert.Equal(19m, v.OutputQuantity);
        Assert.Equal(3.00m, v.Materials.Single(m => m.StockCode == flour.Plu).UsageVariance); // 11 used, 9.5 standard, at 2.00
        Assert.Equal(1.10m, v.Materials.Single(m => m.StockCode == sugar.Plu).UsageVariance);
        Assert.Equal(0m, v.MaterialPriceVariance);
        Assert.Equal((144m, 6m), (v.StandardLabour, v.LabourVariance)); // (30 + 6 x 19) minutes at 60 an hour
        await production.CloseAsync(order.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => production.IssueAsync(order.Id, flour.Plu, 1));
        await Assert.ThrowsAsync<BusinessRuleException>(() => production.CancelAsync(order.Id));
    }

    [SkippableFact]
    public async Task Production_posts_issue_conversion_receipt_and_close_to_the_ledger_and_leaves_no_work_in_progress()
    {
        var (loc, _, flour, sugar, _, cake, crumb) = await BakeryAsync(sugarOnHand: 10);
        await new LedgerService(Data).SeedAsync();
        var production = new ProductionService(Data);
        var order = await production.CreateAsync(new NewProductionOrder(cake.Plu, 20, loc.Id, loc.Id));
        await production.ReleaseAsync(order.Id);
        await production.IssueAsync(order.Id, flour.Plu, 10);
        await production.IssueAsync(order.Id, sugar.Plu, 4.4m);
        await production.ReportAsync(order.Id, 10, 150, 19, 1);
        await production.ReceiveAsync(order.Id, 10, complete: false);
        await production.ReceiveAsync(order.Id, 9, complete: true, [new ByProductQuantity(crumb.Plu, 2)]);
        await production.IssueAsync(order.Id, flour.Plu, 1);
        await production.CloseAsync(order.Id);

        var costing = new ProductionCostingService(Data);
        var entries = await costing.PostAsync(order.Id);
        Assert.Equal(4, entries.Count);
        await using var ctx = db.CreateContext();
        var accounts = await ctx.LedgerAccounts.AsNoTracking().Where(a => a.Role != null).ToDictionaryAsync(a => a.Role!, a => a.Id);
        var mine = await ctx.JournalEntries.AsNoTracking().Include(e => e.Lines).Where(e => e.Source!.StartsWith($"prod:{order.Id:N}:")).ToListAsync();
        decimal Net(string role) => mine.SelectMany(e => e.Lines).Where(l => l.AccountId == accounts[role]).Sum(l => l.Debit - l.Credit);
        Assert.Equal(0m, Net(LedgerRoles.WorkInProgress));
        Assert.Equal(53.50m, mine.Single(e => e.Source!.Contains(":issue:", StringComparison.Ordinal)).Lines.Sum(l => l.Debit));
        Assert.Equal(-257.68m, Net(LedgerRoles.ManufacturingOverhead)); // 255 for time, 2.68 on 53.50 of material
        Assert.Equal(309.08m - 53.50m, Net(LedgerRoles.Inventory));
        Assert.Equal(2.10m, Net(LedgerRoles.ProductionVariance));
        Assert.All(mine, e => Assert.Equal((JournalKind.Production, order.StoreId), (e.Kind, e.StoreId)));

        // Posting again changes nothing; the summary lists the four entries.
        var again = await costing.PostAsync(order.Id);
        Assert.Equal(entries.Select(e => e.Id).Order(), again.Select(e => e.Id).Order());
        Assert.Equal(4, (await costing.SummaryAsync(order.Id))!.Postings.Count);
        var (posted, problems) = await costing.PostOpenAsync(loc.StoreId);
        Assert.True(posted >= 1);
        Assert.Empty(problems);
    }

    [SkippableFact]
    public async Task Release_sets_aside_only_what_other_orders_have_not_and_cancel_needs_an_untouched_order()
    {
        var (loc, _, flour, sugar, _, cake, _) = await BakeryAsync(sugarOnHand: 3);
        var production = new ProductionService(Data);
        var first = await production.CreateAsync(new NewProductionOrder(cake.Plu, 20, loc.Id, loc.Id));
        await production.ReleaseAsync(first.Id);
        var second = await production.CreateAsync(new NewProductionOrder(cake.Plu, 10, loc.Id, loc.Id));
        var result = await production.ReleaseAsync(second.Id);
        // The first order holds all 3 sugar; the second is short of all 2.2 it needs.
        var shortage = Assert.Single(result.Shortages);
        Assert.Equal((sugar.Plu, 0m, 2.2m), (shortage.StockCode, shortage.Available, shortage.Missing));
        Assert.Equal(5m, result.Order.Lines.Single(l => l.StockCode == flour.Plu).QuantityReserved);
        await Assert.ThrowsAsync<BusinessRuleException>(() => production.ReleaseAsync(second.Id));

        await production.IssueAsync(first.Id, flour.Plu, 1);
        await Assert.ThrowsAsync<BusinessRuleException>(() => production.CancelAsync(first.Id));
        var cancelled = await production.CancelAsync(second.Id, "Not needed");
        Assert.Equal(ProductionOrderStatus.Cancelled, cancelled.Status);
        Assert.Contains(await production.FloorOrdersAsync(loc.Id), o => o.Id == first.Id);
        Assert.DoesNotContain(await production.FloorOrdersAsync(loc.Id), o => o.Id == second.Id);

        // A planned order can still change; an old version is refused.
        var third = await production.CreateAsync(new NewProductionOrder(cake.Plu, 5, loc.Id, loc.Id));
        var changed = await production.UpdateAsync(third.Id, third.Version, new NewProductionOrder(cake.Plu, 8, loc.Id, loc.Id) { Priority = 1 });
        Assert.Equal((8m, 1), (changed.Quantity, changed.Priority));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => production.UpdateAsync(third.Id, third.Version, new NewProductionOrder(cake.Plu, 9, loc.Id, loc.Id)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => production.CreateAsync(new NewProductionOrder(flour.Plu, 1, loc.Id, loc.Id)));
    }

    // Planning --------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Planning_explodes_nets_and_offsets_then_firms_orders_and_a_purchase_order()
    {
        var loc = await LocationAsync();
        var supplier = await new PartyService(Data).SaveSupplierAsync(new Supplier { Company = Unique("Legs Ltd ") });
        var table = await ItemAsync(loc.Id, Unique("TB"), 1, 0);
        var top = await ItemAsync(loc.Id, Unique("TP"), 2, 0);
        var leg = await ItemAsync(loc.Id, Unique("LG"), 8, 3m, supplierId: supplier.Id);
        var wood = await ItemAsync(loc.Id, Unique("WD"), 0, 4m);
        var assembly = await CentreAsync(loc.Id, Unique("A"), 50, 0, 0, hoursPerDay: 8);
        var saw = await CentreAsync(loc.Id, Unique("S"), 40, 0, 0, hoursPerDay: 8);
        var manufacturing = new ManufacturingService(Data);
        await manufacturing.SaveAsync(new BillOfMaterials
        {
            ProductCode = table.Plu, Lines = [new BomLine { ComponentCode = top.Plu, Quantity = 1 }, new BomLine { ComponentCode = leg.Plu, Quantity = 4 }],
            Operations = [new RoutingOperation { Sequence = 10, WorkCentreId = assembly.Id, Name = "Assemble", RunMinutesPerUnit = 60 }],
        });
        await manufacturing.SaveAsync(new BillOfMaterials
        {
            ProductCode = top.Plu, Lines = [new BomLine { ComponentCode = wood.Plu, Quantity = 2 }],
            Operations = [new RoutingOperation { Sequence = 10, WorkCentreId = saw.Id, Name = "Cut", RunMinutesPerUnit = 30 }],
        });
        var mrp = new MrpService(Data);
        await mrp.SetLeadTimeAsync(supplier.Id, 5);
        var today = DateOnly.FromDateTime(DateTime.Today);
        await using (var ctx = db.CreateContext())
        {
            ctx.CustomerOrders.Add(new CustomerOrder { StoreId = loc.StoreId, LocationId = loc.Id, Plu = table.Plu, Description = "Table", Quantity = 5, OrderDate = today.AddDays(10), Name = "Client" });
            await ctx.SaveChangesAsync();
        }

        var run = await mrp.RunAsync(new MrpOptions(loc.StoreId) { LocationId = loc.Id, DefaultLeadDays = 7, Today = today });
        var by = run.Suggestions.Where(s => s.StockCode == table.Plu || s.StockCode == top.Plu || s.StockCode == leg.Plu || s.StockCode == wood.Plu).ToDictionary(s => s.StockCode);
        Assert.Equal((SuggestionKind.Make, 4m, today.AddDays(10), today.AddDays(9), 0), (by[table.Plu].Kind, by[table.Plu].Quantity, by[table.Plu].NeedBy, by[table.Plu].StartBy, by[table.Plu].Level));
        Assert.Equal((SuggestionKind.Make, 2m, today.AddDays(8), 1), (by[top.Plu].Kind, by[top.Plu].Quantity, by[top.Plu].StartBy, by[top.Plu].Level));
        Assert.Equal((SuggestionKind.Buy, 8m, 5, today.AddDays(4), supplier.Id), (by[leg.Plu].Kind, by[leg.Plu].Quantity, by[leg.Plu].LeadDays, by[leg.Plu].StartBy, by[leg.Plu].SupplierId!.Value));
        Assert.Equal((SuggestionKind.Buy, 4m, 7, 2), (by[wood.Plu].Kind, by[wood.Plu].Quantity, by[wood.Plu].LeadDays, by[wood.Plu].Level));

        var firmed = await mrp.FirmAsync([by[table.Plu].Id, by[leg.Plu].Id, by[wood.Plu].Id], today: today);
        Assert.Empty(firmed.Problems);
        Assert.Equal(2, firmed.Made.Count);
        var planned = Assert.Single(await new ProductionService(Data).ListAsync(loc.StoreId, [ProductionOrderStatus.Planned], table.Plu));
        Assert.Equal((4m, today.AddDays(9)), (planned.Quantity, planned.PlannedStart));
        await using (var ctx = db.CreateContext())
        {
            var po = await ctx.PurchaseOrders.AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.LocationId == loc.Id);
            Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
            Assert.Equal(new[] { leg.Plu, wood.Plu }.Order(), po.Lines.Select(l => l.Plu).Order());
        }
        Assert.Equal(SuggestionStatus.Firmed, (await mrp.SuggestionsAsync(loc.StoreId, null)).Single(s => s.Id == by[leg.Plu].Id).Status);

        // The next run counts the planned order and the purchase order: only the second top is still to make.
        var second = await mrp.RunAsync(new MrpOptions(loc.StoreId) { LocationId = loc.Id, Today = today });
        var mine = second.Suggestions.Where(s => s.StockCode == table.Plu || s.StockCode == top.Plu || s.StockCode == leg.Plu || s.StockCode == wood.Plu).ToList();
        Assert.Equal((top.Plu, 2m), (Assert.Single(mine).StockCode, mine[0].Quantity));
        Assert.Equal(SuggestionStatus.Superseded, (await mrp.SuggestionsAsync(loc.StoreId, null)).Single(s => s.Id == by[top.Plu].Id).Status);

        // The planned order loads the assembly centre with 4 hours.
        var load = await mrp.CapacityAsync(loc.StoreId, today, weeks: 4);
        Assert.Equal(4m, load.Where(r => r.WorkCentreId == assembly.Id).Sum(r => r.LoadHours));
        Assert.Equal(8m * 5, load.First(r => r.WorkCentreId == assembly.Id).CapacityHours);
        Assert.Equal(1, await mrp.DismissAsync([mine[0].Id]));
    }

    // Quality -----------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Inspecting_output_holds_or_scraps_what_failed_and_incoming_goods_are_inspected_on_a_received_order()
    {
        var loc = await LocationAsync();
        var hold = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Hold");
        var part = await ItemAsync(loc.Id, Unique("PT"), 100, 1m);
        var widget = await ItemAsync(loc.Id, Unique("WG"), 0, 0);
        await new ManufacturingService(Data).SaveAsync(new BillOfMaterials { ProductCode = widget.Plu, Lines = [new BomLine { ComponentCode = part.Plu, Quantity = 1 }] });
        var production = new ProductionService(Data);
        var order = await production.CreateAsync(new NewProductionOrder(widget.Plu, 10, loc.Id, loc.Id));
        await production.ReleaseAsync(order.Id);
        await production.IssueAsync(order.Id, part.Plu, 10);
        await production.ReceiveAsync(order.Id, 10, complete: true);

        var quality = new QualityService(Data);
        await quality.SavePlanAsync(new QualityPlan
        {
            ProductCode = widget.Plu, Name = "Widget checks", ForProduction = true, ForIncoming = true,
            Checks =
            [
                new QualityCheck { Name = "Weight", Kind = QualityCheckKind.Number, Minimum = 90, Maximum = 110, Unit = "g" },
                new QualityCheck { Name = "Looks right", Kind = QualityCheckKind.PassFail },
                new QualityCheck { Name = "Remark", Kind = QualityCheckKind.Text, Required = false },
            ],
        });
        await Assert.ThrowsAsync<BusinessRuleException>(() => quality.SavePlanAsync(new QualityPlan { ProductCode = widget.Plu, Name = "Twin", Checks = [new QualityCheck { Name = "X" }] }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => quality.InspectOutputAsync(order.Id, new InspectionRequest(5, 0, [new CheckAnswer(1, Number: 100)])));

        var held = await quality.InspectOutputAsync(order.Id, new InspectionRequest(5, 2, [new CheckAnswer(1, Number: 120), new CheckAnswer(2, PassFail: true)])
        {
            Disposition = InspectionDisposition.Hold, HoldLocationId = hold.Id,
        });
        Assert.False(held.Passed);
        Assert.False(held.Results.Single(r => r.CheckName == "Weight").Passed);
        Assert.Equal(8m, (await ReloadAsync(widget.Id)).OnHand);
        var heldItem = await new InventoryService(Data).FindByCodeAsync(hold.Id, widget.Plu);
        Assert.Equal((2m, 1m), (heldItem.Item!.OnHand, (await ReloadAsync(widget.Id)).AverageCost));

        var scrapped = await quality.InspectOutputAsync(order.Id, new InspectionRequest(8, 1, [new CheckAnswer(1, Number: 100), new CheckAnswer(2, PassFail: true)])
        {
            Disposition = InspectionDisposition.Scrap,
        });
        Assert.True(scrapped.Results.All(r => r.Passed));
        Assert.Equal(7m, (await ReloadAsync(widget.Id)).OnHand);
        await Assert.ThrowsAsync<BusinessRuleException>(() => quality.InspectOutputAsync(order.Id, new InspectionRequest(8, 0, [new CheckAnswer(1, Number: 100), new CheckAnswer(2, PassFail: true)])));
        // Scrapped output goes back to the order's cost: a variance when it closes.
        Assert.Equal(1.00m, (await new ProductionCostingService(Data).SummaryAsync(order.Id))!.Costs.WorkInProgress);
        Assert.Equal(2, (await quality.InspectionsAsync(loc.StoreId, InspectionKind.Production, productionOrderId: order.Id)).Count);

        // Incoming: a purchase order received for the widget.
        var purchasing = new PurchasingService(Data);
        var po = await purchasing.CreateAsync(loc.Id, OrderMethod.Blank, "QC test");
        await purchasing.AddItemAsync(po.Id, widget.Plu, 4);
        await Assert.ThrowsAsync<BusinessRuleException>(() => quality.InspectReceiptAsync(po.Id, widget.Plu, new InspectionRequest(4, 0, [])));
        var line = (await purchasing.GetAsync(po.Id))!.Lines.Single();
        await purchasing.PostShipmentAsync(po.Id, [new ReceivedLine(line.Id, 4, 1m)]);
        var incoming = await quality.InspectReceiptAsync(po.Id, widget.Plu, new InspectionRequest(4, 1, [new CheckAnswer(1, Number: 95), new CheckAnswer(2, PassFail: false)]));
        Assert.Equal((InspectionKind.Incoming, po.Number, false, InspectionDisposition.None), (incoming.Kind, incoming.DocumentNumber!.Value, incoming.Passed, incoming.Disposition));
        Assert.Equal(11m, (await ReloadAsync(widget.Id)).OnHand);
    }

    // Reports -------------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Every_production_report_runs()
    {
        var (loc, _, flour, sugar, _, cake, crumb) = await BakeryAsync(sugarOnHand: 10);
        var production = new ProductionService(Data);
        var order = await production.CreateAsync(new NewProductionOrder(cake.Plu, 20, loc.Id, loc.Id));
        await production.ReleaseAsync(order.Id);
        await production.IssueAsync(order.Id, flour.Plu, 10);
        await production.ScrapMaterialAsync(order.Id, sugar.Plu, 0.5m, "Spilt");
        await production.ReportAsync(order.Id, 10, 150, 19, 1);
        await production.ReceiveAsync(order.Id, 19, complete: true, [new ByProductQuantity(crumb.Plu, 2)]);
        var reports = new ProductionReports(Data);
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var definition in ProductionReports.All)
        {
            var table = await reports.RunAsync(definition.Key, loc.StoreId, today, today);
            Assert.False(string.IsNullOrWhiteSpace(table.Title));
            Assert.All(table.Rows, r => Assert.Equal(table.Columns.Count, r.Length));
        }
        var costs = await reports.RunAsync("production-costs", loc.StoreId, today, today);
        Assert.Contains(costs.Rows, r => (long)r[0]! == order.Number);
        var scrap = await reports.RunAsync("production-scrap", loc.StoreId, today, today);
        Assert.Contains(scrap.Rows, r => (string)r[2]! == "Material thrown away" && (string)r[3]! == sugar.Plu);
        Assert.Contains(scrap.Rows, r => (string)r[2]! == "At operation 10");
        var output = await reports.RunAsync("production-output", loc.StoreId, today, today);
        Assert.True(output.Rows.Sum(r => (decimal)r[3]!) >= 19m);
        await Assert.ThrowsAsync<BusinessRuleException>(() => reports.RunAsync("nothing", loc.StoreId, today, today));
    }
}

public sealed class ManufacturingTestsOnSqlite(SqliteFixture db) : ManufacturingTests(db), IClassFixture<SqliteFixture>;

public sealed class ManufacturingTestsOnSqlServer(SqlServerFixture db) : ManufacturingTests(db), IClassFixture<SqlServerFixture>;
