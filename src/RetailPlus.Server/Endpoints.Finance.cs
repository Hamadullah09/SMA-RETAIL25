using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Reports;

namespace RetailPlus.Server;

public sealed record FinanceHomeInfo(DateOnly Today, IReadOnlyList<BankAccountRow> Accounts, decimal CashInHand, decimal PayablesOpen, decimal PayablesDue, int BillsDue,
    decimal PayablesOverdue, decimal Receivables, DateOnly MonthFrom, decimal MonthIncome, decimal MonthExpenses, decimal MonthProfit, decimal Suspense, int DraftJournals,
    bool FiscalEnabled, int FiscalPending, int FiscalFailed);
public sealed record FinanceAccountRow(Guid Id, string Version, string Code, string Name, AccountType Type, Guid? ParentId, bool IsPosting, bool IsActive, string? Role,
    string? Description, CashFlowSection? CashFlow, decimal Balance);
public sealed record FinanceAccountRequest(string Code, string Name, AccountType Type, Guid? ParentId, bool IsPosting, bool IsActive, string? Role, string? Description,
    CashFlowSection? CashFlow, string? Version);
public sealed record FinanceSettingsInfo(string Version, string BaseCurrency, int FiscalYearStartMonth, string Country, bool ManualEntriesNeedApproval, bool AutoPostOnSync,
    int NightlyDays, int NightlyHour, int AttachmentMaxKilobytes, bool FiscalEnabled = false);
public sealed record SalesTaxRateInfo(string Name, decimal Rate, bool IsDefault);
public sealed record WithholdingRuleInfo(WithholdingKind Kind, string? Section, decimal FilerRate, decimal NonFilerRate, decimal Threshold);
public sealed record TaxRuleSetInfo(Guid? Id, string? Version, string Country, string Name, bool InputTaxClaimable, string? Notes, bool IsActive,
    IReadOnlyList<SalesTaxRateInfo> SalesTaxRates, IReadOnlyList<WithholdingRuleInfo> WithholdingRules);
public sealed record StoreFinanceInfo(Guid StoreId, Guid? TaxRuleSetId, string? RuleSet, Guid? DepositBankAccountId);
public sealed record MappingRow(AccountMappingKind Kind, string Key, string? AccountCode, string? AccountName);
public sealed record PostDaysRequest(DateOnly From, DateOnly To, Guid? StoreId);
public sealed record PostDaysResult(int Entries, IReadOnlyList<string> Problems);
public sealed record SuspenseRow(Guid EntryId, long Number, DateOnly Date, Guid? StoreId, string StoreCode, string Memo, decimal Debit, decimal Credit);
public sealed record JournalListRow(Guid Id, long Number, DateOnly Date, JournalKind Kind, JournalStatus Status, string? StoreCode, string Memo, string? Reference, decimal Total,
    string? CreatedBy, string? PostedBy, DateTime? PostedAtUtc);
public sealed record JournalLineInfo(int LineNumber, Guid AccountId, string Code, string Name, Guid? StoreId, decimal Debit, decimal Credit, string? Memo,
    LedgerPartyKind? PartyKind, Guid? PartyId);
public sealed record AttachmentInfo(Guid Id, string FileName, string ContentType, int Size, string? UploadedBy, DateTime CreatedAtUtc);
public sealed record JournalDetail(Guid Id, string Version, long Number, DateOnly Date, JournalKind Kind, JournalStatus Status, Guid? StoreId, string? StoreCode, string Memo,
    string? Reference, string? Source, string? CreatedBy, string? PostedBy, DateTime? PostedAtUtc, Guid? ReversalOfId, Guid? ReversedById, IReadOnlyList<JournalLineInfo> Lines,
    IReadOnlyList<AttachmentInfo> Attachments, bool NeedsApproval);
public sealed record JournalLineRequest(Guid AccountId, decimal Debit, decimal Credit, Guid? StoreId = null, string? Memo = null, LedgerPartyKind? PartyKind = null, Guid? PartyId = null);
public sealed record JournalRequest(DateOnly Date, Guid? StoreId, string Memo, string? Reference, IReadOnlyList<JournalLineRequest> Lines, bool Post = false, string? Version = null);
public sealed record ReverseRequest(DateOnly Date, string Reason);
public sealed record PeriodInfo(Guid Id, int Number, string Name, DateOnly StartDate, DateOnly EndDate, FiscalStatus Status);
public sealed record YearInfo(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, FiscalStatus Status, DateTime? ClosedAtUtc, string? ClosedBy, IReadOnlyList<PeriodInfo> Periods);
public sealed record NewYearRequest(DateOnly Date);
public sealed record ReasonRequest(string Reason);
public sealed record OpeningRequest(DateOnly Date, Guid? StoreId, bool BalanceToRetainedEarnings, IReadOnlyList<JournalLineRequest> Lines);
public sealed record BudgetAmountInfo(Guid AccountId, DateOnly Month, decimal Amount);
public sealed record BudgetInfo(Guid YearId, string YearName, Guid? StoreId, IReadOnlyList<DateOnly> Months, IReadOnlyList<BudgetAmountInfo> Amounts);
public sealed record BudgetSaveRequest(IReadOnlyList<BudgetAmountInfo> Amounts);
public sealed record BudgetCopyRequest(Guid YearId, Guid? StoreId, bool FromBudget, decimal Percent);
public sealed record FinanceReportCatalogRow(string Key, string Title, bool UsesDates, bool UsesComparison, bool UsesAccount, bool AsOfDate);
public sealed record FiscalRow(Guid Id, Guid SaleId, string StoreCode, int StationNumber, long InvoiceNumber, DateTime SoldAt, decimal Total, FiscalInvoiceStatus Status,
    int Attempts, string? LastError, string? FiscalNumber, DateTime? SentAtUtc);
public sealed record FiscalQueueInfo(bool Enabled, IReadOnlyList<FiscalRow> Rows);

/// <summary>
/// Finance and accounting (phase E1), under <c>/api/finance</c>: the finance home, chart of accounts, ledger settings, tax
/// rule sets, the daily journal's mapping, posting days and what went to suspense, journal entries made by hand, years and
/// periods, opening balances, budgets, the statements and reports, the exports and the FBR queue. Payables and bank are in
/// their own files.
/// </summary>
public static partial class Endpoints
{
    private static void MapFinance(RouteGroupBuilder api)
    {
        api.MapGet("/home", FinanceHomeAsync).RequireAuthorization(Need(Permissions.FinanceView));

        // Chart of accounts and settings.
        api.MapGet("/accounts", AccountsAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/accounts", (FinanceAccountRequest body, ApiRequest a) => SaveAccountAsync(null, body, a)).RequireAuthorization(Need(Permissions.FinanceSetup));
        api.MapPut("/accounts/{id:guid}", (Guid id, FinanceAccountRequest body, ApiRequest a) => SaveAccountAsync(id, body, a)).RequireAuthorization(Need(Permissions.FinanceSetup));
        api.MapDelete("/accounts/{id:guid}", async (Guid id, ApiRequest a) =>
        {
            await a.RequireCompanyChangeAsync();
            await new LedgerService(await a.ForChangeAsync()).DeleteAccountAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.FinanceSetup));
        api.MapGet("/roles", () => Results.Ok(LedgerRoles.All)).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapGet("/settings", FinanceSettingsAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPut("/settings", SaveFinanceSettingsAsync).RequireAuthorization(Need(Permissions.FinanceSetup));
        api.MapGet("/tax-rules", TaxRulesAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/tax-rules", (TaxRuleSetInfo body, ApiRequest a) => SaveTaxRulesAsync(null, body, a)).RequireAuthorization(Need(Permissions.FinanceSetup));
        api.MapPut("/tax-rules/{id:guid}", (Guid id, TaxRuleSetInfo body, ApiRequest a) => SaveTaxRulesAsync(id, body, a)).RequireAuthorization(Need(Permissions.FinanceSetup));
        api.MapDelete("/tax-rules/{id:guid}", async (Guid id, ApiRequest a) =>
        {
            await a.RequireCompanyChangeAsync();
            await new FinanceSetupService(await a.ForChangeAsync()).DeleteRuleSetAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.FinanceSetup));
        api.MapGet("/stores/{id:guid}", StoreFinanceAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPut("/stores/{id:guid}", SaveStoreFinanceAsync).RequireAuthorization(Need(Permissions.FinanceSetup));
        api.MapGet("/mapping", MappingAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPut("/mapping", SaveMappingAsync).RequireAuthorization(Need(Permissions.FinanceSetup));

        // Posting the days, and what went to suspense.
        api.MapPost("/post-days", PostDaysAsync).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapGet("/suspense", SuspenseAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapGet("/daily-journal", DailyJournalCsvAsync).RequireAuthorization(Need(Permissions.AccountingExport));

        // Journal entries.
        api.MapGet("/journals", JournalsAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapGet("/journals/{id:guid}", JournalAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/journals", (JournalRequest body, ApiRequest a) => SaveJournalAsync(null, body, a)).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapPut("/journals/{id:guid}", (Guid id, JournalRequest body, ApiRequest a) => SaveJournalAsync(id, body, a)).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapDelete("/journals/{id:guid}", async (Guid id, ApiRequest a) =>
        {
            var entry = await EntryInScopeAsync(id, a);
            await new LedgerService(await a.ForChangeAsync(entry.StoreId)).DeleteDraftAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapPost("/journals/{id:guid}/approve", async (Guid id, ApiRequest a) =>
        {
            var entry = await EntryInScopeAsync(id, a);
            await new LedgerService(await a.ForChangeAsync(entry.StoreId)).ApproveAsync(id);
            return Results.Ok(await JournalDetailAsync(id, a));
        }).RequireAuthorization(Need(Permissions.FinanceApprove));
        api.MapPost("/journals/{id:guid}/reverse", async (Guid id, ReverseRequest body, ApiRequest a) =>
        {
            var entry = await EntryInScopeAsync(id, a);
            var reversal = await new LedgerService(await a.ForChangeAsync(entry.StoreId)).ReverseAsync(id, body.Date, body.Reason ?? "");
            return Results.Ok(await JournalDetailAsync(reversal.Id, a));
        }).RequireAuthorization(Need(Permissions.FinancePost));
        api.MapPost("/journals/{id:guid}/attachments", async (Guid id, HttpRequest request, ApiRequest a) =>
        {
            var entry = await EntryInScopeAsync(id, a);
            return await AttachAsync(AttachmentOwner.JournalEntry, id, entry.StoreId, request, a);
        }).RequireAuthorization(Need(Permissions.FinancePost)).DisableAntiforgery();
        api.MapGet("/attachments/{id:guid}", AttachmentFileAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapDelete("/attachments/{id:guid}", DeleteAttachmentAsync).RequireAuthorization(Need(Permissions.FinancePost));

        // Years, periods, opening balances.
        api.MapGet("/years", YearsAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/years", async (NewYearRequest body, ApiRequest a) =>
        {
            await a.RequireCompanyChangeAsync();
            await new LedgerService(await a.ForChangeAsync()).EnsureYearAsync(body.Date);
            return await YearsAsync(a);
        }).RequireAuthorization(Need(Permissions.FinanceClose));
        api.MapPost("/periods/{id:guid}/lock", (Guid id, ApiRequest a) => PeriodStatusAsync(id, FiscalStatus.Locked, a)).RequireAuthorization(Need(Permissions.FinanceClose));
        api.MapPost("/periods/{id:guid}/unlock", (Guid id, ApiRequest a) => PeriodStatusAsync(id, FiscalStatus.Open, a)).RequireAuthorization(Need(Permissions.FinanceClose));
        api.MapPost("/years/{id:guid}/close", async (Guid id, ApiRequest a) =>
        {
            await a.RequireCompanyChangeAsync();
            await new FinanceYearService(await a.ForChangeAsync()).CloseAsync(id);
            return await YearsAsync(a);
        }).RequireAuthorization(Need(Permissions.FinanceClose));
        api.MapPost("/years/{id:guid}/reopen", async (Guid id, ReasonRequest body, ApiRequest a) =>
        {
            await a.RequireCompanyChangeAsync();
            await new FinanceYearService(await a.ForChangeAsync()).ReopenAsync(id, body.Reason ?? "");
            return await YearsAsync(a);
        }).RequireAuthorization(Need(Permissions.FinanceClose));
        api.MapPost("/opening-balances", OpeningBalancesAsync).RequireAuthorization(Need(Permissions.FinancePost));

        // Budgets.
        api.MapGet("/budgets", BudgetAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPut("/budgets", SaveBudgetAsync).RequireAuthorization(Need(Permissions.FinanceBudget));
        api.MapPost("/budgets/copy", CopyBudgetAsync).RequireAuthorization(Need(Permissions.FinanceBudget));

        // Statements, reports and exports.
        api.MapGet("/reports", () => Results.Ok(FinanceReports.Catalog.Select(r => new FinanceReportCatalogRow(r.Key, r.Title, r.UsesDates, r.UsesComparison, r.UsesAccount, r.AsOfDate))))
            .RequireAuthorization(Need(Permissions.FinanceView));
        api.MapGet("/reports/{key}", async (string key, ApiRequest a) =>
        {
            var (report, from, to) = await FinanceReportAsync(key, a);
            return Results.Ok(new ReportResult(report.Title, report.Subtitle, [.. report.Columns.Select(c => new ReportColumnInfo(c.Header, c.Kind, c.Total))],
                report.Rows, report.HasTotals ? report.TotalsRow() : new object?[report.Columns.Count], from, to));
        }).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapGet("/reports/{key}/csv", async (string key, ApiRequest a) =>
        {
            var (report, from, to) = await FinanceReportAsync(key, a);
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(report.ToCsv())).ToArray(), "text/csv; charset=utf-8", $"{key}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapGet("/export", ExportAsync).RequireAuthorization(Need(Permissions.AccountingExport));

        // FBR.
        api.MapGet("/fiscal", FiscalAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/fiscal/{id:guid}/retry", async (Guid id, ApiRequest a) =>
        {
            await using (var db = a.Root.Create())
            {
                var row = await db.FiscalInvoices.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id) ?? throw ApiException.NotFound("That queue row");
                a.RequireStore(row.StoreId, "That queue row");
            }
            await new FiscalService(a.Root).RetryAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.FinanceSetup));

        MapPayables(api);
        MapBank(api);
    }

    // Scope helpers ---------------------------------------------------------------------------------------------------

    /// <summary>A record of one store is checked like any store record; a company record (no store) is seen by users of every store only.</summary>
    private static void RequireScope(ApiRequest api, Guid? store, string what)
    {
        if (store is Guid s) api.RequireStore(s, what);
        else if (!api.User.AllStores) throw ApiException.NotFound(what);
    }

    /// <summary>A store asked for in a body: one the user sees, or (null) the company, which needs every store.</summary>
    private static void RequireBodyStore(ApiRequest api, Guid? store)
    {
        if (store is Guid s && !api.User.CanSee(s)) throw ApiException.Forbidden("You may not work for that store.");
        if (store is null && !api.User.AllStores) throw ApiException.Forbidden("Entries for the whole company are made by a user who sees every store.");
    }

    private static async Task<Guid> RequiredStoreAsync(ApiRequest api, Guid? store)
    {
        if (store is Guid s)
        {
            if (!api.User.CanSee(s)) throw ApiException.Forbidden("You may not work for that store.");
            return s;
        }
        return api.User.StoreId ?? await api.FirstStoreAsync() ?? throw ApiException.Bad("Choose the store.");
    }

    private static Task<Dictionary<Guid, string>> CodesAsync(RetailPlusDbContext db) => StoreCodesAsync(db);

    // Home ------------------------------------------------------------------------------------------------------------

    private static async Task<IResult> FinanceHomeAsync(ApiRequest api, IFiscalInvoiceSender fiscal)
    {
        var store = api.ListStore();
        var today = await api.TodayAsync(store);
        var monthFrom = new DateOnly(today.Year, today.Month, 1);
        var data = api.For(store);
        var ledger = new LedgerService(data);
        var month = await ledger.BalancesAsync(monthFrom, today, store, exceptClosing: true);
        var all = await ledger.BalancesAsync(today, today, store);
        var accounts = await ledger.GetAccountsAsync();
        decimal Role(string role) => accounts.FirstOrDefault(a => a.Role == role) is { } a ? all.Where(b => b.AccountId == a.Id).Sum(b => b.Closing) : 0;
        decimal income = -month.Where(b => b.Type == AccountType.Income).Sum(b => b.Debit - b.Credit);
        decimal expenses = month.Where(b => b.Type == AccountType.Expense).Sum(b => b.Debit - b.Credit);
        var banks = await BankRowsAsync(api, store);
        var due = await new PayablesService(data).BillsDueAsync(today.AddDays(7), store);
        await using var db = api.Root.Create();
        var drafts = db.JournalEntries.AsNoTracking().Where(e => e.Status == JournalStatus.Draft);
        if (store is Guid s) drafts = drafts.Where(e => e.StoreId == s);
        var queue = db.FiscalInvoices.AsNoTracking();
        if (store is Guid s2) queue = queue.Where(f => f.StoreId == s2);
        return Results.Ok(new FinanceHomeInfo(today, banks, Role(LedgerRoles.Cash), -Role(LedgerRoles.AccountsPayable),
            due.Sum(b => FinanceBase(b.Open, b.ExchangeRate)), due.Count, due.Where(b => b.DueDate < today).Sum(b => FinanceBase(b.Open, b.ExchangeRate)),
            Role(LedgerRoles.AccountsReceivable), monthFrom, income, expenses, income - expenses, Role(LedgerRoles.Suspense), await drafts.CountAsync(), fiscal.Enabled,
            await queue.CountAsync(f => f.Status == FiscalInvoiceStatus.Pending), await queue.CountAsync(f => f.Status == FiscalInvoiceStatus.Failed)));
    }

    private static decimal FinanceBase(decimal amount, decimal rate) => Money.Round(amount * rate);

    // Accounts --------------------------------------------------------------------------------------------------------

    private static async Task<IResult> AccountsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var ledger = new LedgerService(api.For(store));
        var today = await api.TodayAsync(store);
        var balances = (await ledger.BalancesAsync(today, today, store)).ToDictionary(b => b.AccountId, b => b.Closing);
        return Results.Ok((await ledger.GetAccountsAsync()).Select(a => ToRow(a, balances.GetValueOrDefault(a.Id))));
    }

    private static FinanceAccountRow ToRow(LedgerAccount a, decimal balance) =>
        new(a.Id, ApiRequest.Version(a.UpdatedAtUtc), a.Code, a.Name, a.Type, a.ParentId, a.IsPosting, a.IsActive, a.Role, a.Description, a.CashFlow, balance);

    private static async Task<IResult> SaveAccountAsync(Guid? id, FinanceAccountRequest body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        if (id is Guid existing)
        {
            await using var db = api.Root.Create();
            var stored = await db.LedgerAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == existing) ?? throw ApiException.NotFound("That account");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
        }
        var saved = await new LedgerService(await api.ForChangeAsync()).SaveAccountAsync(new LedgerAccount
        {
            Id = id ?? Guid.Empty, Code = body.Code ?? "", Name = body.Name ?? "", Type = body.Type, ParentId = body.ParentId, IsPosting = body.IsPosting, IsActive = body.IsActive,
            Role = string.IsNullOrWhiteSpace(body.Role) ? null : body.Role.Trim(), Description = body.Description, CashFlow = body.CashFlow,
        });
        var row = ToRow(saved, 0);
        return id is null ? Results.Created($"/api/finance/accounts/{saved.Id}", row) : Results.Ok(row);
    }

    // Settings --------------------------------------------------------------------------------------------------------

    private static async Task<IResult> FinanceSettingsAsync(ApiRequest api, IFiscalInvoiceSender fiscal) => Results.Ok(await SettingsInfoAsync(api, fiscal));

    private static async Task<FinanceSettingsInfo> SettingsInfoAsync(ApiRequest api, IFiscalInvoiceSender fiscal)
    {
        await using var db = api.Root.Create();
        var ledger = await db.LedgerSettings.AsNoTracking().FirstOrDefaultAsync() ?? new LedgerSettings();
        var finance = await db.FinanceSettings.AsNoTracking().FirstOrDefaultAsync() ?? new FinanceSettings();
        return new FinanceSettingsInfo(SettingsVersion(ledger, finance), ledger.BaseCurrency, ledger.FiscalYearStartMonth, ledger.Country, ledger.ManualEntriesNeedApproval,
            finance.AutoPostOnSync, finance.NightlyDays, finance.NightlyHour, finance.AttachmentMaxKilobytes, fiscal.Enabled);
    }

    private static string SettingsVersion(LedgerSettings ledger, FinanceSettings finance) =>
        ApiRequest.Version(ledger.UpdatedAtUtc > finance.UpdatedAtUtc ? ledger.UpdatedAtUtc : finance.UpdatedAtUtc);

    private static async Task<IResult> SaveFinanceSettingsAsync(FinanceSettingsInfo body, ApiRequest api, IFiscalInvoiceSender fiscal)
    {
        await api.RequireCompanyChangeAsync();
        ApiRequest.CheckVersion(body.Version, (await SettingsInfoAsync(api, fiscal)).Version);
        var data = await api.ForChangeAsync();
        await new LedgerService(data).SaveSettingsAsync(new LedgerSettings
        {
            BaseCurrency = body.BaseCurrency ?? "", FiscalYearStartMonth = body.FiscalYearStartMonth, Country = body.Country ?? "", ManualEntriesNeedApproval = body.ManualEntriesNeedApproval,
        });
        await new FinanceSetupService(data).SaveSettingsAsync(new FinanceSettings
        {
            AutoPostOnSync = body.AutoPostOnSync, NightlyDays = body.NightlyDays, NightlyHour = body.NightlyHour, AttachmentMaxKilobytes = body.AttachmentMaxKilobytes,
        });
        return Results.Ok(await SettingsInfoAsync(api, fiscal));
    }

    private static TaxRuleSetInfo ToInfo(TaxRuleSet s) => new(s.Id, ApiRequest.Version(s.UpdatedAtUtc), s.Country, s.Name, s.InputTaxClaimable, s.Notes, s.IsActive,
        [.. s.SalesTaxRates.Select(r => new SalesTaxRateInfo(r.Name, r.Rate, r.IsDefault))],
        [.. s.WithholdingRules.Select(r => new WithholdingRuleInfo(r.Kind, r.Section, r.FilerRate, r.NonFilerRate, r.Threshold))]);

    private static async Task<IResult> TaxRulesAsync(ApiRequest api) =>
        Results.Ok((await new FinanceSetupService(api.Root).GetRuleSetsAsync()).Select(ToInfo));

    private static async Task<IResult> SaveTaxRulesAsync(Guid? id, TaxRuleSetInfo body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        if (id is Guid existing)
        {
            await using var db = api.Root.Create();
            var stored = await db.TaxRuleSets.AsNoTracking().SingleOrDefaultAsync(r => r.Id == existing) ?? throw ApiException.NotFound("That rule set");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
        }
        var saved = await new FinanceSetupService(await api.ForChangeAsync()).SaveRuleSetAsync(new TaxRuleSet
        {
            Id = id ?? Guid.Empty, Country = body.Country ?? "", Name = body.Name ?? "", InputTaxClaimable = body.InputTaxClaimable, Notes = body.Notes, IsActive = body.IsActive,
            SalesTaxRates = [.. (body.SalesTaxRates ?? []).Select(r => new SalesTaxRate { Name = r.Name ?? "", Rate = r.Rate, IsDefault = r.IsDefault })],
            WithholdingRules = [.. (body.WithholdingRules ?? []).Select(r => new WithholdingRule { Kind = r.Kind, Section = r.Section, FilerRate = r.FilerRate, NonFilerRate = r.NonFilerRate, Threshold = r.Threshold })],
        });
        var info = ToInfo((await new FinanceSetupService(api.Root).GetRuleSetsAsync()).Single(s => s.Id == saved.Id));
        return id is null ? Results.Created($"/api/finance/tax-rules/{saved.Id}", info) : Results.Ok(info);
    }

    private static async Task<IResult> StoreFinanceAsync(Guid id, ApiRequest api)
    {
        api.RequireStore(id, "That store");
        var setup = new FinanceSetupService(api.Root);
        var row = await setup.GetStoreAsync(id);
        return Results.Ok(new StoreFinanceInfo(id, row.TaxRuleSetId, (await setup.RuleSetForStoreAsync(id))?.Name, row.DepositBankAccountId));
    }

    private static async Task<IResult> SaveStoreFinanceAsync(Guid id, StoreFinanceInfo body, ApiRequest api)
    {
        api.RequireStore(id, "That store");
        var setup = new FinanceSetupService(await api.ForChangeAsync(id));
        await setup.SaveStoreAsync(new StoreFinance { StoreId = id, TaxRuleSetId = body.TaxRuleSetId, DepositBankAccountId = body.DepositBankAccountId });
        return await StoreFinanceAsync(id, api);
    }

    private static async Task<IResult> MappingAsync(ApiRequest api)
    {
        var store = await RequiredStoreAsync(api, api.AskedStore);
        // A store added since the server started gets the standard accounts first.
        await new LedgerService(api.Root).SeedAsync();
        var rows = await new AccountingService(api.For(store)).MappingTableAsync();
        return Results.Ok(rows.Select(r => new MappingRow(r.Kind, r.Key, r.AccountCode.Length == 0 ? null : r.AccountCode, r.AccountName)));
    }

    private static async Task<IResult> SaveMappingAsync(List<MappingRow> body, ApiRequest api)
    {
        var store = await RequiredStoreAsync(api, api.AskedStore);
        await new FinanceSetupService(await api.ForChangeAsync(store)).SaveMappingAsync(store,
            body.Select(r => new AccountMapping { Kind = r.Kind, Key = r.Key ?? "", AccountCode = r.AccountCode ?? "", AccountName = r.AccountName }));
        return await MappingAsync(api);
    }

    // Posting days and suspense ---------------------------------------------------------------------------------------

    private static async Task<IResult> PostDaysAsync(PostDaysRequest body, ApiRequest api)
    {
        if (body.StoreId is Guid s && !api.User.CanSee(s)) throw ApiException.Forbidden("You may not see that store.");
        Guid? store = body.StoreId ?? (api.User.AllStores ? null : api.User.StoreId ?? Guid.Empty);
        var result = await new LedgerAutomation(await api.ForChangeAsync(store)).PostDaysAsync(body.From, body.To, store);
        return Results.Ok(new PostDaysResult(result.Entries, result.Problems));
    }

    /// <summary>The lines that went to suspense (amounts the daily journal could not map), newest first.</summary>
    private static async Task<IResult> SuspenseAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var suspense = await new LedgerService(api.Root).AccountForRoleAsync(LedgerRoles.Suspense);
        if (suspense is null) return Results.Ok(Array.Empty<SuspenseRow>());
        await using var db = api.Root.Create();
        var q = db.JournalLines.AsNoTracking().Where(l => l.AccountId == suspense.Id)
            .Join(db.JournalEntries.AsNoTracking().Where(e => e.Status != JournalStatus.Draft), l => l.JournalEntryId, e => e.Id,
                (l, e) => new { e.Id, e.Number, e.Date, l.StoreId, Memo = l.Memo ?? e.Memo, l.Debit, l.Credit });
        if (store is Guid s) q = q.Where(l => l.StoreId == s);
        if (api.Text("from") is not null || api.Text("to") is not null)
        {
            var (from, to) = await api.PeriodAsync(store);
            q = q.Where(l => l.Date >= from && l.Date <= to);
        }
        var codes = await CodesAsync(db);
        var rows = await q.OrderByDescending(l => l.Date).ThenByDescending(l => l.Number).Take(1000).ToListAsync();
        return Results.Ok(rows.Select(l => new SuspenseRow(l.Id, l.Number, l.Date, l.StoreId, l.StoreId is Guid id ? codes.GetValueOrDefault(id) ?? "" : "", l.Memo, l.Debit, l.Credit)));
    }

    private static async Task<IResult> DailyJournalCsvAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        var export = await new AccountingService(api.For(store)).ExportAsync(from, to);
        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(export.ToCsv())).ToArray(), "text/csv; charset=utf-8", $"journal-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }

    // Journal entries -------------------------------------------------------------------------------------------------

    private static async Task<IResult> JournalsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var q = db.JournalEntries.AsNoTracking().AsQueryable();
        if (store is Guid s) q = q.Where(e => e.StoreId == s || e.Lines.Any(l => l.StoreId == s));
        if (api.Text("from") is not null || api.Text("to") is not null)
        {
            var (from, to) = await api.PeriodAsync(store);
            q = q.Where(e => e.Date >= from && e.Date <= to);
        }
        if (api.Text("status") is { } status)
            q = Enum.TryParse<JournalStatus>(status, true, out var st) ? q.Where(e => e.Status == st) : throw ApiException.Bad("status is Draft, Posted or Reversed.");
        if (api.Text("kind") is { } kind)
            q = Enum.TryParse<JournalKind>(kind, true, out var k) ? q.Where(e => e.Kind == k) : throw ApiException.Bad($"{kind} is not a kind of entry.");
        if (api.Text("search") is { } search)
        {
            long? number = long.TryParse(search, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
            q = q.Where(e => e.Number == number || e.Memo.Contains(search) || (e.Reference != null && e.Reference.Contains(search)));
        }
        var codes = await CodesAsync(db);
        var rows = q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Number).ThenByDescending(e => e.CreatedAtUtc)
            .Select(e => new { e.Id, e.Number, e.Date, e.Kind, e.Status, e.StoreId, e.Memo, e.Reference, Total = e.Lines.Sum(l => l.Debit), e.CreatedBy, e.PostedBy, e.PostedAtUtc });
        return Results.Ok(await api.PageAsync(rows, e => new JournalListRow(e.Id, e.Number, e.Date, e.Kind, e.Status, e.StoreId is Guid id ? codes.GetValueOrDefault(id) : null,
            e.Memo, e.Reference, e.Total, e.CreatedBy, e.PostedBy, ApiRequest.Utc(e.PostedAtUtc))));
    }

    private static async Task<JournalEntry> EntryInScopeAsync(Guid id, ApiRequest api)
    {
        await using var db = api.Root.Create();
        var entry = await db.JournalEntries.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id) ?? throw ApiException.NotFound("That entry");
        RequireScope(api, entry.StoreId, "That entry");
        return entry;
    }

    private static async Task<IResult> JournalAsync(Guid id, ApiRequest api)
    {
        await EntryInScopeAsync(id, api);
        return Results.Ok(await JournalDetailAsync(id, api));
    }

    private static async Task<JournalDetail> JournalDetailAsync(Guid id, ApiRequest api)
    {
        var entry = await new LedgerService(api.Root).GetEntryAsync(id) ?? throw ApiException.NotFound("That entry");
        var attachments = await new ExpenseService(api.Root).AttachmentsAsync(AttachmentOwner.JournalEntry, id);
        await using var db = api.Root.Create();
        var codes = await CodesAsync(db);
        var settings = await db.LedgerSettings.AsNoTracking().FirstOrDefaultAsync() ?? new LedgerSettings();
        return new JournalDetail(entry.Id, ApiRequest.Version(entry.UpdatedAtUtc), entry.Number, entry.Date, entry.Kind, entry.Status, entry.StoreId,
            entry.StoreId is Guid s ? codes.GetValueOrDefault(s) : null, entry.Memo, entry.Reference, entry.Source, entry.CreatedBy, entry.PostedBy, ApiRequest.Utc(entry.PostedAtUtc),
            entry.ReversalOfId, entry.ReversedById,
            [.. entry.Lines.OrderBy(l => l.LineNumber).Select(l => new JournalLineInfo(l.LineNumber, l.AccountId, l.Account?.Code ?? "", l.Account?.Name ?? "", l.StoreId, l.Debit, l.Credit,
                l.Memo, l.PartyKind, l.PartyId))],
            [.. attachments.Select(ToInfo)], entry.Status == JournalStatus.Draft && settings.ManualEntriesNeedApproval);
    }

    private static AttachmentInfo ToInfo(FinanceAttachment a) => new(a.Id, a.FileName, a.ContentType, a.Size, a.UploadedBy, ApiRequest.Utc(a.CreatedAtUtc));

    private static async Task<IResult> SaveJournalAsync(Guid? id, JournalRequest body, ApiRequest api)
    {
        RequireBodyStore(api, body.StoreId);
        foreach (var line in body.Lines ?? []) if (line.StoreId is Guid s && !api.User.CanSee(s)) throw ApiException.Forbidden("A line is for a store you may not see.");
        if (id is Guid existing)
        {
            var stored = await EntryInScopeAsync(existing, api);
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
        }
        var data = await api.ForChangeAsync(body.StoreId);
        var ledger = new LedgerService(data);
        await using var db = api.Root.Create();
        var codes = await db.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);
        var lines = (body.Lines ?? []).Select(l => JournalDraftLine.Code(codes.TryGetValue(l.AccountId, out var code) ? code : throw ApiException.Bad("An account chosen no longer exists."),
            l.Debit, l.Credit, l.StoreId, l.Memo, l.PartyKind, l.PartyId)).ToList();
        var draft = await ledger.SaveDraftAsync(new JournalDraft(body.Date, JournalKind.Manual, body.Memo ?? "", lines) { StoreId = body.StoreId, Reference = body.Reference }, id);
        var settings = await db.LedgerSettings.AsNoTracking().FirstOrDefaultAsync() ?? new LedgerSettings();
        if (body.Post && !settings.ManualEntriesNeedApproval) await ledger.ApproveAsync(draft.Id);
        var detail = await JournalDetailAsync(draft.Id, api);
        return id is null ? Results.Created($"/api/finance/journals/{draft.Id}", detail) : Results.Ok(detail);
    }

    /// <summary>Takes a multipart upload (field <c>file</c>) and keeps it with the record.</summary>
    private static async Task<IResult> AttachAsync(AttachmentOwner owner, Guid ownerId, Guid? store, HttpRequest request, ApiRequest api)
    {
        if (!request.HasFormContentType) throw ApiException.Bad("Send the file as a form upload.");
        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null) ?? throw ApiException.Bad("Choose a file.");
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var saved = await new ExpenseService(await api.ForChangeAsync(store)).AttachAsync(owner, ownerId, file.FileName, buffer.ToArray());
        return Results.Created($"/api/finance/attachments/{saved.Id}", ToInfo(saved));
    }

    private static async Task<Guid?> AttachmentStoreAsync(FinanceAttachment a, ApiRequest api)
    {
        await using var db = api.Root.Create();
        return a.OwnerKind switch
        {
            AttachmentOwner.Expense => await db.Expenses.AsNoTracking().Where(e => e.Id == a.OwnerId).Select(e => (Guid?)e.StoreId).FirstOrDefaultAsync(),
            AttachmentOwner.SupplierBill => await db.SupplierBills.AsNoTracking().Where(b => b.Id == a.OwnerId).Select(b => (Guid?)b.StoreId).FirstOrDefaultAsync(),
            _ => await db.JournalEntries.AsNoTracking().Where(e => e.Id == a.OwnerId).Select(e => e.StoreId).FirstOrDefaultAsync(),
        };
    }

    private static async Task<IResult> AttachmentFileAsync(Guid id, ApiRequest api)
    {
        var file = await new ExpenseService(api.Root).AttachmentAsync(id) ?? throw ApiException.NotFound("That file");
        RequireScope(api, await AttachmentStoreAsync(file, api), "That file");
        return Results.File(file.Data, file.ContentType, file.FileName);
    }

    private static async Task<IResult> DeleteAttachmentAsync(Guid id, ApiRequest api)
    {
        var file = await new ExpenseService(api.Root).AttachmentAsync(id) ?? throw ApiException.NotFound("That file");
        var store = await AttachmentStoreAsync(file, api);
        RequireScope(api, store, "That file");
        await new ExpenseService(await api.ForChangeAsync(store)).DeleteAttachmentAsync(id);
        return Results.NoContent();
    }

    // Years -----------------------------------------------------------------------------------------------------------

    private static async Task<IResult> YearsAsync(ApiRequest api) =>
        Results.Ok((await new LedgerService(api.Root).GetYearsAsync()).OrderByDescending(y => y.StartDate).Select(y => new YearInfo(y.Id, y.Name, y.StartDate, y.EndDate, y.Status,
            ApiRequest.Utc(y.ClosedAtUtc), y.ClosedBy, [.. y.Periods.Select(p => new PeriodInfo(p.Id, p.Number, p.Name, p.StartDate, p.EndDate, p.Status))])));

    private static async Task<IResult> PeriodStatusAsync(Guid id, FiscalStatus status, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        await new LedgerService(await api.ForChangeAsync()).SetPeriodStatusAsync(id, status);
        return await YearsAsync(api);
    }

    private static async Task<IResult> OpeningBalancesAsync(OpeningRequest body, ApiRequest api)
    {
        RequireBodyStore(api, body.StoreId);
        var entry = await new FinanceYearService(await api.ForChangeAsync(body.StoreId)).PostOpeningAsync(body.Date, body.StoreId,
            [.. (body.Lines ?? []).Select(l => new OpeningLine(l.AccountId, l.Debit, l.Credit, l.StoreId, l.PartyKind, l.PartyId))], body.BalanceToRetainedEarnings);
        return Results.Ok(await JournalDetailAsync(entry.Id, api));
    }

    // Budgets ---------------------------------------------------------------------------------------------------------

    private static async Task<IResult> BudgetAsync(ApiRequest api)
    {
        var yearId = api.Id("yearId") ?? throw ApiException.Bad("Choose the year (yearId).");
        Guid? store = api.AskedStore;
        RequireScope(api, store, "That budget");
        var year = (await new LedgerService(api.Root).GetYearsAsync()).SingleOrDefault(y => y.Id == yearId) ?? throw ApiException.NotFound("That year");
        var lines = await new BudgetService(api.Root).GetAsync(yearId, store);
        return Results.Ok(new BudgetInfo(year.Id, year.Name, store, [.. year.Periods.Select(p => new DateOnly(p.StartDate.Year, p.StartDate.Month, 1))],
            [.. lines.Select(l => new BudgetAmountInfo(l.AccountId, l.Month, l.Amount))]));
    }

    private static async Task<IResult> SaveBudgetAsync(BudgetSaveRequest body, ApiRequest api)
    {
        var yearId = api.Id("yearId") ?? throw ApiException.Bad("Choose the year (yearId).");
        Guid? store = api.AskedStore;
        RequireBodyStore(api, store);
        await new BudgetService(await api.ForChangeAsync(store)).SaveAsync(yearId, store, [.. (body.Amounts ?? []).Select(a => new BudgetAmount(a.AccountId, a.Month, a.Amount))]);
        return await BudgetAsync(api);
    }

    private static async Task<IResult> CopyBudgetAsync(BudgetCopyRequest body, ApiRequest api)
    {
        RequireBodyStore(api, body.StoreId);
        int copied = await new BudgetService(await api.ForChangeAsync(body.StoreId)).CopyFromLastYearAsync(body.YearId, body.StoreId, body.FromBudget, body.Percent);
        return Results.Ok(new { copied });
    }

    // Reports and exports ---------------------------------------------------------------------------------------------

    private static async Task<(ReportTable Report, DateOnly From, DateOnly To)> FinanceReportAsync(string key, ApiRequest api)
    {
        var info = FinanceReports.Catalog.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase)) ?? throw ApiException.NotFound("That report");
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        var compare = api.Text("compare") is { } c
            ? Enum.TryParse<FinanceComparison>(c.Replace("-", "", StringComparison.Ordinal), true, out var parsed) ? parsed : throw ApiException.Bad("compare is none, previousPeriod, previousYear or budget.")
            : FinanceComparison.None;
        var report = await new FinanceReports(api.For(store)).RunAsync(info.Key, new FinanceReportOptions(from, to, store, compare, api.Id("accountId")));
        return (report, from, to);
    }

    private static async Task<IResult> ExportAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        var entries = await new LedgerExportService(api.Root).EntriesAsync(from, to, store);
        var name = $"ledger-{from:yyyyMMdd}-{to:yyyyMMdd}";
        switch ((api.Text("format") ?? "csv").ToLowerInvariant())
        {
            case "tally":
                string company;
                await using (var db = api.Root.Create())
                    company = await db.StoreSettings.AsNoTracking().Where(s => store == null || s.StoreId == store).Select(s => s.CompanyName).FirstOrDefaultAsync() ?? "SMA RETAIL";
                return Results.File(Encoding.UTF8.GetBytes(LedgerExportService.ToTallyXml(entries, company)), "application/xml", name + ".xml");
            case "iif":
                return Results.File(Encoding.UTF8.GetBytes(LedgerExportService.ToIif(entries)), "text/plain; charset=utf-8", name + ".iif");
            case "qbo":
                return Results.File(Encoding.UTF8.GetBytes(LedgerExportService.ToQuickBooksOnlineCsv(entries)), "text/csv; charset=utf-8",
                    $"ledger-quickbooks-online-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
            case "csv":
                return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(LedgerExportService.ToCsv(entries))).ToArray(), "text/csv; charset=utf-8", name + ".csv");
            default:
                throw ApiException.Bad("format is tally, iif, qbo or csv.");
        }
    }

    private static async Task<IResult> FiscalAsync(ApiRequest api, IFiscalInvoiceSender fiscal)
    {
        var store = api.ListStore();
        FiscalInvoiceStatus? status = api.Text("status") is { } s
            ? Enum.TryParse<FiscalInvoiceStatus>(s, true, out var st) ? st : throw ApiException.Bad("status is Pending, Sent, Failed or Skipped.")
            : null;
        var rows = await new FiscalService(api.Root).QueueAsync(store, status);
        return Results.Ok(new FiscalQueueInfo(fiscal.Enabled, [.. rows.Select(r => new FiscalRow(r.Item.Id, r.Item.SaleId, r.StoreCode, r.StationNumber, r.Item.InvoiceNumber,
            r.SoldAt, r.Total, r.Item.Status, r.Item.Attempts, r.Item.LastError, r.Item.FiscalNumber, ApiRequest.Utc(r.Item.SentAtUtc)))]));
    }
}
