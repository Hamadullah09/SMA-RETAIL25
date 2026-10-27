using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Data;

namespace RetailPlus.Cutover;

public sealed partial class CutoverRunner
{
    /// <summary>Days listed one by one in the checks; with more days, only those that differ are.</summary>
    private const int DaysListed = 31;

    /// <summary>
    /// The checks: totals worked out from Retail25 and again from the new database (inside the run's transaction, so a dry
    /// run checks what it would have written). Stock per location (units and value at cost), receivables, gift card
    /// liability, loyalty points, and sales per business day and per tender, plus the row counts of the main areas.
    /// </summary>
    private async Task CheckAsync(CancellationToken ct)
    {
        await using var db = _w.NewContext();
        await CheckStockAsync(db, ct);
        await CheckMoneyAsync(db, ct);
        await CheckSalesAsync(db, ct);
        await CheckCountsAsync(db, ct);
    }

    private async Task CheckStockAsync(RetailPlusDbContext db, CancellationToken ct)
    {
        var layaway = OpenLayawayStockLines().GroupBy(l => l.Long("product_id")).ToDictionary(g => g.Key, g => g.Sum(l => R3(l.Dec("quantity"))));
        var levels = _src["stock_levels"].Rows.ToLookup(l => l.Long("product_id"));
        foreach (var store in _stores.Values)
        {
            decimal units = 0, value = 0, retail25Units = 0;
            foreach (var item in _items.Values.Where(i => i.Store == store))
            {
                decimal onHand = levels[item.Row.Id].Where(l => l.Long("location_id") == item.Row.Long("location_id")).Sum(l => R3(l.Dec("on_hand")));
                retail25Units += onHand;
                onHand -= layaway.GetValueOrDefault(item.Row.Id);
                units += onHand;
                value += onHand * R3(ValuationCost(item.Row.Dec("avg_cost"), item.Row.Dec("last_cost")));
            }
            var items = await db.Items.AsNoTracking().Where(i => i.LocationId == store.LocationId).Select(i => new { i.Id, i.OnHand, i.AverageCost, i.LastCost }).ToListAsync(ct);
            var ids = items.Select(i => i.Id).ToList();
            decimal ledger = 0;
            foreach (var chunk in ids.Chunk(2000))
                ledger += (await db.StockMovements.Where(m => chunk.Contains(m.ItemId)).Select(m => m.Quantity).ToListAsync(ct)).Sum();
            _report.Check($"Stock {store.Code}", "Units on hand", units, items.Sum(i => i.OnHand));
            _report.Check($"Stock {store.Code}", "Units in the stock movements", units, ledger);
            _report.Check($"Stock {store.Code}", "Value at cost", R2(value), R2(items.Sum(i => i.OnHand * ValuationCost(i.AverageCost, i.LastCost))));
            if (retail25Units != units)
                Note($"Store {store.Code}: Retail25 counts {retail25Units:0.###} units in stock, {retail25Units - units:0.###} of them on open layaways; here layaway goods are out of stock, so {units:0.###} are on hand.");
        }
    }

    private async Task CheckMoneyAsync(RetailPlusDbContext db, CancellationToken ct)
    {
        var storeIds = _stores.Values.Select(s => s.StoreId).ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => storeIds.Contains(i.StoreId) && !i.IsVoided)
            .Select(i => new { i.Kind, i.Balance, i.ClientId }).ToListAsync(ct);
        _report.Check("Receivables", "Open invoice balances", InvoiceOwing().Values.Sum(), invoices.Where(i => i.Kind == InvoiceKind.Invoice).Sum(i => i.Balance));
        _report.Check("Receivables", "Owing on open layaways", LayawayOwing().Values.Sum(), invoices.Where(i => i.Kind == InvoiceKind.Layaway).Sum(i => i.Balance));

        var clientIds = _clients.Values.Select(c => c.Id).ToList();
        var accounts = _src["customer_accounts"].Rows.Where(a => _clients.ContainsKey(a.Long("customer_id"))).Sum(a => R2(a.Dec("balance_due")));
        decimal balances = 0, points = 0, pointsLedger = 0;
        foreach (var chunk in clientIds.Chunk(2000))
        {
            var rows = await db.Clients.AsNoTracking().Where(c => chunk.Contains(c.Id)).Select(c => new { c.Balance, c.Points }).ToListAsync(ct);
            balances += rows.Sum(r => r.Balance);
            points += rows.Sum(r => r.Points);
            pointsLedger += (await db.LoyaltyTransactions.Where(t => chunk.Contains(t.ClientId)).Select(t => t.Points).ToListAsync(ct)).Sum();
        }
        _report.Check("Receivables", "Client balances (customer accounts and layaways owing)", accounts + LayawayOwing().Values.Sum(), balances);

        decimal liability = 0;
        var cardIds = new List<Guid>();
        foreach (var table in new[] { "gift_cards", "gift_certificates" })
            foreach (var row in _src[table].Rows)
            {
                var id = StableIds.For(table, row.Id);
                if (_report.StoreCode is not null && ClientOf(row.LongOrNull("issued_to_customer_id")) is null && row.LongOrNull("issued_to_customer_id") is not null) continue;
                cardIds.Add(id);
                liability += R2(row.Dec("remaining_value"));
            }
        decimal cardBalances = 0;
        foreach (var chunk in cardIds.Chunk(2000))
            cardBalances += (await db.GiftCardTransactions.Where(t => chunk.Contains(t.GiftCardId)).Select(t => t.Amount).ToListAsync(ct)).Sum();
        _report.Check("Gift cards", "Liability (value left on cards and certificates)", liability, cardBalances);

        decimal sourcePoints = _clients.Values.Sum(c => R3(c.Client.Points));
        _report.Check("Loyalty", "Points held by clients", sourcePoints, points);
        _report.Check("Loyalty", "Points in the loyalty transactions", sourcePoints, pointsLedger);
    }

    /// <summary>The store's business day of a moment in its local time (Retail25 days may start after midnight).</summary>
    private static DateOnly BusinessDay(CarriedStore store, DateTime local) => DateOnly.FromDateTime(local - store.DayStart);

    private async Task CheckSalesAsync(RetailPlusDbContext db, CancellationToken ct)
    {
        var tenders = _src["sale_tenders"].Rows.ToLookup(t => t.Long("transaction_id"));
        foreach (var store in _stores.Values)
        {
            var source = _src["sales_transactions"].Rows.Where(s => s.Long("location_id") == store.Row.Id && IsRealSale(s)).ToList();
            var sourceDays = source.GroupBy(s => s.Date("business_date") ?? BusinessDay(store, Local(store, s.Instant("completed_at") ?? DateTimeOffset.UnixEpoch)))
                .ToDictionary(g => g.Key, g => (Count: g.Count(), Total: g.Where(s => !Is(s, "status", "Voided", 1)).Sum(s => R2(s.Dec("grand_total") + s.Dec("rounding_adjustment")))));
            var target = await db.Sales.AsNoTracking().Where(s => s.StoreId == store.StoreId)
                .Select(s => new { s.Id, s.SoldAt, s.Status, s.Total }).ToListAsync(ct);
            var targetDays = target.GroupBy(s => BusinessDay(store, s.SoldAt))
                .ToDictionary(g => g.Key, g => (Count: g.Count(), Total: g.Where(s => s.Status == SaleStatus.Completed).Sum(s => s.Total)));

            string area = $"Sales {store.Code}";
            _report.Check(area, "Sales (completed and voided)", source.Count, target.Count);
            _report.Check(area, "Net total of completed sales", sourceDays.Values.Sum(d => d.Total), targetDays.Values.Sum(d => d.Total));
            var days = sourceDays.Keys.Union(targetDays.Keys).Order().ToList();
            int same = 0;
            foreach (var day in days)
            {
                var s = sourceDays.GetValueOrDefault(day);
                var t = targetDays.GetValueOrDefault(day);
                bool match = s == t;
                if (match) same++;
                if (match && days.Count > DaysListed) continue;
                string name = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                _report.Check(area, $"{name}: sales", s.Count, t.Count);
                _report.Check(area, $"{name}: net total", s.Total, t.Total);
            }
            if (days.Count > DaysListed) _report.Check(area, $"Business days alike (of {days.Count})", days.Count, same);

            var sourceTenders = source.Where(s => !Is(s, "status", "Voided", 1)).SelectMany(s => tenders[s.Id])
                .GroupBy(t => TenderName(t.Long("tender_type_id"))).ToDictionary(g => g.Key, g => g.Sum(t => R2(t.Dec("amount"))), StringComparer.OrdinalIgnoreCase);
            var completed = target.Where(s => s.Status == SaleStatus.Completed).Select(s => s.Id).ToList();
            var targetTenders = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in completed.Chunk(2000))
                foreach (var p in await db.SalePayments.AsNoTracking().Where(p => chunk.Contains(p.SaleId)).Select(p => new { p.TenderType, p.Amount }).ToListAsync(ct))
                    targetTenders[p.TenderType] = targetTenders.GetValueOrDefault(p.TenderType) + p.Amount;
            foreach (var tender in sourceTenders.Keys.Union(targetTenders.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
                _report.Check(area, $"Tender {tender}", sourceTenders.GetValueOrDefault(tender), targetTenders.GetValueOrDefault(tender));
        }
    }

    private async Task CheckCountsAsync(RetailPlusDbContext db, CancellationToken ct)
    {
        async Task Count<T>(string name, IEnumerable<Guid> ids) where T : Entity
        {
            var list = ids.ToList();
            int found = 0;
            foreach (var chunk in list.Chunk(2000)) found += await db.Set<T>().CountAsync(e => chunk.Contains(e.Id), ct);
            _report.Check("Rows", name, list.Count, found);
        }
        await Count<Item>("Items", _items.Values.Select(i => i.Id));
        await Count<Client>("Clients", _clients.Values.Select(c => c.Id));
        await Count<Supplier>("Suppliers", _suppliers.Values.Select(s => s.Id));
        await Count<UserAccount>("Users", _users.Values.Select(u => u.Id).Distinct());
        await Count<Sale>("Sales", _sales.Values.Select(s => s.Sale.Id));
    }

    private static readonly Dictionary<string, string> NotCarriedReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audit_log_entries"] = "Retail25's audit log stays with Retail25 (keep its backup); the new audit log starts with this run's Import line.",
        ["drawer_sessions"] = "Drawer sessions are kept per till here. Close every drawer in Retail25 before the freeze; their history stays in Retail25.",
        ["drawer_ledger_entries"] = "Pay-ins, pay-outs and floats of Retail25's drawer sessions (history).",
        ["stock_ledger_entries"] = "Retail25's stock history; each item starts here with one opening movement of its stock level.",
        ["stock_counts"] = "Counts in progress are not carried over: post or cancel them before the freeze.",
        ["stock_count_lines"] = "Lines of Retail25's stock counts (see stock_counts).",
        ["supervisor_approvals"] = "Approvals were for one action at one moment.",
        ["fiscal_years"] = "Year end starts afresh here.",
        ["sales_history_archives"] = "Sales of closed years archived by Retail25 stay in Retail25.",
        ["purchase_order_receipts"] = "Receipts of purchase orders (history; the stock levels include them).",
        ["pricing_rule_settings"] = "The order of the pricing rules is fixed in the new program.",
        ["number_sequences"] = "New documents are numbered per station from station × 10,000,000 + 1; Retail25's numbers stay on what it made.",
        ["permissions"] = "The new program has its own permission list; each role's Retail25 keys are translated.",
        ["external_entity_maps"] = "Links to the accounting program kept by Retail25's export.",
        ["sync_logs"] = "Retail25's accounting export log.",
        ["migration_batches"] = "Retail25's own import from Retail Plus.",
        ["migration_staging_rows"] = "Retail25's own import from Retail Plus.",
        ["shoppers"] = "Self-checkout shopper accounts have no counterpart in the new program yet.",
        ["shopper_devices"] = "Self-checkout shopper phones have no counterpart in the new program yet.",
        ["trolleys"] = "Self-checkout trolleys have no counterpart in the new program yet.",
        ["trolley_sessions"] = "Self-checkout trolleys have no counterpart in the new program yet.",
        ["carts"] = "Unfinished sales on the web POS. Stop selling before the freeze.",
        ["cart_lines"] = "Unfinished sales on the web POS.",
        ["cart_adjustments"] = "Unfinished sales on the web POS.",
        ["cart_tax_overrides"] = "Unfinished sales on the web POS.",
        ["devices"] = "Web POS terminal agents. Tills are enrolled again with the new server.",
        ["device_enrolments"] = "Web POS terminal agents. Tills are enrolled again with the new server.",
        ["rfid_readers"] = "RFID readers are set up again (RFID readers screen).",
        ["reader_profiles"] = "Hardware settings are made again at each till (Setup › This station).",
        ["reader_antenna_assignments"] = "RFID readers are set up again (RFID readers screen).",
        ["printer_profiles"] = "Hardware settings are made again at each till (Setup › This station).",
        ["scale_profiles"] = "Hardware settings are made again at each till (Setup › This station).",
        ["pole_display_profiles"] = "Hardware settings are made again at each till (Setup › This station).",
        ["data_protection_keys"] = "Keys of the web system's sign-in cookies; users sign in again.",
        ["AspNetRoleClaims"] = "Sign-in data of the web system.",
        ["AspNetUserClaims"] = "Sign-in data of the web system.",
        ["AspNetUserLogins"] = "Sign-in data of the web system.",
        ["AspNetUserTokens"] = "Sign-in data of the web system.",
        ["OpenIddictApplications"] = "Sign-in data of the web system.",
        ["OpenIddictAuthorizations"] = "Sign-in data of the web system.",
        ["OpenIddictScopes"] = "Sign-in data of the web system.",
        ["OpenIddictTokens"] = "Sign-in data of the web system.",
        ["__EFMigrationsHistory"] = "Retail25's schema history.",
    };

    /// <summary>Every Retail25 table with rows that the cutover does not read, with the reason.</summary>
    private void ListNotCarriedOver()
    {
        foreach (var (table, rows) in _src.RowCounts.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (rows == 0 || Tables.Contains(table, StringComparer.OrdinalIgnoreCase)) continue;
            string reason = NotCarriedReasons.TryGetValue(table, out var why) ? why
                : table.StartsWith("cached_", StringComparison.OrdinalIgnoreCase) ? "Web POS caches."
                : char.IsUpper(table[0]) ? "Retail25's background job queue (Hangfire) or other system table."
                : "No place in the new model.";
            _report.NotCarriedOver.Add(new NotCarried(table, rows, reason));
        }
        int stockHistory = _src["ar_ledger_entries"].Count;
        if (stockHistory > 0)
            _report.NotCarriedOver.Add(new NotCarried("Receivables ledger history (ar_ledger_entries)", stockHistory,
                "Invoices come over with their balances, penalties and payments; the ledger lines behind them stay in Retail25."));
    }
}
