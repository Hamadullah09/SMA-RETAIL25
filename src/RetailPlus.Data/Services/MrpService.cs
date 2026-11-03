using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>What a planning run looks at.</summary>
public sealed record MrpOptions(Guid StoreId)
{
    /// <summary>Where firmed orders make and receive; the store's first location when null. Stock at every location of the store counts.</summary>
    public Guid? LocationId { get; init; }
    /// <summary>Customer orders and production due later than this many days from today are left for a later run.</summary>
    public int HorizonDays { get; init; } = 60;
    /// <summary>The lead time of suppliers without one of their own.</summary>
    public int DefaultLeadDays { get; init; } = 7;
    public DateOnly? Today { get; init; }
}

/// <summary>A planning run and what it suggests.</summary>
public sealed record MrpResult(MrpRun Run, IReadOnlyList<MrpSuggestion> Suggestions);

/// <summary>What firming made: production orders, purchase orders, and what could not be firmed (with why).</summary>
public sealed record FirmResult(IReadOnlyList<string> Made, IReadOnlyList<string> Problems);

/// <summary>A work centre's load in one week (Monday to Sunday), in hours, against what it can do.</summary>
public sealed record CapacityRow(Guid WorkCentreId, string Code, string Name, DateOnly WeekStart, decimal CapacityHours, decimal LoadHours)
{
    public decimal LoadPercent => CapacityHours == 0 ? (LoadHours == 0 ? 0 : 100) : Math.Round(LoadHours / CapacityHours * 100m, 1, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Planning (MRP) for a store. Demand comes from open customer orders, items below their reorder point (or base stock),
/// and production orders (their components still to issue; for planned orders, from their bill). Bills are exploded over
/// every level, lowest level last, and each stock code is netted against the store's stock, its open purchase orders
/// and its open production orders. What is still needed becomes a suggestion: make it (it has a bill in force) or buy it,
/// offset by its lead time: the supplier's, or the production time from the routing and the work centres' capacity. A
/// suggestion changes nothing until it is firmed into a planned production order or a purchase order.
/// </summary>
public sealed class MrpService(DataContext data)
{
    private sealed record Demand(decimal Quantity, DateOnly NeedBy, string Reason);

    public async Task<MrpResult> RunAsync(MrpOptions options)
    {
        if (options.HorizonDays is < 1 or > 730) throw new BusinessRuleException("Plan 1 to 730 days ahead.");
        if (options.DefaultLeadDays is < 0 or > 365) throw new BusinessRuleException("A lead time is 0 to 365 days.");
        var today = options.Today ?? DateOnly.FromDateTime(DateTime.Today);
        var horizon = today.AddDays(options.HorizonDays);
        await using var db = data.Create();
        var locations = await db.Locations.AsNoTracking().Where(l => l.StoreId == options.StoreId).OrderBy(l => l.Code).ToListAsync();
        if (locations.Count == 0) throw new BusinessRuleException("The store has no locations.");
        var runLocation = options.LocationId is Guid wanted
            ? locations.SingleOrDefault(l => l.Id == wanted) ?? throw new BusinessRuleException("That location is not one of the store's.")
            : locations[0];
        var locationIds = locations.Select(l => l.Id).ToList();

        // Stock: every location of the store, per stock code.
        var items = await db.Items.AsNoTracking().Where(i => locationIds.Contains(i.LocationId) && !i.IsDeleted)
            .Select(i => new { i.Plu, i.Name, i.LocationId, i.OnHand, i.ReorderPoint, i.ReorderQuantity, i.BaseStock, i.SupplierId }).ToListAsync();
        var stock = items.GroupBy(i => i.Plu, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => new
        {
            Name = g.First().Name,
            OnHand = g.Sum(i => i.OnHand),
            ReorderPoint = g.Sum(i => i.ReorderPoint),
            ReorderQuantity = g.Sum(i => i.ReorderQuantity),
            BaseStock = g.Sum(i => i.BaseStock),
            SupplierId = g.OrderBy(i => i.LocationId == runLocation.Id ? 0 : 1).Select(i => i.SupplierId).FirstOrDefault(s => s != null),
        }, StringComparer.OrdinalIgnoreCase);

        // Bills in force today, by product.
        var bills = await db.BillsOfMaterials.AsNoTracking().Include(b => b.Lines).Include(b => b.Operations).ThenInclude(o => o.WorkCentre)
            .Where(b => b.IsActive).AsSplitQuery().ToListAsync();
        var billOf = bills.Where(b => b.InForce(today)).GroupBy(b => b.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.Revision).First(), StringComparer.OrdinalIgnoreCase);

        var demand = new Dictionary<string, List<Demand>>(StringComparer.OrdinalIgnoreCase);
        void Need(string code, decimal qty, DateOnly by, string reason)
        {
            if (qty <= 0) return;
            (demand.TryGetValue(code, out var list) ? list : demand[code] = []).Add(new Demand(qty, by < today ? today : by, reason));
        }
        var supply = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        void Supply(string code, decimal qty)
        {
            if (qty > 0) supply[code] = supply.GetValueOrDefault(code) + qty;
        }

        // Open customer orders.
        var customerOrders = await db.CustomerOrders.AsNoTracking().Where(c => c.StoreId == options.StoreId && !c.Paid && c.Plu != null && c.Quantity > 0).ToListAsync();
        foreach (var c in customerOrders)
        {
            var by = c.OrderDate ?? today;
            if (by > horizon) continue;
            Need(c.Plu!.Trim().ToUpperInvariant(), c.Quantity, by, $"Customer order {c.InvoiceNumber} ({c.Name ?? c.Company})".Trim());
        }

        // Open purchase orders (drafts too: they are what someone means to buy).
        var poLines = await db.PurchaseOrderLines.AsNoTracking()
            .Join(db.PurchaseOrders.AsNoTracking().Where(p => p.StoreId == options.StoreId && p.Status != PurchaseOrderStatus.Received), l => l.PurchaseOrderId, p => p.Id,
                (l, p) => new { l.Plu, l.OrderQuantity, l.CaseQuantity, l.QuantityReceived })
            .ToListAsync();
        foreach (var l in poLines) Supply(l.Plu, l.OrderQuantity * (l.CaseQuantity <= 0 ? 1 : l.CaseQuantity) - l.QuantityReceived);

        // Open production orders: their output is supply, their components still to issue are demand.
        var orders = await db.ProductionOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.StoreId == options.StoreId && (o.Status == ProductionOrderStatus.Planned || o.Status == ProductionOrderStatus.Released || o.Status == ProductionOrderStatus.InProgress))
            .ToListAsync();
        var orderIds = orders.Select(o => o.Id).ToList();
        var moved = await db.StockMovements.AsNoTracking().Where(m => m.ProductionOrderId != null && orderIds.Contains(m.ProductionOrderId.Value))
            .Join(db.Items.AsNoTracking(), m => m.ItemId, i => i.Id, (m, i) => new { Order = m.ProductionOrderId!.Value, i.Plu, m.Kind, m.Quantity }).ToListAsync();
        foreach (var o in orders)
        {
            var mine = moved.Where(m => m.Order == o.Id).ToList();
            decimal made = mine.Where(m => m.Kind == StockMovementKind.ProductionReceipt && string.Equals(m.Plu, o.ProductCode, StringComparison.OrdinalIgnoreCase)).Sum(m => m.Quantity);
            Supply(o.ProductCode, o.Quantity - made);
            var by = o.PlannedStart ?? today;
            if (o.Status == ProductionOrderStatus.Planned)
            {
                if (o.BillOfMaterialsId is Guid id && bills.FirstOrDefault(b => b.Id == id) is { } bom)
                    foreach (var l in bom.Lines)
                        Need(l.ComponentCode, Math.Round(l.Quantity / bom.YieldQuantity * (1 + l.ScrapPercent / 100m) * (o.Quantity - made), 3, MidpointRounding.AwayFromZero), by,
                            $"Production order {o.Number} ({o.ProductCode})");
            }
            else
            {
                foreach (var l in o.Lines.Where(l => l.Kind == ProductionLineKind.Component))
                {
                    decimal issued = -mine.Where(m => string.Equals(m.Plu, l.StockCode, StringComparison.OrdinalIgnoreCase)
                                                      && m.Kind is StockMovementKind.ProductionIssue or StockMovementKind.ProductionScrap).Sum(m => m.Quantity);
                    Need(l.StockCode, l.QuantityPlanned - issued, by, $"Production order {o.Number} ({o.ProductCode})");
                }
            }
        }

        // Low-level codes: a code is planned after every code whose bill uses it.
        var level = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in stock.Keys.Concat(demand.Keys).Concat(billOf.Keys)) level.TryAdd(code, 0);
        for (bool changed = true, guard = false; changed && !guard;)
        {
            changed = false;
            foreach (var bom in billOf.Values)
                foreach (var l in bom.Lines)
                {
                    int want = level.GetValueOrDefault(bom.ProductCode) + 1;
                    if (level.GetValueOrDefault(l.ComponentCode) < want)
                    {
                        level[l.ComponentCode] = want;
                        changed = true;
                    }
                }
            guard = level.Values.DefaultIfEmpty().Max() > 64;
        }

        var leadTimes = await db.SupplierLeadTimes.AsNoTracking().ToDictionaryAsync(t => t.SupplierId, t => t.Days);
        var supplierNames = await db.Suppliers.AsNoTracking().Select(s => new { s.Id, s.Company }).ToDictionaryAsync(s => s.Id, s => s.Company);

        var run = new MrpRun
        {
            StoreId = options.StoreId, LocationId = runLocation.Id, RunAtUtc = SyncColumns.Now(), RunBy = ManufacturingService.Cut(data.CurrentUser?.UserName, 50),
            HorizonDays = options.HorizonDays, DefaultLeadDays = options.DefaultLeadDays,
        };
        run.Id = Guid.CreateVersion7();
        var suggestions = new List<MrpSuggestion>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Codes added by explosion get planned when their level comes up, so the loop takes the lowest level not yet planned each time.
        while (true)
        {
            var next = level.Where(l => !done.Contains(l.Key)).OrderBy(l => l.Value).ThenBy(l => l.Key, StringComparer.OrdinalIgnoreCase).Select(l => l.Key).FirstOrDefault();
            if (next is null) break;
            done.Add(next);
            var needs = demand.GetValueOrDefault(next) ?? [];
            stock.TryGetValue(next, out var have);
            decimal gross = needs.Sum(d => d.Quantity);
            decimal available = Math.Max(0, have?.OnHand ?? 0) + supply.GetValueOrDefault(next);
            decimal shortfall = Math.Max(0, gross - available);
            decimal after = available + shortfall - gross;
            string reason = needs.Count == 0 ? "" : string.Join("; ", needs.Select(d => d.Reason).Distinct().Take(3)) + (needs.Select(d => d.Reason).Distinct().Count() > 3 ? "; …" : "");
            if (have is { ReorderPoint: > 0 } && after < have.ReorderPoint)
            {
                shortfall += have.ReorderQuantity > 0 ? have.ReorderQuantity : Math.Max(have.ReorderPoint, have.BaseStock) - after;
                reason = (reason.Length == 0 ? "" : reason + "; ") + "below its reorder point";
            }
            if (shortfall <= 0) continue;
            shortfall = Math.Round(shortfall, 3, MidpointRounding.AwayFromZero);
            var needBy = needs.Count == 0 ? today : needs.Min(d => d.NeedBy);
            var suggestion = new MrpSuggestion
            {
                StoreId = options.StoreId, RunId = run.Id, Status = SuggestionStatus.Open, StockCode = next.ToUpperInvariant(),
                Description = ManufacturingService.Cut(have?.Name ?? billOf.GetValueOrDefault(next)?.ProductName ?? next, 60)!, LocationId = runLocation.Id,
                Quantity = shortfall, NeedBy = needBy, Level = level[next], Reason = ManufacturingService.Cut(reason, 200),
            };
            if (billOf.TryGetValue(next, out var bill))
            {
                suggestion.Kind = SuggestionKind.Make;
                suggestion.BillOfMaterialsId = bill.Id;
                suggestion.LeadDays = ProductionDays(bill, shortfall);
                suggestion.StartBy = needBy.AddDays(-suggestion.LeadDays);
                foreach (var l in bill.Lines)
                {
                    Need(l.ComponentCode, Math.Round(l.Quantity / bill.YieldQuantity * (1 + l.ScrapPercent / 100m) * shortfall, 3, MidpointRounding.AwayFromZero),
                        suggestion.StartBy, $"To make {shortfall:0.###} x {bill.ProductCode}");
                    level.TryAdd(l.ComponentCode, level[next] + 1);
                }
            }
            else
            {
                suggestion.Kind = SuggestionKind.Buy;
                suggestion.SupplierId = have?.SupplierId;
                suggestion.SupplierName = have?.SupplierId is Guid s ? ManufacturingService.Cut(supplierNames.GetValueOrDefault(s), 60) : null;
                suggestion.LeadDays = have?.SupplierId is Guid sid && leadTimes.TryGetValue(sid, out var days) ? days : options.DefaultLeadDays;
                suggestion.StartBy = needBy.AddDays(-suggestion.LeadDays);
            }
            suggestions.Add(suggestion);
        }

        // The run replaces what earlier runs (and released orders' shortages) suggested and nobody firmed.
        foreach (var old in await db.MrpSuggestions.Where(s => s.StoreId == options.StoreId && s.Status == SuggestionStatus.Open).ToListAsync())
            old.Status = SuggestionStatus.Superseded;
        run.Suggestions = suggestions.Count;
        db.MrpRuns.Add(run);
        db.MrpSuggestions.AddRange(suggestions);
        db.Audit(AuditActions.PlanningRun, $"Store planning run: {suggestions.Count(s => s.Kind == SuggestionKind.Make)} to make, {suggestions.Count(s => s.Kind == SuggestionKind.Buy)} to buy",
            nameof(MrpRun), run.Id);
        await db.SaveChangesAsync();
        return new MrpResult(run, [.. suggestions.OrderBy(s => s.Level).ThenBy(s => s.StartBy).ThenBy(s => s.StockCode)]);
    }

    /// <summary>The working days it takes to make <paramref name="quantity"/>: each operation's hours over its work centre's hours a day, one after the other.</summary>
    public static int ProductionDays(BillOfMaterials bill, decimal quantity)
    {
        decimal days = 0;
        foreach (var o in bill.Operations)
        {
            decimal hours = (o.SetupMinutes + o.RunMinutesPerUnit * quantity) / 60m;
            decimal perDay = o.WorkCentre is { CapacityHoursPerDay: > 0 } centre ? centre.CapacityHoursPerDay : 8;
            days += hours / perDay;
        }
        return (int)Math.Ceiling(days);
    }

    /// <summary>The latest run of a store.</summary>
    public async Task<MrpRun?> LatestRunAsync(Guid store)
    {
        await using var db = data.Create();
        return await db.MrpRuns.AsNoTracking().Where(r => r.StoreId == store).OrderByDescending(r => r.RunAtUtc).FirstOrDefaultAsync();
    }

    /// <summary>A store's suggestions (null: every store's), open ones by default, lowest level and earliest start first.</summary>
    public async Task<List<MrpSuggestion>> SuggestionsAsync(Guid? store, SuggestionStatus? status = SuggestionStatus.Open, SuggestionKind? kind = null)
    {
        await using var db = data.Create();
        var q = db.MrpSuggestions.AsNoTracking();
        if (store is Guid s) q = q.Where(x => x.StoreId == s);
        if (status is { } st) q = q.Where(x => x.Status == st);
        if (kind is { } k) q = q.Where(x => x.Kind == k);
        var rows = await q.ToListAsync();
        return [.. rows.OrderBy(x => x.Level).ThenBy(x => x.StartBy).ThenBy(x => x.StockCode, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Firms suggestions: each "make" becomes a planned production order (starting on its start date, or today when that
    /// passed); the "buy" ones become draft purchase orders, one per location, with a line each. The quantity of each can
    /// be changed on the way (<paramref name="quantities"/>).
    /// </summary>
    public async Task<FirmResult> FirmAsync(IReadOnlyCollection<Guid> ids, IReadOnlyDictionary<Guid, decimal>? quantities = null, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        List<MrpSuggestion> chosen;
        await using (var db = data.Create())
            chosen = await db.MrpSuggestions.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync();
        if (chosen.Count == 0) throw new BusinessRuleException("Choose the suggestions to firm.");
        var made = new List<string>();
        var problems = new List<string>();
        var firmed = new Dictionary<Guid, (Guid Id, string Document)>();
        decimal QuantityOf(MrpSuggestion s) => quantities is not null && quantities.TryGetValue(s.Id, out var q) && q > 0 ? q : s.Quantity;
        foreach (var s in chosen.Where(s => s.Status != SuggestionStatus.Open))
            problems.Add($"{s.StockCode}: the suggestion is {s.Status.ToString().ToLowerInvariant()}.");

        var open = chosen.Where(s => s.Status == SuggestionStatus.Open).ToList();
        foreach (var s in open.Where(s => s.Kind == SuggestionKind.Make))
        {
            decimal qty = QuantityOf(s);
            try
            {
                var order = await new ProductionService(ForStore(s.StoreId)).CreateAsync(new NewProductionOrder(s.StockCode, qty, s.LocationId, s.LocationId)
                {
                    BillOfMaterialsId = s.BillOfMaterialsId, PlannedStart = s.StartBy < day ? day : s.StartBy, PlannedEnd = s.NeedBy < day ? day : s.NeedBy,
                    Notes = s.Reason,
                });
                firmed[s.Id] = (order.Id, $"Production order {order.Number}");
                made.Add($"Production order {order.Number}: {qty:0.###} x {s.StockCode}");
            }
            catch (BusinessRuleException ex)
            {
                problems.Add($"{s.StockCode}: {ex.Message}");
            }
        }
        foreach (var group in open.Where(s => s.Kind == SuggestionKind.Buy).GroupBy(s => (s.StoreId, s.LocationId)))
        {
            var purchasing = new PurchasingService(ForStore(group.Key.StoreId, numbered: true));
            PurchaseOrder? po = null;
            foreach (var s in group.OrderBy(s => s.SupplierName).ThenBy(s => s.StockCode))
            {
                decimal qty = QuantityOf(s);
                po ??= await purchasing.CreateAsync(group.Key.LocationId, OrderMethod.Blank, "From planning");
                if (await purchasing.AddItemAsync(po.Id, s.StockCode, qty))
                    firmed[s.Id] = (po.Id, $"Purchase order {po.Number}");
                else
                    problems.Add($"{s.StockCode}: there is no item with this code at the location to order.");
            }
            if (po is not null && firmed.Values.Any(f => f.Id == po.Id)) made.Add($"Purchase order {po.Number} (draft): {firmed.Count(f => f.Value.Id == po.Id)} line(s)");
            else if (po is not null) await purchasing.DeleteAsync(po.Id);
        }

        await using (var db = data.Create())
        {
            foreach (var row in await db.MrpSuggestions.Where(s => firmed.Keys.Contains(s.Id)).ToListAsync())
            {
                var (id, document) = firmed[row.Id];
                row.Status = SuggestionStatus.Firmed;
                row.FirmedId = id;
                row.FirmedDocument = document;
                row.FirmedAtUtc = SyncColumns.Now();
                row.FirmedBy = ManufacturingService.Cut(data.CurrentUser?.UserName, 50);
                row.Quantity = QuantityOf(row);
            }
            if (made.Count > 0) db.Audit(AuditActions.PlanningFirm, string.Join("; ", made), nameof(MrpSuggestion));
            await db.SaveChangesAsync();
        }
        return new FirmResult(made, problems);
    }

    /// <summary>Dismisses open suggestions (nobody will act on them).</summary>
    public async Task<int> DismissAsync(IReadOnlyCollection<Guid> ids)
    {
        await using var db = data.Create();
        var rows = await db.MrpSuggestions.Where(s => ids.Contains(s.Id) && s.Status == SuggestionStatus.Open).ToListAsync();
        foreach (var row in rows) row.Status = SuggestionStatus.Dismissed;
        await db.SaveChangesAsync();
        return rows.Count;
    }

    // Lead times ----------------------------------------------------------------------------------------------------------

    public async Task<Dictionary<Guid, int>> LeadTimesAsync()
    {
        await using var db = data.Create();
        return await db.SupplierLeadTimes.AsNoTracking().ToDictionaryAsync(t => t.SupplierId, t => t.Days);
    }

    /// <summary>Sets a supplier's lead time in days (null removes it, so the run's default applies).</summary>
    public async Task SetLeadTimeAsync(Guid supplierId, int? days)
    {
        if (days is < 0 or > 365) throw new BusinessRuleException("A lead time is 0 to 365 days.");
        await using var db = data.Create();
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId)) throw new BusinessRuleException("That supplier no longer exists.");
        var row = await db.SupplierLeadTimes.SingleOrDefaultAsync(t => t.SupplierId == supplierId);
        if (days is null)
        {
            if (row is not null) db.SupplierLeadTimes.Remove(row);
        }
        else
        {
            if (row is null) db.SupplierLeadTimes.Add(row = new SupplierLeadTime { SupplierId = supplierId });
            row.Days = days.Value;
        }
        await db.SaveChangesAsync();
    }

    // Capacity ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The load of a store's work centres week by week, from <paramref name="from"/>'s week on: the time still to work on
    /// open production orders (their operations not finished, the rest of their quantity; planned orders by their bill's
    /// routing), spread evenly over each order's planned days (from today for what is late), and optionally the open
    /// suggestions to make (over their start to need-by dates).
    /// </summary>
    public async Task<List<CapacityRow>> CapacityAsync(Guid store, DateOnly? from = null, int weeks = 8, bool includeSuggestions = false)
    {
        if (weeks is < 1 or > 52) throw new BusinessRuleException("Show 1 to 52 weeks.");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var first = TimeClock.WeekStart(from ?? today);
        var last = first.AddDays(7 * weeks - 1);
        await using var db = data.Create();
        var centres = await db.WorkCentres.AsNoTracking().Where(w => w.StoreId == store && w.IsActive).OrderBy(w => w.Code).ToListAsync();
        var load = new Dictionary<(Guid, DateOnly), decimal>();
        void Spread(Guid? centre, decimal hours, DateOnly start, DateOnly end)
        {
            if (centre is not Guid c || hours <= 0) return;
            if (start < today) start = today;
            if (end < start) end = start;
            int days = end.DayNumber - start.DayNumber + 1;
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                if (d < first || d > last) continue;
                var key = (c, TimeClock.WeekStart(d));
                load[key] = load.GetValueOrDefault(key) + hours / days;
            }
        }

        var orders = await db.ProductionOrders.AsNoTracking().Include(o => o.Operations)
            .Where(o => o.StoreId == store && (o.Status == ProductionOrderStatus.Planned || o.Status == ProductionOrderStatus.Released || o.Status == ProductionOrderStatus.InProgress))
            .ToListAsync();
        var orderIds = orders.Select(o => o.Id).ToList();
        var good = (await db.ProductionReports.AsNoTracking().Where(r => orderIds.Contains(r.ProductionOrderId))
                .Select(r => new { r.ProductionOrderId, r.OperationSequence, r.GoodQuantity, r.ScrapQuantity }).ToListAsync())
            .GroupBy(r => (r.ProductionOrderId, r.OperationSequence)).ToDictionary(g => g.Key, g => g.Sum(r => r.GoodQuantity + r.ScrapQuantity));
        var bomIds = orders.Where(o => o.Status == ProductionOrderStatus.Planned && o.BillOfMaterialsId != null).Select(o => o.BillOfMaterialsId!.Value).ToList();
        var routings = (await db.RoutingOperations.AsNoTracking().Where(r => bomIds.Contains(r.BillOfMaterialsId)).ToListAsync())
            .GroupBy(r => r.BillOfMaterialsId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var o in orders)
        {
            var start = o.PlannedStart ?? today;
            var end = o.PlannedEnd ?? start;
            if (o.Status == ProductionOrderStatus.Planned)
            {
                foreach (var r in o.BillOfMaterialsId is Guid b ? routings.GetValueOrDefault(b) ?? [] : [])
                    Spread(r.WorkCentreId, (r.SetupMinutes + r.RunMinutesPerUnit * o.Quantity) / 60m, start, end);
                continue;
            }
            foreach (var op in o.Operations.Where(op => op.Status != OperationStatus.Done))
            {
                decimal left = Math.Max(0, o.Quantity - good.GetValueOrDefault((o.Id, op.Sequence)));
                decimal minutes = (op.Status == OperationStatus.Waiting ? op.SetupMinutes : 0) + op.RunMinutesPerUnit * left;
                Spread(op.WorkCentreId, minutes / 60m, start, end);
            }
        }
        if (includeSuggestions)
        {
            var makes = await db.MrpSuggestions.AsNoTracking().Where(s => s.StoreId == store && s.Status == SuggestionStatus.Open && s.Kind == SuggestionKind.Make && s.BillOfMaterialsId != null)
                .ToListAsync();
            var ids = makes.Select(s => s.BillOfMaterialsId!.Value).Distinct().ToList();
            var ops = (await db.RoutingOperations.AsNoTracking().Where(r => ids.Contains(r.BillOfMaterialsId)).ToListAsync()).GroupBy(r => r.BillOfMaterialsId)
                .ToDictionary(g => g.Key, g => g.ToList());
            foreach (var s in makes)
                foreach (var r in ops.GetValueOrDefault(s.BillOfMaterialsId!.Value) ?? [])
                    Spread(r.WorkCentreId, (r.SetupMinutes + r.RunMinutesPerUnit * s.Quantity) / 60m, s.StartBy, s.NeedBy);
        }

        var rows = new List<CapacityRow>();
        foreach (var c in centres)
            for (int w = 0; w < weeks; w++)
            {
                var week = first.AddDays(7 * w);
                rows.Add(new CapacityRow(c.Id, c.Code, c.Name, week, c.CapacityHoursPerDay * c.DaysPerWeek, Math.Round(load.GetValueOrDefault((c.Id, week)), 2, MidpointRounding.AwayFromZero)));
            }
        return rows;
    }

    /// <summary>Services for a store; with <paramref name="numbered"/> on the server's own station, for documents numbered there.</summary>
    private DataContext ForStore(Guid store, bool numbered = false)
    {
        if (data.Station is { StationNumber: > 0 } here && here.StoreId == store) return data;
        return data.For(new StationIdentity(store, numbered ? ProductionService.ServerStation : 0), data.CurrentUser);
    }
}
