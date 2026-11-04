using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

public sealed record BankAccountRow(Guid Id, string Version, string Name, BankAccountKind Kind, string? BankName, string? AccountNumber, string? Iban, string Currency,
    Guid LedgerAccountId, string LedgerCode, Guid? StoreId, string? StoreCode, decimal OpeningBalance, DateOnly? OpeningDate, bool IsActive, decimal Balance);
public sealed record BankAccountRequest(string Name, BankAccountKind Kind, string? BankName, string? AccountNumber, string? Iban, string? Currency, Guid? LedgerAccountId,
    Guid? StoreId, decimal OpeningBalance, DateOnly? OpeningDate, bool IsActive, string? Version = null);
public sealed record BankTransferRequest(Guid FromAccountId, Guid ToAccountId, DateOnly Date, decimal Amount, decimal? ToAmount, decimal Rate = 1, decimal ToRate = 1,
    string? Reference = null, string? Memo = null);
public sealed record BankTransferRow(Guid Id, long Number, DateOnly Date, Guid FromAccountId, string FromAccount, Guid ToAccountId, string ToAccount, decimal Amount, decimal ToAmount,
    string? Reference, string? Memo, string? CreatedBy);
public sealed record DepositCandidateRow(Guid DrawerSessionId, Guid StoreId, string StoreCode, int StationNumber, DateTime ClosedAt, decimal Cash, Guid? DepositId);
public sealed record DepositRequest(Guid StoreId, Guid BankAccountId, DateOnly Date, decimal Amount, Guid? DrawerSessionId, string? Reference);
public sealed record DepositRow(Guid Id, long Number, DateOnly Date, Guid StoreId, string StoreCode, Guid BankAccountId, string BankAccount, decimal Amount, Guid? DrawerSessionId,
    string? Reference, string? CreatedBy);
public sealed record StatementFormatInfo(string BankName, bool HasHeader, string Delimiter, string DateColumn, string DateFormat, string? DescriptionColumn, string? ReferenceColumn,
    string? AmountColumn, string? DebitColumn, string? CreditColumn, string? BalanceColumn);
public sealed record StatementImportResult(Guid StatementId, int Lines, DateOnly FromDate, DateOnly ToDate, decimal? ClosingBalance);
public sealed record StatementLineRow(Guid Id, DateOnly Date, string? Description, string? Reference, decimal Amount, decimal? Balance, StatementLineStatus Status, Guid? JournalLineId,
    string? MatchedBy);
public sealed record LedgerLineRow(Guid JournalLineId, Guid EntryId, long Number, DateOnly Date, string Memo, string? Reference, decimal Amount, bool Matched);
public sealed record ReconciliationInfo(Guid BankAccountId, DateOnly AsOf, decimal? StatementBalance, decimal LedgerBalance, decimal Uncleared, decimal UnmatchedStatement,
    int UnmatchedLines, decimal ReconciledBalance, decimal? Difference, IReadOnlyList<StatementLineRow> StatementLines, IReadOnlyList<LedgerLineRow> LedgerLines);
public sealed record MatchRequest(Guid JournalLineId);
public sealed record StatementEntryRequest(Guid? AccountId, string? Memo);

/// <summary>Bank and cash accounts, transfers, deposits of till cash, statements and reconciliation, under <c>/api/finance</c>.</summary>
public static partial class Endpoints
{
    private static void MapBank(RouteGroupBuilder api)
    {
        api.MapGet("/bank-accounts", async (ApiRequest a) => Results.Ok(await BankRowsAsync(a, a.ListStore()))).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/bank-accounts", (BankAccountRequest body, ApiRequest a) => SaveBankAccountAsync(null, body, a)).RequireAuthorization(Need(Permissions.FinanceBank));
        api.MapPut("/bank-accounts/{id:guid}", (Guid id, BankAccountRequest body, ApiRequest a) => SaveBankAccountAsync(id, body, a)).RequireAuthorization(Need(Permissions.FinanceBank));
        api.MapGet("/bank-transfers", BankTransfersAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/bank-transfers", BankTransferAsync).RequireAuthorization(Need(Permissions.FinanceBank));
        api.MapGet("/deposits", DepositsAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapGet("/deposits/candidates", DepositCandidatesAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/deposits", DepositAsync).RequireAuthorization(Need(Permissions.FinanceBank));

        api.MapGet("/bank-accounts/{id:guid}/statement-format", async (Guid id, ApiRequest a) =>
        {
            var bank = await BankInScopeAsync(id, a);
            var f = await new BankService(a.Root).FormatForAsync(bank.BankName ?? bank.Name);
            return Results.Ok(f is null ? new StatementFormatInfo(bank.BankName ?? bank.Name, true, ",", "Date", "yyyy-MM-dd", "Description", "Reference", "Amount", null, null, "Balance")
                : ToInfo(f));
        }).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/bank-accounts/{id:guid}/statements", ImportStatementAsync).RequireAuthorization(Need(Permissions.FinanceBank)).DisableAntiforgery();
        api.MapGet("/bank-accounts/{id:guid}/reconciliation", ReconciliationAsync).RequireAuthorization(Need(Permissions.FinanceView));
        api.MapPost("/bank-accounts/{id:guid}/auto-match", async (Guid id, ApiRequest a) =>
        {
            var bank = await BankForChangeAsync(id, a);
            int matched = await new BankService(await a.ForChangeAsync(bank.StoreId)).AutoMatchAsync(id);
            return Results.Ok(new { matched });
        }).RequireAuthorization(Need(Permissions.FinanceBank));
        api.MapPost("/statement-lines/{id:guid}/match", async (Guid id, MatchRequest body, ApiRequest a) =>
        {
            var bank = await LineBankAsync(id, a, change: true);
            await new BankService(await a.ForChangeAsync(bank.StoreId)).MatchAsync(id, body.JournalLineId);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.FinanceBank));
        api.MapPost("/statement-lines/{id:guid}/unmatch", async (Guid id, ApiRequest a) =>
        {
            var bank = await LineBankAsync(id, a, change: true);
            await new BankService(await a.ForChangeAsync(bank.StoreId)).UnmatchAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.FinanceBank));
        api.MapPost("/statement-lines/{id:guid}/ignore", async (Guid id, ApiRequest a) =>
        {
            var bank = await LineBankAsync(id, a, change: true);
            await new BankService(await a.ForChangeAsync(bank.StoreId)).IgnoreAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.FinanceBank));
        api.MapPost("/statement-lines/{id:guid}/entry", async (Guid id, StatementEntryRequest body, ApiRequest a) =>
        {
            var bank = await LineBankAsync(id, a, change: true);
            var entry = await new BankService(await a.ForChangeAsync(bank.StoreId)).CreateEntryAsync(id, body.AccountId, body.Memo);
            return Results.Ok(await JournalDetailAsync(entry.Id, a));
        }).RequireAuthorization(Need(Permissions.FinancePost));
    }

    /// <summary>The bank accounts a list shows: the store's and the company's for one store, every one for every store.</summary>
    private static async Task<List<BankAccountRow>> BankRowsAsync(ApiRequest api, Guid? store)
    {
        var accounts = await new BankService(api.Root).GetAccountsAsync(store);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        return [.. accounts.Select(b => new BankAccountRow(b.Account.Id, ApiRequest.Version(b.Account.Version), b.Account.Name, b.Account.Kind, b.Account.BankName, b.Account.AccountNumber,
            b.Account.Iban, b.Account.Currency, b.Account.LedgerAccountId, b.LedgerCode, b.Account.StoreId, b.Account.StoreId is Guid s ? codes.GetValueOrDefault(s) : null,
            b.Account.OpeningBalance, b.Account.OpeningDate, b.Account.IsActive, b.Balance))];
    }

    /// <summary>A bank account the user may pay from or into: their store's, or the company's.</summary>
    private static async Task CheckBankAsync(ApiRequest api, Guid? id)
    {
        if (id is not Guid bank) return;
        await using var db = api.Root.Create();
        var store = await db.BankAccounts.AsNoTracking().Where(b => b.Id == bank).Select(b => new { b.StoreId }).FirstOrDefaultAsync() ?? throw ApiException.NotFound("That bank account");
        if (store.StoreId is Guid s && !api.User.CanSee(s)) throw ApiException.NotFound("That bank account");
    }

    private static async Task<BankAccount> BankInScopeAsync(Guid id, ApiRequest api)
    {
        var bank = await new BankService(api.Root).GetAccountAsync(id) ?? throw ApiException.NotFound("That bank account");
        if (bank.StoreId is Guid s) api.RequireStore(s, "That bank account");
        return bank;
    }

    private static async Task<IResult> SaveBankAccountAsync(Guid? id, BankAccountRequest body, ApiRequest api)
    {
        // A company account is the company's to change; a store's account, that store's administrators'.
        if (body.StoreId is Guid s) { if (!api.User.CanSee(s)) throw ApiException.Forbidden("You may not work for that store."); }
        else await api.RequireCompanyChangeAsync();
        Guid version = Guid.Empty;
        if (id is Guid existing)
        {
            var stored = await BankInScopeAsync(existing, api);
            if (stored.StoreId is null) await api.RequireCompanyChangeAsync();
            version = ApiRequest.ParseVersion(body.Version);
        }
        var saved = await new BankService(await api.ForChangeAsync(body.StoreId)).SaveAccountAsync(new BankAccount
        {
            Id = id ?? Guid.Empty, Version = version, Name = body.Name ?? "", Kind = body.Kind, BankName = body.BankName, AccountNumber = body.AccountNumber, Iban = body.Iban,
            Currency = body.Currency ?? "", LedgerAccountId = body.LedgerAccountId ?? Guid.Empty, StoreId = body.StoreId, OpeningBalance = body.OpeningBalance,
            OpeningDate = body.OpeningDate, IsActive = body.IsActive,
        });
        var row = (await BankRowsAsync(api, null)).Single(b => b.Id == saved.Id);
        return id is null ? Results.Created($"/api/finance/bank-accounts/{saved.Id}", row) : Results.Ok(row);
    }

    private static async Task<IResult> BankTransfersAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        await using var db = api.Root.Create();
        var banks = await db.BankAccounts.AsNoTracking().ToDictionaryAsync(b => b.Id);
        var rows = await db.BankTransfers.AsNoTracking().Where(t => t.Date >= from && t.Date <= to).OrderByDescending(t => t.Date).ThenByDescending(t => t.Number).Take(500).ToListAsync();
        bool Visible(Guid account) => store is null || banks.GetValueOrDefault(account)?.StoreId is not Guid s || s == store;
        return Results.Ok(rows.Where(t => Visible(t.FromAccountId) && Visible(t.ToAccountId)).Select(t => new BankTransferRow(t.Id, t.Number, t.Date, t.FromAccountId,
            banks.GetValueOrDefault(t.FromAccountId)?.Name ?? "", t.ToAccountId, banks.GetValueOrDefault(t.ToAccountId)?.Name ?? "", t.Amount, t.ToAmount, t.Reference, t.Memo, t.CreatedBy)));
    }

    private static async Task<IResult> BankTransferAsync(BankTransferRequest body, ApiRequest api)
    {
        var from = await BankInScopeAsync(body.FromAccountId, api);
        var to = await BankInScopeAsync(body.ToAccountId, api);
        var t = await new BankService(await api.ForChangeAsync(from.StoreId ?? to.StoreId)).TransferAsync(body.FromAccountId, body.ToAccountId, body.Date, body.Amount, body.ToAmount,
            body.Rate <= 0 ? 1 : body.Rate, body.ToRate <= 0 ? 1 : body.ToRate, body.Reference, body.Memo);
        return Results.Created($"/api/finance/bank-transfers/{t.Id}",
            new BankTransferRow(t.Id, t.Number, t.Date, t.FromAccountId, from.Name, t.ToAccountId, to.Name, t.Amount, t.ToAmount, t.Reference, t.Memo, t.CreatedBy));
    }

    private static async Task<IResult> DepositsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        await using var db = api.Root.Create();
        var q = db.BankDeposits.AsNoTracking().Where(d => d.Date >= from && d.Date <= to);
        if (store is Guid s) q = q.Where(d => d.StoreId == s);
        var codes = await StoreCodesAsync(db);
        var banks = await db.BankAccounts.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name);
        return Results.Ok((await q.OrderByDescending(d => d.Date).ThenByDescending(d => d.Number).Take(500).ToListAsync()).Select(d => new DepositRow(d.Id, d.Number, d.Date, d.StoreId,
            codes.GetValueOrDefault(d.StoreId) ?? "", d.BankAccountId, banks.GetValueOrDefault(d.BankAccountId) ?? "", d.Amount, d.DrawerSessionId, d.Reference, d.CreatedBy)));
    }

    private static async Task<IResult> DepositCandidatesAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        var rows = await new BankService(api.Root).DepositCandidatesAsync(from, to, store);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        return Results.Ok(rows.Select(r => new DepositCandidateRow(r.DrawerSessionId, r.StoreId, codes.GetValueOrDefault(r.StoreId) ?? "", r.StationNumber, r.ClosedAt, r.Cash, r.DepositId)));
    }

    private static async Task<IResult> DepositAsync(DepositRequest body, ApiRequest api)
    {
        if (!api.User.CanSee(body.StoreId)) throw ApiException.Forbidden("You may not work for that store.");
        var bank = await BankInScopeAsync(body.BankAccountId, api);
        var d = await new BankService(await api.ForChangeAsync(body.StoreId)).DepositAsync(body.StoreId, body.BankAccountId, body.Date, body.Amount, body.DrawerSessionId, body.Reference);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        return Results.Created($"/api/finance/deposits/{d.Id}", new DepositRow(d.Id, d.Number, d.Date, d.StoreId, codes.GetValueOrDefault(d.StoreId) ?? "", d.BankAccountId, bank.Name,
            d.Amount, d.DrawerSessionId, d.Reference, d.CreatedBy));
    }

    private static StatementFormatInfo ToInfo(BankStatementFormat f) => new(f.BankName, f.HasHeader, f.Delimiter, f.DateColumn, f.DateFormat, f.DescriptionColumn, f.ReferenceColumn,
        f.AmountColumn, f.DebitColumn, f.CreditColumn, f.BalanceColumn);

    /// <summary>A statement CSV (form field <c>file</c>) with its layout in the other form fields (the names of <see cref="StatementFormatInfo"/>).</summary>
    private static async Task<IResult> ImportStatementAsync(Guid id, HttpRequest request, ApiRequest api)
    {
        var bank = await BankForChangeAsync(id, api);
        if (!request.HasFormContentType) throw ApiException.Bad("Send the statement as a form upload.");
        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null) ?? throw ApiException.Bad("Choose the statement file.");
        if (file.Length > 10 * 1024 * 1024) throw ApiException.Bad("A statement file is at most 10 MB.");
        string text;
        using (var reader = new StreamReader(file.OpenReadStream()))
            text = await reader.ReadToEndAsync();
        string? Field(string name) => form.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v.ToString()) ? v.ToString() : null;
        var format = new BankStatementFormat
        {
            BankName = Field("bankName") ?? bank.BankName ?? bank.Name, HasHeader = !string.Equals(Field("hasHeader"), "false", StringComparison.OrdinalIgnoreCase),
            // A tab is a delimiter, though it is white space.
            Delimiter = form.TryGetValue("delimiter", out var delimiter) && delimiter.ToString().Length > 0 ? delimiter.ToString() : ",", DateColumn = Field("dateColumn") ?? "", DateFormat = Field("dateFormat") ?? "yyyy-MM-dd",
            DescriptionColumn = Field("descriptionColumn"), ReferenceColumn = Field("referenceColumn"), AmountColumn = Field("amountColumn"), DebitColumn = Field("debitColumn"),
            CreditColumn = Field("creditColumn"), BalanceColumn = Field("balanceColumn"),
        };
        var statement = await new BankService(await api.ForChangeAsync(bank.StoreId)).ImportStatementAsync(id, file.FileName, text, format);
        return Results.Ok(new StatementImportResult(statement.Id, statement.LineCount, statement.FromDate, statement.ToDate, statement.ClosingBalance));
    }

    private static async Task<IResult> ReconciliationAsync(Guid id, ApiRequest api)
    {
        var bank = await BankInScopeAsync(id, api);
        var today = await api.TodayAsync(bank.StoreId);
        var asOf = DateOnly.TryParseExact(api.Text("asOf"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : today;
        var service = new BankService(api.Root);
        var summary = await service.SummaryAsync(id, asOf);
        DateOnly? from = DateOnly.TryParseExact(api.Text("from"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var f) ? f : null;
        var statement = await service.StatementLinesAsync(id, from, asOf);
        var ledger = await service.LedgerLinesAsync(id, from, asOf);
        return Results.Ok(new ReconciliationInfo(id, asOf, summary.StatementBalance, summary.LedgerBalance, summary.Uncleared, summary.UnmatchedStatement, summary.UnmatchedLines,
            summary.ReconciledBalance, summary.Difference,
            [.. statement.Select(l => new StatementLineRow(l.Id, l.Date, l.Description, l.Reference, l.Amount, l.Balance, l.Status, l.JournalLineId, l.MatchedBy))],
            [.. ledger.Select(l => new LedgerLineRow(l.JournalLineId, l.EntryId, l.Number, l.Date, l.Memo, l.Reference, l.Amount, l.Matched))]));
    }

    private static async Task<BankAccount> LineBankAsync(Guid lineId, ApiRequest api, bool change = false)
    {
        Guid bankId;
        await using (var db = api.Root.Create())
            bankId = await db.BankStatementLines.AsNoTracking().Where(l => l.Id == lineId).Select(l => (Guid?)l.BankAccountId).FirstOrDefaultAsync()
                ?? throw ApiException.NotFound("That statement line");
        return change ? await BankForChangeAsync(bankId, api) : await BankInScopeAsync(bankId, api);
    }

    /// <summary>
    /// An account whose statements and matches are changed: a store's by that store, the company's by users who change
    /// what the company shares (store users still pay from it and deposit to it).
    /// </summary>
    private static async Task<BankAccount> BankForChangeAsync(Guid id, ApiRequest api)
    {
        var bank = await BankInScopeAsync(id, api);
        if (bank.StoreId is null) await api.RequireCompanyChangeAsync();
        return bank;
    }
}
