using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

public sealed record PurchaseOrderRow(Guid Id, long Number, DateTime CreatedAt, PurchaseOrderStatus Status, OrderMethod Method, string? Description,
    int Lines, decimal TotalCost);

/// <summary>
/// What arrived for one purchase order line. For an item kept in batches, <paramref name="Batches"/> says what arrived of
/// each batch (adding up to the pieces received).
/// </summary>
public sealed record ReceivedLine(Guid LineId, decimal PiecesReceived, decimal CostEach, IReadOnlyList<ReceivedBatch>? Batches = null);

/// <summary>An item to put on a new purchase order, in pieces.</summary>
public sealed record OrderItem(Guid ItemId, decimal Pieces, decimal? CostEach = null);

/// <summary>The Purchase Order window: generate, edit, post the order, post the shipment.</summary>
public sealed class PurchasingService(DataContext data)
{
    public async Task<List<PurchaseOrderRow>> ListAsync(Guid locationId, PurchaseOrderStatus? status = null)
    {
        await using var db = data.Create();
        var q = db.PurchaseOrders.AsNoTracking().Where(p => p.LocationId == locationId);
        if (status is { } s) q = q.Where(p => p.Status == s);
        return await q.OrderByDescending(p => p.Number)
            .Select(p => new PurchaseOrderRow(p.Id, p.Number, p.CreatedAt, p.Status, p.Method, p.Description, p.Lines.Count, p.Lines.Sum(l => l.OrderCost)))
            .ToListAsync();
    }

    public async Task<PurchaseOrder?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines.OrderBy(l => l.SupplierName).ThenBy(l => l.Plu)).SingleOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>
    /// File / New: creates an order and fills it with the items that need ordering by the chosen method, optionally
    /// only one supplier's, one department's or the flagged items.
    /// </summary>
    public async Task<PurchaseOrder> CreateAsync(Guid locationId, OrderMethod method, string? description, string? supplierName = null,
        string? department = null, bool flaggedOnly = false, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        await using var db = data.Create();
        var po = new PurchaseOrder
        {
            LocationId = locationId, CreatedAt = DateTime.Now, Status = PurchaseOrderStatus.Draft, Method = method, Description = description,
            Number = await db.NextNumberAsync(RetailPlusDbContext.Sequences.PurchaseOrderNumbers),
            CreatedBy = data.CurrentUser?.UserName, CreatedById = data.CurrentUser?.UserId,
        };
        if (method != OrderMethod.Blank)
        {
            var q = db.Items.AsNoTracking().Include(i => i.Supplier).Include(i => i.Suppliers)
                .Where(i => i.LocationId == locationId && !i.IsDeleted
                            && (i.ItemType == ItemType.Standard || i.ItemType == ItemType.Matrix || i.ItemType == ItemType.Serialized));
            if (flaggedOnly) q = q.Where(i => i.IsFlagged);
            if (!string.IsNullOrWhiteSpace(supplierName)) q = q.Where(i => i.Supplier != null && i.Supplier.Company == supplierName);
            if (!string.IsNullOrWhiteSpace(department)) q = q.Where(i => i.Department != null && i.Department.Name == department);
            var items = await q.ToListAsync();
            var ids = items.Select(i => i.Id).ToList();

            int week = (day.DayOfYear - 1) / 7 + 1;
            var weeks = Enumerable.Range(1, 3).Select(k => week - k).Where(w => w >= 1).ToList();
            var weekly = await db.ItemPeriodSales.AsNoTracking()
                .Where(p => ids.Contains(p.ItemId) && p.Year == day.Year && p.Kind == SalesPeriodKind.Week && weeks.Contains(p.Period))
                .GroupBy(p => p.ItemId).Select(g => new { g.Key, Units = g.Sum(p => p.Units) }).ToDictionaryAsync(x => x.Key, x => x.Units);
            var lastYear = await db.ItemPeriodSales.AsNoTracking()
                .Where(p => ids.Contains(p.ItemId) && p.Year == day.Year - 1 && p.Kind == SalesPeriodKind.Month && p.Period == day.Month)
                .GroupBy(p => p.ItemId).Select(g => new { g.Key, Units = g.Sum(p => p.Units) }).ToDictionaryAsync(x => x.Key, x => x.Units);

            foreach (var item in items)
            {
                decimal avgWeekly = weeks.Count == 0 ? 0 : weekly.GetValueOrDefault(item.Id) / weeks.Count;
                decimal pieces = Reordering.SuggestedPieces(method, item, avgWeekly, lastYear.GetValueOrDefault(item.Id));
                if (pieces <= 0) continue;
                po.Lines.Add(LineFor(item, pieces));
            }
        }
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();
        return po;
    }

    private static PurchaseOrderLine LineFor(Item item, decimal pieces)
    {
        var preferred = item.Suppliers.OrderBy(s => s.Rank).FirstOrDefault();
        decimal caseQty = item.PackQuantity > 1 ? item.PackQuantity : (preferred?.CaseQuantity > 1 ? preferred.CaseQuantity : 1);
        decimal cost = item.LastCost > 0 ? item.LastCost : preferred?.Cost ?? 0;
        var line = new PurchaseOrderLine
        {
            ItemId = item.Id, Plu = item.Plu, Description = item.Name.Length > 30 ? item.Name[..30] : item.Name, Size = item.Size,
            CaseQuantity = caseQty, OrderQuantity = Reordering.ToOrderQuantity(pieces, caseQty), CostEach = cost,
            OnHandAtOrder = item.OnHand, OnOrderAtOrder = item.OnOrder, BaseStock = item.BaseStock, ReorderPoint = item.ReorderPoint,
            ReorderQuantity = item.ReorderQuantity, BackOrders = item.CustomerOrdered, SupplierId = item.SupplierId,
            SupplierName = item.Supplier?.Company ?? preferred?.SupplierName, ReorderNumber = item.SupplierCode ?? preferred?.SupplierStockCode,
        };
        line.OrderCost = Money.Round(line.OrderQuantity * line.CaseQuantity * line.CostEach);
        return line;
    }

    /// <summary>Adds an item to a draft order by its stock code; returns false when no such item exists.</summary>
    public async Task<bool> AddItemAsync(Guid purchaseOrderId, string plu, decimal pieces = 1)
    {
        await using var db = data.Create();
        var po = await db.PurchaseOrders.Include(p => p.Lines).SingleAsync(p => p.Id == purchaseOrderId);
        if (po.Status != PurchaseOrderStatus.Draft) throw new BusinessRuleException("Only a draft order can be changed.");
        var code = plu.Trim().ToUpperInvariant();
        var item = await db.Items.AsNoTracking().Include(i => i.Supplier).Include(i => i.Suppliers)
            .FirstOrDefaultAsync(i => i.LocationId == po.LocationId && i.Plu == code && !i.IsDeleted);
        if (item is null) return false;
        po.Lines.Add(LineFor(item, pieces));
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Saves the editable columns of a draft order (quantities, costs, description) and removes deleted lines.</summary>
    public async Task SaveAsync(PurchaseOrder edited)
    {
        await using var db = data.Create();
        var po = await db.PurchaseOrders.Include(p => p.Lines).SingleAsync(p => p.Id == edited.Id);
        po.Description = edited.Description;
        if (po.Status == PurchaseOrderStatus.Draft)
        {
            var keep = edited.Lines.ToDictionary(l => l.Id);
            foreach (var line in po.Lines.ToList())
            {
                if (!keep.TryGetValue(line.Id, out var e)) { db.PurchaseOrderLines.Remove(line); continue; }
                line.OrderQuantity = e.OrderQuantity;
                line.CaseQuantity = e.CaseQuantity <= 0 ? 1 : e.CaseQuantity;
                line.CostEach = e.CostEach;
                line.OrderCost = Money.Round(line.OrderQuantity * line.CaseQuantity * line.CostEach);
                line.IsFlagged = e.IsFlagged;
            }
        }
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("This order was changed at another station. Reload it and try again.");
        }
    }

    /// <summary>Post Order: the ordered pieces are added to each item's On Order count.</summary>
    public async Task PostOrderAsync(Guid purchaseOrderId)
    {
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var po = await db.PurchaseOrders.Include(p => p.Lines).SingleAsync(p => p.Id == purchaseOrderId);
        if (po.Status != PurchaseOrderStatus.Draft) throw new BusinessRuleException("This order was already posted.");
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var line in po.Lines.Where(l => l.ItemId is not null && l.OrderQuantity > 0))
        {
            decimal pieces = line.OrderQuantity * line.CaseQuantity;
            await db.Items.Where(i => i.Id == line.ItemId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.OnOrder, i => i.OnOrder + pieces).SetProperty(i => i.LastOrderDate, today).Touched());
        }
        // The price agreed, which the supplier's bill is matched against (receiving may value the goods otherwise).
        foreach (var line in po.Lines) line.OrderedCost = line.CostEach;
        po.Status = PurchaseOrderStatus.Posted;
        po.PostedAt = DateTime.Now;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// Post Shipment: adds what arrived to On Hand, takes the ordered pieces off On Order, and updates last cost and
    /// average cost (with the shipping cost spread over the pieces received, by value). An item kept in batches is
    /// received per batch: each batch named on its line is found (or made, with its expiry date, or one worked out from the
    /// item's shelf life) and gets a receipt movement of its own.
    /// </summary>
    public async Task PostShipmentAsync(Guid purchaseOrderId, IReadOnlyList<ReceivedLine> received, decimal shippingCost = 0)
    {
        await using var db = data.Create();
        // The receipt is its own record (stock movements): the item counts and costs it changes are not audited one by one.
        db.SuppressAudit = true;
        await using var tx = await db.Database.BeginTransactionAsync();
        var po = await db.PurchaseOrders.Include(p => p.Lines).SingleAsync(p => p.Id == purchaseOrderId);
        if (po.Status == PurchaseOrderStatus.Received) throw new BusinessRuleException("This shipment was already posted.");
        bool wasPosted = po.Status == PurchaseOrderStatus.Posted;
        var byLine = received.ToDictionary(r => r.LineId);
        decimal totalValue = received.Sum(r => r.PiecesReceived * r.CostEach);
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var line in po.Lines)
        {
            byLine.TryGetValue(line.Id, out var r);
            decimal pieces = r?.PiecesReceived ?? 0;
            decimal cost = r?.CostEach ?? line.CostEach;
            if (line.OrderedCost == 0) line.OrderedCost = line.CostEach;
            line.QuantityReceived = pieces;
            line.CostEach = cost;
            if (line.ItemId is not Guid itemId) continue;
            var item = await db.Items.SingleAsync(i => i.Id == itemId);
            if (wasPosted) item.OnOrder = Math.Max(0, item.OnOrder - line.OrderQuantity * line.CaseQuantity);
            if (pieces == 0) continue;
            decimal share = totalValue == 0 ? 0 : shippingCost * (pieces * cost) / totalValue;
            item.AverageCost = Reordering.AverageCostAfterReceipt(item.OnHand, item.AverageCost, pieces, cost, share);
            item.LastCost = cost;
            item.LastReceivedDate = today;
            item.LastReceivedQuantity = pieces;
            if (item.TracksBatches && pieces > 0)
            {
                var batches = (r?.Batches ?? []).Where(b => b.Pieces != 0).ToList();
                if (batches.Count == 0)
                    throw new BusinessRuleException($"{item.Plu} {item.Name} is kept in batches: enter the batch number and expiry date of what arrived.");
                if (batches.Sum(b => b.Pieces) != pieces)
                    throw new BusinessRuleException($"The batches of {item.Plu} add up to {batches.Sum(b => b.Pieces):0.###}, not the {pieces:0.###} pieces received.");
                decimal running = item.OnHand;
                foreach (var b in batches)
                {
                    if (string.IsNullOrWhiteSpace(b.BatchNumber)) throw new BusinessRuleException($"{item.Plu}: enter the batch number.");
                    var batch = await BatchService.FindOrMakeAsync(db, itemId, po.StoreId, b.BatchNumber, b.ExpiresOn ?? Fefo.SuggestedExpiry(item.ShelfLifeDays, b.MadeOn, today),
                        b.MadeOn, line.SupplierId ?? po.SupplierId, po.Number, today);
                    InventoryService.Record(db, itemId, StockMovementKind.Receipt, b.Pieces, running += b.Pieces, document: po.Number, unitCost: cost, batchId: batch.Id);
                }
                item.OnHand += pieces;
            }
            else
            {
                item.OnHand += pieces;
                InventoryService.Record(db, itemId, StockMovementKind.Receipt, pieces, item.OnHand, document: po.Number, unitCost: cost);
            }
            if (item.Price1 > 0) item.MarginPercent = Money.MarginPercent(item.Price1, item.AverageCost);
        }
        po.Status = PurchaseOrderStatus.Received;
        po.ReceivedAt = DateTime.Now;
        po.ReceivedBy = data.CurrentUser?.UserName;
        po.ReceivedById = data.CurrentUser?.UserId;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// A draft order of the given items for one supplier (or none), as requisitions and reorder suggestions make them. The
    /// pieces are rounded up to whole cases of the supplier's case quantity; the cost is the one given, else the item's.
    /// Works at a till and on the server (numbered from the server's own range).
    /// </summary>
    public async Task<PurchaseOrder> CreateFromItemsAsync(Guid locationId, Guid? supplierId, string? description, IReadOnlyList<OrderItem> items,
        DateOnly? expectedOn = null, OrderMethod method = OrderMethod.Blank)
    {
        if (items.Count == 0) throw new BusinessRuleException("There is nothing to order.");
        await using var db = data.Create();
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == locationId) ?? throw new BusinessRuleException("That location does not exist.");
        var supplier = supplierId is Guid sid ? await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sid) : null;
        var po = new PurchaseOrder
        {
            StoreId = location.StoreId, LocationId = locationId, CreatedAt = DateTime.Now, Status = PurchaseOrderStatus.Draft, Method = method,
            Description = Cut(description ?? supplier?.Company, 100), SupplierId = supplier?.Id, ExpectedOn = expectedOn,
            CreatedBy = data.CurrentUser?.UserName, CreatedById = data.CurrentUser?.UserId,
        };
        po.Number = await DocumentNumbers.NextAsync(data, db, RetailPlusDbContext.Sequences.PurchaseOrderNumbers);
        var ids = items.Select(i => i.ItemId).Distinct().ToList();
        var found = await db.Items.AsNoTracking().Include(i => i.Supplier).Include(i => i.Suppliers).Where(i => ids.Contains(i.Id) && i.LocationId == locationId).ToListAsync();
        foreach (var wanted in items.Where(i => i.Pieces > 0))
        {
            var item = found.FirstOrDefault(i => i.Id == wanted.ItemId) ?? throw new BusinessRuleException("An item to order is not at this location.");
            var line = LineFor(item, wanted.Pieces);
            var theirs = item.Suppliers.Where(s => supplier is not null && s.SupplierId == supplier.Id).OrderBy(s => s.Rank).FirstOrDefault();
            if (supplier is not null)
            {
                line.SupplierId = supplier.Id;
                line.SupplierName = Cut(supplier.Company, 30);
                if (theirs is not null)
                {
                    line.ReorderNumber = theirs.SupplierStockCode ?? line.ReorderNumber;
                    if (theirs.CaseQuantity > 1 && item.PackQuantity <= 1)
                    {
                        line.CaseQuantity = theirs.CaseQuantity;
                        line.OrderQuantity = Math.Ceiling(wanted.Pieces / theirs.CaseQuantity);
                    }
                    if (theirs.Cost > 0 && item.LastCost <= 0) line.CostEach = theirs.Cost;
                }
            }
            if (wanted.CostEach is decimal cost && cost > 0) line.CostEach = cost;
            // Never fewer pieces than asked for: requisitions and suggestions are rounded up to whole cases.
            if (line.CaseQuantity > 1 && line.OrderQuantity * line.CaseQuantity < wanted.Pieces) line.OrderQuantity = Math.Ceiling(wanted.Pieces / line.CaseQuantity);
            line.OrderCost = Money.Round(line.OrderQuantity * line.CaseQuantity * line.CostEach);
            po.Lines.Add(line);
        }
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();
        return po;
    }

    private static string? Cut(string? text, int length) => text is null || text.Length <= length ? text : text[..length];

    /// <summary>Deletes an order; a posted (not received) order first takes its pieces back off On Order.</summary>
    public async Task DeleteAsync(Guid purchaseOrderId)
    {
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var po = await db.PurchaseOrders.Include(p => p.Lines).SingleAsync(p => p.Id == purchaseOrderId);
        if (po.Status == PurchaseOrderStatus.Posted)
        {
            foreach (var line in po.Lines.Where(l => l.ItemId is not null))
            {
                decimal pieces = line.OrderQuantity * line.CaseQuantity;
                await db.Items.Where(i => i.Id == line.ItemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.OnOrder, i => i.OnOrder - pieces < 0 ? 0 : i.OnOrder - pieces).Touched());
            }
        }
        db.PurchaseOrders.Remove(po);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
