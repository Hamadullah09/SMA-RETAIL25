using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>
/// The reorder suggestion of one item: its sales, the supplier and lead time, its stock, and the pieces (and cases) to
/// order.
/// </summary>
public sealed record ReorderRow(Guid ItemId, string Plu, string Name, Guid? SupplierId, string? Supplier, decimal OnHand, decimal OnOrder, decimal CustomerOrdered,
    decimal UnitsSold, decimal AverageDailySales, int LeadTimeDays, decimal ReorderPoint, decimal ReorderLevel, decimal TargetStock, decimal CaseQuantity,
    decimal MinimumOrder, decimal SuggestedPieces, decimal OrderQuantity, decimal CostEach, decimal Value);

/// <summary>A suggestion turned into an order line or a requisition line: the item, the pieces and the supplier chosen.</summary>
public sealed record ReorderChoice(Guid ItemId, decimal Pieces, Guid? SupplierId);

/// <summary>
/// Reorder suggestions from sales history: the average daily sales over the store's window
/// (<see cref="SupplySettings.SalesWindowDays"/>), the supplier's lead time (the item's supplier entry, else the supplier's,
/// else the store's default), safety stock days and the days an order should cover, against the stock on hand and on
/// order. The item's own reorder point stays as a floor. Suggestions become requisitions or purchase orders.
/// </summary>
public sealed class ReorderService(DataContext data)
{
    /// <summary>
    /// The items of a location that need ordering now (all items with <paramref name="includeAll"/>), optionally of one
    /// supplier, by supplier and stock code.
    /// </summary>
    public async Task<List<ReorderRow>> SuggestAsync(Guid locationId, Guid? supplierId = null, bool includeAll = false, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        await using var db = data.Create();
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == locationId) ?? throw new BusinessRuleException("That location does not exist.");
        var settings = await SupplySettingsService.ForStoreAsync(db, location.StoreId);
        var items = await db.Items.AsNoTracking().Include(i => i.Suppliers).Include(i => i.Supplier)
            .Where(i => i.LocationId == locationId && !i.IsDeleted
                        && (i.ItemType == ItemType.Standard || i.ItemType == ItemType.Matrix || i.ItemType == ItemType.Serialized))
            .ToListAsync();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => !s.IsDeleted).ToDictionaryAsync(s => s.Id);
        var start = day.AddDays(-settings.SalesWindowDays).ToDateTime(TimeOnly.MinValue);
        var end = day.ToDateTime(TimeOnly.MinValue);
        var ids = items.Select(i => i.Id).ToList();
        // Units sold: sales, less returns, less voided sales put back (added up here: SQLite keeps decimals as text).
        var sold = new Dictionary<Guid, decimal>();
        foreach (var chunk in ids.Chunk(500))
        {
            var part = chunk.ToList();
            foreach (var m in await db.StockMovements.AsNoTracking()
                         .Where(m => part.Contains(m.ItemId) && m.At >= start && m.At < end
                                     && (m.Kind == StockMovementKind.Sale || m.Kind == StockMovementKind.Return || m.Kind == StockMovementKind.Void))
                         .Select(m => new { m.ItemId, m.Quantity }).ToListAsync())
                sold[m.ItemId] = sold.GetValueOrDefault(m.ItemId) - m.Quantity;
        }
        var rows = new List<ReorderRow>();
        foreach (var item in items)
        {
            var entry = item.Suppliers.OrderBy(s => s.Rank).FirstOrDefault(s => s.SupplierId is not null && (item.SupplierId is null || s.SupplierId == item.SupplierId))
                        ?? item.Suppliers.OrderBy(s => s.Rank).FirstOrDefault();
            Guid? supplier = item.SupplierId ?? entry?.SupplierId;
            if (supplierId is Guid only && supplier != only) continue;
            var theirs = supplier is Guid sid ? suppliers.GetValueOrDefault(sid) : null;
            int lead = entry?.LeadTimeDays > 0 ? entry.LeadTimeDays : theirs?.LeadTimeDays > 0 ? theirs.LeadTimeDays : settings.DefaultLeadTimeDays;
            decimal caseQty = item.PackQuantity > 1 ? item.PackQuantity : entry?.CaseQuantity > 1 ? entry.CaseQuantity : 1;
            var s = ReorderRules.Suggest(new ReorderInputs(sold.GetValueOrDefault(item.Id), settings.SalesWindowDays, lead, settings.SafetyStockDays, settings.OrderCoverDays,
                item.OnHand, item.OnOrder, item.CustomerOrdered, item.ReorderPoint, item.ReorderQuantity, caseQty, entry?.MinimumOrder ?? 0));
            if (s.Pieces <= 0 && !includeAll) continue;
            decimal cost = item.LastCost > 0 ? item.LastCost : entry?.Cost > 0 ? entry.Cost : item.AverageCost;
            rows.Add(new ReorderRow(item.Id, item.Plu, item.Name, supplier, theirs?.Company ?? entry?.SupplierName, item.OnHand, item.OnOrder, item.CustomerOrdered,
                sold.GetValueOrDefault(item.Id), s.AverageDailySales, lead, item.ReorderPoint, s.ReorderLevel, s.TargetStock, caseQty, entry?.MinimumOrder ?? 0,
                s.Pieces, s.OrderQuantity, cost, Money.Round(s.Pieces * cost)));
        }
        return [.. rows.OrderBy(r => r.Supplier ?? "￿", StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Plu, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Draft purchase orders of the chosen suggestions, one per supplier. Returns the orders made.</summary>
    public async Task<List<PurchaseOrder>> ToPurchaseOrdersAsync(Guid locationId, IReadOnlyList<ReorderChoice> choices)
    {
        var purchasing = new PurchasingService(data);
        var orders = new List<PurchaseOrder>();
        foreach (var group in choices.Where(c => c.Pieces > 0).GroupBy(c => c.SupplierId))
            orders.Add(await purchasing.CreateFromItemsAsync(locationId, group.Key, "Reorder suggestions",
                [.. group.Select(c => new OrderItem(c.ItemId, c.Pieces))], method: OrderMethod.ReorderPoints));
        if (orders.Count == 0) throw new BusinessRuleException("Choose the items to order first.");
        return orders;
    }

    /// <summary>A requisition of the chosen suggestions (a draft, or sent for approval with <paramref name="submit"/>).</summary>
    public async Task<PurchaseRequisition> ToRequisitionAsync(Guid locationId, IReadOnlyList<ReorderChoice> choices, bool submit = false, DateOnly? neededBy = null)
    {
        var lines = choices.Where(c => c.Pieces > 0).Select(c => new RequisitionLineInput(c.ItemId, null, c.Pieces, null, c.SupplierId)).ToList();
        if (lines.Count == 0) throw new BusinessRuleException("Choose the items to ask for first.");
        var requisitions = new RequisitionService(data);
        var requisition = await requisitions.CreateAsync(locationId, neededBy, "From the reorder suggestions", lines);
        return submit ? await requisitions.SubmitAsync(requisition.Id) : requisition;
    }
}
