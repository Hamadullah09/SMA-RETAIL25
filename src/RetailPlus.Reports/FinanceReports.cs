using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Reports;

/// <summary>What a statement is compared with.</summary>
public enum FinanceComparison
{
    None,
    /// <summary>The same number of days just before.</summary>
    PreviousPeriod,
    /// <summary>The same days a year before.</summary>
    PreviousYear,
    /// <summary>The budget of the same months (profit and loss).</summary>
    Budget,
}

/// <summary>A finance report the web offers, and what it is run with.</summary>
public sealed record FinanceReportInfo(string Key, string Title, bool UsesDates, bool UsesComparison, bool UsesAccount, bool AsOfDate);

/// <summary>How a finance report is run: the days (or the day it is as of), the store (null: the company), a comparison, an account.</summary>
public sealed record FinanceReportOptions(DateOnly From, DateOnly To, Guid? Store = null, FinanceComparison Compare = FinanceComparison.None, Guid? AccountId = null);

/// <summary>
/// The finance statements and reports, per store or for the company, as report tables (shown, printed and exported as CSV
/// like every other report): trial balance, general ledger, account ledger, profit and loss, balance sheet, cash flow
/// (indirect method), sales tax return, receivables and payables ageing, and budget against actual.
/// </summary>
public sealed class FinanceReports(DataContext data)
{
    public static IReadOnlyList<FinanceReportInfo> Catalog { get; } =
    [
        new("trial-balance", "Trial balance", true, false, false, false),
        new("profit-and-loss", "Profit and loss", true, true, false, false),
        new("balance-sheet", "Balance sheet", true, true, false, true),
        new("cash-flow", "Cash flow", true, false, false, false),
        new("general-ledger", "General ledger", true, false, true, false),
        new("account-ledger", "Account ledger", true, false, true, false),
        new("sales-tax", "Sales tax return", true, false, false, false),
        new("receivables-ageing", "Receivables ageing", true, false, false, true),
        new("payables-ageing", "Payables ageing", true, false, false, true),
        new("budget-vs-actual", "Budget against actual", true, false, false, false),
    ];

    private LedgerService Ledger => new(data);

    public async Task<ReportTable> RunAsync(string key, FinanceReportOptions options) => key switch
    {
        "trial-balance" => await TrialBalanceAsync(options),
        "profit-and-loss" => await ProfitAndLossAsync(options),
        "balance-sheet" => await BalanceSheetAsync(options),
        "cash-flow" => await CashFlowAsync(options),
        "general-ledger" => await GeneralLedgerAsync(options),
        "account-ledger" => await AccountLedgerAsync(options),
        "sales-tax" => await SalesTaxAsync(options),
        "receivables-ageing" => await ReceivablesAgeingAsync(options),
        "payables-ageing" => await PayablesAgeingAsync(options),
        "budget-vs-actual" => await BudgetAsync(options),
        _ => throw new BusinessRuleException($"There is no finance report {key}."),
    };

    // Trial balance and ledgers ---------------------------------------------------------------------------------------

    public async Task<ReportTable> TrialBalanceAsync(FinanceReportOptions o)
    {
        var balances = await Ledger.BalancesAsync(o.From, o.To, o.Store);
        var table = new ReportTable
        {
            Title = "Trial balance", Subtitle = Period(o),
            Columns =
            [
                new("Code"), new("Account"), new("Type"), new("Opening", ColumnKind.Money, true), new("Debit", ColumnKind.Money, true), new("Credit", ColumnKind.Money, true),
                new("Closing debit", ColumnKind.Money, true), new("Closing credit", ColumnKind.Money, true),
            ],
        };
        foreach (var b in balances.Where(b => b.Opening != 0 || b.Debit != 0 || b.Credit != 0))
            table.Rows.Add([b.Code, b.Name, b.Type.ToString(), b.Opening, b.Debit, b.Credit, b.Closing > 0 ? b.Closing : 0m, b.Closing < 0 ? -b.Closing : 0m]);
        return table;
    }

    public async Task<ReportTable> GeneralLedgerAsync(FinanceReportOptions o)
    {
        var balances = await Ledger.BalancesAsync(o.From, o.To, o.Store);
        var table = new ReportTable
        {
            Title = "General ledger", Subtitle = Period(o),
            Columns = [new("Date", ColumnKind.Date), new("Entry"), new("Account"), new("Memo"), new("Reference"), new("Debit", ColumnKind.Money), new("Credit", ColumnKind.Money), new("Balance", ColumnKind.Money)],
        };
        foreach (var b in balances.Where(b => o.AccountId is null || b.AccountId == o.AccountId))
        {
            if (b.Debit == 0 && b.Credit == 0 && b.Opening == 0) continue;
            var name = $"{b.Code} {b.Name}";
            table.Rows.Add([null, null, name, "Opening balance", null, null, null, b.Opening]);
            foreach (var r in await Ledger.AccountLedgerAsync(b.AccountId, o.From, o.To, o.Store))
                table.Rows.Add([r.Date, r.Number.ToString(CultureInfo.InvariantCulture), name, r.LineMemo ?? r.Memo, r.Reference, r.Debit == 0 ? null : r.Debit, r.Credit == 0 ? null : r.Credit, r.Balance]);
            table.Rows.Add([null, null, name, "Closing balance", null, b.Debit, b.Credit, b.Closing]);
        }
        return table;
    }

    public async Task<ReportTable> AccountLedgerAsync(FinanceReportOptions o)
    {
        if (o.AccountId is not Guid id) throw new BusinessRuleException("Choose the account.");
        var account = (await Ledger.GetAccountsAsync()).SingleOrDefault(a => a.Id == id) ?? throw new BusinessRuleException("That account no longer exists.");
        var rows = await Ledger.AccountLedgerAsync(id, o.From, o.To, o.Store);
        decimal opening = (await Ledger.BalancesAsync(o.From, o.To, o.Store)).SingleOrDefault(b => b.AccountId == id)?.Opening ?? 0;
        var table = new ReportTable
        {
            Title = $"Account ledger: {account.Code} {account.Name}", Subtitle = Period(o),
            Columns = [new("Date", ColumnKind.Date), new("Entry"), new("Kind"), new("Memo"), new("Reference"), new("Debit", ColumnKind.Money, true), new("Credit", ColumnKind.Money, true), new("Balance", ColumnKind.Money)],
        };
        table.Rows.Add([null, null, null, "Opening balance", null, null, null, opening]);
        foreach (var r in rows)
            table.Rows.Add([r.Date, r.Number.ToString(CultureInfo.InvariantCulture), r.Kind.ToString(), r.LineMemo ?? r.Memo, r.Reference, r.Debit, r.Credit, r.Balance]);
        return table;
    }

    // Statements ------------------------------------------------------------------------------------------------------

    public async Task<ReportTable> ProfitAndLossAsync(FinanceReportOptions o)
    {
        var accounts = await Ledger.GetAccountsAsync();
        var now = await PeriodAmountsAsync(o.From, o.To, o.Store);
        Dictionary<Guid, decimal>? before = null;
        string? compareTitle = null;
        if (o.Compare == FinanceComparison.Budget)
        {
            before = await new BudgetService(data).AmountsAsync(o.From, o.To, o.Store);
            compareTitle = "Budget";
        }
        else if (o.Compare is FinanceComparison.PreviousPeriod or FinanceComparison.PreviousYear)
        {
            var (from, to) = Earlier(o);
            before = await PeriodAmountsAsync(from, to, o.Store);
            compareTitle = o.Compare == FinanceComparison.PreviousYear ? "Previous year" : "Previous period";
        }
        var table = Statement("Profit and loss", Period(o), compareTitle);
        decimal Total(Dictionary<Guid, decimal> amounts, AccountType type) =>
            accounts.Where(a => a.Type == type).Sum(a => amounts.GetValueOrDefault(a.Id));
        Section(table, accounts, AccountType.Income, now, before, sign: -1, "Total income");
        Section(table, accounts, AccountType.Expense, now, before, sign: 1, "Total expenses");
        decimal profit = -(Total(now, AccountType.Income) + Total(now, AccountType.Expense));
        decimal? profitBefore = before is null ? null : -(Total(before, AccountType.Income) + Total(before, AccountType.Expense));
        AddRow(table, "", profit >= 0 ? "Net profit" : "Net loss", profit, profitBefore);
        return table;
    }

    public async Task<ReportTable> BalanceSheetAsync(FinanceReportOptions o)
    {
        var accounts = await Ledger.GetAccountsAsync();
        var asOf = o.To;
        var now = await AsOfAsync(asOf, o.Store);
        Dictionary<Guid, decimal>? before = null;
        string? compareTitle = null;
        if (o.Compare is FinanceComparison.PreviousPeriod or FinanceComparison.PreviousYear)
        {
            var earlier = o.Compare == FinanceComparison.PreviousYear ? asOf.AddYears(-1) : o.From.AddDays(-1);
            before = await AsOfAsync(earlier, o.Store);
            compareTitle = $"At {earlier:yyyy-MM-dd}";
        }
        var table = Statement("Balance sheet", $"At {asOf:yyyy-MM-dd}{StoreText(o)}", compareTitle);
        Section(table, accounts, AccountType.Asset, now, before, sign: 1, "Total assets");
        Section(table, accounts, AccountType.Liability, now, before, sign: -1, "Total liabilities");
        // Income and expenses not yet closed to retained earnings belong to the owners: shown as this year's profit.
        decimal Unclosed(Dictionary<Guid, decimal> amounts) => -accounts.Where(a => a.Type is AccountType.Income or AccountType.Expense).Sum(a => amounts.GetValueOrDefault(a.Id));
        Section(table, accounts, AccountType.Equity, now, before, sign: -1, null);
        decimal equity = -accounts.Where(a => a.Type == AccountType.Equity).Sum(a => now.GetValueOrDefault(a.Id));
        decimal? equityBefore = before is null ? null : -accounts.Where(a => a.Type == AccountType.Equity).Sum(a => before.GetValueOrDefault(a.Id));
        AddRow(table, "", "Profit not yet closed", Unclosed(now), before is null ? null : Unclosed(before));
        AddRow(table, "", "Total equity", equity + Unclosed(now), equityBefore + (before is null ? null : Unclosed(before)));
        decimal liabilities = -accounts.Where(a => a.Type == AccountType.Liability).Sum(a => now.GetValueOrDefault(a.Id));
        decimal? liabilitiesBefore = before is null ? null : -accounts.Where(a => a.Type == AccountType.Liability).Sum(a => before.GetValueOrDefault(a.Id));
        AddRow(table, "", "Total liabilities and equity", liabilities + equity + Unclosed(now),
            before is null ? null : liabilitiesBefore + equityBefore + Unclosed(before));
        return table;
    }

    /// <summary>
    /// The cash flow by the indirect method: the profit, then the change of every other balance sheet account (an asset
    /// growing takes cash, a liability growing gives it) in its section (operating unless the account says investing or
    /// financing; equity is financing), ending with cash and bank at the start and the end. Year-end closing entries are
    /// left out (they move no cash).
    /// </summary>
    public async Task<ReportTable> CashFlowAsync(FinanceReportOptions o)
    {
        var accounts = (await Ledger.GetAccountsAsync()).ToDictionary(a => a.Id);
        var balances = (await Ledger.BalancesAsync(o.From, o.To, o.Store, exceptClosing: true)).ToDictionary(b => b.AccountId);
        HashSet<Guid> cash;
        await using (var db = data.Create())
            cash = (await db.BankAccounts.AsNoTracking().Select(b => b.LedgerAccountId).ToListAsync()).ToHashSet();
        foreach (var a in accounts.Values.Where(a => a.Role is LedgerRoles.Cash or LedgerRoles.Bank)) cash.Add(a.Id);
        decimal Effect(Guid id) => balances.TryGetValue(id, out var b) ? b.Credit - b.Debit : 0;
        var table = new ReportTable
        {
            Title = "Cash flow", Subtitle = Period(o) + " · indirect method",
            Columns = [new("Section"), new("Code"), new("Line"), new("Amount", ColumnKind.Money)],
        };
        decimal profit = accounts.Values.Where(a => a.Type is AccountType.Income or AccountType.Expense).Sum(a => Effect(a.Id));
        CashFlowSection Of(LedgerAccount a) => a.CashFlow ?? (a.Type == AccountType.Equity ? CashFlowSection.Financing : CashFlowSection.Operating);
        decimal total = 0;
        foreach (var section in Enum.GetValues<CashFlowSection>())
        {
            string title = section switch { CashFlowSection.Operating => "Operating", CashFlowSection.Investing => "Investing", _ => "Financing" };
            decimal sum = 0;
            if (section == CashFlowSection.Operating)
            {
                table.Rows.Add([title, null, "Profit for the period", profit]);
                sum += profit;
            }
            foreach (var a in accounts.Values.Where(a => a.Type is AccountType.Asset or AccountType.Liability or AccountType.Equity && !cash.Contains(a.Id) && Of(a) == section)
                         .OrderBy(a => a.Code, StringComparer.Ordinal))
            {
                decimal effect = Effect(a.Id);
                if (effect == 0) continue;
                string verb = a.Type == AccountType.Asset ? (effect < 0 ? "More" : "Less") : (effect > 0 ? "More" : "Less");
                table.Rows.Add([title, a.Code, $"{verb} {a.Name.ToLowerInvariant()}", effect]);
                sum += effect;
            }
            table.Rows.Add([title, null, $"Net cash from {title.ToLowerInvariant()} activities", sum]);
            total += sum;
        }
        decimal opening = cash.Sum(id => balances.TryGetValue(id, out var b) ? b.Opening : 0);
        decimal closing = cash.Sum(id => balances.TryGetValue(id, out var b) ? b.Closing : 0);
        table.Rows.Add(["Cash", null, "Net change in cash and bank", total]);
        table.Rows.Add(["Cash", null, "Cash and bank at the start", opening]);
        table.Rows.Add(["Cash", null, "Cash and bank at the end", closing]);
        return table;
    }

    // Tax -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The sales tax return: output tax from the daily journal's tax lines (the accounts the stores map taxes 1 and 2 to,
    /// and the sales tax payable account) against input tax on supplier bills (less debit notes) and on expenses paid in
    /// cash or from a bank, where the store's rule set lets it be claimed.
    /// </summary>
    public async Task<ReportTable> SalesTaxAsync(FinanceReportOptions o)
    {
        await using var db = data.Create();
        var mappings = db.AccountMappings.AsNoTracking().Where(m => m.Kind == AccountMappingKind.Tax1 || m.Kind == AccountMappingKind.Tax2);
        if (o.Store is Guid st) mappings = mappings.Where(m => m.StoreId == st);
        var taxCodes = (await mappings.Select(m => m.AccountCode).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        var accounts = await db.LedgerAccounts.AsNoTracking().ToListAsync();
        foreach (var a in accounts.Where(a => a.Role == LedgerRoles.SalesTaxPayable)) taxCodes.Add(a.Code);
        var taxIds = accounts.Where(a => taxCodes.Contains(a.Code)).ToDictionary(a => a.Id);
        var incomeIds = accounts.Where(a => a.Type == AccountType.Income).Select(a => a.Id).ToHashSet();

        var daily = db.JournalLines.AsNoTracking()
            .Join(db.JournalEntries.AsNoTracking().Where(e => e.Status != JournalStatus.Draft && e.Kind == JournalKind.Daily && e.Date >= o.From && e.Date <= o.To),
                l => l.JournalEntryId, e => e.Id, (l, e) => new { l.AccountId, l.StoreId, l.Debit, l.Credit });
        if (o.Store is Guid s1) daily = daily.Where(l => l.StoreId == s1);
        var dailyLines = await daily.ToListAsync();
        decimal sales = dailyLines.Where(l => incomeIds.Contains(l.AccountId)).Sum(l => l.Credit - l.Debit);

        var bills = db.SupplierBills.AsNoTracking().Where(b => b.Status != SupplierBillStatus.Draft && b.Status != SupplierBillStatus.Void && b.BillDate >= o.From && b.BillDate <= o.To);
        if (o.Store is Guid s2) bills = bills.Where(b => b.StoreId == s2);
        var billRows = await bills.Select(b => new { b.Kind, b.StoreId, b.Subtotal, b.TaxTotal, b.ExchangeRate }).ToListAsync();
        var expenses = db.Expenses.AsNoTracking().Where(e => e.Status == ExpenseStatus.Posted && e.PaidBy != ExpensePaidBy.OnAccount && e.Date >= o.From && e.Date <= o.To);
        if (o.Store is Guid s3) expenses = expenses.Where(e => e.StoreId == s3);
        var expenseRows = await expenses.Select(e => new { e.StoreId, e.Amount, e.TaxAmount }).ToListAsync();
        var claimable = new Dictionary<Guid, bool>();
        foreach (var store in billRows.Select(b => b.StoreId).Concat(expenseRows.Select(e => e.StoreId)).Distinct())
            claimable[store] = (await new FinanceSetupService(data).RuleSetForStoreAsync(store))?.InputTaxClaimable ?? true;

        var table = new ReportTable
        {
            Title = "Sales tax return", Subtitle = Period(o),
            Columns = [new("Part"), new("Line"), new("Value before tax", ColumnKind.Money), new("Tax", ColumnKind.Money)],
        };
        decimal output = 0;
        foreach (var account in taxIds.Values.OrderBy(a => a.Code, StringComparer.Ordinal))
        {
            decimal tax = dailyLines.Where(l => l.AccountId == account.Id).Sum(l => l.Credit - l.Debit);
            if (tax == 0) continue;
            table.Rows.Add(["Output", $"{account.Code} {account.Name}", null, tax]);
            output += tax;
        }
        table.Rows.Add(["Output", "Sales from the daily journal", sales, output]);
        static decimal Base(decimal amount, decimal rate) => Money.Round(amount * rate);
        var purchases = billRows.Where(b => b.Kind == SupplierBillKind.Bill).ToList();
        var returns = billRows.Where(b => b.Kind == SupplierBillKind.DebitNote).ToList();
        decimal billValue = purchases.Sum(b => Base(b.Subtotal, b.ExchangeRate)), billTax = purchases.Where(b => claimable[b.StoreId]).Sum(b => Base(b.TaxTotal, b.ExchangeRate));
        decimal returnValue = returns.Sum(b => Base(b.Subtotal, b.ExchangeRate)), returnTax = returns.Where(b => claimable[b.StoreId]).Sum(b => Base(b.TaxTotal, b.ExchangeRate));
        decimal expenseValue = expenseRows.Sum(e => e.Amount), expenseTax = expenseRows.Where(e => claimable[e.StoreId]).Sum(e => e.TaxAmount);
        table.Rows.Add(["Input", $"Supplier bills ({purchases.Count})", billValue, billTax]);
        table.Rows.Add(["Input", $"Debit notes ({returns.Count})", -returnValue, -returnTax]);
        table.Rows.Add(["Input", $"Expenses ({expenseRows.Count})", expenseValue, expenseTax]);
        decimal input = billTax - returnTax + expenseTax;
        table.Rows.Add(["Input", "Input tax to claim", null, input]);
        table.Rows.Add(["Net", output - input >= 0 ? "Tax to pay" : "Tax to carry forward", null, output - input]);
        return table;
    }

    // Ageing and budgets ----------------------------------------------------------------------------------------------

    /// <summary>Customers' open invoices and layaways by age (days since the invoice) on the day the report is as of.</summary>
    public async Task<ReportTable> ReceivablesAgeingAsync(FinanceReportOptions o)
    {
        var asOf = o.To;
        await using var db = data.Create();
        var q = db.Invoices.AsNoTracking().Where(i => !i.IsVoided && i.Balance != 0 && i.InvoiceDate <= asOf && (i.Kind == InvoiceKind.Invoice || i.Kind == InvoiceKind.Layaway));
        if (o.Store is Guid s) q = q.Where(i => i.StoreId == s);
        var invoices = await q.Select(i => new { i.ClientId, Name = i.Company ?? ((i.FirstName ?? "") + " " + (i.LastName ?? "")), i.InvoiceDate, i.Balance }).ToListAsync();
        var table = Ageing("Receivables ageing", $"At {asOf:yyyy-MM-dd}{StoreText(o)} · by invoice date", "Client", ["0–30 days", "31–60 days", "61–90 days", "Over 90 days"]);
        foreach (var g in invoices.GroupBy(i => (i.ClientId, Name: i.Name.Trim())).OrderBy(g => g.Key.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var buckets = new decimal[4];
            foreach (var i in g)
            {
                int days = asOf.DayNumber - i.InvoiceDate.DayNumber;
                buckets[days <= 30 ? 0 : days <= 60 ? 1 : days <= 90 ? 2 : 3] += i.Balance;
            }
            table.Rows.Add([g.Key.Name.Length == 0 ? "(no name)" : g.Key.Name, buckets[0], buckets[1], buckets[2], buckets[3], buckets.Sum()]);
        }
        return table;
    }

    /// <summary>Suppliers' open bills by how long they are overdue on the day the report is as of.</summary>
    public async Task<ReportTable> PayablesAgeingAsync(FinanceReportOptions o)
    {
        var rows = await new PayablesService(data).AgeingAsync(o.To, o.Store);
        var table = Ageing("Payables ageing", $"At {o.To:yyyy-MM-dd}{StoreText(o)} · by due date", "Supplier", ["Not due", "1–30 days over", "31–60 days over", "61–90 days over", "Over 90 days"]);
        foreach (var r in rows) table.Rows.Add([r.Name, r.NotDue, r.Days1To30, r.Days31To60, r.Days61To90, r.Over90, r.Total]);
        return table;
    }

    public async Task<ReportTable> BudgetAsync(FinanceReportOptions o)
    {
        var rows = await new BudgetService(data).CompareAsync(o.From, o.To, o.Store);
        var table = new ReportTable
        {
            Title = "Budget against actual", Subtitle = Period(o),
            Columns = [new("Code"), new("Account"), new("Type"), new("Budget", ColumnKind.Money, true), new("Actual", ColumnKind.Money, true), new("Difference", ColumnKind.Money, true), new("Used", ColumnKind.Percent)],
        };
        foreach (var r in rows)
        {
            // Income is a credit: shown positive, like the profit and loss.
            int sign = r.Type is AccountType.Income or AccountType.Liability or AccountType.Equity ? -1 : 1;
            table.Rows.Add([r.Code, r.Name, r.Type.ToString(), sign * r.Budget, sign * r.Actual, sign * r.Difference, r.Budget == 0 ? null : Math.Round(r.Actual * 100m / r.Budget, 1)]);
        }
        return table;
    }

    // Helpers ---------------------------------------------------------------------------------------------------------

    /// <summary>Each account's movement over the days (debit positive), closing entries left out.</summary>
    private async Task<Dictionary<Guid, decimal>> PeriodAmountsAsync(DateOnly from, DateOnly to, Guid? store) =>
        (await Ledger.BalancesAsync(from, to, store, exceptClosing: true)).ToDictionary(b => b.AccountId, b => b.Debit - b.Credit);

    /// <summary>Each account's balance at the end of the day (debit positive).</summary>
    private async Task<Dictionary<Guid, decimal>> AsOfAsync(DateOnly asOf, Guid? store) =>
        (await Ledger.BalancesAsync(asOf, asOf, store)).ToDictionary(b => b.AccountId, b => b.Closing);

    private static (DateOnly From, DateOnly To) Earlier(FinanceReportOptions o)
    {
        if (o.Compare == FinanceComparison.PreviousYear) return (o.From.AddYears(-1), o.To.AddYears(-1));
        int days = o.To.DayNumber - o.From.DayNumber + 1;
        return (o.From.AddDays(-days), o.From.AddDays(-1));
    }

    private static ReportTable Statement(string title, string subtitle, string? compare)
    {
        var columns = new List<ReportColumn> { new("Code"), new("Account"), new("Amount", ColumnKind.Money) };
        if (compare is not null)
        {
            columns.Add(new(compare, ColumnKind.Money));
            columns.Add(new("Change", ColumnKind.Money));
        }
        return new ReportTable { Title = title, Subtitle = subtitle, Columns = columns };
    }

    private static void AddRow(ReportTable table, string code, string name, decimal amount, decimal? before)
    {
        if (table.Columns.Count > 3) table.Rows.Add([code, name, amount, before ?? 0m, amount - (before ?? 0m)]);
        else table.Rows.Add([code, name, amount]);
    }

    /// <summary>
    /// One part of a statement: the accounts of a type under their top header, each header with its total, then the part's
    /// total. <paramref name="sign"/> turns credit balances (income, liabilities, equity) positive.
    /// </summary>
    private static void Section(ReportTable table, List<LedgerAccount> accounts, AccountType type, Dictionary<Guid, decimal> now, Dictionary<Guid, decimal>? before,
        int sign, string? totalTitle)
    {
        var byId = accounts.ToDictionary(a => a.Id);
        LedgerAccount Top(LedgerAccount a)
        {
            var current = a;
            for (int guard = 0; current.ParentId is Guid p && byId.TryGetValue(p, out var parent) && guard < 20; guard++) current = parent;
            return current;
        }
        var posting = accounts.Where(a => a.Type == type && a.IsPosting && (now.GetValueOrDefault(a.Id) != 0 || (before?.GetValueOrDefault(a.Id) ?? 0) != 0)).ToList();
        decimal total = 0, totalBefore = 0;
        var groups = posting.GroupBy(Top).OrderBy(g => g.Key.Code, StringComparer.Ordinal).ToList();
        foreach (var group in groups)
        {
            bool header = !group.Key.IsPosting;
            if (header) table.Rows.Add(table.Columns.Count > 3 ? [group.Key.Code, group.Key.Name, null, null, null] : [group.Key.Code, group.Key.Name, null]);
            decimal sum = 0, sumBefore = 0;
            foreach (var a in group.OrderBy(a => a.Code, StringComparer.Ordinal))
            {
                decimal value = sign * now.GetValueOrDefault(a.Id), old = sign * (before?.GetValueOrDefault(a.Id) ?? 0);
                AddRow(table, a.Code, (header ? "  " : "") + a.Name, value, before is null ? null : old);
                sum += value;
                sumBefore += old;
            }
            // A header's own total only when the part has several (one header's total is the part's).
            if (header && groups.Count > 1) AddRow(table, "", "Total " + group.Key.Name.ToLowerInvariant(), sum, before is null ? null : sumBefore);
            total += sum;
            totalBefore += sumBefore;
        }
        if (totalTitle is not null) AddRow(table, "", totalTitle, total, before is null ? null : totalBefore);
    }

    private static ReportTable Ageing(string title, string subtitle, string party, string[] buckets)
    {
        var columns = new List<ReportColumn> { new(party) };
        columns.AddRange(buckets.Select(b => new ReportColumn(b, ColumnKind.Money, true)));
        columns.Add(new("Total", ColumnKind.Money, true));
        return new ReportTable { Title = title, Subtitle = subtitle, Columns = columns };
    }

    private static string Period(FinanceReportOptions o) =>
        (o.From == o.To ? o.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : $"{o.From:yyyy-MM-dd} to {o.To:yyyy-MM-dd}") + StoreText(o);

    private static string StoreText(FinanceReportOptions o) => o.Store is null ? " · company" : "";
}
