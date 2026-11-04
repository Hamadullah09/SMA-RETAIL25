using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using JournalLine = RetailPlus.Core.Model.JournalLine;

namespace RetailPlus.Data.Services;

/// <summary>
/// A line of an entry to post: an account by its code or by its role (<see cref="LedgerRoles"/>), and a debit or a credit.
/// </summary>
public sealed record JournalDraftLine(string? AccountCode, string? AccountRole, decimal Debit, decimal Credit, Guid? StoreId = null,
    string? Memo = null, LedgerPartyKind? PartyKind = null, Guid? PartyId = null)
{
    public static JournalDraftLine Code(string code, decimal debit, decimal credit, Guid? store = null, string? memo = null,
        LedgerPartyKind? partyKind = null, Guid? partyId = null) => new(code, null, debit, credit, store, memo, partyKind, partyId);

    public static JournalDraftLine Role(string role, decimal debit, decimal credit, Guid? store = null, string? memo = null,
        LedgerPartyKind? partyKind = null, Guid? partyId = null) => new(null, role, debit, credit, store, memo, partyKind, partyId);
}

/// <summary>
/// An entry to post. Give a <see cref="Source"/> for anything the program posts (a supplier bill, a payroll run): posting
/// the same source again replaces the entry while its period is open, and posts only the difference once it is locked.
/// </summary>
public sealed record JournalDraft(DateOnly Date, JournalKind Kind, string Memo, IReadOnlyList<JournalDraftLine> Lines)
{
    public string? Source { get; init; }
    public Guid? StoreId { get; init; }
    public string? Reference { get; init; }
}

/// <summary>An account's movement over a period: what it held before, the debits and credits, and what it holds after.</summary>
public sealed record AccountBalance(Guid AccountId, string Code, string Name, AccountType Type, decimal Opening, decimal Debit, decimal Credit)
{
    /// <summary>Debit balance positive, credit balance negative.</summary>
    public decimal Closing => Opening + Debit - Credit;
}

/// <summary>One line of an account's ledger, with the balance after it (debit positive).</summary>
public sealed record LedgerRow(Guid EntryId, long Number, DateOnly Date, JournalKind Kind, string Memo, string? Reference, Guid? StoreId,
    decimal Debit, decimal Credit, decimal Balance, string? LineMemo);

/// <summary>
/// The general ledger: the chart of accounts, journal entries, financial years and their periods, and balances. Every
/// module that moves money posts here (<see cref="PostAsync"/>); nothing else writes journal entries. The ledger is kept
/// on the server; entries are numbered from the server's own range (<see cref="LedgerStation"/>).
/// </summary>
public sealed class LedgerService(DataContext data)
{
    /// <summary>The station range journal entries are numbered from: the server's, which no till has.</summary>
    public const int LedgerStation = 1000;

    /// <summary>The standard chart a new ledger starts with: code, name, type, role, and the header account it is under.</summary>
    public static IReadOnlyList<(string Code, string Name, AccountType Type, string? Role, string? Parent)> StandardChart { get; } =
    [
        ("1000", "Assets", AccountType.Asset, null, null),
        ("1010", "Cash in hand", AccountType.Asset, LedgerRoles.Cash, "1000"),
        ("1020", "Card and wallet payments to receive", AccountType.Asset, null, "1000"),
        ("1030", "Cheques to deposit", AccountType.Asset, null, "1000"),
        ("1050", "Bank", AccountType.Asset, LedgerRoles.Bank, "1000"),
        ("1100", "Accounts receivable", AccountType.Asset, LedgerRoles.AccountsReceivable, "1000"),
        ("1150", "Advances to employees", AccountType.Asset, LedgerRoles.EmployeeAdvances, "1000"),
        ("1200", "Inventory", AccountType.Asset, LedgerRoles.Inventory, "1000"),
        ("1210", "Inventory in transit", AccountType.Asset, LedgerRoles.InventoryInTransit, "1000"),
        ("1220", "Work in progress", AccountType.Asset, LedgerRoles.WorkInProgress, "1000"),
        ("1300", "Input tax to recover", AccountType.Asset, LedgerRoles.InputTax, "1000"),
        ("2000", "Liabilities", AccountType.Liability, null, null),
        ("2010", "Accounts payable", AccountType.Liability, LedgerRoles.AccountsPayable, "2000"),
        ("2020", "Goods received, not billed", AccountType.Liability, LedgerRoles.PurchasesClearing, "2000"),
        ("2030", "Landed costs to allocate", AccountType.Liability, LedgerRoles.LandedCostsClearing, "2000"),
        ("2100", "Sales tax payable", AccountType.Liability, LedgerRoles.SalesTaxPayable, "2000"),
        ("2110", "Second sales tax payable", AccountType.Liability, null, "2000"),
        ("2120", "Withholding tax payable", AccountType.Liability, LedgerRoles.WithholdingTaxPayable, "2000"),
        ("2200", "Gift cards outstanding", AccountType.Liability, LedgerRoles.GiftCardLiability, "2000"),
        ("2300", "Salaries payable", AccountType.Liability, LedgerRoles.SalariesPayable, "2000"),
        ("2310", "Income tax withheld", AccountType.Liability, LedgerRoles.PayrollTaxPayable, "2000"),
        ("2320", "Social security and pension contributions payable", AccountType.Liability, LedgerRoles.StatutoryContributionsPayable, "2000"),
        ("3000", "Equity", AccountType.Equity, null, null),
        ("3010", "Owner's capital", AccountType.Equity, null, "3000"),
        ("3100", "Retained earnings", AccountType.Equity, LedgerRoles.RetainedEarnings, "3000"),
        ("4000", "Income", AccountType.Income, null, null),
        ("4010", "Sales", AccountType.Income, LedgerRoles.Sales, "4000"),
        ("4020", "Discounts given", AccountType.Income, LedgerRoles.Discounts, "4000"),
        ("4900", "Other income", AccountType.Income, LedgerRoles.OtherIncome, "4000"),
        ("4950", "Cash rounding", AccountType.Income, LedgerRoles.Rounding, "4000"),
        ("5000", "Cost of sales", AccountType.Expense, null, null),
        ("5010", "Cost of goods sold", AccountType.Expense, LedgerRoles.CostOfGoods, "5000"),
        ("5020", "Stock found and lost", AccountType.Expense, LedgerRoles.StockAdjustments, "5000"),
        ("5030", "Gift card corrections", AccountType.Expense, LedgerRoles.GiftCardAdjustments, "5000"),
        ("5040", "Production variances", AccountType.Expense, LedgerRoles.ProductionVariance, "5000"),
        ("5050", "Manufacturing overhead", AccountType.Expense, LedgerRoles.ManufacturingOverhead, "5000"),
        ("6000", "Expenses", AccountType.Expense, null, null),
        ("6010", "Salaries and wages", AccountType.Expense, LedgerRoles.SalariesExpense, "6000"),
        ("6020", "Employer contributions", AccountType.Expense, LedgerRoles.EmployerContributionsExpense, "6000"),
        ("6100", "Rent", AccountType.Expense, null, "6000"),
        ("6110", "Electricity, gas and water", AccountType.Expense, null, "6000"),
        ("6120", "Telephone and internet", AccountType.Expense, null, "6000"),
        ("6200", "Repairs and maintenance", AccountType.Expense, null, "6000"),
        ("6300", "Marketing", AccountType.Expense, null, "6000"),
        ("6400", "Bank charges", AccountType.Expense, LedgerRoles.BankCharges, "6000"),
        ("6500", "Exchange differences", AccountType.Expense, LedgerRoles.ExchangeDifferences, "6000"),
        ("6900", "General expenses", AccountType.Expense, LedgerRoles.GeneralExpenses, "6000"),
        ("9999", "Suspense (to sort out)", AccountType.Asset, LedgerRoles.Suspense, null),
    ];

    /// <summary>The standard account each kind of the daily journal's mapping (Setup › Accounting) starts with.</summary>
    private static readonly Dictionary<AccountMappingKind, string> StandardMappings = new()
    {
        [AccountMappingKind.Sales] = "4010",
        [AccountMappingKind.Tax1] = "2100",
        [AccountMappingKind.Tax2] = "2110",
        [AccountMappingKind.GiftCardLiability] = "2200",
        [AccountMappingKind.CostOfGoods] = "5010",
        [AccountMappingKind.Inventory] = "1200",
        [AccountMappingKind.AccountsReceivable] = "1100",
        [AccountMappingKind.Discounts] = "4020",
        [AccountMappingKind.Rounding] = "4950",
        [AccountMappingKind.StockAdjustments] = "5020",
        [AccountMappingKind.PurchasesClearing] = "2020",
        [AccountMappingKind.InventoryInTransit] = "1210",
        [AccountMappingKind.GiftCardAdjustments] = "5030",
    };

    private static readonly Dictionary<TenderKind, string> StandardTenderAccounts = new()
    {
        [TenderKind.Cash] = "1010",
        [TenderKind.Card] = "1020",
        [TenderKind.Cheque] = "1030",
    };

    // Settings and the chart of accounts ------------------------------------------------------------------------------

    public async Task<LedgerSettings> GetSettingsAsync()
    {
        await using var db = data.Create();
        return await db.LedgerSettings.AsNoTracking().FirstOrDefaultAsync() ?? new LedgerSettings();
    }

    public async Task SaveSettingsAsync(LedgerSettings settings)
    {
        if (settings.FiscalYearStartMonth is < 1 or > 12) throw new BusinessRuleException("A financial year starts in a month from 1 to 12.");
        if (settings.BaseCurrency.Trim().Length != 3) throw new BusinessRuleException("The currency is a three-letter code, such as PKR.");
        if (settings.Country.Trim().Length != 2) throw new BusinessRuleException("The country is a two-letter code, such as PK.");
        await using var db = data.Create();
        var row = await db.LedgerSettings.FirstOrDefaultAsync();
        if (row is null) db.LedgerSettings.Add(row = new LedgerSettings());
        row.BaseCurrency = settings.BaseCurrency.Trim().ToUpperInvariant();
        row.Country = settings.Country.Trim().ToUpperInvariant();
        row.FiscalYearStartMonth = settings.FiscalYearStartMonth;
        row.ManualEntriesNeedApproval = settings.ManualEntriesNeedApproval;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Gives a new ledger its settings and the standard chart of accounts, and each store the standard account of every
    /// daily-journal mapping it has not set. Running it again changes nothing that is already there.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        if (!await db.LedgerSettings.AnyAsync(cancellationToken)) db.LedgerSettings.Add(new LedgerSettings());
        if (!await db.LedgerAccounts.AnyAsync(cancellationToken))
        {
            var added = new Dictionary<string, LedgerAccount>(StringComparer.Ordinal);
            foreach (var (code, name, type, role, parent) in StandardChart)
            {
                var account = new LedgerAccount
                {
                    Code = code, Name = name, Type = type, Role = role, IsPosting = parent is not null || role is not null,
                    ParentId = parent is null ? null : added[parent].Id,
                };
                account.Id = Guid.CreateVersion7();
                added[code] = account;
                db.LedgerAccounts.Add(account);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await AddMissingRolesAsync(db, cancellationToken);

        var accounts = await db.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Code, a => a.Name, cancellationToken);
        var mapped = (await db.AccountMappings.AsNoTracking().Select(m => new { m.StoreId, m.Kind, m.Key }).ToListAsync(cancellationToken))
            .Select(m => (m.StoreId, m.Kind, m.Key.ToUpperInvariant())).ToHashSet();
        var tenders = await db.TenderTypes.AsNoTracking().Select(t => new { t.Name, t.Kind }).ToListAsync(cancellationToken);
        foreach (var store in await db.Stores.AsNoTracking().Select(s => s.Id).ToListAsync(cancellationToken))
        {
            void Map(AccountMappingKind kind, string key, string code)
            {
                if (!accounts.TryGetValue(code, out var name) || !mapped.Add((store, kind, key.ToUpperInvariant()))) return;
                db.AccountMappings.Add(new AccountMapping { StoreId = store, Kind = kind, Key = key, AccountCode = code, AccountName = name.Length > 60 ? name[..60] : name });
            }
            foreach (var (kind, code) in StandardMappings) Map(kind, "", code);
            foreach (var tender in tenders)
                if (StandardTenderAccounts.TryGetValue(tender.Kind, out var code)) Map(AccountMappingKind.Tender, tender.Name, code);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A chart made before a role was added to the standard chart gets it: the standard account with that code takes the
    /// role when it has none, else the standard account is added (under its standard header when that is still a header).
    /// </summary>
    private static async Task AddMissingRolesAsync(RetailPlusDbContext db, CancellationToken cancellationToken)
    {
        var accounts = await db.LedgerAccounts.ToListAsync(cancellationToken);
        var roles = accounts.Where(a => a.Role is not null).Select(a => a.Role!).ToHashSet(StringComparer.Ordinal);
        bool changed = false;
        foreach (var (code, name, type, role, parent) in StandardChart)
        {
            if (role is null || roles.Contains(role)) continue;
            var same = accounts.FirstOrDefault(a => a.Code == code);
            if (same is not null)
            {
                if (same.Role is not null || !same.IsPosting) continue;
                same.Role = role;
            }
            else
            {
                var header = parent is null ? null : accounts.FirstOrDefault(a => a.Code == parent && !a.IsPosting);
                var account = new LedgerAccount { Code = code, Name = name, Type = type, Role = role, IsPosting = true, ParentId = header?.Id };
                account.Id = Guid.CreateVersion7();
                db.LedgerAccounts.Add(account);
                accounts.Add(account);
            }
            roles.Add(role);
            changed = true;
        }
        if (changed) await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<LedgerAccount>> GetAccountsAsync()
    {
        await using var db = data.Create();
        return await db.LedgerAccounts.AsNoTracking().OrderBy(a => a.Code).ToListAsync();
    }

    /// <summary>The account with the given role (<see cref="LedgerRoles"/>), or null when the chart has none.</summary>
    public async Task<LedgerAccount?> AccountForRoleAsync(string role)
    {
        await using var db = data.Create();
        return await db.LedgerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Role == role);
    }

    /// <summary>Adds or changes an account of the chart.</summary>
    public async Task<LedgerAccount> SaveAccountAsync(LedgerAccount account)
    {
        var code = account.Code.Trim();
        var name = account.Name.Trim();
        if (code.Length is 0 or > 20) throw new BusinessRuleException("An account needs a code of up to 20 characters.");
        if (name.Length is 0 or > 100) throw new BusinessRuleException("An account needs a name of up to 100 characters.");
        if (account.Role is { } role && !LedgerRoles.All.Contains(role)) throw new BusinessRuleException($"{role} is not an account role the program knows.");
        if (account.Role is not null && !account.IsActive) throw new BusinessRuleException($"The program posts to its {account.Role} account, so it stays active; give the role to another account first.");
        await using var db = data.Create();
        if (await db.LedgerAccounts.AnyAsync(a => a.Code == code && a.Id != account.Id)) throw new BusinessRuleException($"Account code {code} is taken.");
        if (account.Role is not null && await db.LedgerAccounts.AnyAsync(a => a.Role == account.Role && a.Id != account.Id))
            throw new BusinessRuleException($"Another account already has the role {account.Role}.");
        if (account.ParentId is Guid parentId)
        {
            if (parentId == account.Id) throw new BusinessRuleException("An account cannot be under itself.");
            var parent = await db.LedgerAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == parentId)
                ?? throw new BusinessRuleException("The account it should go under no longer exists.");
            if (parent.IsPosting) throw new BusinessRuleException($"{parent.Code} {parent.Name} is posted to, so no account can go under it.");
        }
        var row = account.Id == Guid.Empty ? null : await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == account.Id);
        if (row is null)
        {
            db.LedgerAccounts.Add(row = new LedgerAccount());
        }
        else
        {
            bool posted = await db.JournalLines.AnyAsync(l => l.AccountId == row.Id);
            if (!account.IsPosting && posted) throw new BusinessRuleException($"{row.Code} has postings, so it cannot become a header account.");
            if (account.Type != row.Type && posted) throw new BusinessRuleException($"{row.Code} has postings, so its type cannot change.");
            if (account.IsPosting && await db.LedgerAccounts.AnyAsync(a => a.ParentId == row.Id))
                throw new BusinessRuleException($"{row.Code} has accounts under it, so it must stay a header account.");
        }
        row.Code = code;
        row.Name = name;
        row.Type = account.Type;
        row.ParentId = account.ParentId;
        row.IsPosting = account.IsPosting;
        row.IsActive = account.IsActive;
        row.Role = account.Role;
        row.Description = string.IsNullOrWhiteSpace(account.Description) ? null : account.Description.Trim();
        row.CashFlow = account.CashFlow;
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Deletes an account that was never posted to and has nothing under it.</summary>
    public async Task DeleteAccountAsync(Guid accountId)
    {
        await using var db = data.Create();
        var row = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == accountId) ?? throw new BusinessRuleException("That account no longer exists.");
        if (await db.JournalLines.AnyAsync(l => l.AccountId == accountId)) throw new BusinessRuleException($"{row.Code} has postings; make it inactive instead.");
        if (await db.LedgerAccounts.AnyAsync(a => a.ParentId == accountId)) throw new BusinessRuleException($"{row.Code} has accounts under it.");
        if (row.Role is not null) throw new BusinessRuleException($"{row.Code} is the program's {row.Role} account; give the role to another account first.");
        db.LedgerAccounts.Remove(row);
        await db.SaveChangesAsync();
    }

    // Posting ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Posts an entry. With a <see cref="JournalDraft.Source"/> that was posted before, the entry is replaced (same number)
    /// while both dates are in open periods; once the old one is in a locked or closed period, only the difference is
    /// posted, as an adjustment dated in the first open period. Returns the entry that holds the posting (the adjustment,
    /// or the existing entry when nothing changed).
    /// </summary>
    public async Task<JournalEntry> PostAsync(JournalDraft draft, CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var lines = await ResolveAsync(db, draft, cancellationToken);
        await EnsureYearAsync(db, draft.Date, cancellationToken);

        JournalEntry? existing = draft.Source is null ? null
            : await db.JournalEntries.Include(e => e.Lines).SingleOrDefaultAsync(e => e.Source == draft.Source, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == JournalStatus.Draft)
                throw new BusinessRuleException($"{draft.Source} is a draft waiting for approval; approve or delete it first.");
            if (existing.Status == JournalStatus.Reversed)
                throw new BusinessRuleException($"The entry for {draft.Source} was reversed; post it under another source.");
            bool open = await IsOpenAsync(db, existing.Date, cancellationToken) && await IsOpenAsync(db, draft.Date, cancellationToken);
            if (open)
            {
                if (existing.Date == draft.Date && SameLines(existing.Lines, lines)) return existing;
                db.JournalLines.RemoveRange(existing.Lines);
                existing.Lines.Clear();
                existing.Date = draft.Date;
                existing.Kind = draft.Kind;
                existing.Memo = Cut(draft.Memo, 200)!;
                existing.Reference = Cut(draft.Reference, 40);
                existing.StoreId = draft.StoreId;
                AddLines(existing, lines);
                Stamp(existing);
                db.Audit(AuditActions.JournalPosted, $"#{existing.Number} {existing.Memo}: posted again, {Total(lines):N2}", nameof(JournalEntry), existing.Id);
                await db.SaveChangesAsync(cancellationToken);
                return existing;
            }
            return await PostDifferenceAsync(db, draft, lines, cancellationToken) ?? existing;
        }

        if (!await IsOpenAsync(db, draft.Date, cancellationToken))
            throw new BusinessRuleException($"{draft.Date:d} is in a locked or closed period; open it or post on a later date.");
        var entry = NewEntry(draft, JournalStatus.Posted);
        AddLines(entry, lines);
        entry.Number = await NextNumberAsync(cancellationToken);
        Stamp(entry);
        db.JournalEntries.Add(entry);
        db.Audit(AuditActions.JournalPosted, $"#{entry.Number} {entry.Memo}: {Total(lines):N2}", nameof(JournalEntry), entry.Id);
        await db.SaveChangesAsync(cancellationToken);
        return entry;
    }

    /// <summary>The adjustment for a source whose entry is in a locked period: what the draft adds to everything posted for it so far.</summary>
    private async Task<JournalEntry?> PostDifferenceAsync(RetailPlusDbContext db, JournalDraft draft, List<JournalLine> lines, CancellationToken cancellationToken)
    {
        string source = draft.Source!;
        string prefix = source + "#adj";
        var posted = await db.JournalEntries.AsNoTracking().Include(e => e.Lines)
            .Where(e => e.Status != JournalStatus.Draft && (e.Source == source || e.Source!.StartsWith(prefix)))
            .ToListAsync(cancellationToken);
        var net = new Dictionary<(Guid Account, Guid? Store, LedgerPartyKind? Kind, Guid? Party), decimal>();
        foreach (var l in posted.SelectMany(e => e.Lines)) Add(l, -1);
        foreach (var l in lines) Add(l, 1);
        void Add(JournalLine l, int sign)
        {
            var key = (l.AccountId, l.StoreId, l.PartyKind, l.PartyId);
            net[key] = net.GetValueOrDefault(key) + sign * (l.Debit - l.Credit);
        }
        var difference = net.Where(n => n.Value != 0).ToList();
        if (difference.Count == 0) return null;

        var date = await FirstOpenDateAsync(db, draft.Date, cancellationToken);
        var entry = NewEntry(draft with { Date = date, Kind = JournalKind.Adjustment, Memo = Cut("Change to " + draft.Memo, 200)! }, JournalStatus.Posted);
        entry.Source = $"{prefix}{posted.Count(e => e.Source != source) + 1}";
        int n = 0;
        foreach (var (key, value) in difference)
            entry.Lines.Add(new JournalLine
            {
                LineNumber = ++n, AccountId = key.Account, StoreId = key.Store, PartyKind = key.Kind, PartyId = key.Party,
                Debit = value > 0 ? value : 0, Credit = value < 0 ? -value : 0,
                Memo = Cut($"Change to {draft.Date:d}", 200),
            });
        entry.Number = await NextNumberAsync(cancellationToken);
        Stamp(entry);
        db.JournalEntries.Add(entry);
        db.Audit(AuditActions.JournalPosted, $"#{entry.Number} {entry.Memo}", nameof(JournalEntry), entry.Id);
        await db.SaveChangesAsync(cancellationToken);
        return entry;
    }

    /// <summary>Saves an entry made by hand, to be approved (and so posted) by <see cref="ApproveAsync"/>.</summary>
    public async Task<JournalEntry> SaveDraftAsync(JournalDraft draft, Guid? draftId = null)
    {
        if (draft.Source is not null) throw new BusinessRuleException("Entries the program posts are posted directly, not as drafts.");
        await using var db = data.Create();
        var lines = await ResolveAsync(db, draft, CancellationToken.None);
        JournalEntry entry;
        if (draftId is Guid id)
        {
            entry = await db.JournalEntries.Include(e => e.Lines).SingleOrDefaultAsync(e => e.Id == id) ?? throw new BusinessRuleException("That draft no longer exists.");
            if (entry.Status != JournalStatus.Draft) throw new BusinessRuleException($"Entry #{entry.Number} is already posted.");
            db.JournalLines.RemoveRange(entry.Lines);
            entry.Lines.Clear();
            entry.Date = draft.Date;
            entry.Memo = Cut(draft.Memo, 200)!;
            entry.Reference = Cut(draft.Reference, 40);
            entry.StoreId = draft.StoreId;
        }
        else
        {
            entry = NewEntry(draft, JournalStatus.Draft);
            entry.CreatedBy = Cut(data.CurrentUser?.UserName, 50);
            entry.CreatedById = data.CurrentUser?.UserId;
            db.JournalEntries.Add(entry);
        }
        AddLines(entry, lines);
        await db.SaveChangesAsync();
        return entry;
    }

    /// <summary>
    /// Posts a draft. When the ledger settings ask for it, the person approving must not be the one who made it.
    /// </summary>
    public async Task<JournalEntry> ApproveAsync(Guid draftId)
    {
        await using var db = data.Create();
        var entry = await db.JournalEntries.Include(e => e.Lines).SingleOrDefaultAsync(e => e.Id == draftId) ?? throw new BusinessRuleException("That draft no longer exists.");
        if (entry.Status != JournalStatus.Draft) throw new BusinessRuleException($"Entry #{entry.Number} is already posted.");
        var settings = await db.LedgerSettings.AsNoTracking().FirstOrDefaultAsync() ?? new LedgerSettings();
        if (settings.ManualEntriesNeedApproval && entry.CreatedById is Guid maker && maker == data.CurrentUser?.UserId)
            throw new BusinessRuleException("Someone else must approve an entry you made.");
        await EnsureYearAsync(db, entry.Date, CancellationToken.None);
        if (!await IsOpenAsync(db, entry.Date, CancellationToken.None))
            throw new BusinessRuleException($"{entry.Date:d} is in a locked or closed period; change the date or open the period.");
        entry.Status = JournalStatus.Posted;
        entry.Number = await NextNumberAsync(CancellationToken.None);
        Stamp(entry);
        db.Audit(AuditActions.JournalPosted, $"#{entry.Number} {entry.Memo}: approved, {entry.Lines.Sum(l => l.Debit):N2}", nameof(JournalEntry), entry.Id);
        await db.SaveChangesAsync();
        return entry;
    }

    public async Task DeleteDraftAsync(Guid draftId)
    {
        await using var db = data.Create();
        var entry = await db.JournalEntries.Include(e => e.Lines).SingleOrDefaultAsync(e => e.Id == draftId) ?? throw new BusinessRuleException("That draft no longer exists.");
        if (entry.Status != JournalStatus.Draft) throw new BusinessRuleException($"Entry #{entry.Number} is posted; reverse it instead.");
        db.JournalEntries.Remove(entry);
        await db.SaveChangesAsync();
    }

    /// <summary>Cancels a posted entry with a mirror entry on <paramref name="date"/> (which must be in an open period).</summary>
    public async Task<JournalEntry> ReverseAsync(Guid entryId, DateOnly date, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Say why the entry is reversed.");
        await using var db = data.Create();
        var entry = await db.JournalEntries.Include(e => e.Lines).SingleOrDefaultAsync(e => e.Id == entryId) ?? throw new BusinessRuleException("That entry no longer exists.");
        if (entry.Status != JournalStatus.Posted) throw new BusinessRuleException(entry.Status == JournalStatus.Draft ? "A draft is deleted, not reversed." : $"Entry #{entry.Number} is already reversed.");
        if (entry.Kind == JournalKind.Reversal) throw new BusinessRuleException("A reversing entry cannot itself be reversed.");
        await EnsureYearAsync(db, date, CancellationToken.None);
        if (!await IsOpenAsync(db, date, CancellationToken.None)) throw new BusinessRuleException($"{date:d} is in a locked or closed period.");
        var reversal = new JournalEntry
        {
            Date = date, Kind = JournalKind.Reversal, Status = JournalStatus.Posted, StoreId = entry.StoreId, Reference = entry.Reference,
            Memo = Cut($"Reverses #{entry.Number}: {reason.Trim()}", 200)!, ReversalOfId = entry.Id,
        };
        reversal.Id = Guid.CreateVersion7();
        foreach (var l in entry.Lines.OrderBy(l => l.LineNumber))
            reversal.Lines.Add(new JournalLine
            {
                LineNumber = l.LineNumber, AccountId = l.AccountId, StoreId = l.StoreId, Debit = l.Credit, Credit = l.Debit, Memo = l.Memo,
                PartyKind = l.PartyKind, PartyId = l.PartyId,
            });
        reversal.Number = await NextNumberAsync(CancellationToken.None);
        Stamp(reversal);
        db.JournalEntries.Add(reversal);
        entry.Status = JournalStatus.Reversed;
        entry.ReversedById = reversal.Id;
        db.Audit(AuditActions.JournalReversed, $"#{entry.Number} reversed by #{reversal.Number}: {reason.Trim()}", nameof(JournalEntry), entry.Id);
        await db.SaveChangesAsync();
        return reversal;
    }

    public async Task<JournalEntry?> GetEntryAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.JournalEntries.AsNoTracking().Include(e => e.Lines).ThenInclude(l => l.Account).SingleOrDefaultAsync(e => e.Id == id);
    }

    /// <summary>
    /// Posts each store's business days from <paramref name="from"/> to <paramref name="to"/>: the daily journal of Setup ›
    /// Accounting (sales, payments on account, stock movements, gift cards) as one entry per store and day. Amounts whose
    /// mapping has no account in the chart go to the suspense account. Posting the same days again changes only what
    /// changed (late packages from the tills). Returns the number of entries posted or changed.
    /// </summary>
    public async Task<int> PostDailyAsync(DateOnly from, DateOnly to, Guid? store = null, CancellationToken cancellationToken = default)
    {
        var scope = data.For(store is Guid s ? new StationIdentity(s, 0) : null, data.CurrentUser);
        var export = await new AccountingService(scope).ExportAsync(from, to);
        Dictionary<string, Guid> stores;
        HashSet<string> codes;
        await using (var db = data.Create())
        {
            stores = await db.Stores.AsNoTracking().ToDictionaryAsync(x => x.Code, x => x.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
            codes = (await db.LedgerAccounts.AsNoTracking().Where(a => a.IsPosting && a.IsActive).Select(a => a.Code).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
        }
        int posted = 0;
        foreach (var day in export.Lines.GroupBy(l => (l.Store, l.Date)))
        {
            if (!stores.TryGetValue(day.Key.Store, out var storeId)) continue;
            var lines = day.Select(l => codes.Contains(l.Account)
                    ? JournalDraftLine.Code(l.Account, l.Debit, l.Credit, storeId, l.Memo)
                    : JournalDraftLine.Role(LedgerRoles.Suspense, l.Debit, l.Credit, storeId, Cut($"{l.Memo} (no account {l.Account} {l.AccountName})".Trim(), 200)))
                .ToList();
            var draft = new JournalDraft(day.Key.Date, JournalKind.Daily, $"Business day {day.Key.Date.ToString("d", CultureInfo.CurrentCulture)}, store {day.Key.Store}", lines)
            {
                Source = $"daily:{storeId:N}:{day.Key.Date:yyyy-MM-dd}", StoreId = storeId, Reference = $"{day.Key.Store}-{day.Key.Date:yyyyMMdd}",
            };
            var before = await SourceStampAsync(draft.Source, cancellationToken);
            var entry = await PostAsync(draft, cancellationToken);
            if (before is null || before != entry.UpdatedAtUtc || entry.Source != draft.Source) posted++;
        }
        return posted;
    }

    private async Task<DateTime?> SourceStampAsync(string? source, CancellationToken cancellationToken)
    {
        await using var db = data.Create();
        return await db.JournalEntries.AsNoTracking().Where(e => e.Source == source).Select(e => (DateTime?)e.UpdatedAtUtc).SingleOrDefaultAsync(cancellationToken);
    }

    // Financial years and periods -------------------------------------------------------------------------------------

    public async Task<List<FiscalYear>> GetYearsAsync()
    {
        await using var db = data.Create();
        var years = await db.FiscalYears.AsNoTracking().Include(y => y.Periods).OrderBy(y => y.StartDate).ToListAsync();
        foreach (var y in years) y.Periods = [.. y.Periods.OrderBy(p => p.Number)];
        return years;
    }

    /// <summary>The financial year holding <paramref name="date"/>, made (with its twelve monthly periods) when there is none.</summary>
    public async Task<FiscalYear> EnsureYearAsync(DateOnly date)
    {
        await using var db = data.Create();
        var year = await EnsureYearAsync(db, date, CancellationToken.None);
        await db.SaveChangesAsync();
        return year;
    }

    public async Task<bool> IsOpenAsync(DateOnly date)
    {
        await using var db = data.Create();
        return await IsOpenAsync(db, date, CancellationToken.None);
    }

    /// <summary>Locks a period (no postings) or opens it again. Closing is done for the whole year.</summary>
    public async Task SetPeriodStatusAsync(Guid periodId, FiscalStatus status)
    {
        if (status == FiscalStatus.Closed) throw new BusinessRuleException("Periods are closed by closing their financial year.");
        await using var db = data.Create();
        var period = await db.FiscalPeriods.SingleOrDefaultAsync(p => p.Id == periodId) ?? throw new BusinessRuleException("That period no longer exists.");
        var year = await db.FiscalYears.AsNoTracking().SingleAsync(y => y.Id == period.FiscalYearId);
        if (year.Status == FiscalStatus.Closed) throw new BusinessRuleException($"{year.Name} is closed.");
        if (period.Status == status) return;
        period.Status = status;
        db.Audit(AuditActions.PeriodStatus, $"{period.Name}: {status}", nameof(FiscalPeriod), period.Id);
        await db.SaveChangesAsync();
    }

    private static async Task<FiscalYear> EnsureYearAsync(RetailPlusDbContext db, DateOnly date, CancellationToken cancellationToken)
    {
        var year = await db.FiscalYears.FirstOrDefaultAsync(y => y.StartDate <= date && y.EndDate >= date, cancellationToken)
            ?? db.FiscalYears.Local.FirstOrDefault(y => y.StartDate <= date && y.EndDate >= date);
        if (year is not null) return year;
        var settings = await db.LedgerSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new LedgerSettings();
        int month = settings.FiscalYearStartMonth;
        var start = new DateOnly(date.Month >= month ? date.Year : date.Year - 1, month, 1);
        // A year after a gap (or after the start month changed) begins the day after the year before it, so years never overlap.
        var previous = await db.FiscalYears.AsNoTracking().Where(y => y.EndDate < date).OrderByDescending(y => y.EndDate).FirstOrDefaultAsync(cancellationToken);
        if (previous is not null && previous.EndDate >= start) start = previous.EndDate.AddDays(1);
        var end = new DateOnly(start.Year, start.Month, 1).AddYears(1).AddDays(-1);
        var next = await db.FiscalYears.AsNoTracking().Where(y => y.StartDate > date).OrderBy(y => y.StartDate).FirstOrDefaultAsync(cancellationToken);
        if (next is not null && next.StartDate <= end) end = next.StartDate.AddDays(-1);
        year = new FiscalYear
        {
            StartDate = start, EndDate = end,
            Name = start.Month == 1 && end.Year == start.Year ? start.Year.ToString(CultureInfo.InvariantCulture) : $"{start.Year}-{end.Year % 100:00}",
        };
        year.Id = Guid.CreateVersion7();
        int number = 0;
        for (var p = start; p <= end; p = new DateOnly(p.Year, p.Month, 1).AddMonths(1))
        {
            var pEnd = new DateOnly(p.Year, p.Month, 1).AddMonths(1).AddDays(-1);
            if (pEnd > end) pEnd = end;
            year.Periods.Add(new FiscalPeriod
            {
                FiscalYearId = year.Id, Number = ++number, StartDate = p, EndDate = pEnd,
                Name = p.ToString("MMM yyyy", CultureInfo.InvariantCulture),
            });
        }
        db.FiscalYears.Add(year);
        return year;
    }

    private static async Task<bool> IsOpenAsync(RetailPlusDbContext db, DateOnly date, CancellationToken cancellationToken)
    {
        var status = await db.FiscalPeriods.AsNoTracking().Where(p => p.StartDate <= date && p.EndDate >= date).Select(p => (FiscalStatus?)p.Status)
            .FirstOrDefaultAsync(cancellationToken)
            ?? db.FiscalPeriods.Local.Where(p => p.StartDate <= date && p.EndDate >= date).Select(p => (FiscalStatus?)p.Status).FirstOrDefault();
        return status is null or FiscalStatus.Open;
    }

    /// <summary>The first date on or after <paramref name="date"/> that is in an open period (making next year's periods when needed).</summary>
    private static async Task<DateOnly> FirstOpenDateAsync(RetailPlusDbContext db, DateOnly date, CancellationToken cancellationToken)
    {
        var period = await db.FiscalPeriods.AsNoTracking().Where(p => p.Status == FiscalStatus.Open && p.EndDate >= date).OrderBy(p => p.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (period is not null) return period.StartDate > date ? period.StartDate : date;
        throw new BusinessRuleException($"There is no open period on or after {date:d}; open one first.");
    }

    // Reading ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Each account's balance before <paramref name="from"/> and its debits and credits up to <paramref name="to"/>, for one
    /// store or (null) the company. Only accounts with postings are listed. Drafts do not count.
    /// </summary>
    public Task<List<AccountBalance>> BalancesAsync(DateOnly from, DateOnly to, Guid? store = null) => BalancesAsync(from, to, store, exceptClosing: false);

    /// <summary>
    /// As <see cref="BalancesAsync(DateOnly, DateOnly, Guid?)"/>; with <paramref name="exceptClosing"/> the year-end closing
    /// entries are left out, so income and expenses show what the period earned even across a closed year's last day.
    /// </summary>
    public async Task<List<AccountBalance>> BalancesAsync(DateOnly from, DateOnly to, Guid? store, bool exceptClosing)
    {
        await using var db = data.Create();
        var entries = db.JournalEntries.AsNoTracking().Where(e => e.Status != JournalStatus.Draft && e.Date <= to);
        if (exceptClosing) entries = entries.Where(e => e.Kind != JournalKind.Closing);
        var lines = db.JournalLines.AsNoTracking()
            .Join(entries, l => l.JournalEntryId, e => e.Id,
                (l, e) => new { l.AccountId, l.StoreId, e.Date, l.Debit, l.Credit });
        if (store is Guid s) lines = lines.Where(l => l.StoreId == s);
        var rows = await lines.ToListAsync();
        var accounts = await db.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id);
        return rows.GroupBy(r => r.AccountId)
            .Select(g =>
            {
                var a = accounts[g.Key];
                return new AccountBalance(a.Id, a.Code, a.Name, a.Type,
                    g.Where(r => r.Date < from).Sum(r => r.Debit - r.Credit),
                    g.Where(r => r.Date >= from).Sum(r => r.Debit),
                    g.Where(r => r.Date >= from).Sum(r => r.Credit));
            })
            .OrderBy(b => b.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>The postings to one account from <paramref name="from"/> to <paramref name="to"/>, oldest first, with the running balance.</summary>
    public async Task<List<LedgerRow>> AccountLedgerAsync(Guid accountId, DateOnly from, DateOnly to, Guid? store = null)
    {
        await using var db = data.Create();
        var lines = db.JournalLines.AsNoTracking().Where(l => l.AccountId == accountId)
            .Join(db.JournalEntries.AsNoTracking().Where(e => e.Status != JournalStatus.Draft && e.Date <= to), l => l.JournalEntryId, e => e.Id,
                (l, e) => new { e.Id, e.Number, e.Date, e.Kind, e.Memo, e.Reference, l.StoreId, l.Debit, l.Credit, LineMemo = l.Memo, l.LineNumber });
        if (store is Guid s) lines = lines.Where(l => l.StoreId == s);
        var all = await lines.ToListAsync();
        decimal balance = all.Where(l => l.Date < from).Sum(l => l.Debit - l.Credit);
        var rows = new List<LedgerRow>();
        foreach (var l in all.Where(l => l.Date >= from).OrderBy(l => l.Date).ThenBy(l => l.Number).ThenBy(l => l.LineNumber))
        {
            balance += l.Debit - l.Credit;
            rows.Add(new LedgerRow(l.Id, l.Number, l.Date, l.Kind, l.Memo, l.Reference, l.StoreId, l.Debit, l.Credit, balance, l.LineMemo));
        }
        return rows;
    }

    // Helpers -----------------------------------------------------------------------------------------------------------

    /// <summary>Checks a draft and turns its lines into journal lines on real, open-for-posting accounts.</summary>
    private static async Task<List<JournalLine>> ResolveAsync(RetailPlusDbContext db, JournalDraft draft, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(draft.Memo)) throw new BusinessRuleException("An entry needs a description.");
        if (draft.Lines.Count < 2) throw new BusinessRuleException("An entry needs at least two lines.");
        var accounts = await db.LedgerAccounts.AsNoTracking().ToListAsync(cancellationToken);
        var byCode = accounts.ToDictionary(a => a.Code, StringComparer.Ordinal);
        var byRole = accounts.Where(a => a.Role is not null).ToDictionary(a => a.Role!, StringComparer.Ordinal);
        var lines = new List<JournalLine>();
        foreach (var d in draft.Lines)
        {
            decimal debit = Money.Round(d.Debit), credit = Money.Round(d.Credit);
            if (debit < 0 || credit < 0) throw new BusinessRuleException("Debits and credits are entered as positive amounts.");
            if ((debit == 0) == (credit == 0)) throw new BusinessRuleException("Each line is either a debit or a credit.");
            LedgerAccount account;
            if (d.AccountRole is { } role)
                account = byRole.GetValueOrDefault(role) ?? throw new BusinessRuleException($"No account has the role {role}; give it to one in the chart of accounts.");
            else
                account = byCode.GetValueOrDefault(d.AccountCode?.Trim() ?? "") ?? throw new BusinessRuleException($"There is no account {d.AccountCode}.");
            if (!account.IsPosting) throw new BusinessRuleException($"{account.Code} {account.Name} is a header account; post to an account under it.");
            if (!account.IsActive) throw new BusinessRuleException($"{account.Code} {account.Name} is inactive.");
            lines.Add(new JournalLine
            {
                AccountId = account.Id, StoreId = d.StoreId ?? draft.StoreId, Debit = debit, Credit = credit, Memo = Cut(d.Memo, 200),
                PartyKind = d.PartyKind, PartyId = d.PartyId,
            });
        }
        decimal debits = lines.Sum(l => l.Debit), credits = lines.Sum(l => l.Credit);
        if (debits != credits) throw new BusinessRuleException($"The entry does not balance: debits {debits:N2}, credits {credits:N2}.");
        return lines;
    }

    private static JournalEntry NewEntry(JournalDraft draft, JournalStatus status)
    {
        var entry = new JournalEntry
        {
            Date = draft.Date, Kind = draft.Kind, Status = status, Source = draft.Source, StoreId = draft.StoreId,
            Memo = Cut(draft.Memo.Trim(), 200)!, Reference = Cut(draft.Reference, 40),
        };
        entry.Id = Guid.CreateVersion7();
        return entry;
    }

    private static void AddLines(JournalEntry entry, List<JournalLine> lines)
    {
        int n = 0;
        foreach (var l in lines)
        {
            l.JournalEntryId = entry.Id;
            l.LineNumber = ++n;
            entry.Lines.Add(l);
        }
    }

    private void Stamp(JournalEntry entry)
    {
        entry.PostedAtUtc = DateTime.UtcNow;
        entry.PostedBy = Cut(data.CurrentUser?.UserName, 50);
        entry.PostedById = data.CurrentUser?.UserId;
    }

    private static bool SameLines(List<JournalLine> existing, List<JournalLine> lines) =>
        existing.Count == lines.Count && existing.OrderBy(l => l.LineNumber).Zip(lines).All(p =>
            p.First.AccountId == p.Second.AccountId && p.First.StoreId == p.Second.StoreId && p.First.Debit == p.Second.Debit
            && p.First.Credit == p.Second.Credit && p.First.Memo == p.Second.Memo && p.First.PartyKind == p.Second.PartyKind && p.First.PartyId == p.Second.PartyId);

    private static decimal Total(List<JournalLine> lines) => lines.Sum(l => l.Debit);

    /// <summary>The next entry number, from the server's range (<see cref="LedgerStation"/>).</summary>
    private async Task<long> NextNumberAsync(CancellationToken cancellationToken)
    {
        var store = data.Station?.StoreId ?? Guid.Empty;
        await using var db = data.For(new StationIdentity(store, LedgerStation), data.CurrentUser).Create();
        return await db.NextNumberAsync(RetailPlusDbContext.Sequences.JournalNumbers, cancellationToken);
    }

    private static string? Cut(string? text, int length) => text is null ? null : text.Length <= length ? text : text[..length];
}
