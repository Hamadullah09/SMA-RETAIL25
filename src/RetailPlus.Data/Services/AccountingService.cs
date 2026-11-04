using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>One line of the journal for the accounting program: one account's debit or credit on one business day.</summary>
public sealed record JournalExportLine(DateOnly Date, string Journal, string Store, string Account, string? AccountName, decimal Debit, decimal Credit, string Memo);

/// <summary>An exported journal: its lines (one journal per business day per store) and the amounts no account was set for.</summary>
public sealed record JournalExport(IReadOnlyList<JournalExportLine> Lines, IReadOnlyList<string> Unmapped)
{
    /// <summary>Total debits and credits of each journal; they are equal for every journal.</summary>
    public IReadOnlyList<(string Journal, decimal Debit, decimal Credit)> Totals =>
        Lines.GroupBy(l => l.Journal).Select(g => (g.Key, g.Sum(l => l.Debit), g.Sum(l => l.Credit))).OrderBy(t => t.Key, StringComparer.Ordinal).ToList();

    /// <summary>The journal as CSV: Date, Journal, Store, Account, Account name, Debit, Credit, Memo (invariant numbers, ISO dates).</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder("Date,Journal,Store,Account,Account name,Debit,Credit,Memo\r\n");
        static string E(string? s) => s is null ? "" : s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;
        foreach (var l in Lines)
            sb.Append(l.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',').Append(E(l.Journal)).Append(',').Append(E(l.Store)).Append(',')
              .Append(E(l.Account)).Append(',').Append(E(l.AccountName)).Append(',')
              .Append(l.Debit == 0 ? "" : l.Debit.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(l.Credit == 0 ? "" : l.Credit.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(E(l.Memo)).Append("\r\n");
        return sb.ToString();
    }
}

/// <summary>
/// The accounting export (Setup › Accounting): account codes for tenders, departments, taxes, gift card liability, cost
/// of goods and inventory, and a balanced journal per business day per store of the sales, payments, taxes, cost of
/// goods, stock received and stock adjustments in a date range.
/// <para>
/// Voided sales are left out (on the day they were sold too: export again after a void). Stock movements are valued at
/// the unit cost kept on them (the purchase order's cost for receipts, the transfer's, the count's, the case's), and
/// at the item's average cost (last cost when none) where no cost was kept.
/// </para>
/// </summary>
public sealed class AccountingService(DataContext data)
{
    /// <summary>The account code used where no account is set; the export lists what was not mapped.</summary>
    public const string Unmapped = "UNMAPPED";

    /// <summary>Station settings section and keys of the export at End of Day.</summary>
    public const string SettingsSection = "Accounting";
    public const string AutoExportKey = "AutoExport";
    public const string ExportFolderKey = "ExportFolder";

    /// <summary>The kinds that have one account each (tenders and departments have one per name).</summary>
    public static readonly AccountMappingKind[] SingleKinds =
    [
        AccountMappingKind.Sales, AccountMappingKind.Tax1, AccountMappingKind.Tax2, AccountMappingKind.GiftCardLiability, AccountMappingKind.CostOfGoods,
        AccountMappingKind.Inventory, AccountMappingKind.AccountsReceivable, AccountMappingKind.Discounts, AccountMappingKind.Rounding,
        AccountMappingKind.StockAdjustments, AccountMappingKind.PurchasesClearing, AccountMappingKind.InventoryInTransit,
        AccountMappingKind.GiftCardAdjustments,
    ];

    /// <summary>This store's mappings (every store's when the database works for no store).</summary>
    public async Task<List<AccountMapping>> GetMappingsAsync()
    {
        await using var db = data.Create();
        var q = db.AccountMappings.AsNoTracking();
        if (data.Station is { } here) q = q.Where(m => m.StoreId == here.StoreId);
        return await q.OrderBy(m => m.Kind).ThenBy(m => m.Key).ToListAsync();
    }

    /// <summary>
    /// The rows of the Setup › Accounting table: one per account kind, one per tender type and one per department, with
    /// the account set for it (empty when none).
    /// </summary>
    public async Task<List<AccountMapping>> MappingTableAsync()
    {
        var saved = await GetMappingsAsync();
        await using var db = data.Create();
        var tenders = await db.TenderTypes.AsNoTracking().OrderBy(t => t.SortOrder).Select(t => t.Name).ToListAsync();
        var departments = await db.Departments.AsNoTracking().OrderBy(d => d.Name).Select(d => d.Name).ToListAsync();
        AccountMapping Row(AccountMappingKind kind, string key) =>
            saved.FirstOrDefault(m => m.Kind == kind && string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? new AccountMapping { Kind = kind, Key = key };
        var rows = SingleKinds.Select(k => Row(k, "")).ToList();
        rows.AddRange(tenders.Select(t => Row(AccountMappingKind.Tender, t)));
        rows.AddRange(departments.Select(d => Row(AccountMappingKind.Department, d)));
        return rows;
    }

    /// <summary>Saves the table: rows with an account code are kept, rows without one are removed.</summary>
    public async Task SaveMappingsAsync(IEnumerable<AccountMapping> rows)
    {
        await using var db = data.Create();
        var q = db.AccountMappings.AsQueryable();
        if (data.Station is { } here) q = q.Where(m => m.StoreId == here.StoreId);
        var existing = await q.ToListAsync();
        foreach (var row in rows)
        {
            var key = (row.Key ?? "").Trim();
            var code = (row.AccountCode ?? "").Trim();
            var match = existing.FirstOrDefault(m => m.Kind == row.Kind && string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
            if (code.Length == 0)
            {
                if (match is not null) db.AccountMappings.Remove(match);
                continue;
            }
            if (code.Length > 30) throw new BusinessRuleException($"Account code {code} is too long (30 characters at most).");
            if (match is null) db.AccountMappings.Add(match = new AccountMapping { Kind = row.Kind, Key = key });
            match.AccountCode = code;
            match.AccountName = string.IsNullOrWhiteSpace(row.AccountName) ? null : row.AccountName.Trim()[..Math.Min(row.AccountName.Trim().Length, 60)];
        }
        await db.SaveChangesAsync();
    }

    /// <summary>The journal of every business day from <paramref name="from"/> to <paramref name="to"/>, per store.</summary>
    public async Task<JournalExport> ExportAsync(DateOnly from, DateOnly to)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        Guid? store = data.Station?.StoreId;
        await using var db = data.Create();
        var stores = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code);
        var mappings = (await db.AccountMappings.AsNoTracking().ToListAsync()).ToLookup(m => m.StoreId);
        var tenderKinds = await db.TenderTypes.AsNoTracking().ToDictionaryAsync(t => t.Name, t => t.Kind, StringComparer.OrdinalIgnoreCase);
        var book = new Book(stores, mappings, tenderKinds);

        var sales = db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Completed && s.SoldAt >= start && s.SoldAt < end);
        if (store is Guid s1) sales = sales.Where(s => s.StoreId == s1);
        foreach (var sale in await sales.Include(s => s.Lines).Include(s => s.Payments).AsSplitQuery().ToListAsync())
            book.Sale(sale);

        var payments = db.InvoicePayments.AsNoTracking().Where(p => p.PaidOn >= from && p.PaidOn <= to && p.TenderType != "Deposit");
        if (store is Guid s2) payments = payments.Where(p => p.StoreId == s2);
        foreach (var p in await payments.ToListAsync())
        {
            book.Post(p.StoreId, p.PaidOn, book.TenderAccount(p.StoreId, p.TenderType), p.Amount, "Payments on account");
            book.Post(p.StoreId, p.PaidOn, book.Account(p.StoreId, AccountMappingKind.AccountsReceivable), -p.Amount, "Payments on account");
        }

        // Gift card balances a supervisor corrected. An adjustment with a sale belongs to that sale (a card returned, or a
        // voided sale undone, and voided sales are left out), so only those without one are posted here.
        var corrections = db.GiftCardTransactions.AsNoTracking()
            .Where(t => t.Kind == GiftCardTransactionKind.Adjust && t.SaleId == null && t.At >= start && t.At < end);
        if (store is Guid s4) corrections = corrections.Where(t => t.StoreId == s4);
        foreach (var t in await corrections.ToListAsync())
        {
            var day = DateOnly.FromDateTime(t.At);
            book.Post(t.StoreId, day, book.Account(t.StoreId, AccountMappingKind.GiftCardAdjustments), t.Amount, "Gift card corrections");
            book.Post(t.StoreId, day, book.Account(t.StoreId, AccountMappingKind.GiftCardLiability), -t.Amount, "Gift card corrections");
        }

        StockMovementKind[] kinds =
        [
            StockMovementKind.Receipt, StockMovementKind.Adjustment, StockMovementKind.Import, StockMovementKind.Count, StockMovementKind.TransferOut,
            StockMovementKind.TransferIn, StockMovementKind.CaseBreakOut, StockMovementKind.CaseBreakIn,
        ];
        var moves = db.StockMovements.AsNoTracking().Where(m => m.At >= start && m.At < end
            && (m.Kind == kinds[0] || m.Kind == kinds[1] || m.Kind == kinds[2] || m.Kind == kinds[3] || m.Kind == kinds[4] || m.Kind == kinds[5] || m.Kind == kinds[6] || m.Kind == kinds[7]));
        if (store is Guid s3) moves = moves.Where(m => m.StoreId == s3);
        var movements = await moves.ToListAsync();
        var itemIds = movements.Select(m => m.ItemId).Distinct().ToList();
        var costs = (await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.AverageCost, i.LastCost }).ToListAsync())
            .ToDictionary(i => i.Id, i => i.AverageCost > 0 ? i.AverageCost : i.LastCost);
        var poNumbers = movements.Where(m => m.Kind == StockMovementKind.Receipt && m.DocumentNumber != null).Select(m => m.DocumentNumber!.Value).Distinct().ToList();
        var poCosts = (await db.PurchaseOrderLines.AsNoTracking().Where(l => l.ItemId != null)
                .Join(db.PurchaseOrders.AsNoTracking().Where(p => poNumbers.Contains(p.Number)), l => l.PurchaseOrderId, p => p.Id, (l, p) => new { p.Number, ItemId = l.ItemId!.Value, l.CostEach })
                .ToListAsync())
            .GroupBy(x => (x.Number, x.ItemId)).ToDictionary(g => g.Key, g => g.First().CostEach);
        var transferNumbers = movements.Where(m => m.Kind is StockMovementKind.TransferOut or StockMovementKind.TransferIn && m.DocumentNumber != null)
            .Select(m => m.DocumentNumber!.Value).Distinct().ToList();
        var transferLines = await db.StockTransferLines.AsNoTracking()
            .Join(db.StockTransfers.AsNoTracking().Where(t => transferNumbers.Contains(t.Number)), l => l.StockTransferId, t => t.Id,
                (l, t) => new { t.Number, l.SourceItemId, l.DestinationItemId, l.UnitCost })
            .ToListAsync();
        foreach (var m in movements)
        {
            // The cost kept on the movement; for movements made before it was kept, the order's or transfer's cost, else the item's.
            decimal cost = costs.GetValueOrDefault(m.ItemId);
            if (m.Kind == StockMovementKind.Receipt && m.DocumentNumber is long po && poCosts.TryGetValue((po, m.ItemId), out var c)) cost = c;
            if (m.Kind is StockMovementKind.TransferOut or StockMovementKind.TransferIn && m.DocumentNumber is long number
                && transferLines.FirstOrDefault(l => l.Number == number && (l.SourceItemId == m.ItemId || l.DestinationItemId == m.ItemId)) is { } line)
                cost = line.UnitCost;
            if (m.UnitCost > 0) cost = m.UnitCost;
            decimal value = Money.Round(m.Quantity * cost);
            if (value == 0) continue;
            var day = DateOnly.FromDateTime(m.At);
            var (other, memo) = m.Kind switch
            {
                StockMovementKind.Receipt => (AccountMappingKind.PurchasesClearing, "Stock received"),
                StockMovementKind.Count => (AccountMappingKind.StockAdjustments, "Stock counts"),
                StockMovementKind.TransferOut or StockMovementKind.TransferIn => (AccountMappingKind.InventoryInTransit, "Transfers"),
                StockMovementKind.CaseBreakOut or StockMovementKind.CaseBreakIn => (AccountMappingKind.StockAdjustments, "Case breaks"),
                _ => (AccountMappingKind.StockAdjustments, "Stock adjustments"),
            };
            book.Post(m.StoreId, day, book.Account(m.StoreId, AccountMappingKind.Inventory), value, memo);
            book.Post(m.StoreId, day, book.Account(m.StoreId, other), -value, memo);
        }
        return book.Close();
    }

    /// <summary>
    /// Writes the journal of one business day to <paramref name="folder"/> (the End of Day export) and returns the file:
    /// journal-STORE-yyyyMMdd-station-NNN.csv, replaced when the day is exported again.
    /// </summary>
    public async Task<string> ExportDayToFolderAsync(string folder, DateOnly day, int stationNumber)
    {
        var export = await ExportAsync(day, day);
        Directory.CreateDirectory(folder);
        await using var db = data.Create();
        var code = data.Station is { } here ? await db.Stores.Where(s => s.Id == here.StoreId).Select(s => s.Code).SingleOrDefaultAsync() : null;
        var file = Path.Combine(folder, $"journal-{code ?? "ALL"}-{day:yyyyMMdd}-station-{stationNumber:000}.csv");
        await File.WriteAllTextAsync(file, export.ToCsv(), new UTF8Encoding(true));
        return file;
    }

    /// <summary>Gathers the postings of the export, then nets them per day, account and memo into journal lines.</summary>
    private sealed class Book(Dictionary<Guid, string> stores, ILookup<Guid, AccountMapping> mappings, Dictionary<string, TenderKind> tenderKinds)
    {
        private readonly Dictionary<(Guid Store, DateOnly Day, string Account, string Memo), decimal> _amounts = [];
        private readonly Dictionary<(Guid Store, string Account), string?> _names = [];
        private readonly SortedSet<string> _unmapped = new(StringComparer.Ordinal);

        /// <summary>Adds an amount: positive is a debit, negative a credit.</summary>
        public void Post(Guid store, DateOnly day, string account, decimal amount, string memo)
        {
            if (amount == 0) return;
            var key = (store, day, account, memo);
            _amounts[key] = _amounts.GetValueOrDefault(key) + amount;
        }

        public string Account(Guid store, AccountMappingKind kind, string key = "", string? fallbackName = null)
        {
            var m = mappings[store].FirstOrDefault(x => x.Kind == kind && string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (m is not null)
            {
                _names[(store, m.AccountCode)] = m.AccountName;
                return m.AccountCode;
            }
            _unmapped.Add(key.Length == 0 ? Describe(kind) : $"{Describe(kind)} {key}");
            _names[(store, Unmapped)] = fallbackName;
            return Unmapped;
        }

        private bool Has(Guid store, AccountMappingKind kind, string key) =>
            mappings[store].Any(x => x.Kind == kind && string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>A tender's account; gift card and on-account tenders fall back to the liability and receivables accounts.</summary>
        public string TenderAccount(Guid store, string tender)
        {
            if (Has(store, AccountMappingKind.Tender, tender)) return Account(store, AccountMappingKind.Tender, tender);
            return tenderKinds.GetValueOrDefault(tender, TenderKind.Other) switch
            {
                TenderKind.GiftCard => Account(store, AccountMappingKind.GiftCardLiability),
                TenderKind.OnAccount => Account(store, AccountMappingKind.AccountsReceivable),
                _ => Account(store, AccountMappingKind.Tender, tender),
            };
        }

        /// <summary>A department's sales account, or the general sales account.</summary>
        private string SalesAccount(Guid store, string? department) =>
            department is not null && Has(store, AccountMappingKind.Department, department)
                ? Account(store, AccountMappingKind.Department, department)
                : Account(store, AccountMappingKind.Sales);

        /// <summary>
        /// A sale: the money taken (and owed, for layaways) against sales by department, gift cards sold, discounts,
        /// taxes and rounding; and its cost of goods out of inventory. Whatever cents the line taxes of tax-inclusive
        /// prices leave go to the general sales account, so every sale balances.
        /// </summary>
        public void Sale(Sale sale)
        {
            var day = DateOnly.FromDateTime(sale.SoldAt);
            var st = sale.StoreId;
            decimal debits = 0, credits = 0;
            void Dr(string account, decimal amount, string memo) { Post(st, day, account, amount, memo); debits += amount; }
            void Cr(string account, decimal amount, string memo) { Post(st, day, account, -amount, memo); credits += amount; }

            foreach (var p in sale.Payments) Dr(TenderAccount(st, p.TenderType), p.Amount, "Takings " + p.TenderType);
            decimal owing = sale.Total - sale.Payments.Sum(p => p.Amount);
            if (Math.Abs(owing) >= 0.005m) Dr(Account(st, AccountMappingKind.AccountsReceivable), owing, "Layaways and sales on account");
            if (sale.SubtotalDiscount != 0) Dr(Account(st, AccountMappingKind.Discounts), sale.SubtotalDiscount, "Discounts on sales");

            foreach (var line in sale.Lines.Where(l => l.Kind != SaleLineKind.Text))
            {
                if (line.GiftCardNumber is not null) Cr(Account(st, AccountMappingKind.GiftCardLiability), line.LineTotal, "Gift cards sold");
                else Cr(SalesAccount(st, line.Department), line.LineTotal - (sale.TaxInclusive ? line.Tax1 + line.Tax2 : 0), "Sales " + (line.Department ?? "(no department)"));
            }
            if (sale.Tax1 != 0) Cr(Account(st, AccountMappingKind.Tax1), sale.Tax1, sale.Tax1Name ?? "Tax 1");
            if (sale.Tax2 != 0) Cr(Account(st, AccountMappingKind.Tax2), sale.Tax2, sale.Tax2Name ?? "Tax 2");
            if (sale.Rounding != 0) Cr(Account(st, AccountMappingKind.Rounding), sale.Rounding, "Cash rounding");
            decimal rest = debits - credits;
            if (rest != 0) Cr(Account(st, AccountMappingKind.Sales), rest, "Sales (tax cents)");

            if (sale.Cogs != 0)
            {
                Post(st, day, Account(st, AccountMappingKind.CostOfGoods), sale.Cogs, "Cost of goods sold");
                Post(st, day, Account(st, AccountMappingKind.Inventory), -sale.Cogs, "Cost of goods sold");
            }
        }

        public JournalExport Close()
        {
            var lines = _amounts.Where(a => a.Value != 0)
                .Select(a =>
                {
                    var code = stores.GetValueOrDefault(a.Key.Store, "STORE");
                    return new JournalExportLine(a.Key.Day, $"{code}-{a.Key.Day:yyyyMMdd}", code, a.Key.Account, _names.GetValueOrDefault((a.Key.Store, a.Key.Account)),
                        a.Value > 0 ? a.Value : 0, a.Value < 0 ? -a.Value : 0, a.Key.Memo);
                })
                .OrderBy(l => l.Date).ThenBy(l => l.Store, StringComparer.Ordinal).ThenBy(l => l.Debit == 0).ThenBy(l => l.Account, StringComparer.Ordinal)
                .ThenBy(l => l.Memo, StringComparer.Ordinal).ToList();
            return new JournalExport(lines, _unmapped.ToList());
        }

        private static string Describe(AccountMappingKind kind) => kind switch
        {
            AccountMappingKind.GiftCardLiability => "Gift card liability",
            AccountMappingKind.CostOfGoods => "Cost of goods",
            AccountMappingKind.AccountsReceivable => "Accounts receivable",
            AccountMappingKind.StockAdjustments => "Stock adjustments",
            AccountMappingKind.PurchasesClearing => "Purchases clearing",
            AccountMappingKind.InventoryInTransit => "Inventory in transit",
            AccountMappingKind.GiftCardAdjustments => "Gift card adjustments",
            AccountMappingKind.Tax1 => "Tax 1",
            AccountMappingKind.Tax2 => "Tax 2",
            _ => kind.ToString(),
        };
    }
}
