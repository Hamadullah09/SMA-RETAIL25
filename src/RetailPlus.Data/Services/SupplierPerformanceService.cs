using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>
/// One supplier's performance over a period: orders received and how many on time, fill rate (pieces received against
/// ordered), price changes between receipts of the same item, returns to the supplier (RMAs) and the average lead time.
/// Percentages are null where there is nothing to work them out from.
/// </summary>
public sealed record SupplierPerformanceRow(Guid? SupplierId, string Supplier, int Orders, int OnTime, decimal? OnTimePercent, decimal OrderedPieces,
    decimal ReceivedPieces, decimal? FillRatePercent, int PriceIncreases, int PriceDecreases, decimal? AveragePriceChangePercent, int Returns,
    decimal ReturnedPieces, decimal ReturnedValue, decimal? AverageLeadDays, decimal ReceivedValue);

/// <summary>
/// The supplier performance report. An order counts for each supplier on its lines (or the order's supplier); it is on
/// time when it was received by its expected date (<see cref="PurchaseOrder.ExpectedOn"/>), else within the supplier's
/// lead time of being posted (the store's default lead time when the supplier has none). Lead time runs from posting the
/// order (its creation when never posted) to receiving it.
/// </summary>
public sealed class SupplierPerformanceService(DataContext data)
{
    public async Task<List<SupplierPerformanceRow>> ReportAsync(DateOnly from, DateOnly to, Guid? store = null)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        await using var db = data.Create();
        var q = db.PurchaseOrders.AsNoTracking().Where(p => p.Status == PurchaseOrderStatus.Received && p.ReceivedAt != null);
        if (store is Guid s) q = q.Where(p => p.StoreId == s);
        // Every received order up to the end: the earlier ones give the last price before the period.
        var orders = await q.Where(p => p.ReceivedAt < end).Include(p => p.Lines).ToListAsync();
        var suppliers = await db.Suppliers.AsNoTracking().ToListAsync();
        var byId = suppliers.ToDictionary(x => x.Id);
        var byName = suppliers.Where(x => !string.IsNullOrWhiteSpace(x.Company)).GroupBy(x => x.Company!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var defaults = new Dictionary<Guid, int>();
        foreach (var storeId in orders.Select(o => o.StoreId).Distinct())
            defaults[storeId] = (await SupplySettingsService.ForStoreAsync(db, storeId)).DefaultLeadTimeDays;

        // Each line under its supplier (by id, else by the name kept on the line).
        string Key(PurchaseOrder po, PurchaseOrderLine l) =>
            (l.SupplierId ?? po.SupplierId) is Guid id ? id.ToString() : (l.SupplierName?.Trim() is { Length: > 0 } name ? "name:" + name.ToUpperInvariant() : "none");
        var stats = new Dictionary<string, Stats>();
        Stats For(string key, Guid? id, string name) => stats.TryGetValue(key, out var x) ? x : stats[key] = new Stats(id, name);

        foreach (var po in orders.Where(o => o.ReceivedAt >= start))
            foreach (var group in po.Lines.GroupBy(l => Key(po, l)))
            {
                var first = group.First();
                Guid? id = first.SupplierId ?? po.SupplierId;
                var supplier = id is Guid sid ? byId.GetValueOrDefault(sid) : null;
                var st = For(group.Key, id, supplier?.Company ?? first.SupplierName ?? "(no supplier)");
                st.Orders++;
                var placed = po.PostedAt ?? po.CreatedAt;
                var received = po.ReceivedAt!.Value;
                st.LeadDays.Add((decimal)(received.Date - placed.Date).TotalDays);
                int lead = supplier?.LeadTimeDays > 0 ? supplier.LeadTimeDays : defaults.GetValueOrDefault(po.StoreId, 7);
                var due = po.ExpectedOn ?? DateOnly.FromDateTime(placed).AddDays(lead);
                if (DateOnly.FromDateTime(received) <= due) st.OnTime++;
                st.Ordered += group.Sum(l => l.OrderQuantity * l.CaseQuantity);
                st.Received += group.Sum(l => l.QuantityReceived);
                st.Value += group.Sum(l => l.QuantityReceived * l.CostEach);
            }

        // Price changes: each receipt of an item in the period against the receipt of it from the same supplier before.
        var receipts = orders.SelectMany(po => po.Lines.Where(l => l.QuantityReceived > 0 && l.CostEach > 0).Select(l => (Key: Key(po, l), Item: l.ItemId?.ToString() ?? l.Plu,
                At: po.ReceivedAt!.Value, Cost: l.CostEach, Line: l, Po: po)))
            .OrderBy(r => r.At).ThenBy(r => r.Po.Number).ToList();
        foreach (var group in receipts.GroupBy(r => (r.Key, r.Item)))
        {
            decimal? last = null;
            foreach (var r in group)
            {
                if (last is decimal before && r.At >= start && before != r.Cost)
                {
                    Guid? id = r.Line.SupplierId ?? r.Po.SupplierId;
                    var st = For(r.Key, id, (id is Guid sid ? byId.GetValueOrDefault(sid)?.Company : null) ?? r.Line.SupplierName ?? "(no supplier)");
                    if (r.Cost > before) st.Increases++; else st.Decreases++;
                    st.Changes.Add((r.Cost - before) / before * 100m);
                }
                last = r.Cost;
            }
        }

        // Returns to the supplier: RMAs written to them in the period (the RMA keeps the supplier's name).
        var rmas = db.Rmas.AsNoTracking().Where(r => r.IsSupplierRma);
        if (store is Guid rs) rmas = rmas.Where(r => r.StoreId == rs);
        foreach (var rma in await rmas.ToListAsync())
        {
            var day = rma.SupplierRmaDate ?? rma.SetDate ?? DateOnly.FromDateTime(rma.CreatedAtUtc);
            if (day < from || day > to || string.IsNullOrWhiteSpace(rma.Supplier)) continue;
            var supplier = byName.GetValueOrDefault(rma.Supplier.Trim());
            var key = supplier is not null ? supplier.Id.ToString() : "name:" + rma.Supplier.Trim().ToUpperInvariant();
            var st = For(key, supplier?.Id, supplier?.Company ?? rma.Supplier.Trim());
            st.Returns++;
            st.ReturnedPieces += rma.Quantity;
            st.ReturnedValue += Money.Round(rma.Quantity * rma.UnitPrice);
        }

        return [.. stats.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => new SupplierPerformanceRow(x.Id, x.Name, x.Orders, x.OnTime,
            x.Orders == 0 ? null : Math.Round(100m * x.OnTime / x.Orders, 1), x.Ordered, x.Received, x.Ordered == 0 ? null : Math.Round(100m * x.Received / x.Ordered, 1),
            x.Increases, x.Decreases, x.Changes.Count == 0 ? null : Math.Round(x.Changes.Average(), 2), x.Returns, x.ReturnedPieces, x.ReturnedValue,
            x.LeadDays.Count == 0 ? null : Math.Round(x.LeadDays.Average(), 1), Money.Round(x.Value)))];
    }

    private sealed class Stats(Guid? id, string name)
    {
        public Guid? Id { get; } = id;
        public string Name { get; } = name;
        public int Orders { get; set; }
        public int OnTime { get; set; }
        public decimal Ordered { get; set; }
        public decimal Received { get; set; }
        public decimal Value { get; set; }
        public int Increases { get; set; }
        public int Decreases { get; set; }
        public List<decimal> Changes { get; } = [];
        public List<decimal> LeadDays { get; } = [];
        public int Returns { get; set; }
        public decimal ReturnedPieces { get; set; }
        public decimal ReturnedValue { get; set; }
    }
}
