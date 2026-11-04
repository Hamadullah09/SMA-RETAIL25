using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>The result given for one check (by its line number in the plan).</summary>
public sealed record CheckAnswer(int LineNumber, bool? PassFail = null, decimal? Number = null, string? Text = null);

/// <summary>An inspection to record: how much was looked at, what failed, where the failed goods go, and the answers.</summary>
public sealed record InspectionRequest(decimal QuantityInspected, decimal QuantityFailed, IReadOnlyList<CheckAnswer> Answers)
{
    public InspectionDisposition Disposition { get; init; }
    public Guid? HoldLocationId { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// Quality: a plan of checks per product, and inspections against it. An inspection of a production order's output can
/// move the failed quantity from the output location to a hold location, or scrap it (a production scrap movement whose
/// cost goes back to the order, to be a variance when it closes). An inspection of goods received on a purchase order
/// records the results only; receiving itself is unchanged.
/// </summary>
public sealed class QualityService(DataContext data)
{
    // Plans -------------------------------------------------------------------------------------------------------------

    public async Task<List<QualityPlan>> PlansAsync()
    {
        await using var db = data.Create();
        var plans = await db.QualityPlans.AsNoTracking().Include(p => p.Checks).OrderBy(p => p.ProductCode).ToListAsync();
        foreach (var p in plans) p.Checks = [.. p.Checks.OrderBy(c => c.LineNumber)];
        return plans;
    }

    public async Task<QualityPlan?> PlanForAsync(string productCode, InspectionKind kind)
    {
        await using var db = data.Create();
        return await PlanForAsync(db, productCode, kind);
    }

    private static async Task<QualityPlan?> PlanForAsync(RetailPlusDbContext db, string productCode, InspectionKind kind)
    {
        var code = productCode.Trim().ToUpperInvariant();
        var plan = await db.QualityPlans.AsNoTracking().Include(p => p.Checks).SingleOrDefaultAsync(p => p.ProductCode == code && p.IsActive);
        if (plan is null || (kind == InspectionKind.Production ? !plan.ForProduction : !plan.ForIncoming)) return null;
        plan.Checks = [.. plan.Checks.OrderBy(c => c.LineNumber)];
        return plan;
    }

    /// <summary>Saves a plan and its checks (replacing them). One plan per product.</summary>
    public async Task<QualityPlan> SavePlanAsync(QualityPlan plan)
    {
        var code = (plan.ProductCode ?? "").Trim().ToUpperInvariant();
        if (code.Length is 0 or > 16) throw new BusinessRuleException("A quality plan needs the product's stock code (up to 16 characters).");
        var name = (plan.Name ?? "").Trim();
        if (name.Length is 0 or > 60) throw new BusinessRuleException("A quality plan needs a name of up to 60 characters.");
        if (!plan.ForProduction && !plan.ForIncoming) throw new BusinessRuleException("Say whether the plan is for what is made, what is received, or both.");
        if (plan.Checks.Count == 0) throw new BusinessRuleException("A plan needs at least one check.");
        foreach (var c in plan.Checks)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) throw new BusinessRuleException("Every check needs a name.");
            if (c.Kind == QualityCheckKind.Number && c.Minimum is { } min && c.Maximum is { } max && max < min)
                throw new BusinessRuleException($"{c.Name.Trim()}: the maximum is below the minimum.");
        }
        await using var db = data.Create();
        if (await db.QualityPlans.AnyAsync(p => p.ProductCode == code && p.Id != plan.Id)) throw new BusinessRuleException($"{code} has a quality plan already.");
        var row = plan.Id == Guid.Empty ? null : await db.QualityPlans.Include(p => p.Checks).SingleOrDefaultAsync(p => p.Id == plan.Id);
        if (row is null) db.QualityPlans.Add(row = new QualityPlan());
        row.ProductCode = code;
        row.Name = name;
        row.IsActive = plan.IsActive;
        row.ForProduction = plan.ForProduction;
        row.ForIncoming = plan.ForIncoming;
        row.Notes = ManufacturingService.Cut(plan.Notes?.Trim(), 400);
        db.QualityChecks.RemoveRange(row.Checks);
        row.Checks.Clear();
        int n = 0;
        foreach (var c in plan.Checks)
            row.Checks.Add(new QualityCheck
            {
                QualityPlanId = row.Id, LineNumber = ++n, Name = ManufacturingService.Cut(c.Name.Trim(), 60)!, Kind = c.Kind, Required = c.Required,
                Minimum = c.Kind == QualityCheckKind.Number ? c.Minimum : null, Maximum = c.Kind == QualityCheckKind.Number ? c.Maximum : null,
                Unit = c.Kind == QualityCheckKind.Number ? ManufacturingService.Cut(c.Unit?.Trim(), 10) : null,
            });
        await db.SaveChangesAsync();
        return row;
    }

    public async Task DeletePlanAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.QualityPlans.Include(p => p.Checks).SingleOrDefaultAsync(p => p.Id == id) ?? throw new BusinessRuleException("That quality plan no longer exists.");
        db.QualityPlans.Remove(row);
        await db.SaveChangesAsync();
    }

    // Inspections -------------------------------------------------------------------------------------------------------

    /// <summary>Inspections of a store (null: every store), newest first.</summary>
    public async Task<List<QualityInspection>> InspectionsAsync(Guid? store = null, InspectionKind? kind = null, DateOnly? from = null, DateOnly? to = null, Guid? productionOrderId = null)
    {
        await using var db = data.Create();
        var q = db.QualityInspections.AsNoTracking().Include(i => i.Results).AsQueryable();
        if (store is Guid s) q = q.Where(i => i.StoreId == s);
        if (kind is { } k) q = q.Where(i => i.Kind == k);
        if (from is { } f) q = q.Where(i => i.InspectedAt >= f.ToDateTime(TimeOnly.MinValue));
        if (to is { } t) q = q.Where(i => i.InspectedAt < t.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (productionOrderId is Guid o) q = q.Where(i => i.ProductionOrderId == o);
        var rows = await q.OrderByDescending(i => i.InspectedAt).Take(1000).ToListAsync();
        foreach (var r in rows) r.Results = [.. r.Results.OrderBy(x => x.LineNumber)];
        return rows;
    }

    /// <summary>
    /// Records an inspection of a production order's output (received into its output location). The failed quantity goes
    /// to a hold location of the store, or is scrapped, or stays (no disposition).
    /// </summary>
    public async Task<QualityInspection> InspectOutputAsync(Guid orderId, InspectionRequest request)
    {
        await using var db = data.Create();
        db.SuppressAudit = true;
        await using var tx = await db.Database.BeginTransactionAsync();
        var order = await db.ProductionOrders.Include(o => o.Lines).Include(o => o.Operations).Include(o => o.OutputLocation).AsSplitQuery().SingleOrDefaultAsync(o => o.Id == orderId)
            ?? throw new BusinessRuleException("That production order no longer exists.");
        if (order.Status is ProductionOrderStatus.Planned or ProductionOrderStatus.Cancelled or ProductionOrderStatus.Closed)
            throw new BusinessRuleException($"Order {order.Number} is {ProductionService.Describe(order.Status)}; its output cannot be inspected now.");
        var progress = await ProductionService.ProgressAsync(db, order);
        var held = await HeldAsync(db, order);
        decimal inspectable = progress.Received - progress.OutputScrapped - held;
        Check(request, inspectable, $"Only {inspectable:0.###} of order {order.Number}'s output is in stock to inspect.");
        var plan = await PlanForAsync(db, order.ProductCode, InspectionKind.Production);
        var inspection = NewInspection(InspectionKind.Production, order.StoreId, order.ProductCode, order.ProductName, plan, request);
        inspection.ProductionOrderId = order.Id;
        inspection.DocumentNumber = order.Number;

        if (request.QuantityFailed > 0 && request.Disposition != InspectionDisposition.None)
        {
            var product = await db.Items.FirstOrDefaultAsync(i => i.LocationId == order.OutputLocationId && i.Plu == order.ProductCode && !i.IsDeleted)
                ?? throw new BusinessRuleException($"There is no item {order.ProductCode} at {order.OutputLocation?.Code}.");
            var receipts = progress.Movements.Where(m => m.Kind == StockMovementKind.ProductionReceipt && m.ItemId == product.Id && m.Quantity > 0).ToList();
            decimal unitCost = receipts.Sum(m => m.Quantity) is > 0 and var qty ? Math.Round(receipts.Sum(m => m.Quantity * m.UnitCost) / qty, 3, MidpointRounding.AwayFromZero)
                : ProductionService.CostOf(product);
            var now = DateTime.Now;
            if (request.Disposition == InspectionDisposition.Hold)
            {
                var holdId = request.HoldLocationId ?? throw new BusinessRuleException("Choose the hold location the failed goods go to.");
                var hold = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == holdId && l.StoreId == order.StoreId)
                    ?? throw new BusinessRuleException("The hold location must be one of the order's store.");
                if (hold.Id == order.OutputLocationId) throw new BusinessRuleException("The hold location must be another location than the output's.");
                var heldItem = await db.Items.FirstOrDefaultAsync(i => i.LocationId == hold.Id && i.Plu == order.ProductCode && !i.IsDeleted) ?? CopyTo(db, product, hold.Id);
                // Moved, not received again: the order's output value is unchanged (one out, one in, at the same cost).
                Move(db, order, product, StockMovementKind.ProductionReceipt, -request.QuantityFailed, unitCost, $"Held by inspection at {hold.Code}", now);
                Move(db, order, heldItem, StockMovementKind.ProductionReceipt, request.QuantityFailed, unitCost, "Held by inspection", now);
                inspection.HoldLocationId = hold.Id;
            }
            else
            {
                Move(db, order, product, StockMovementKind.ProductionScrap, -request.QuantityFailed, unitCost, "Failed inspection", now);
            }
        }
        db.QualityInspections.Add(inspection);
        db.Audit(AuditActions.QualityInspection, $"Order {order.Number}: {request.QuantityInspected:0.###} inspected, {request.QuantityFailed:0.###} failed"
            + (request.QuantityFailed > 0 ? $" ({request.Disposition.ToString().ToLowerInvariant()})" : "") + (inspection.Passed ? ", checks passed" : ", checks failed"),
            nameof(QualityInspection), inspection.Id);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("The item was changed at another station. Try again.");
        }
        await tx.CommitAsync();
        return inspection;
    }

    /// <summary>Records an inspection of goods received on a purchase order (results only; nothing moves).</summary>
    public async Task<QualityInspection> InspectReceiptAsync(Guid purchaseOrderId, string stockCode, InspectionRequest request)
    {
        await using var db = data.Create();
        var po = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).SingleOrDefaultAsync(p => p.Id == purchaseOrderId)
            ?? throw new BusinessRuleException("That purchase order no longer exists.");
        if (po.Status != PurchaseOrderStatus.Received) throw new BusinessRuleException($"Purchase order {po.Number} has not been received yet.");
        var code = (stockCode ?? "").Trim().ToUpperInvariant();
        var lines = po.Lines.Where(l => string.Equals(l.Plu, code, StringComparison.OrdinalIgnoreCase)).ToList();
        if (lines.Count == 0) throw new BusinessRuleException($"{code} is not on purchase order {po.Number}.");
        decimal received = lines.Sum(l => l.QuantityReceived);
        Check(request with { Disposition = InspectionDisposition.None }, received, $"Only {received:0.###} of {code} was received on purchase order {po.Number}.");
        var plan = await PlanForAsync(db, code, InspectionKind.Incoming);
        var inspection = NewInspection(InspectionKind.Incoming, po.StoreId, code, lines[0].Description, plan, request with { Disposition = InspectionDisposition.None });
        inspection.PurchaseOrderId = po.Id;
        inspection.DocumentNumber = po.Number;
        db.QualityInspections.Add(inspection);
        db.Audit(AuditActions.QualityInspection, $"Purchase order {po.Number}, {code}: {request.QuantityInspected:0.###} inspected, {request.QuantityFailed:0.###} failed",
            nameof(QualityInspection), inspection.Id);
        await db.SaveChangesAsync();
        return inspection;
    }

    private static void Check(InspectionRequest request, decimal available, string tooMuch)
    {
        if (request.QuantityInspected <= 0) throw new BusinessRuleException("Enter how much was inspected.");
        if (request.QuantityFailed < 0 || request.QuantityFailed > request.QuantityInspected) throw new BusinessRuleException("The failed quantity is 0 up to what was inspected.");
        if (request.QuantityInspected > available) throw new BusinessRuleException(tooMuch);
    }

    /// <summary>The inspection with its results checked against the plan's checks (a required check without an answer is refused).</summary>
    private QualityInspection NewInspection(InspectionKind kind, Guid store, string code, string description, QualityPlan? plan, InspectionRequest request)
    {
        var inspection = new QualityInspection
        {
            StoreId = store, Kind = kind, QualityPlanId = plan?.Id, StockCode = code, Description = ManufacturingService.Cut(description, 60)!,
            QuantityInspected = request.QuantityInspected, QuantityFailed = request.QuantityFailed,
            Disposition = request.QuantityFailed > 0 ? request.Disposition : InspectionDisposition.None, InspectedAt = DateTime.Now,
            InspectedBy = ManufacturingService.Cut(data.CurrentUser?.UserName, 50), InspectedById = data.CurrentUser?.UserId, Notes = ManufacturingService.Cut(request.Notes?.Trim(), 400),
        };
        inspection.Id = Guid.CreateVersion7();
        bool all = true;
        foreach (var check in plan?.Checks ?? [])
        {
            var answer = request.Answers.FirstOrDefault(a => a.LineNumber == check.LineNumber);
            bool given = check.Kind switch
            {
                QualityCheckKind.PassFail => answer?.PassFail is not null,
                QualityCheckKind.Number => answer?.Number is not null,
                _ => !string.IsNullOrWhiteSpace(answer?.Text),
            };
            if (!given && check.Required) throw new BusinessRuleException($"{check.Name} needs a result.");
            bool passed = check.Kind switch
            {
                QualityCheckKind.PassFail => answer?.PassFail == true,
                QualityCheckKind.Number => answer?.Number is { } v && (check.Minimum is null || v >= check.Minimum) && (check.Maximum is null || v <= check.Maximum),
                _ => given,
            };
            if (!given) passed = !check.Required;
            if (check.Required && !passed) all = false;
            inspection.Results.Add(new QualityResult
            {
                QualityInspectionId = inspection.Id, LineNumber = check.LineNumber, CheckName = check.Name, Kind = check.Kind, Minimum = check.Minimum, Maximum = check.Maximum,
                Required = check.Required, PassFail = answer?.PassFail, NumberValue = answer?.Number, TextValue = ManufacturingService.Cut(answer?.Text?.Trim(), 200), Passed = passed,
            });
        }
        inspection.Passed = all && request.QuantityFailed == 0;
        return inspection;
    }

    /// <summary>How much of the order's output was moved to hold already.</summary>
    private static async Task<decimal> HeldAsync(RetailPlusDbContext db, ProductionOrder order)
    {
        var moved = await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductionOrderId == order.Id && m.Kind == StockMovementKind.ProductionReceipt && m.Quantity < 0)
            .Join(db.Items.AsNoTracking().Where(i => i.LocationId == order.OutputLocationId), m => m.ItemId, i => i.Id, (m, i) => new { m.Quantity, i.Plu })
            .ToListAsync();
        return -moved.Where(m => string.Equals(m.Plu, order.ProductCode, StringComparison.OrdinalIgnoreCase)).Sum(m => m.Quantity);
    }

    private static void Move(RetailPlusDbContext db, ProductionOrder order, Item item, StockMovementKind kind, decimal quantity, decimal unitCost, string reason, DateTime at)
    {
        item.OnHand += quantity;
        var movement = InventoryService.Record(db, item.Id, kind, quantity, item.OnHand, at, document: order.Number, reason: ManufacturingService.Cut(reason, 100), unitCost: unitCost);
        movement.ProductionOrderId = order.Id;
        movement.StoreId = order.StoreId;
    }

    /// <summary>The same item at another location of the store, with no stock (for goods put on hold where it was not kept).</summary>
    private static Item CopyTo(RetailPlusDbContext db, Item source, Guid locationId)
    {
        var item = new Item
        {
            LocationId = locationId, Plu = source.Plu, Name = source.Name, Barcode = source.Barcode, Size = source.Size, ItemType = source.ItemType,
            DepartmentId = source.DepartmentId, CategoryId = source.CategoryId, SupplierId = source.SupplierId, Taxable1 = source.Taxable1, Taxable2 = source.Taxable2,
            LastCost = source.LastCost, AverageCost = source.AverageCost, Price1 = source.Price1, Price2 = source.Price2, Price3 = source.Price3, Price4 = source.Price4,
        };
        db.Items.Add(item);
        return item;
    }
}
