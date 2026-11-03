using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A bank or cash account with what its ledger account holds.</summary>
public sealed record BankAccountBalance(BankAccount Account, string LedgerCode, decimal Balance);

/// <summary>An End of Day whose till cash may be taken to the bank, with what was deposited of it already.</summary>
public sealed record DepositCandidate(Guid DrawerSessionId, Guid StoreId, int StationNumber, DateTime ClosedAt, decimal Cash, Guid? DepositId);

/// <summary>A ledger line of a bank's account, for matching against its statement.</summary>
public sealed record BankLedgerLine(Guid JournalLineId, Guid EntryId, long Number, DateOnly Date, string Memo, string? Reference, decimal Amount, bool Matched);

/// <summary>
/// Where a bank account's reconciliation stands on a date: the statement's balance, the ledger's balance, the ledger
/// lines not on any statement yet (uncleared) and the statement lines not matched yet.
/// </summary>
public sealed record ReconciliationSummary(Guid BankAccountId, DateOnly AsOf, decimal? StatementBalance, decimal LedgerBalance, decimal Uncleared,
    decimal UnmatchedStatement, int UnmatchedLines)
{
    /// <summary>The ledger balance with the uncleared lines taken out and the unmatched statement lines added: what the statement should say.</summary>
    public decimal ReconciledBalance => LedgerBalance - Uncleared + UnmatchedStatement;

    /// <summary>What still does not agree (zero when reconciled).</summary>
    public decimal? Difference => StatementBalance is decimal s ? s - ReconciledBalance : null;
}

/// <summary>
/// Bank and cash accounts: their ledger accounts and opening balances, transfers between them, deposits of till cash,
/// statements read from CSV files (with a column layout saved per bank), and reconciliation of statement lines against
/// the account's ledger lines.
/// </summary>
public sealed class BankService(DataContext data)
{
    private LedgerService Ledger => new(data);

    // Accounts --------------------------------------------------------------------------------------------------------

    public async Task<List<BankAccountBalance>> GetAccountsAsync(Guid? store = null, bool activeOnly = false)
    {
        await using var db = data.Create();
        var q = db.BankAccounts.AsNoTracking().Include(b => b.LedgerAccount).AsQueryable();
        if (store is Guid s) q = q.Where(b => b.StoreId == s || b.StoreId == null);
        if (activeOnly) q = q.Where(b => b.IsActive);
        var accounts = await q.OrderBy(b => b.Name).ToListAsync();
        var ids = accounts.Select(a => a.LedgerAccountId).ToList();
        var balances = await db.JournalLines.AsNoTracking().Where(l => ids.Contains(l.AccountId))
            .Join(db.JournalEntries.AsNoTracking().Where(e => e.Status != JournalStatus.Draft), l => l.JournalEntryId, e => e.Id, (l, e) => new { l.AccountId, l.Debit, l.Credit })
            .GroupBy(l => l.AccountId).Select(g => new { g.Key, Balance = g.Sum(l => l.Debit) - g.Sum(l => l.Credit) }).ToListAsync();
        return accounts.Select(a => new BankAccountBalance(a, a.LedgerAccount?.Code ?? "", balances.FirstOrDefault(b => b.Key == a.LedgerAccountId)?.Balance ?? 0)).ToList();
    }

    public async Task<BankAccount?> GetAccountAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.BankAccounts.AsNoTracking().Include(b => b.LedgerAccount).SingleOrDefaultAsync(b => b.Id == id);
    }

    /// <summary>
    /// Adds or changes a bank or cash account. Without a ledger account one is made for it under the chart's assets (the
    /// next free code after the Bank account's). The opening balance is posted as an opening entry on its date (against
    /// retained earnings), and posted again when it changes.
    /// </summary>
    public async Task<BankAccount> SaveAccountAsync(BankAccount account)
    {
        var name = account.Name.Trim();
        if (name.Length is 0 or > 60) throw new BusinessRuleException("A bank account needs a name of up to 60 characters.");
        if (account.OpeningBalance != 0 && account.OpeningDate is null) throw new BusinessRuleException("Give the date of the opening balance.");
        BankAccount row;
        await using (var db = data.Create())
        {
            if (await db.BankAccounts.AnyAsync(b => b.Name == name && b.Id != account.Id)) throw new BusinessRuleException($"There is already an account called {name}.");
            if (account.StoreId is Guid store && !await db.Stores.AnyAsync(s => s.Id == store)) throw new BusinessRuleException("That store no longer exists.");
            var currency = await FinanceNumbers.CurrencyAsync(db, account.Currency);
            if (currency.Length != 3) throw new BusinessRuleException("The currency is a three-letter code, such as PKR.");
            Guid ledgerId = account.LedgerAccountId;
            if (ledgerId == Guid.Empty)
                ledgerId = (await NewLedgerAccountAsync(db, name)).Id;
            var ledger = await db.LedgerAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == ledgerId) ?? throw new BusinessRuleException("That ledger account no longer exists.");
            if (ledger.Type != AccountType.Asset || !ledger.IsPosting) throw new BusinessRuleException($"{ledger.Code} {ledger.Name} is not an asset account that is posted to.");
            if (await db.BankAccounts.AnyAsync(b => b.LedgerAccountId == ledgerId && b.Id != account.Id))
                throw new BusinessRuleException($"{ledger.Code} {ledger.Name} is already another bank account's.");
            row = account.Id == Guid.Empty ? new BankAccount() : await db.BankAccounts.SingleOrDefaultAsync(b => b.Id == account.Id) ?? throw new BusinessRuleException("That bank account no longer exists.");
            if (account.Id == Guid.Empty) db.BankAccounts.Add(row);
            else if (account.Version != Guid.Empty && account.Version != row.Version) throw new ConcurrencyConflictException("Someone else changed this account. Load it again.");
            row.Name = name;
            row.Kind = account.Kind;
            row.BankName = FinanceNumbers.Cut(account.BankName?.Trim(), 60);
            row.AccountNumber = FinanceNumbers.Cut(account.AccountNumber?.Trim(), 40);
            row.Iban = FinanceNumbers.Cut(account.Iban?.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant(), 34);
            row.Currency = currency;
            row.LedgerAccountId = ledgerId;
            row.StoreId = account.StoreId;
            row.OpeningBalance = Money.Round(account.OpeningBalance);
            row.OpeningDate = account.OpeningDate;
            row.IsActive = account.IsActive;
            await db.SaveChangesAsync();
        }
        if (row.OpeningDate is DateOnly date)
        {
            var code = await CodeAsync(row.LedgerAccountId);
            decimal amount = row.OpeningBalance;
            var source = $"bank-opening:{row.Id:N}";
            if (amount != 0 || await SourceExistsAsync(source))
            {
                // An opening balance taken back to zero still replaces what was posted (two zero lines are not an entry).
                var lines = amount == 0
                    ? new List<JournalDraftLine> { JournalDraftLine.Code(code, 0.01m, 0, row.StoreId, "Opening balance taken back"), JournalDraftLine.Code(code, 0, 0.01m, row.StoreId, "Opening balance taken back") }
                    : [JournalDraftLine.Code(code, amount > 0 ? amount : 0, amount < 0 ? -amount : 0, row.StoreId, "Opening balance"),
                       JournalDraftLine.Role(LedgerRoles.RetainedEarnings, amount < 0 ? -amount : 0, amount > 0 ? amount : 0, row.StoreId, "Opening balance of " + row.Name)];
                await Ledger.PostAsync(new JournalDraft(date, JournalKind.Opening, $"Opening balance of {row.Name}", lines) { Source = source, StoreId = row.StoreId });
            }
        }
        return row;
    }

    private static async Task<LedgerAccount> NewLedgerAccountAsync(RetailPlusDbContext db, string name)
    {
        var accounts = await db.LedgerAccounts.AsNoTracking().ToListAsync();
        var bank = accounts.FirstOrDefault(a => a.Role == LedgerRoles.Bank);
        var parent = bank?.ParentId ?? accounts.FirstOrDefault(a => a.Type == AccountType.Asset && !a.IsPosting && a.ParentId == null)?.Id;
        int start = bank is not null && int.TryParse(bank.Code, NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c + 1 : 1051;
        var taken = accounts.Select(a => a.Code).ToHashSet(StringComparer.Ordinal);
        int code = start;
        while (taken.Contains(code.ToString(CultureInfo.InvariantCulture))) code++;
        var account = new LedgerAccount
        {
            Code = code.ToString(CultureInfo.InvariantCulture), Name = FinanceNumbers.Cut(name, 100)!, Type = AccountType.Asset, ParentId = parent, IsPosting = true,
        };
        db.LedgerAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    // Transfers and deposits ------------------------------------------------------------------------------------------

    /// <summary>
    /// Moves money between two accounts: Dr the receiving account, Cr the paying one. Between currencies, what arrived is
    /// given too; both sides are turned into the base currency at <paramref name="rate"/> (base per unit of the paying
    /// account's currency) and the difference is an exchange difference.
    /// </summary>
    public async Task<BankTransfer> TransferAsync(Guid fromId, Guid toId, DateOnly date, decimal amount, decimal? toAmount = null, decimal rate = 1, decimal toRate = 1,
        string? reference = null, string? memo = null)
    {
        if (fromId == toId) throw new BusinessRuleException("Choose two different accounts.");
        if (amount <= 0) throw new BusinessRuleException("A transfer's amount must be more than zero.");
        BankAccount from, to;
        string baseCurrency;
        await using (var db = data.Create())
        {
            from = await db.BankAccounts.AsNoTracking().Include(b => b.LedgerAccount).SingleOrDefaultAsync(b => b.Id == fromId) ?? throw new BusinessRuleException("Choose the account the money leaves.");
            to = await db.BankAccounts.AsNoTracking().Include(b => b.LedgerAccount).SingleOrDefaultAsync(b => b.Id == toId) ?? throw new BusinessRuleException("Choose the account the money goes to.");
            if (!from.IsActive || !to.IsActive) throw new BusinessRuleException("Both accounts must be active.");
            baseCurrency = await FinanceNumbers.BaseCurrencyAsync(db);
        }
        decimal arrived = Money.Round(toAmount ?? amount);
        if (from.Currency == to.Currency && arrived != Money.Round(amount)) throw new BusinessRuleException("Between accounts in one currency, what arrives is what left.");
        decimal fromBase = FinanceNumbers.Base(amount, from.Currency == baseCurrency ? 1 : rate);
        decimal toBase = FinanceNumbers.Base(arrived, to.Currency == baseCurrency ? 1 : toRate);
        var transfer = new BankTransfer
        {
            Date = date, FromAccountId = fromId, ToAccountId = toId, Amount = Money.Round(amount), ToAmount = arrived, Reference = FinanceNumbers.Cut(reference?.Trim(), 40),
            Memo = FinanceNumbers.Cut(memo?.Trim(), 200), CreatedBy = FinanceNumbers.Cut(data.CurrentUser?.UserName, 50),
        };
        transfer.Id = Guid.CreateVersion7();
        transfer.Number = await FinanceNumbers.NextAsync(data, RetailPlusDbContext.Sequences.BankNumbers);
        var store = from.StoreId ?? to.StoreId;
        var text = $"Transfer {transfer.Number}: {from.Name} to {to.Name}";
        var lines = new List<JournalDraftLine>
        {
            JournalDraftLine.Code(to.LedgerAccount!.Code, toBase, 0, store, memo ?? text),
            JournalDraftLine.Code(from.LedgerAccount!.Code, 0, fromBase, store, memo ?? text),
        };
        if (toBase != fromBase)
            lines.Add(JournalDraftLine.Role(LedgerRoles.ExchangeDifferences, toBase < fromBase ? fromBase - toBase : 0, toBase > fromBase ? toBase - fromBase : 0, store, "Exchange difference"));
        var entry = await Ledger.PostAsync(new JournalDraft(date, JournalKind.Bank, text, lines) { Source = $"bank-transfer:{transfer.Id:N}", StoreId = store, Reference = transfer.Reference });
        transfer.JournalEntryId = entry.Id;
        await using (var db = data.Create())
        {
            db.BankTransfers.Add(transfer);
            db.Audit(AuditActions.BankTransfer, $"{text}: {transfer.Amount:N2} {from.Currency}", nameof(BankTransfer), transfer.Id);
            await db.SaveChangesAsync();
        }
        return transfer;
    }

    /// <summary>
    /// The End of Day sessions of the stores from <paramref name="from"/> to <paramref name="to"/> with the cash they took
    /// (the cash tenders of the session) and the deposit made of it, if any.
    /// </summary>
    public async Task<List<DepositCandidate>> DepositCandidatesAsync(DateOnly from, DateOnly to, Guid? store = null)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        await using var db = data.Create();
        var cash = await db.TenderTypes.AsNoTracking().Where(t => t.Kind == TenderKind.Cash).Select(t => t.Name).ToListAsync();
        var q = db.DrawerSessions.AsNoTracking().Where(s => s.ClosedAt != null && s.ClosedAt >= start && s.ClosedAt < end);
        if (store is Guid st) q = q.Where(s => s.StoreId == st);
        var sessions = await q.Include(s => s.Tenders).OrderBy(s => s.ClosedAt).ToListAsync();
        var ids = sessions.Select(s => (Guid?)s.Id).ToList();
        var deposits = await db.BankDeposits.AsNoTracking().Where(d => ids.Contains(d.DrawerSessionId)).ToDictionaryAsync(d => d.DrawerSessionId!.Value, d => d.Id);
        return sessions.Select(s => new DepositCandidate(s.Id, s.StoreId, s.StationNumber, s.ClosedAt!.Value,
                s.Tenders.Where(t => cash.Contains(t.TenderType, StringComparer.OrdinalIgnoreCase)).Sum(t => t.Amount), deposits.TryGetValue(s.Id, out var d) ? d : null))
            .ToList();
    }

    /// <summary>
    /// Till cash taken to the bank: Dr the bank account, Cr Cash in hand, for the store. With an End of Day session it is
    /// that day's cash (once per session); the amount may differ from what the session took (a float kept back).
    /// </summary>
    public async Task<BankDeposit> DepositAsync(Guid storeId, Guid bankAccountId, DateOnly date, decimal amount, Guid? drawerSessionId = null, string? reference = null)
    {
        if (amount <= 0) throw new BusinessRuleException("A deposit's amount must be more than zero.");
        BankAccount bank;
        await using (var db = data.Create())
        {
            bank = await db.BankAccounts.AsNoTracking().Include(b => b.LedgerAccount).SingleOrDefaultAsync(b => b.Id == bankAccountId) ?? throw new BusinessRuleException("Choose the bank account.");
            if (!bank.IsActive) throw new BusinessRuleException($"{bank.Name} is inactive.");
            if (bank.Currency != await FinanceNumbers.BaseCurrencyAsync(db)) throw new BusinessRuleException("Till cash is deposited to an account in the base currency.");
            if (drawerSessionId is Guid session)
            {
                var s = await db.DrawerSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == session) ?? throw new BusinessRuleException("That End of Day no longer exists.");
                if (s.StoreId != storeId) throw new BusinessRuleException("That End of Day belongs to another store.");
                if (await db.BankDeposits.AnyAsync(d => d.DrawerSessionId == session)) throw new BusinessRuleException("That End of Day's cash was deposited already.");
            }
        }
        var deposit = new BankDeposit
        {
            Date = date, StoreId = storeId, BankAccountId = bankAccountId, Amount = Money.Round(amount), DrawerSessionId = drawerSessionId,
            Reference = FinanceNumbers.Cut(reference?.Trim(), 40), CreatedBy = FinanceNumbers.Cut(data.CurrentUser?.UserName, 50),
        };
        deposit.Id = Guid.CreateVersion7();
        deposit.Number = await FinanceNumbers.NextAsync(data, RetailPlusDbContext.Sequences.BankNumbers);
        var text = $"Deposit {deposit.Number} to {bank.Name}";
        var entry = await Ledger.PostAsync(new JournalDraft(date, JournalKind.Bank, text,
            [JournalDraftLine.Code(bank.LedgerAccount!.Code, deposit.Amount, 0, storeId, text), JournalDraftLine.Role(LedgerRoles.Cash, 0, deposit.Amount, storeId, text)])
        {
            Source = $"bank-deposit:{deposit.Id:N}", StoreId = storeId, Reference = deposit.Reference,
        });
        deposit.JournalEntryId = entry.Id;
        await using (var db = data.Create())
        {
            db.BankDeposits.Add(deposit);
            db.Audit(AuditActions.BankDeposit, $"{text}: {deposit.Amount:N2}", nameof(BankDeposit), deposit.Id);
            await db.SaveChangesAsync();
        }
        return deposit;
    }

    // Statements ------------------------------------------------------------------------------------------------------

    public async Task<BankStatementFormat?> FormatForAsync(string? bankName)
    {
        if (string.IsNullOrWhiteSpace(bankName)) return null;
        var name = bankName.Trim();
        await using var db = data.Create();
        return await db.BankStatementFormats.AsNoTracking().FirstOrDefaultAsync(f => f.BankName == name);
    }

    public async Task<BankStatementFormat> SaveFormatAsync(BankStatementFormat format)
    {
        var name = format.BankName.Trim();
        if (name.Length is 0 or > 60) throw new BusinessRuleException("Name the bank the layout is for.");
        if (string.IsNullOrWhiteSpace(format.DateColumn)) throw new BusinessRuleException("Say which column holds the date.");
        if (string.IsNullOrWhiteSpace(format.AmountColumn) && (string.IsNullOrWhiteSpace(format.DebitColumn) || string.IsNullOrWhiteSpace(format.CreditColumn)))
            throw new BusinessRuleException("Say which column holds the amount, or which hold money out and money in.");
        if (format.Delimiter.Length is 0 or > 2) throw new BusinessRuleException("The separator is one character, such as a comma.");
        await using var db = data.Create();
        var row = await db.BankStatementFormats.FirstOrDefaultAsync(f => f.BankName == name);
        if (row is null) db.BankStatementFormats.Add(row = new BankStatementFormat { BankName = name });
        row.HasHeader = format.HasHeader;
        row.Delimiter = format.Delimiter;
        row.DateColumn = format.DateColumn.Trim();
        row.DateFormat = string.IsNullOrWhiteSpace(format.DateFormat) ? "yyyy-MM-dd" : format.DateFormat.Trim();
        row.DescriptionColumn = Blank(format.DescriptionColumn);
        row.ReferenceColumn = Blank(format.ReferenceColumn);
        row.AmountColumn = Blank(format.AmountColumn);
        row.DebitColumn = Blank(format.DebitColumn);
        row.CreditColumn = Blank(format.CreditColumn);
        row.BalanceColumn = Blank(format.BalanceColumn);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>
    /// Reads a statement CSV with the layout given (saved for the account's bank for next time) and keeps its lines,
    /// unmatched. Lines already brought in from an earlier statement (same date, amount and reference) are left out.
    /// </summary>
    public async Task<BankStatement> ImportStatementAsync(Guid bankAccountId, string fileName, string csv, BankStatementFormat format)
    {
        BankAccount bank;
        await using (var db = data.Create())
            bank = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bankAccountId) ?? throw new BusinessRuleException("That bank account no longer exists.");
        format.BankName = string.IsNullOrWhiteSpace(format.BankName) ? bank.BankName ?? bank.Name : format.BankName;
        format = await SaveFormatAsync(format);
        var parsed = ParseStatement(csv, format);
        if (parsed.Count == 0) throw new BusinessRuleException("The file has no statement lines.");
        await using (var db = data.Create())
        {
            var known = (await db.BankStatementLines.AsNoTracking().Where(l => l.BankAccountId == bankAccountId)
                    .Select(l => new { l.Date, l.Amount, l.Reference, l.Description }).ToListAsync())
                .Select(l => (l.Date, l.Amount, l.Reference ?? "", l.Description ?? "")).ToHashSet();
            var fresh = parsed.Where(l => !known.Contains((l.Date, l.Amount, l.Reference ?? "", l.Description ?? ""))).ToList();
            var statement = new BankStatement
            {
                BankAccountId = bankAccountId, FileName = FinanceNumbers.Cut(Path.GetFileName(fileName), 200), FromDate = parsed.Min(l => l.Date), ToDate = parsed.Max(l => l.Date),
                ClosingBalance = parsed.LastOrDefault(l => l.Balance is not null)?.Balance, LineCount = fresh.Count, ImportedBy = FinanceNumbers.Cut(data.CurrentUser?.UserName, 50),
            };
            statement.Id = Guid.CreateVersion7();
            int n = 0;
            foreach (var l in fresh)
            {
                l.BankStatementId = statement.Id;
                l.BankAccountId = bankAccountId;
                l.LineNumber = ++n;
                statement.Lines.Add(l);
            }
            db.BankStatements.Add(statement);
            db.Audit(AuditActions.StatementImported, $"{bank.Name}: {fresh.Count} lines from {statement.FileName} ({parsed.Count - fresh.Count} already there)", nameof(BankStatement), statement.Id);
            await db.SaveChangesAsync();
            return statement;
        }
    }

    /// <summary>The lines of a statement CSV read with a layout (money in positive).</summary>
    public static List<BankStatementLine> ParseStatement(string csv, BankStatementFormat format)
    {
        char separator = format.Delimiter is "\\t" or "\t" ? '\t' : format.Delimiter[0];
        var rows = Csv(csv, separator);
        if (rows.Count == 0) return [];
        string[]? header = format.HasHeader ? rows[0] : null;
        int Column(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return -1;
            name = name.Trim();
            if (header is not null)
            {
                int i = Array.FindIndex(header, h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) return i;
            }
            if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1) return number - 1;
            throw new BusinessRuleException($"The file has no column {name}.");
        }
        int date = Column(format.DateColumn), description = Column(format.DescriptionColumn), reference = Column(format.ReferenceColumn),
            amount = Column(format.AmountColumn), debit = Column(format.DebitColumn), credit = Column(format.CreditColumn), balance = Column(format.BalanceColumn);
        var lines = new List<BankStatementLine>();
        int lineNumber = 0;
        foreach (var row in rows.Skip(header is null ? 0 : 1))
        {
            lineNumber++;
            string Cell(int i) => i >= 0 && i < row.Length ? row[i].Trim() : "";
            if (row.All(c => c.Trim().Length == 0)) continue;
            if (!DateOnly.TryParseExact(Cell(date), format.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var day))
                throw new BusinessRuleException($"Line {lineNumber}: {Cell(date)} is not a date like {format.DateFormat}.");
            decimal value = amount >= 0 ? Number(Cell(amount), lineNumber) : Number(Cell(credit), lineNumber) - Number(Cell(debit), lineNumber);
            if (value == 0) continue;
            lines.Add(new BankStatementLine
            {
                Date = day, Description = FinanceNumbers.Cut(Blank(Cell(description)), 200), Reference = FinanceNumbers.Cut(Blank(Cell(reference)), 60), Amount = Money.Round(value),
                Balance = balance >= 0 && Cell(balance).Length > 0 ? Number(Cell(balance), lineNumber) : null, Status = StatementLineStatus.Unmatched,
            });
        }
        return lines;
    }

    private static decimal Number(string text, int line)
    {
        if (text.Length == 0) return 0;
        var t = text.Replace(",", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        bool negative = t.StartsWith('(') && t.EndsWith(')');
        t = t.Trim('(', ')');
        if (t.EndsWith("CR", StringComparison.OrdinalIgnoreCase)) t = t[..^2];
        else if (t.EndsWith("DR", StringComparison.OrdinalIgnoreCase)) { t = t[..^2]; negative = !negative; }
        if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) throw new BusinessRuleException($"Line {line}: {text} is not an amount.");
        return negative ? -value : value;
    }

    /// <summary>CSV rows (quoted fields with doubled quotes, any line ending).</summary>
    internal static List<string[]> Csv(string text, char separator)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == separator) { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add([.. row]);
                row.Clear();
            }
            else if (c != '﻿' || i != 0) field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add([.. row]);
        }
        return rows.Where(r => r.Any(f => f.Length > 0)).ToList();
    }

    // Reconciliation --------------------------------------------------------------------------------------------------

    public async Task<List<BankStatementLine>> StatementLinesAsync(Guid bankAccountId, DateOnly? from = null, DateOnly? to = null, StatementLineStatus? status = null)
    {
        await using var db = data.Create();
        var q = db.BankStatementLines.AsNoTracking().Where(l => l.BankAccountId == bankAccountId);
        if (from is DateOnly f) q = q.Where(l => l.Date >= f);
        if (to is DateOnly t) q = q.Where(l => l.Date <= t);
        if (status is StatementLineStatus s) q = q.Where(l => l.Status == s);
        return await q.OrderBy(l => l.Date).ThenBy(l => l.CreatedAtUtc).ThenBy(l => l.LineNumber).ToListAsync();
    }

    /// <summary>The ledger lines of the account's ledger account (money in positive), marked when a statement line is matched to them.</summary>
    public async Task<List<BankLedgerLine>> LedgerLinesAsync(Guid bankAccountId, DateOnly? from = null, DateOnly? to = null, bool unmatchedOnly = false)
    {
        await using var db = data.Create();
        var ledgerId = await db.BankAccounts.AsNoTracking().Where(b => b.Id == bankAccountId).Select(b => (Guid?)b.LedgerAccountId).SingleOrDefaultAsync()
            ?? throw new BusinessRuleException("That bank account no longer exists.");
        var matched = db.BankStatementLines.AsNoTracking().Where(s => s.JournalLineId != null).Select(s => s.JournalLineId);
        var q = db.JournalLines.AsNoTracking().Where(l => l.AccountId == ledgerId)
            .Join(db.JournalEntries.AsNoTracking().Where(e => e.Status != JournalStatus.Draft), l => l.JournalEntryId, e => e.Id,
                (l, e) => new { l.Id, EntryId = e.Id, e.Number, e.Date, e.Memo, e.Reference, LineMemo = l.Memo, l.Debit, l.Credit, l.LineNumber, Matched = matched.Contains((Guid?)l.Id) });
        if (from is DateOnly f) q = q.Where(l => l.Date >= f);
        if (to is DateOnly t) q = q.Where(l => l.Date <= t);
        if (unmatchedOnly) q = q.Where(l => !l.Matched);
        return (await q.ToListAsync()).OrderBy(l => l.Date).ThenBy(l => l.Number).ThenBy(l => l.LineNumber)
            .Select(l => new BankLedgerLine(l.Id, l.EntryId, l.Number, l.Date, l.LineMemo ?? l.Memo, l.Reference, l.Debit - l.Credit, l.Matched)).ToList();
    }

    /// <summary>
    /// Matches the account's unmatched statement lines to its unmatched ledger lines of the same amount: first those whose
    /// reference appears in the other's text, then the one nearest in date within <paramref name="days"/> days. Returns the
    /// number matched.
    /// </summary>
    public async Task<int> AutoMatchAsync(Guid bankAccountId, int days = 5)
    {
        var statement = await StatementLinesAsync(bankAccountId, status: StatementLineStatus.Unmatched);
        var ledger = await LedgerLinesAsync(bankAccountId, unmatchedOnly: true);
        var pairs = new List<(Guid Statement, Guid Ledger)>();
        var free = ledger.ToList();
        static bool Mentions(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && a.Trim().Length >= 3 && b.Contains(a.Trim(), StringComparison.OrdinalIgnoreCase);
        foreach (var s in statement)
        {
            var candidates = free.Where(l => l.Amount == s.Amount && Math.Abs(l.Date.DayNumber - s.Date.DayNumber) <= days).ToList();
            if (candidates.Count == 0) continue;
            var byReference = candidates.FirstOrDefault(l => Mentions(s.Reference, l.Reference) || Mentions(s.Reference, l.Memo) || Mentions(l.Reference, s.Description)
                || Mentions(l.Reference, s.Reference));
            var pick = byReference ?? candidates.OrderBy(l => Math.Abs(l.Date.DayNumber - s.Date.DayNumber)).ThenBy(l => l.Number).First();
            pairs.Add((s.Id, pick.JournalLineId));
            free.Remove(pick);
        }
        if (pairs.Count == 0) return 0;
        await using var db = data.Create();
        var ids = pairs.Select(p => p.Statement).ToList();
        var rows = await db.BankStatementLines.Where(l => ids.Contains(l.Id)).ToListAsync();
        foreach (var row in rows) Match(row, pairs.Single(p => p.Statement == row.Id).Ledger, StatementLineStatus.Matched);
        db.Audit(AuditActions.StatementMatched, $"{pairs.Count} statement lines matched by amount, date and reference", nameof(BankAccount), bankAccountId);
        await db.SaveChangesAsync();
        return pairs.Count;
    }

    /// <summary>Matches a statement line to a ledger line of its account by hand (the amounts must agree).</summary>
    public async Task MatchAsync(Guid statementLineId, Guid journalLineId)
    {
        await using var db = data.Create();
        var line = await db.BankStatementLines.SingleOrDefaultAsync(l => l.Id == statementLineId) ?? throw new BusinessRuleException("That statement line no longer exists.");
        if (line.Status != StatementLineStatus.Unmatched) throw new BusinessRuleException("That statement line is matched already; undo it first.");
        var ledgerId = await db.BankAccounts.AsNoTracking().Where(b => b.Id == line.BankAccountId).Select(b => b.LedgerAccountId).SingleAsync();
        var journal = await db.JournalLines.AsNoTracking().SingleOrDefaultAsync(l => l.Id == journalLineId) ?? throw new BusinessRuleException("That ledger line no longer exists.");
        if (journal.AccountId != ledgerId) throw new BusinessRuleException("That ledger line is not on this bank's account.");
        if (journal.Debit - journal.Credit != line.Amount) throw new BusinessRuleException($"The amounts differ: {line.Amount:N2} on the statement, {journal.Debit - journal.Credit:N2} in the ledger.");
        if (await db.BankStatementLines.AnyAsync(l => l.JournalLineId == journalLineId)) throw new BusinessRuleException("That ledger line is matched to another statement line.");
        Match(line, journalLineId, StatementLineStatus.Matched);
        await db.SaveChangesAsync();
    }

    /// <summary>Takes a match (or an "ignored") off a statement line; an entry made for it stays in the ledger.</summary>
    public async Task UnmatchAsync(Guid statementLineId)
    {
        await using var db = data.Create();
        var line = await db.BankStatementLines.SingleOrDefaultAsync(l => l.Id == statementLineId) ?? throw new BusinessRuleException("That statement line no longer exists.");
        line.Status = StatementLineStatus.Unmatched;
        line.JournalLineId = null;
        line.MatchedBy = null;
        line.MatchedAtUtc = null;
        await db.SaveChangesAsync();
    }

    public async Task IgnoreAsync(Guid statementLineId)
    {
        await using var db = data.Create();
        var line = await db.BankStatementLines.SingleOrDefaultAsync(l => l.Id == statementLineId) ?? throw new BusinessRuleException("That statement line no longer exists.");
        if (line.Status != StatementLineStatus.Unmatched) throw new BusinessRuleException("That statement line is matched already.");
        line.Status = StatementLineStatus.Ignored;
        line.MatchedBy = FinanceNumbers.Cut(data.CurrentUser?.UserName, 50);
        line.MatchedAtUtc = SyncColumns.Now();
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Turns an unmatched statement line into an entry (bank charges, profit paid by the bank) against the account given,
    /// or the bank charges account for money out and other income for money in, and matches the line to it.
    /// </summary>
    public async Task<JournalEntry> CreateEntryAsync(Guid statementLineId, Guid? accountId, string? memo, Guid? store = null)
    {
        BankStatementLine line;
        BankAccount bank;
        string? code = null;
        await using (var db = data.Create())
        {
            line = await db.BankStatementLines.AsNoTracking().SingleOrDefaultAsync(l => l.Id == statementLineId) ?? throw new BusinessRuleException("That statement line no longer exists.");
            if (line.Status != StatementLineStatus.Unmatched) throw new BusinessRuleException("That statement line is matched already.");
            bank = await db.BankAccounts.AsNoTracking().Include(b => b.LedgerAccount).SingleAsync(b => b.Id == line.BankAccountId);
            if (bank.Currency != await FinanceNumbers.BaseCurrencyAsync(db)) throw new BusinessRuleException("Entries for an account in another currency are made by hand.");
            if (accountId is Guid a) code = await db.LedgerAccounts.AsNoTracking().Where(x => x.Id == a).Select(x => x.Code).SingleOrDefaultAsync() ?? throw new BusinessRuleException("That account no longer exists.");
        }
        decimal amount = Math.Abs(line.Amount);
        var text = FinanceNumbers.Cut(string.IsNullOrWhiteSpace(memo) ? line.Description ?? (line.Amount < 0 ? "Bank charges" : "Bank profit") : memo.Trim(), 200)!;
        var storeId = store ?? bank.StoreId;
        var other = code is not null ? JournalDraftLine.Code(code, 0, 0, storeId, text)
            : JournalDraftLine.Role(line.Amount < 0 ? LedgerRoles.BankCharges : LedgerRoles.OtherIncome, 0, 0, storeId, text);
        var bankLine = JournalDraftLine.Code(bank.LedgerAccount!.Code, 0, 0, storeId, text);
        var lines = line.Amount > 0
            ? new List<JournalDraftLine> { bankLine with { Debit = amount }, other with { Credit = amount } }
            : [other with { Debit = amount }, bankLine with { Credit = amount }];
        var entry = await Ledger.PostAsync(new JournalDraft(line.Date, JournalKind.Bank, text, lines)
        {
            Source = $"bank-line:{line.Id:N}", StoreId = storeId, Reference = FinanceNumbers.Cut(line.Reference, 40),
        });
        await using (var db = data.Create())
        {
            var journalLine = await db.JournalLines.AsNoTracking().Where(l => l.JournalEntryId == entry.Id && l.AccountId == bank.LedgerAccountId).Select(l => l.Id).FirstAsync();
            var row = await db.BankStatementLines.SingleAsync(l => l.Id == statementLineId);
            Match(row, journalLine, StatementLineStatus.Entered);
            await db.SaveChangesAsync();
        }
        return entry;
    }

    /// <summary>The reconciliation of an account on <paramref name="asOf"/> (see <see cref="ReconciliationSummary"/>).</summary>
    public async Task<ReconciliationSummary> SummaryAsync(Guid bankAccountId, DateOnly asOf)
    {
        var ledger = await LedgerLinesAsync(bankAccountId, to: asOf);
        var statement = await StatementLinesAsync(bankAccountId, to: asOf);
        decimal? balance = statement.LastOrDefault(l => l.Balance is not null)?.Balance;
        await using (var db = data.Create())
        {
            var last = await db.BankStatements.AsNoTracking().Where(s => s.BankAccountId == bankAccountId && s.ToDate <= asOf && s.ClosingBalance != null)
                .OrderByDescending(s => s.ToDate).ThenByDescending(s => s.CreatedAtUtc).FirstOrDefaultAsync();
            if (balance is null && last is not null) balance = last.ClosingBalance;
        }
        var unmatched = statement.Where(l => l.Status == StatementLineStatus.Unmatched).ToList();
        return new ReconciliationSummary(bankAccountId, asOf, balance, ledger.Sum(l => l.Amount), ledger.Where(l => !l.Matched).Sum(l => l.Amount),
            unmatched.Sum(l => l.Amount), unmatched.Count);
    }

    private void Match(BankStatementLine line, Guid journalLineId, StatementLineStatus status)
    {
        line.Status = status;
        line.JournalLineId = journalLineId;
        line.MatchedBy = FinanceNumbers.Cut(data.CurrentUser?.UserName, 50);
        line.MatchedAtUtc = SyncColumns.Now();
    }

    private async Task<string> CodeAsync(Guid ledgerAccountId)
    {
        await using var db = data.Create();
        return await db.LedgerAccounts.AsNoTracking().Where(a => a.Id == ledgerAccountId).Select(a => a.Code).SingleAsync();
    }

    private async Task<bool> SourceExistsAsync(string source)
    {
        await using var db = data.Create();
        return await db.JournalEntries.AnyAsync(e => e.Source == source);
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
