using RetailPlus.Core.Model;
using RetailPlus.Cutover.Source;

namespace RetailPlus.Cutover;

public sealed partial class CutoverRunner
{
    /// <summary>
    /// Purchase orders not yet fully received (draft, posted, partly received), with their lines. Received, closed and
    /// cancelled orders are history: their stock is in the stock levels already.
    /// </summary>
    private async Task PurchaseOrdersAsync(CancellationToken ct)
    {
        Log("Purchase orders");
        var area = _report.Area("Purchase orders", "purchase_orders");
        var lineArea = _report.Area("Purchase order lines", "purchase_order_lines");
        var lines = _src["purchase_order_lines"].Rows.ToLookup(l => l.Long("purchase_order_id"));
        var links = _src["product_suppliers"].Rows.GroupBy(s => (s.Long("product_id"), s.Long("supplier_id"))).ToDictionary(g => g.Key, g => g.First());
        var orders = new List<PurchaseOrder>();
        var orderLines = new List<PurchaseOrderLine>();
        var numbers = new HashSet<(Guid, long)>();
        foreach (var row in _src["purchase_orders"].Rows.OrderBy(r => r.Id))
        {
            area.Read++;
            lineArea.Read += lines[row.Id].Count();
            int status = row.Int("status");
            string? skip = StoreOf(row.Long("location_id")) is null ? "store not carried over"
                : status switch { 3 => "received (history)", 4 => "closed (history)", 5 => "cancelled (history)", > 5 => "unknown status", _ => null };
            if (skip is null && !numbers.Add((StoreOf(row.Long("location_id"))!.StoreId, row.Long("po_number")))) skip = "a second order with the same number in the store";
            if (skip is not null) { area.Skip(skip); lineArea.Skip("order " + skip, lines[row.Id].Count()); continue; }
            var store = StoreOf(row.Long("location_id"))!;
            var supplier = _suppliers.GetValueOrDefault(row.Long("supplier_id"));
            var order = From(new PurchaseOrder
            {
                Id = StableIds.For("purchase-order", row.Id), StoreId = store.StoreId, Number = row.Long("po_number"), LocationId = store.LocationId,
                CreatedAt = Local(store, row.Instant("created_at") ?? DateTimeOffset.UtcNow),
                Status = status == 0 ? PurchaseOrderStatus.Draft : PurchaseOrderStatus.Posted,
                Method = row.Int("quantity_strategy") switch { 1 => OrderMethod.OneWeek, 2 => OrderMethod.TwoWeeks, 3 or 4 => OrderMethod.ReorderPoints, 5 => OrderMethod.MonthlySales, _ => OrderMethod.Blank },
                Description = row.Text("header_text") ?? supplier.Name,
                PostedAt = row.Date("posted_on")?.ToDateTime(TimeOnly.MinValue),
                CreatedAtUtc = Utc(row.Instant("created_at")),
            }, row);
            orders.Add(order);
            if (status == 2) Note($"Retail25 purchase order {order.Number} of {store.Code} is partly received; it is a posted order here with what was received so far on each line.");
            foreach (var l in lines[row.Id].OrderBy(l => l.Id))
            {
                var item = ItemOf(l.Long("product_id"));
                if (item is null) { lineArea.Skip("item not carried over"); continue; }
                orderLines.Add(From(new PurchaseOrderLine
                {
                    Id = StableIds.For("purchase-order-line", l.Id), PurchaseOrderId = order.Id, ItemId = item.Id, Plu = item.Plu, Description = item.Name,
                    CaseQuantity = l.Dec("case_qty") > 0 ? Three(l.Dec("case_qty"), "PurchaseOrderLine.CaseQuantity") : 1,
                    OrderQuantity = Three(l.Dec("order_qty"), "PurchaseOrderLine.OrderQuantity"), CostEach = Three(l.Dec("cost_each"), "PurchaseOrderLine.CostEach"),
                    OrderCost = Money(l.Dec("order_cost"), "PurchaseOrderLine.OrderCost"),
                    OnHandAtOrder = Three(l.Dec("in_stock_at_generation"), "PurchaseOrderLine.OnHandAtOrder"), OnOrderAtOrder = Three(l.Dec("on_order_at_generation"), "PurchaseOrderLine.OnOrderAtOrder"),
                    BackOrders = Three(l.Dec("back_orders"), "PurchaseOrderLine.BackOrders"), QuantityReceived = Three(l.Dec("qty_received"), "PurchaseOrderLine.QuantityReceived"),
                    BaseStock = item.Row.Dec("base_stock"), ReorderPoint = item.Row.Dec("reorder_point"), ReorderQuantity = item.Row.Dec("reorder_qty"),
                    SupplierId = supplier.Id == Guid.Empty ? null : supplier.Id, SupplierName = supplier.Name,
                    ReorderNumber = links.GetValueOrDefault((l.Long("product_id"), row.Long("supplier_id")))?.Text("reorder_number"),
                    CreatedAtUtc = Utc(l.Instant("created_at")),
                }, l));
            }
        }
        await _w.UpsertAsync(area, orders, ct);
        await _w.UpsertAsync(lineArea, orderLines, ct);
        var orderIds = orders.Select(o => o.Id).ToHashSet();
        await _w.RemoveMissingAsync<PurchaseOrderLine>(lineArea, db => db.PurchaseOrderLines.Where(l => orderIds.Contains(l.PurchaseOrderId)), orderLines.Select(l => l.Id).ToHashSet(), ct);
    }

    /// <summary>Open customer orders: one row per item still to be supplied, as the new model (and Retail Plus) keeps them.</summary>
    private async Task CustomerOrdersAsync(CancellationToken ct)
    {
        var area = _report.Area("Customer orders", "customer_orders, customer_order_lines");
        var lines = _src["customer_order_lines"].Rows.ToLookup(l => l.Long("customer_order_id"));
        var rows = new List<CustomerOrder>();
        foreach (var order in _src["customer_orders"].Rows.OrderBy(r => r.Id))
        {
            int status = order.Int("status");
            string? skip = StoreOf(order.Long("location_id")) is null ? "store not carried over"
                : status switch { 2 => "filled (history)", 3 => "cancelled (history)", _ => null };
            foreach (var line in lines[order.Id].OrderBy(l => l.Id))
            {
                area.Read++;
                decimal open = line.Dec("ordered_qty") - line.Dec("filled_qty");
                if (skip is not null) { area.Skip(skip); continue; }
                if (open <= 0) { area.Skip("line already filled"); continue; }
                var store = StoreOf(order.Long("location_id"))!;
                var client = ClientOf(order.Long("customer_id"));
                var item = ItemOf(line.Long("product_id"));
                var staff = StaffOf(order.LongOrNull("staff_id"));
                rows.Add(From(new CustomerOrder
                {
                    Id = StableIds.For("customer-order-line", line.Id), StoreId = store.StoreId, LocationId = store.LocationId, ClientId = client?.Id,
                    Name = client is null ? null : $"{client.Client.FirstName} {client.Client.LastName}".Trim(), Company = client?.Client.Company,
                    Phone = client?.Client.WorkPhone ?? client?.Client.HomePhone, OrderDate = order.Date("ordered_on"), SoldBy = staff?.Name, SoldById = staff?.Id,
                    InvoiceNumber = order.Long("order_number"), CustomerNumber = client?.Client.Number, ItemId = item?.Id, Plu = item?.Plu,
                    Description = item?.Name ?? "Item " + Number(line.Long("product_id")),
                    UnitPrice = Money(line.Dec("unit_price"), "CustomerOrder.UnitPrice"), Quantity = Three(open, "CustomerOrder.Quantity"),
                    CreatedAtUtc = Utc(line.Instant("created_at")),
                }, line));
            }
        }
        await _w.UpsertAsync(area, rows, ct);
    }

    /// <summary>
    /// Transfers in transit (shipped and not received): the goods have left the sending location, so the transfer comes
    /// over as shipped and is received here. Drafts and finished transfers stay behind.
    /// </summary>
    private async Task TransfersAsync(CancellationToken ct)
    {
        var area = _report.Area("Transfers in transit", "stock_transfers");
        var lineArea = _report.Area("Transfer lines", "stock_transfer_lines");
        var lines = _src["stock_transfer_lines"].Rows.ToLookup(l => l.Long("stock_transfer_id"));
        var transfers = new List<StockTransfer>();
        var transferLines = new List<StockTransferLine>();
        foreach (var row in _src["stock_transfers"].Rows.OrderBy(r => r.Id))
        {
            area.Read++;
            lineArea.Read += lines[row.Id].Count();
            var from = StoreOf(row.Long("from_location_id"));
            var to = StoreOf(row.Long("to_location_id"));
            string? skip = !Is(row, "status", "InTransit", 1)
                    ? Is(row, "status", "Draft", 0) ? "draft (not shipped)" : Is(row, "status", "Received", 2) ? "received (history)" : "cancelled (history)"
                : from is null || to is null ? "a store at either end not carried over"
                : from.LocationId == to.LocationId ? "from and to the same location" : null;
            if (skip is not null) { area.Skip(skip); lineArea.Skip("transfer " + skip, lines[row.Id].Count()); continue; }
            var transfer = From(new StockTransfer
            {
                Id = StableIds.For("transfer", row.Id), StoreId = from!.StoreId, ToStoreId = to!.StoreId, Number = row.Long("transfer_number"),
                FromLocationId = from.LocationId, ToLocationId = to.LocationId, Status = StockTransferStatus.Shipped,
                CreatedAt = Local(from, row.Instant("created_at") ?? DateTimeOffset.UtcNow), ShippedAt = row.Instant("shipped_at") is { } shipped ? Local(from, shipped) : null,
                Note = row.Text("notes"), CreatedAtUtc = Utc(row.Instant("created_at")),
            }, row);
            transfers.Add(transfer);
            int number = 0;
            foreach (var l in lines[row.Id].OrderBy(l => l.Id))
            {
                var item = ItemOf(l.Long("product_id"));
                transferLines.Add(From(new StockTransferLine
                {
                    Id = StableIds.For("transfer-line", l.Id), StockTransferId = transfer.Id, LineNumber = ++number,
                    SourceItemId = item?.Id ?? StableIds.For("product", l.Long("product_id")),
                    Plu = l.Text("stock_code")?.ToUpperInvariant() ?? item?.Plu ?? "P" + Number(l.Long("product_id")),
                    Description = l.Text("product_name") ?? item?.Name ?? "", Barcode = item?.Row.Text("upc") is { Length: <= 14 } upc ? upc : null,
                    Department = item?.Department, ItemType = item?.Type ?? ItemType.Standard,
                    Taxable1 = item?.Row.Bool("tax1applies") ?? true, Taxable2 = item?.Row.Bool("tax2applies") ?? true,
                    UnitCost = Three(l.Dec("unit_cost"), "StockTransferLine.UnitCost"), Price = R2(item?.Row.Dec("regular_price") ?? 0),
                    QuantityShipped = Three(l.Dec("quantity"), "StockTransferLine.QuantityShipped"),
                }, l));
            }
        }
        await _w.UpsertAsync(area, transfers, ct);
        await _w.UpsertAsync(lineArea, transferLines, ct);
    }

    /// <summary>
    /// Commission rules (for one salesperson, on one item, one department or everything; percent of sales or an amount per
    /// unit) and the time clock. Retail25's "percent of profit" rules have no counterpart and stay behind.
    /// </summary>
    private async Task CommissionAndTimeClockAsync(CancellationToken ct)
    {
        Log("Commission rules and time clock");
        var ruleArea = _report.Area("Commission rules", "commission_rules");
        var rules = new List<CommissionRule>();
        foreach (var row in _src["commission_rules"].Rows.OrderBy(r => r.Id))
        {
            ruleArea.Read++;
            var staff = StaffOf(row.Long("staff_id"));
            var type = row.Text("commission_type");
            CommissionBasis? basis = type switch { "Percentage" or "0" => CommissionBasis.PercentOfNetSales, "Fixed" or "1" => CommissionBasis.AmountPerUnit, _ => null };
            CarriedItem? item = ItemOf(row.LongOrNull("product_id"));
            (Guid Id, string Name)? department = row.LongOrNull("department_id") is long d && _departments.TryGetValue(d, out var dept) ? dept : null;
            string? skip = staff is null ? "salesperson not carried over"
                : basis is null ? "percent of profit (no such rule here)"
                : row.LongOrNull("product_id") is not null && item is null ? "item not carried over"
                : row.LongOrNull("department_id") is not null && department is null && item is null ? "department not carried over" : null;
            if (skip is not null) { ruleArea.Skip(skip); continue; }
            var scope = item is not null ? CommissionScope.Item : department is not null ? CommissionScope.Department : CommissionScope.AllItems;
            var on = item is not null ? item.Plu : department is not null ? department.Value.Name : "all items";
            rules.Add(From(new CommissionRule
            {
                Id = StableIds.For("commission-rule", row.Id), Name = $"{staff!.Name}: {on}", UserId = staff.Id, Scope = scope,
                ItemId = item?.Id, DepartmentId = scope == CommissionScope.Department ? department!.Value.Id : null,
                Basis = basis!.Value, Rate = Three(row.Dec("value"), "CommissionRule.Rate"), IsActive = row.Bool("is_active"),
                CreatedAtUtc = Utc(row.Instant("created_at")),
            }, row));
            if (row.DecOrNull("max_commission") is { } cap)
                Warn($"commission_rules {row.Id} ({staff.Name}, {on}): the cap of {cap:0.00} per item is not carried over (caps are set per item here, Inventory › Special)");
        }
        await _w.UpsertAsync(ruleArea, rules, ct);

        var clockArea = _report.Area("Time clock", "time_clock_entries");
        var entries = new List<TimeClockEntry>();
        var open = new HashSet<Guid>();
        foreach (var row in _src["time_clock_entries"].Rows.OrderByDescending(r => r.Instant("clock_in")).ThenByDescending(r => r.Id))
        {
            clockArea.Read++;
            var staff = StaffOf(row.Long("staff_id"));
            var store = StoreOf(row.Long("location_id"));
            if (staff is null || store is null) { clockArea.Skip(staff is null ? "staff member not carried over" : "store not carried over"); continue; }
            if (row.Instant("clock_out") is null && !open.Add(staff.Id))
            {
                clockArea.Skip("a second open entry of the same person (only the latest stays open)");
                continue;
            }
            entries.Add(From(new TimeClockEntry
            {
                Id = StableIds.For("time-clock", row.Id), StoreId = store.StoreId, UserId = staff.Id, UserName = staff.Name,
                ClockIn = Local(store, row.Instant("clock_in") ?? DateTimeOffset.UnixEpoch),
                ClockOut = row.Instant("clock_out") is { } outAt ? Local(store, outAt) : null,
                CreatedAtUtc = Utc(row.Instant("created_at")),
            }, row));
        }
        await _w.UpsertAsync(clockArea, entries, ct, merge: (stored, mapped) =>
        {
            // A correction made here stays.
            if (stored.EditReason is null) return;
            (mapped.ClockIn, mapped.ClockOut) = (stored.ClockIn, stored.ClockOut);
            (mapped.EditReason, mapped.EditedBy, mapped.EditedById, mapped.EditedAtUtc) = (stored.EditReason, stored.EditedBy, stored.EditedById, stored.EditedAtUtc);
        });
    }
}
