using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

public sealed record PurchaseOrderRow(int Id, long Number, DateTime CreatedAt, PurchaseOrderStatus Status, OrderMethod Method, string? Description,
    int Lines, decimal TotalCost);

/// <summary>What arrived for one purchase order line.</summary>
public sealed record ReceivedLine(int LineId, decimal PiecesReceived, decimal CostEach);

/// <summary>The Purchase Order window: generate, edit, post the order, post the shipment.</summary>
public sealed class PurchasingService(DataContext data)
{
    public async Task<List<PurchaseOrderRow>> ListAsync(int locationId, PurchaseOrderStatus? status = null)
    {
        await using var db = data.Create();
        var q = db.PurchaseOrders.AsNoTracking().Where(p => p.LocationId == locationId);
        if (status is { } s) q = q.Where(p => p.Status == s);
        return await q.OrderByDescending(p => p.Number)
            .Select(p => new PurchaseOrderRow(p.Id, p.Number, p.CreatedAt, p.Status, p.Method, p.Description, p.Lines.Count, p.Lines.Sum(l => l.OrderCost)))
            .ToListAsync();
    }

    public async Task<PurchaseOrder?> GetAsync(int id)
    {
        await using var db = data.Create();
        return await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines.OrderBy(l => l.SupplierName).ThenBy(l => l.Plu)).SingleOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>
    /// File / New: creates an order and fills it with the items that need ordering by the chosen method, optionally
    /// only one supplier's, one department's or the flagged items.
    /// </summary>
    public async Task<PurchaseOrder> CreateAsync(int locationId, OrderMethod method, string? description, string? supplierName = null,
        string? department = null, bool flaggedOnly = false, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        await using var db = data.Create();
        var po = new PurchaseOrder
        {
            LocationId = locationId, CreatedAt = DateTime.Now, Status = PurchaseOrderStatus.Draft, Method = method, Description = description,
            Number = await db.NextNumberAsync(RetailPlusDbContext.Sequences.PurchaseOrderNumbers),
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
                .ToDictionaryAsync(p => p.ItemId, p => p.Units);

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
    public async Task<bool> AddItemAsync(int purchaseOrderId, string plu, decimal pieces = 1)
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
    public async Task PostOrderAsync(int purchaseOrderId)
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
                .SetProperty(i => i.OnOrder, i => i.OnOrder + pieces).SetProperty(i => i.LastOrderDate, today));
        }
        po.Status = PurchaseOrderStatus.Posted;
        po.PostedAt = DateTime.Now;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// Post Shipment: adds what arrived to On Hand, takes the ordered pieces off On Order, and updates last cost and
    /// average cost (with the shipping cost spread over the pieces received, by value).
    /// </summary>
    public async Task PostShipmentAsync(int purchaseOrderId, IReadOnlyList<ReceivedLine> received, decimal shippingCost = 0)
    {
        await using var db = data.Create();
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
            line.QuantityReceived = pieces;
            line.CostEach = cost;
            if (line.ItemId is not int itemId) continue;
            var item = await db.Items.SingleAsync(i => i.Id == itemId);
            if (wasPosted) item.OnOrder = Math.Max(0, item.OnOrder - line.OrderQuantity * line.CaseQuantity);
            if (pieces == 0) continue;
            decimal share = totalValue == 0 ? 0 : shippingCost * (pieces * cost) / totalValue;
            item.AverageCost = Reordering.AverageCostAfterReceipt(item.OnHand, item.AverageCost, pieces, cost, share);
            item.LastCost = cost;
            item.OnHand += pieces;
            item.LastReceivedDate = today;
            item.LastReceivedQuantity = pieces;
            if (item.Price1 > 0) item.MarginPercent = Money.MarginPercent(item.Price1, item.AverageCost);
        }
        po.Status = PurchaseOrderStatus.Received;
        po.ReceivedAt = DateTime.Now;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>Deletes an order; a posted (not received) order first takes its pieces back off On Order.</summary>
    public async Task DeleteAsync(int purchaseOrderId)
    {
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var po = await db.PurchaseOrders.Include(p => p.Lines).SingleAsync(p => p.Id == purchaseOrderId);
        if (po.Status == PurchaseOrderStatus.Posted)
        {
            foreach (var line in po.Lines.Where(l => l.ItemId is not null))
            {
                decimal pieces = line.OrderQuantity * line.CaseQuantity;
                await db.Items.Where(i => i.Id == line.ItemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.OnOrder, i => i.OnOrder - pieces < 0 ? 0 : i.OnOrder - pieces));
            }
        }
        db.PurchaseOrders.Remove(po);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
