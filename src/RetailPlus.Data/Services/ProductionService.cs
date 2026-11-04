using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A new production order: what to make, how many, and where materials come from and output goes.</summary>
public sealed record NewProductionOrder(string ProductCode, decimal Quantity, Guid MaterialLocationId, Guid OutputLocationId)
{
    /// <summary>The bill to make it by; the product's bill in force on the planned start when null.</summary>
    public Guid? BillOfMaterialsId { get; init; }
    public DateOnly? PlannedStart { get; init; }
    public DateOnly? PlannedEnd { get; init; }
    public int Priority { get; init; } = 5;
    public Guid? CustomerOrderId { get; init; }
    public string? Notes { get; init; }
}

/// <summary>A production order in a list, with how much of it was made.</summary>
public sealed record ProductionOrderRow(Guid Id, Guid StoreId, long Number, ProductionOrderStatus Status, string ProductCode, string ProductName, decimal Quantity,
    decimal Received, string MaterialLocation, string OutputLocation, DateOnly? PlannedStart, DateOnly? PlannedEnd, int Priority, Guid? CustomerOrderId, int BomRevision);

/// <summary>A component the release could not set aside in full.</summary>
public sealed record Shortage(string StockCode, string Description, decimal Required, decimal Available, decimal Missing, bool IsMade);

public sealed record ReleaseResult(ProductionOrder Order, IReadOnlyList<Shortage> Shortages);

/// <summary>A by-product received with an order's completion.</summary>
public sealed record ByProductQuantity(string StockCode, decimal Quantity);

/// <summary>
/// What an order cost so far, from its stock movements and reports: material issued (and scrapped), output scrapped back
/// into it, labour, machine and overhead applied, and the value received into stock. What is charged and not received is
/// its work in progress.
/// </summary>
public sealed record OrderCosts(decimal Material, decimal OutputScrap, decimal Labour, decimal Machine, decimal HourlyOverhead, decimal MaterialOverhead, decimal Received)
{
    public decimal Conversion => Labour + Machine + HourlyOverhead + MaterialOverhead;
    public decimal Charged => Material + OutputScrap + Conversion;
    public decimal WorkInProgress => Charged - Received;
}

/// <summary>An operation of an order with what was reported on it.</summary>
public sealed record OperationProgress(int Sequence, string Name, string WorkCentre, OperationStatus Status, decimal StandardMinutes, decimal Minutes,
    decimal Good, decimal Scrap, DateTime? StartedAt, DateTime? FinishedAt);

/// <summary>A line of an order with how much was issued (components) or received (by-products).</summary>
public sealed record LineProgress(Guid LineId, int LineNumber, ProductionLineKind Kind, string StockCode, string Description, decimal Planned, decimal Done,
    decimal Reserved, IssueMethod IssueMethod, int? OperationSequence, decimal StandardUnitCost);

/// <summary>Everything the floor and the web show about an order.</summary>
public sealed record OrderProgress(ProductionOrder Order, decimal Received, decimal OutputScrapped, decimal ReportedScrap, IReadOnlyList<LineProgress> Lines,
    IReadOnlyList<OperationProgress> Operations, OrderCosts Costs, IReadOnlyList<StockMovement> Movements, IReadOnlyList<ProductionReport> Reports,
    IReadOnlyDictionary<Guid, string> ItemCodes);

/// <summary>
/// Production orders from planning to closing: make (planned), release (copy the bill and routing, reserve material,
/// report shortages to purchasing), issue material by scan or list, start and finish operations, report time, good and
/// scrapped quantities, receive output at cost (and by-products), backflush at completion, close and cancel. Works the
/// same at a till (the shop floor, offline) and on the server; every quantity issued or received is a stock movement
/// of the order, never a count on the order, so tills never overwrite each other. Posting to the ledger is done on the
/// server by <see cref="ProductionCostingService"/>.
/// </summary>
public sealed class ProductionService(DataContext data)
{
    /// <summary>Where the server numbers its own documents (no till has it).</summary>
    public const int ServerStation = 1000;

    // Reading ---------------------------------------------------------------------------------------------------------

    /// <summary>Orders of one store (null: every store), newest first, optionally of some statuses or matching a number or product.</summary>
    public async Task<List<ProductionOrderRow>> ListAsync(Guid? store = null, IReadOnlyCollection<ProductionOrderStatus>? statuses = null, string? search = null, int take = 1000)
    {
        await using var db = data.Create();
        var q = Query(db, store, statuses, search);
        var orders = await q.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Number).Take(take).ToListAsync();
        return await RowsAsync(db, orders);
    }

    /// <summary>The orders a list shows (for paging on the server).</summary>
    public static IQueryable<ProductionOrder> Query(RetailPlusDbContext db, Guid? store, IReadOnlyCollection<ProductionOrderStatus>? statuses, string? search)
    {
        var q = db.ProductionOrders.AsNoTracking().Include(o => o.MaterialLocation).Include(o => o.OutputLocation).AsQueryable();
        if (store is Guid s) q = q.Where(o => o.StoreId == s);
        if (statuses is { Count: > 0 }) q = q.Where(o => statuses.Contains(o.Status));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim();
            q = long.TryParse(t, out var number) ? q.Where(o => o.Number == number) : q.Where(o => o.ProductCode.Contains(t) || o.ProductName.Contains(t));
        }
        return q;
    }

    /// <summary>List rows for orders, with the quantity received of each.</summary>
    public static async Task<List<ProductionOrderRow>> RowsAsync(RetailPlusDbContext db, IReadOnlyList<ProductionOrder> orders)
    {
        var ids = orders.Select(o => o.Id).ToList();
        var receipts = await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductionOrderId != null && ids.Contains(m.ProductionOrderId.Value) && m.Kind == StockMovementKind.ProductionReceipt)
            .Join(db.Items.AsNoTracking(), m => m.ItemId, i => i.Id, (m, i) => new { Order = m.ProductionOrderId!.Value, i.Plu, m.Quantity })
            .ToListAsync();
        return [.. orders.Select(o => new ProductionOrderRow(o.Id, o.StoreId, o.Number, o.Status, o.ProductCode, o.ProductName, o.Quantity,
            receipts.Where(r => r.Order == o.Id && string.Equals(r.Plu, o.ProductCode, StringComparison.OrdinalIgnoreCase)).Sum(r => r.Quantity),
            o.MaterialLocation?.Code ?? "", o.OutputLocation?.Code ?? "", o.PlannedStart, o.PlannedEnd, o.Priority, o.CustomerOrderId, o.BomRevision))];
    }

    /// <summary>The released and started orders the shop floor at a location works on (its material or output location), most urgent first.</summary>
    public async Task<List<ProductionOrderRow>> FloorOrdersAsync(Guid locationId)
    {
        await using var db = data.Create();
        var orders = await db.ProductionOrders.AsNoTracking().Include(o => o.MaterialLocation).Include(o => o.OutputLocation)
            .Where(o => (o.MaterialLocationId == locationId || o.OutputLocationId == locationId)
                        && (o.Status == ProductionOrderStatus.Released || o.Status == ProductionOrderStatus.InProgress))
            .ToListAsync();
        return await RowsAsync(db, [.. orders.OrderBy(o => o.Priority).ThenBy(o => o.PlannedStart ?? DateOnly.MaxValue).ThenBy(o => o.Number)]);
    }

    public async Task<ProductionOrder?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        return await LoadAsync(db, id, tracked: false);
    }

    private static async Task<ProductionOrder?> LoadAsync(RetailPlusDbContext db, Guid id, bool tracked)
    {
        var q = db.ProductionOrders.Include(o => o.Lines).Include(o => o.Operations).Include(o => o.MaterialLocation).Include(o => o.OutputLocation).AsSplitQuery();
        var order = await (tracked ? q : q.AsNoTracking()).SingleOrDefaultAsync(o => o.Id == id);
        if (order is null) return null;
        order.Lines = [.. order.Lines.OrderBy(l => l.Kind).ThenBy(l => l.LineNumber)];
        order.Operations = [.. order.Operations.OrderBy(o => o.Sequence)];
        return order;
    }

    /// <summary>An order with what was issued, reported and received on it, and what it cost so far.</summary>
    public async Task<OrderProgress?> ProgressAsync(Guid id)
    {
        await using var db = data.Create();
        var order = await LoadAsync(db, id, tracked: false);
        return order is null ? null : await ProgressAsync(db, order);
    }

    internal static async Task<OrderProgress> ProgressAsync(RetailPlusDbContext db, ProductionOrder order)
    {
        var movements = await db.StockMovements.AsNoTracking().Where(m => m.ProductionOrderId == order.Id).OrderBy(m => m.At).ThenBy(m => m.CreatedAtUtc).ToListAsync();
        var reports = await db.ProductionReports.AsNoTracking().Where(r => r.ProductionOrderId == order.Id).OrderBy(r => r.At).ToListAsync();
        var itemIds = movements.Select(m => m.ItemId).Distinct().ToList();
        var codes = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Plu);
        string CodeOf(StockMovement m) => codes.GetValueOrDefault(m.ItemId) ?? "";
        bool IsOutput(string code) => string.Equals(code, order.ProductCode, StringComparison.OrdinalIgnoreCase)
                                      || order.Lines.Any(l => l.Kind == ProductionLineKind.ByProduct && string.Equals(l.StockCode, code, StringComparison.OrdinalIgnoreCase));
        decimal Value(StockMovement m) => m.Quantity * m.UnitCost;

        var used = movements.Where(m => m.Kind is StockMovementKind.ProductionIssue or StockMovementKind.ProductionScrap).ToList();
        decimal material = Money.Round(-used.Where(m => !IsOutput(CodeOf(m))).Sum(Value));
        decimal outputScrap = Money.Round(-used.Where(m => IsOutput(CodeOf(m))).Sum(Value));
        decimal received = Money.Round(movements.Where(m => m.Kind == StockMovementKind.ProductionReceipt).Sum(Value));
        var costs = new OrderCosts(material, outputScrap, Money.Round(reports.Sum(r => r.LabourCost)), Money.Round(reports.Sum(r => r.MachineCost)),
            Money.Round(reports.Sum(r => r.OverheadCost)), Money.Round(material * order.OverheadPercent / 100m), received);

        var lines = order.Lines.Select(l =>
        {
            var mine = movements.Where(m => string.Equals(CodeOf(m), l.StockCode, StringComparison.OrdinalIgnoreCase));
            decimal done = l.Kind == ProductionLineKind.Component
                ? -mine.Where(m => m.Kind is StockMovementKind.ProductionIssue or StockMovementKind.ProductionScrap).Sum(m => m.Quantity)
                : mine.Where(m => m.Kind == StockMovementKind.ProductionReceipt).Sum(m => m.Quantity);
            return new LineProgress(l.Id, l.LineNumber, l.Kind, l.StockCode, l.Description, l.QuantityPlanned, done, l.QuantityReserved, l.IssueMethod,
                l.OperationSequence, l.StandardUnitCost);
        }).ToList();
        var product = movements.Where(m => string.Equals(CodeOf(m), order.ProductCode, StringComparison.OrdinalIgnoreCase)).ToList();
        var ops = order.Operations.Select(o =>
        {
            var mine = reports.Where(r => r.OperationSequence == o.Sequence).ToList();
            return new OperationProgress(o.Sequence, o.Name, o.WorkCentreName, o.Status, o.StandardMinutes(order.Quantity), mine.Sum(r => r.Minutes),
                mine.Sum(r => r.GoodQuantity), mine.Sum(r => r.ScrapQuantity), o.StartedAt, o.FinishedAt);
        }).ToList();
        return new OrderProgress(order, product.Where(m => m.Kind == StockMovementKind.ProductionReceipt).Sum(m => m.Quantity),
            -product.Where(m => m.Kind == StockMovementKind.ProductionScrap).Sum(m => m.Quantity), reports.Sum(r => r.ScrapQuantity), lines, ops, costs,
            movements, reports, codes);
    }

    // Planning --------------------------------------------------------------------------------------------------------

    /// <summary>Makes a planned order. Its number comes from this station's range (the server's own on the web).</summary>
    public async Task<ProductionOrder> CreateAsync(NewProductionOrder request)
    {
        var code = (request.ProductCode ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) throw new BusinessRuleException("Say which product to make.");
        if (request.Quantity <= 0) throw new BusinessRuleException("The quantity to make must be above zero.");
        if (request.Priority is < 1 or > 9) throw new BusinessRuleException("Priority is 1 (most urgent) to 9.");
        if (request.PlannedStart is { } s && request.PlannedEnd is { } e && e < s) throw new BusinessRuleException("The planned end is before the planned start.");
        await using var db = data.Create();
        var (material, output) = await LocationsAsync(db, request.MaterialLocationId, request.OutputLocationId);
        var product = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.LocationId == output.Id && i.Plu == code && !i.IsDeleted)
            ?? throw new BusinessRuleException($"There is no item {code} at {output.Code} to receive the output into; add it there first.");
        var day = request.PlannedStart ?? DateOnly.FromDateTime(DateTime.Today);
        Guid? bomId = request.BillOfMaterialsId ?? await ManufacturingService.ActiveIdAsync(db, code, day);
        int revision = 0;
        if (bomId is Guid id)
        {
            var bom = await db.BillsOfMaterials.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id) ?? throw new BusinessRuleException("That bill of materials no longer exists.");
            if (!string.Equals(bom.ProductCode, code, StringComparison.OrdinalIgnoreCase)) throw new BusinessRuleException($"That bill makes {bom.ProductCode}, not {code}.");
            revision = bom.Revision;
        }
        else
        {
            throw new BusinessRuleException($"{code} has no bill of materials in force; make one first.");
        }
        if (request.CustomerOrderId is Guid customerOrder && !await db.CustomerOrders.AnyAsync(c => c.Id == customerOrder && c.StoreId == material.StoreId))
            throw new BusinessRuleException("That customer order is not one of this store's.");
        var order = new ProductionOrder
        {
            StoreId = material.StoreId, ProductCode = code, ProductName = product.Name, BillOfMaterialsId = bomId, BomRevision = revision, Quantity = request.Quantity,
            MaterialLocationId = material.Id, OutputLocationId = output.Id, PlannedStart = request.PlannedStart, PlannedEnd = request.PlannedEnd,
            Priority = request.Priority, CustomerOrderId = request.CustomerOrderId, Notes = ManufacturingService.Cut(request.Notes?.Trim(), 400),
            Status = ProductionOrderStatus.Planned, CreatedAt = DateTime.Now, CreatedBy = Who(), Number = await NextNumberAsync(material.StoreId),
            // An order made on the web is numbered from the server's own range, and says so.
            StationNumber = data.Station is { StationNumber: > 0 } ? 0 : ServerStation,
        };
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Changes an order. A planned order can change everything; once released only its dates, priority and notes (the
    /// rest was copied from its bill). <paramref name="version"/> must be the order's current one.
    /// </summary>
    public async Task<ProductionOrder> UpdateAsync(Guid id, Guid version, NewProductionOrder changes)
    {
        await using var db = data.Create();
        var order = await LoadAsync(db, id, tracked: true) ?? throw new BusinessRuleException("That production order no longer exists.");
        if (order.Version != version) throw new ConcurrencyConflictException("Someone else changed this order. Load it again and make your changes again.");
        if (order.Status is ProductionOrderStatus.Closed or ProductionOrderStatus.Cancelled or ProductionOrderStatus.Completed)
            throw new BusinessRuleException($"Order {order.Number} is {Describe(order.Status)}; it cannot change.");
        if (changes.Priority is < 1 or > 9) throw new BusinessRuleException("Priority is 1 (most urgent) to 9.");
        if (changes.PlannedStart is { } s && changes.PlannedEnd is { } e && e < s) throw new BusinessRuleException("The planned end is before the planned start.");
        if (order.Status == ProductionOrderStatus.Planned)
        {
            var code = (changes.ProductCode ?? "").Trim().ToUpperInvariant();
            if (changes.Quantity <= 0) throw new BusinessRuleException("The quantity to make must be above zero.");
            var (material, output) = await LocationsAsync(db, changes.MaterialLocationId, changes.OutputLocationId);
            if (material.StoreId != order.StoreId) throw new BusinessRuleException("An order stays in its store.");
            if (!string.Equals(code, order.ProductCode, StringComparison.OrdinalIgnoreCase) || (changes.BillOfMaterialsId is not null && changes.BillOfMaterialsId != order.BillOfMaterialsId))
            {
                var bomId = changes.BillOfMaterialsId ?? await ManufacturingService.ActiveIdAsync(db, code, changes.PlannedStart ?? DateOnly.FromDateTime(DateTime.Today))
                    ?? throw new BusinessRuleException($"{code} has no bill of materials in force; make one first.");
                var bom = await db.BillsOfMaterials.AsNoTracking().SingleAsync(b => b.Id == bomId);
                if (!string.Equals(bom.ProductCode, code, StringComparison.OrdinalIgnoreCase)) throw new BusinessRuleException($"That bill makes {bom.ProductCode}, not {code}.");
                order.ProductCode = code;
                order.BillOfMaterialsId = bom.Id;
                order.BomRevision = bom.Revision;
            }
            var product = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.LocationId == output.Id && i.Plu == order.ProductCode && !i.IsDeleted)
                ?? throw new BusinessRuleException($"There is no item {order.ProductCode} at {output.Code} to receive the output into; add it there first.");
            order.ProductName = product.Name;
            order.Quantity = changes.Quantity;
            order.MaterialLocationId = material.Id;
            order.OutputLocationId = output.Id;
            order.CustomerOrderId = changes.CustomerOrderId;
        }
        order.PlannedStart = changes.PlannedStart;
        order.PlannedEnd = changes.PlannedEnd;
        order.Priority = changes.Priority;
        order.Notes = ManufacturingService.Cut(changes.Notes?.Trim(), 400);
        await SaveAsync(db);
        return order;
    }

    /// <summary>
    /// Releases a planned order to the floor: copies its bill's components (scaled to the order, scrap included), by-products
    /// and routing (with the work centres' rates) onto it, rolls up its standard cost at the material location, and sets aside
    /// what stock there is of each component that other released orders have not set aside. What is short goes to purchasing
    /// as a suggestion (to make, for a component with a bill of its own); the order is released all the same.
    /// </summary>
    public async Task<ReleaseResult> ReleaseAsync(Guid id)
    {
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var order = await LoadAsync(db, id, tracked: true) ?? throw new BusinessRuleException("That production order no longer exists.");
        if (order.Status != ProductionOrderStatus.Planned) throw new BusinessRuleException($"Order {order.Number} is {Describe(order.Status)}, not planned.");
        var bomId = order.BillOfMaterialsId ?? throw new BusinessRuleException($"Order {order.Number} has no bill of materials.");
        var bom = await db.BillsOfMaterials.AsNoTracking().Include(b => b.Lines).Include(b => b.ByProducts).Include(b => b.Operations).ThenInclude(o => o.WorkCentre)
            .AsSplitQuery().SingleOrDefaultAsync(b => b.Id == bomId) ?? throw new BusinessRuleException("The order's bill of materials no longer exists.");
        var day = DateOnly.FromDateTime(DateTime.Today);
        var rollUp = await new ManufacturingService.CostBook(db, day, order.MaterialLocationId).RollUpAsync(bom, order.Quantity);

        var componentCodes = bom.Lines.Select(l => l.ComponentCode).ToList();
        var materialItems = (await db.Items.AsNoTracking().Where(i => i.LocationId == order.MaterialLocationId && componentCodes.Contains(i.Plu) && !i.IsDeleted).ToListAsync())
            .GroupBy(i => i.Plu, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var byCodes = bom.ByProducts.Select(b => b.StockCode).ToList();
        var outputItems = (await db.Items.AsNoTracking().Where(i => i.LocationId == order.OutputLocationId && byCodes.Contains(i.Plu) && !i.IsDeleted).ToListAsync())
            .GroupBy(i => i.Plu, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var b in bom.ByProducts.Where(b => !outputItems.ContainsKey(b.StockCode)))
            throw new BusinessRuleException($"There is no item {b.StockCode} at {order.OutputLocation?.Code} for the by-product; add it there first.");
        var reserved = await ReservedByOthersAsync(db, order.Id, [.. materialItems.Values.Select(i => i.Id)]);

        var shortages = new List<Shortage>();
        int n = 0;
        foreach (var l in bom.Lines.OrderBy(l => l.LineNumber))
        {
            decimal perUnit = Math.Round(l.Quantity / bom.YieldQuantity * (1 + l.ScrapPercent / 100m), 6, MidpointRounding.AwayFromZero);
            decimal planned = Math.Round(perUnit * order.Quantity, 3, MidpointRounding.AwayFromZero);
            var item = materialItems.GetValueOrDefault(l.ComponentCode);
            decimal available = item is null ? 0 : Math.Max(0, item.OnHand - reserved.GetValueOrDefault(item.Id));
            decimal set = Math.Min(planned, available);
            var cost = rollUp.Lines.FirstOrDefault(c => string.Equals(c.StockCode, l.ComponentCode, StringComparison.OrdinalIgnoreCase));
            order.Lines.Add(new ProductionOrderLine
            {
                ProductionOrderId = order.Id, LineNumber = ++n, Kind = ProductionLineKind.Component, StockCode = l.ComponentCode,
                Description = ManufacturingService.Cut(item?.Name ?? l.Description ?? l.ComponentCode, 60)!, ItemId = item?.Id, QuantityPerUnit = perUnit, QuantityPlanned = planned,
                ScrapPercent = l.ScrapPercent, IssueMethod = l.IssueMethod, OperationSequence = l.OperationSequence, StandardUnitCost = cost?.UnitCost ?? 0, QuantityReserved = set,
            });
            if (set < planned)
                shortages.Add(new Shortage(l.ComponentCode, item?.Name ?? l.Description ?? "", planned, available, planned - set, cost?.IsMade ?? false));
        }
        n = 0;
        foreach (var b in bom.ByProducts.OrderBy(b => b.LineNumber))
        {
            decimal perUnit = Math.Round(b.Quantity / bom.YieldQuantity, 6, MidpointRounding.AwayFromZero);
            order.Lines.Add(new ProductionOrderLine
            {
                ProductionOrderId = order.Id, LineNumber = ++n, Kind = ProductionLineKind.ByProduct, StockCode = b.StockCode,
                Description = ManufacturingService.Cut(outputItems[b.StockCode].Name, 60)!, ItemId = outputItems[b.StockCode].Id, QuantityPerUnit = perUnit,
                QuantityPlanned = Math.Round(perUnit * order.Quantity, 3, MidpointRounding.AwayFromZero), CostSharePercent = b.CostSharePercent,
            });
        }
        foreach (var o in bom.Operations.OrderBy(o => o.Sequence))
            order.Operations.Add(new ProductionOrderOperation
            {
                ProductionOrderId = order.Id, Sequence = o.Sequence, WorkCentreId = o.WorkCentreId, WorkCentreName = o.WorkCentre?.Name ?? "", Name = o.Name,
                SetupMinutes = o.SetupMinutes, RunMinutesPerUnit = o.RunMinutesPerUnit, Instructions = o.Instructions,
                LabourRate = o.WorkCentre?.LabourCostPerHour ?? 0, MachineRate = o.WorkCentre?.MachineCostPerHour ?? 0, OverheadRate = o.WorkCentre?.OverheadPerHour ?? 0,
            });

        decimal q = order.Quantity;
        order.StandardMaterialCost = Math.Round(rollUp.Material / q, 3, MidpointRounding.AwayFromZero);
        order.StandardLabourCost = Math.Round(rollUp.Labour / q, 3, MidpointRounding.AwayFromZero);
        order.StandardMachineCost = Math.Round(rollUp.Machine / q, 3, MidpointRounding.AwayFromZero);
        order.StandardOverheadCost = Math.Round(rollUp.Overhead / q, 3, MidpointRounding.AwayFromZero);
        order.OverheadPercent = bom.OverheadPercent;
        order.BomRevision = bom.Revision;
        order.Status = ProductionOrderStatus.Released;
        order.ReleasedAt = DateTime.Now;
        order.ReleasedBy = Who();

        // The shortage list goes to purchasing (and to planning, for made components) as suggestions; nothing is ordered.
        var need = order.PlannedStart ?? day;
        foreach (var s in shortages)
        {
            var item = materialItems.GetValueOrDefault(s.StockCode);
            db.MrpSuggestions.Add(new MrpSuggestion
            {
                StoreId = order.StoreId, Kind = s.IsMade ? SuggestionKind.Make : SuggestionKind.Buy, Status = SuggestionStatus.Open, StockCode = s.StockCode,
                Description = ManufacturingService.Cut(s.Description, 60)!, LocationId = order.MaterialLocationId, Quantity = s.Missing, NeedBy = need, StartBy = need,
                SupplierId = item?.SupplierId, Level = 1, Reason = ManufacturingService.Cut($"Short for production order {order.Number} ({order.ProductCode})", 200),
            });
        }
        db.Audit(AuditActions.ProductionRelease, $"Order {order.Number}: {order.Quantity:0.###} x {order.ProductCode}"
            + (shortages.Count == 0 ? ", all material set aside" : $", {shortages.Count} component(s) short"), nameof(ProductionOrder), order.Id);
        await SaveAsync(db);
        await tx.CommitAsync();
        return new ReleaseResult(order, shortages);
    }

    /// <summary>What open orders other than <paramref name="orderId"/> still have set aside of each item: reserved less issued.</summary>
    private static async Task<Dictionary<Guid, decimal>> ReservedByOthersAsync(RetailPlusDbContext db, Guid orderId, List<Guid> itemIds)
    {
        var lines = await db.ProductionOrderLines.AsNoTracking()
            .Where(l => l.ItemId != null && itemIds.Contains(l.ItemId.Value) && l.Kind == ProductionLineKind.Component && l.ProductionOrderId != orderId)
            .Join(db.ProductionOrders.AsNoTracking().Where(o => o.Status == ProductionOrderStatus.Released || o.Status == ProductionOrderStatus.InProgress),
                l => l.ProductionOrderId, o => o.Id, (l, o) => new { l.ProductionOrderId, Item = l.ItemId!.Value, l.QuantityReserved })
            .ToListAsync();
        var orders = lines.Select(l => l.ProductionOrderId).Distinct().ToList();
        var issued = (await db.StockMovements.AsNoTracking()
                .Where(m => m.ProductionOrderId != null && orders.Contains(m.ProductionOrderId.Value) && itemIds.Contains(m.ItemId)
                            && (m.Kind == StockMovementKind.ProductionIssue || m.Kind == StockMovementKind.ProductionScrap))
                .Select(m => new { Order = m.ProductionOrderId!.Value, m.ItemId, m.Quantity }).ToListAsync())
            .GroupBy(m => (m.Order, m.ItemId)).ToDictionary(g => g.Key, g => -g.Sum(m => m.Quantity));
        return lines.GroupBy(l => l.Item).ToDictionary(g => g.Key, g => g.Sum(l => Math.Max(0, l.QuantityReserved - issued.GetValueOrDefault((l.ProductionOrderId, l.Item)))));
    }

    /// <summary>Cancels an order nothing was done on yet (no material issued, no time reported, nothing received).</summary>
    public async Task<ProductionOrder> CancelAsync(Guid id, string? reason = null)
    {
        await using var db = data.Create();
        var order = await LoadAsync(db, id, tracked: true) ?? throw new BusinessRuleException("That production order no longer exists.");
        if (order.Status is not (ProductionOrderStatus.Planned or ProductionOrderStatus.Released or ProductionOrderStatus.InProgress))
            throw new BusinessRuleException($"Order {order.Number} is {Describe(order.Status)}; it cannot be cancelled.");
        if (await db.StockMovements.AnyAsync(m => m.ProductionOrderId == id) || await db.ProductionReports.AnyAsync(r => r.ProductionOrderId == id))
            throw new BusinessRuleException($"Work was recorded on order {order.Number}; complete and close it instead.");
        order.Status = ProductionOrderStatus.Cancelled;
        order.CancelledAt = DateTime.Now;
        order.CancelledBy = Who();
        db.Audit(AuditActions.ProductionCancel, $"Order {order.Number}" + (string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason.Trim()), nameof(ProductionOrder), order.Id);
        await SaveAsync(db);
        return order;
    }

    // The shop floor --------------------------------------------------------------------------------------------------

    /// <summary>Starts an operation (and the order, when it is the first thing done on it).</summary>
    public async Task<ProductionOrder> StartOperationAsync(Guid orderId, int sequence)
    {
        await using var db = data.Create();
        var order = await WorkableAsync(db, orderId);
        var op = order.Operations.SingleOrDefault(o => o.Sequence == sequence) ?? throw new BusinessRuleException($"Order {order.Number} has no operation {sequence}.");
        if (op.Status == OperationStatus.Done) throw new BusinessRuleException($"Operation {sequence} is finished already.");
        if (op.Status == OperationStatus.Waiting)
        {
            op.Status = OperationStatus.Started;
            op.StartedAt = DateTime.Now;
        }
        Started(order);
        await SaveAsync(db);
        return order;
    }

    /// <summary>Finishes an operation.</summary>
    public async Task<ProductionOrder> FinishOperationAsync(Guid orderId, int sequence)
    {
        await using var db = data.Create();
        var order = await WorkableAsync(db, orderId);
        var op = order.Operations.SingleOrDefault(o => o.Sequence == sequence) ?? throw new BusinessRuleException($"Order {order.Number} has no operation {sequence}.");
        op.Status = OperationStatus.Done;
        op.StartedAt ??= DateTime.Now;
        op.FinishedAt = DateTime.Now;
        Started(order);
        await SaveAsync(db);
        return order;
    }

    /// <summary>
    /// Reports work on an operation: the minutes worked, the good and scrapped quantity, by the user signed in (or the
    /// operator named). Labour, machine and overhead are applied at the operation's rates.
    /// </summary>
    public async Task<ProductionReport> ReportAsync(Guid orderId, int sequence, decimal minutes, decimal good, decimal scrap, string? note = null,
        Guid? operatorId = null, string? operatorName = null)
    {
        if (minutes < 0 || good < 0 || scrap < 0) throw new BusinessRuleException("Time and quantities cannot be below zero.");
        if (minutes == 0 && good == 0 && scrap == 0) throw new BusinessRuleException("Report some time or a quantity.");
        await using var db = data.Create();
        var order = await WorkableAsync(db, orderId);
        var op = order.Operations.SingleOrDefault(o => o.Sequence == sequence) ?? throw new BusinessRuleException($"Order {order.Number} has no operation {sequence}.");
        decimal hours = minutes / 60m;
        var report = new ProductionReport
        {
            StoreId = order.StoreId, ProductionOrderId = order.Id, OperationSequence = sequence, At = DateTime.Now, Minutes = minutes, GoodQuantity = good,
            ScrapQuantity = scrap, UserId = operatorId ?? data.CurrentUser?.UserId, UserName = ManufacturingService.Cut(operatorName ?? Who(), 50),
            LabourCost = Money.Round(hours * op.LabourRate), MachineCost = Money.Round(hours * op.MachineRate), OverheadCost = Money.Round(hours * op.OverheadRate),
            Note = ManufacturingService.Cut(note?.Trim(), 200),
        };
        db.ProductionReports.Add(report);
        if (op.Status == OperationStatus.Waiting)
        {
            op.Status = OperationStatus.Started;
            op.StartedAt = report.At;
        }
        Started(order);
        await SaveAsync(db);
        return report;
    }

    /// <summary>
    /// Issues material to an order by its code (stock code or barcode, as a scanner sends it) from the order's material
    /// location, at its average cost. A negative quantity returns material to stock (no more than was issued). Material not
    /// on the order's bill may be issued too (a substitute); it counts as material of the order.
    /// </summary>
    public async Task<StockMovement> IssueAsync(Guid orderId, string code, decimal quantity, string? note = null)
    {
        if (quantity == 0) throw new BusinessRuleException("Enter how much to issue (a negative quantity returns material).");
        await using var db = data.Create();
        db.SuppressAudit = true;
        await using var tx = await db.Database.BeginTransactionAsync();
        var order = await WorkableAsync(db, orderId, allowCompleted: true);
        var item = await FindAsync(db, order.MaterialLocationId, code) ?? throw new BusinessRuleException($"No item at {order.MaterialLocation?.Code} has the code {code.Trim()}.");
        if (string.Equals(item.Plu, order.ProductCode, StringComparison.OrdinalIgnoreCase)) throw new BusinessRuleException($"{item.Plu} is what the order makes; it cannot be issued to it.");
        if (quantity < 0)
        {
            var moved = await db.StockMovements.Where(m => m.ProductionOrderId == orderId && m.ItemId == item.Id && m.Kind == StockMovementKind.ProductionIssue)
                .Select(m => m.Quantity).ToListAsync();
            decimal issued = -moved.Sum();
            if (-quantity > issued) throw new BusinessRuleException($"Only {issued:0.###} of {item.Plu} was issued to order {order.Number}; no more can come back.");
        }
        var movement = Move(db, order, item, StockMovementKind.ProductionIssue, -quantity, CostOf(item), note ?? (quantity > 0 ? "Issued to production" : "Returned from production"));
        Started(order);
        await SaveAsync(db);
        await tx.CommitAsync();
        return movement;
    }

    /// <summary>Issues every manual component (at one operation, or all of them) what is still to issue of it.</summary>
    public async Task<int> IssueRemainingAsync(Guid orderId, int? operationSequence = null)
    {
        var progress = await ProgressAsync(orderId) ?? throw new BusinessRuleException("That production order no longer exists.");
        int issued = 0;
        foreach (var line in progress.Lines.Where(l => l.Kind == ProductionLineKind.Component && l.IssueMethod == IssueMethod.Manual
                                                       && (operationSequence is null || l.OperationSequence == operationSequence)))
        {
            decimal left = line.Planned - line.Done;
            if (left <= 0) continue;
            await IssueAsync(orderId, line.StockCode, left, "Issued from the list");
            issued++;
        }
        return issued;
    }

    /// <summary>Throws away material spoiled on the floor (from the material location), charged to the order.</summary>
    public async Task<StockMovement> ScrapMaterialAsync(Guid orderId, string code, decimal quantity, string? reason = null)
    {
        if (quantity <= 0) throw new BusinessRuleException("Enter how much was thrown away (above zero).");
        await using var db = data.Create();
        db.SuppressAudit = true;
        await using var tx = await db.Database.BeginTransactionAsync();
        var order = await WorkableAsync(db, orderId, allowCompleted: true);
        var item = await FindAsync(db, order.MaterialLocationId, code) ?? throw new BusinessRuleException($"No item at {order.MaterialLocation?.Code} has the code {code.Trim()}.");
        var movement = Move(db, order, item, StockMovementKind.ProductionScrap, -quantity, CostOf(item), reason ?? "Spoiled in production");
        Started(order);
        await SaveAsync(db);
        await tx.CommitAsync();
        return movement;
    }

    /// <summary>
    /// Receives output into the order's output location. Before completion the product is valued at the order's standard
    /// cost (never more than its work in progress). Completing the order first takes the backflushed components out of
    /// stock (for the good quantity made, scrap allowance included), then values this last receipt so that the order's
    /// output carries all its cost: by-products their cost share of everything charged, the product the rest.
    /// </summary>
    public async Task<OrderProgress> ReceiveAsync(Guid orderId, decimal quantity, bool complete, IReadOnlyList<ByProductQuantity>? byProducts = null)
    {
        if (quantity < 0) throw new BusinessRuleException("The quantity received cannot be below zero.");
        if (quantity == 0 && !complete) throw new BusinessRuleException("Enter the quantity made.");
        if (!complete && byProducts is { Count: > 0 }) throw new BusinessRuleException("By-products are received when the order is completed.");
        await using var db = data.Create();
        db.SuppressAudit = true;
        await using var tx = await db.Database.BeginTransactionAsync();
        var order = await WorkableAsync(db, orderId);
        var product = await db.Items.FirstOrDefaultAsync(i => i.LocationId == order.OutputLocationId && i.Plu == order.ProductCode && !i.IsDeleted)
            ?? throw new BusinessRuleException($"There is no item {order.ProductCode} at {order.OutputLocation?.Code} to receive the output into.");
        var now = DateTime.Now;
        if (!complete)
        {
            var progress = await ProgressAsync(db, order);
            decimal share = 100m - order.Lines.Where(l => l.Kind == ProductionLineKind.ByProduct).Sum(l => l.CostSharePercent);
            decimal value = Math.Min(Money.Round(order.StandardUnitCost * share / 100m * quantity), Math.Max(0, progress.Costs.WorkInProgress));
            Receive(db, order, product, quantity, value, now, "Made");
        }
        else
        {
            // Backflush: components issued by themselves for the good quantity made, before the output is valued.
            var before = await ProgressAsync(db, order);
            decimal made = before.Received + quantity;
            foreach (var line in before.Lines.Where(l => l.Kind == ProductionLineKind.Component && l.IssueMethod == IssueMethod.Backflush))
            {
                var orderLine = order.Lines.Single(l => l.Id == line.LineId);
                decimal take = Math.Round(orderLine.QuantityPerUnit * made, 3, MidpointRounding.AwayFromZero) - line.Done;
                if (take <= 0) continue;
                var item = await FindAsync(db, order.MaterialLocationId, line.StockCode)
                    ?? throw new BusinessRuleException($"There is no item {line.StockCode} at {order.MaterialLocation?.Code} to backflush.");
                Move(db, order, item, StockMovementKind.ProductionIssue, -take, CostOf(item), "Backflushed at completion", now);
            }
            await db.SaveChangesAsync();
            var progress = await ProgressAsync(db, order);
            decimal wip = Math.Max(0, progress.Costs.WorkInProgress);
            decimal charged = progress.Costs.Charged;
            var given = (byProducts ?? []).Where(b => b.Quantity > 0).GroupBy(b => b.StockCode.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity), StringComparer.OrdinalIgnoreCase);
            foreach (var code in given.Keys)
                if (!order.Lines.Any(l => l.Kind == ProductionLineKind.ByProduct && string.Equals(l.StockCode, code, StringComparison.OrdinalIgnoreCase)))
                    throw new BusinessRuleException($"{code} is not a by-product of order {order.Number}.");
            var byValues = order.Lines.Where(l => l.Kind == ProductionLineKind.ByProduct && given.ContainsKey(l.StockCode))
                .Select(l => (Line: l, Quantity: given[l.StockCode], Value: Money.Round(charged * l.CostSharePercent / 100m))).ToList();
            decimal byTotal = byValues.Sum(b => b.Value);
            if (byTotal > wip && byTotal > 0)
                byValues = [.. byValues.Select(b => (b.Line, b.Quantity, Money.Round(b.Value * wip / byTotal)))];
            byTotal = byValues.Sum(b => b.Value);
            if (quantity > 0) Receive(db, order, product, quantity, Math.Max(0, wip - byTotal), now, "Made, order completed");
            foreach (var (line, qty, value) in byValues)
            {
                var item = await db.Items.FirstOrDefaultAsync(i => i.LocationId == order.OutputLocationId && i.Plu == line.StockCode && !i.IsDeleted)
                    ?? throw new BusinessRuleException($"There is no item {line.StockCode} at {order.OutputLocation?.Code} for the by-product.");
                Receive(db, order, item, qty, value, now, "By-product");
            }
            order.Status = ProductionOrderStatus.Completed;
            order.CompletedAt = now;
            order.CompletedBy = Who();
            foreach (var op in order.Operations.Where(o => o.Status != OperationStatus.Done))
            {
                op.Status = OperationStatus.Done;
                op.StartedAt ??= now;
                op.FinishedAt = now;
            }
            db.Audit(AuditActions.ProductionComplete, $"Order {order.Number}: {made:0.###} x {order.ProductCode} made", nameof(ProductionOrder), order.Id);
        }
        Started(order);
        await SaveAsync(db);
        var result = await ProgressAsync(db, order);
        await tx.CommitAsync();
        return result;
    }

    /// <summary>Closes a completed order: nothing more is issued, reported or received, and its remaining cost is a variance.</summary>
    public async Task<ProductionOrder> CloseAsync(Guid id)
    {
        await using var db = data.Create();
        var order = await LoadAsync(db, id, tracked: true) ?? throw new BusinessRuleException("That production order no longer exists.");
        if (order.Status != ProductionOrderStatus.Completed) throw new BusinessRuleException($"Order {order.Number} is {Describe(order.Status)}; complete it first.");
        var progress = await ProgressAsync(db, order);
        order.Status = ProductionOrderStatus.Closed;
        order.ClosedAt = DateTime.Now;
        order.ClosedBy = Who();
        db.Audit(AuditActions.ProductionClose, $"Order {order.Number}: variance {progress.Costs.WorkInProgress:N2}", nameof(ProductionOrder), order.Id);
        await SaveAsync(db);
        return order;
    }

    // Helpers -----------------------------------------------------------------------------------------------------------

    private static async Task<ProductionOrder> WorkableAsync(RetailPlusDbContext db, Guid id, bool allowCompleted = false)
    {
        var order = await LoadAsync(db, id, tracked: true) ?? throw new BusinessRuleException("That production order no longer exists.");
        bool ok = order.Status is ProductionOrderStatus.Released or ProductionOrderStatus.InProgress || (allowCompleted && order.Status == ProductionOrderStatus.Completed);
        if (!ok)
            throw new BusinessRuleException(order.Status == ProductionOrderStatus.Planned ? $"Order {order.Number} is not released yet."
                : $"Order {order.Number} is {Describe(order.Status)}.");
        return order;
    }

    /// <summary>The first thing done on a released order starts it.</summary>
    private static void Started(ProductionOrder order)
    {
        if (order.Status != ProductionOrderStatus.Released) return;
        order.Status = ProductionOrderStatus.InProgress;
        order.StartedAt = DateTime.Now;
    }

    private static StockMovement Move(RetailPlusDbContext db, ProductionOrder order, Item item, StockMovementKind kind, decimal quantity, decimal unitCost, string reason, DateTime? at = null)
    {
        if (item.ItemType == ItemType.Matrix) throw new BusinessRuleException($"{item.Plu} is a matrix item; production uses items without matrix lines.");
        item.OnHand += quantity;
        var movement = InventoryService.Record(db, item.Id, kind, quantity, item.OnHand, at ?? DateTime.Now, document: order.Number,
            reason: ManufacturingService.Cut(reason, 100), unitCost: unitCost);
        movement.ProductionOrderId = order.Id;
        movement.StoreId = order.StoreId;
        return movement;
    }

    /// <summary>Puts output into stock with its value: the item's average and last cost take in the unit cost.</summary>
    private static void Receive(RetailPlusDbContext db, ProductionOrder order, Item item, decimal quantity, decimal value, DateTime at, string reason)
    {
        if (quantity <= 0) return;
        decimal unitCost = Math.Round(value / quantity, 3, MidpointRounding.AwayFromZero);
        if (unitCost > 0)
        {
            item.AverageCost = Reordering.AverageCostAfterReceipt(item.OnHand, item.AverageCost, quantity, unitCost, 0);
            item.LastCost = unitCost;
            if (item.Price1 > 0) item.MarginPercent = Money.MarginPercent(item.Price1, item.AverageCost);
        }
        item.LastReceivedDate = DateOnly.FromDateTime(at);
        item.LastReceivedQuantity = quantity;
        Move(db, order, item, StockMovementKind.ProductionReceipt, quantity, unitCost, reason, at);
    }

    internal static decimal CostOf(Item item) => item.AverageCost > 0 ? item.AverageCost : item.LastCost;

    /// <summary>An item at a location by its stock code or barcode.</summary>
    internal static async Task<Item?> FindAsync(RetailPlusDbContext db, Guid locationId, string code)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        if (c.Length == 0) return null;
        // A scanner may send a UPC-A as EAN-13 (with a leading 0): every form of the code is looked for, as at the POS.
        var forms = Upc.Forms(c);
        return await db.Items.FirstOrDefaultAsync(i => i.LocationId == locationId && !i.IsDeleted && i.Plu == c)
               ?? await db.Items.FirstOrDefaultAsync(i => i.LocationId == locationId && !i.IsDeleted && i.Barcode != null && forms.Contains(i.Barcode));
    }

    private static async Task<(Location Material, Location Output)> LocationsAsync(RetailPlusDbContext db, Guid materialId, Guid outputId)
    {
        var material = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == materialId) ?? throw new BusinessRuleException("Choose the location materials come from.");
        var output = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == outputId) ?? throw new BusinessRuleException("Choose the location the output goes to.");
        if (material.StoreId != output.StoreId) throw new BusinessRuleException("Materials and output must be at locations of the same store.");
        return (material, output);
    }

    private static async Task SaveAsync(RetailPlusDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("The order or one of its items was changed at another station. Try again.");
        }
    }

    private async Task<long> NextNumberAsync(Guid store)
    {
        if (data.Station is { StationNumber: > 0 })
        {
            await using var own = data.Create();
            return await own.NextNumberAsync(RetailPlusDbContext.Sequences.ProductionOrderNumbers);
        }
        await using var db = data.For(new StationIdentity(store, ServerStation), data.CurrentUser).Create();
        return await db.NextNumberAsync(RetailPlusDbContext.Sequences.ProductionOrderNumbers);
    }

    private string? Who() => ManufacturingService.Cut(data.CurrentUser?.UserName, 50);

    public static string Describe(ProductionOrderStatus status) => status switch
    {
        ProductionOrderStatus.InProgress => "in progress",
        _ => status.ToString().ToLowerInvariant(),
    };
}
