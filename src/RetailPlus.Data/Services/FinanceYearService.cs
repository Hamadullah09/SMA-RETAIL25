using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A line of opening balances: an account, the store it is for, and what it held (debit or credit).</summary>
public sealed record OpeningLine(Guid AccountId, decimal Debit, decimal Credit, Guid? StoreId = null, LedgerPartyKind? PartyKind = null, Guid? PartyId = null);

/// <summary>
/// The financial year's end: closing a year (its income and expenses moved to retained earnings by a closing entry, the
/// year and its periods closed, the next year opened), opening it again, and the opening balances of a company that starts
/// keeping its books here part way through a year.
/// </summary>
public sealed class FinanceYearService(DataContext data)
{
    private LedgerService Ledger => new(data);

    /// <summary>
    /// Closes a year: every income and expense account's movement in the year, per store, is moved to retained earnings by
    /// a closing entry on the year's last day; then the year and its periods are closed (no postings; changes to their days
    /// post as adjustments in the next open period) and the next year is made. Drafts dated in the year must go first.
    /// </summary>
    public async Task<FiscalYear> CloseAsync(Guid yearId)
    {
        FiscalYear year;
        List<(Guid Account, Guid? Store, decimal Net)> moves;
        int closings;
        Guid? lastPeriod = null;
        await using (var db = data.Create())
        {
            year = await db.FiscalYears.AsNoTracking().Include(y => y.Periods).SingleOrDefaultAsync(y => y.Id == yearId) ?? throw new BusinessRuleException("That year no longer exists.");
            if (year.Status == FiscalStatus.Closed) throw new BusinessRuleException($"{year.Name} is closed already.");
            if (await db.JournalEntries.AnyAsync(e => e.Status == JournalStatus.Draft && e.Date >= year.StartDate && e.Date <= year.EndDate))
                throw new BusinessRuleException($"{year.Name} has entries waiting for approval; approve or delete them first.");
            var pl = await db.LedgerAccounts.AsNoTracking().Where(a => a.Type == AccountType.Income || a.Type == AccountType.Expense).Select(a => a.Id).ToListAsync();
            var rows = await db.JournalLines.AsNoTracking().Where(l => pl.Contains(l.AccountId))
                .Join(db.JournalEntries.AsNoTracking().Where(e => e.Status != JournalStatus.Draft && e.Date >= year.StartDate && e.Date <= year.EndDate),
                    l => l.JournalEntryId, e => e.Id, (l, e) => new { l.AccountId, l.StoreId, l.Debit, l.Credit })
                .ToListAsync();
            moves = rows.GroupBy(r => (r.AccountId, r.StoreId)).Select(g => (g.Key.AccountId, g.Key.StoreId, g.Sum(r => r.Debit - r.Credit))).Where(m => m.Item3 != 0).ToList();
            closings = await db.JournalEntries.CountAsync(e => e.Source != null && e.Source.StartsWith($"close:{year.Id:N}"));
            var last = year.Periods.OrderBy(p => p.Number).LastOrDefault();
            if (last is not null && last.Status != FiscalStatus.Open) lastPeriod = last.Id;
        }
        // The next year exists before this one closes, so late postings have an open period to go to.
        await Ledger.EnsureYearAsync(year.EndDate.AddDays(1));
        if (moves.Count > 0)
        {
            if (lastPeriod is Guid locked) await Ledger.SetPeriodStatusAsync(locked, FiscalStatus.Open);
            var codes = await CodesAsync();
            var lines = new List<JournalDraftLine>();
            foreach (var (account, store, net) in moves.OrderBy(m => m.Store).ThenBy(m => codes[m.Account], StringComparer.Ordinal))
                lines.Add(JournalDraftLine.Code(codes[account], net < 0 ? -net : 0, net > 0 ? net : 0, store, "Closing"));
            foreach (var store in moves.GroupBy(m => m.Store))
            {
                decimal result = store.Sum(m => m.Net);
                lines.Add(JournalDraftLine.Role(LedgerRoles.RetainedEarnings, result > 0 ? result : 0, result < 0 ? -result : 0, store.Key,
                    result <= 0 ? $"Profit of {year.Name}" : $"Loss of {year.Name}"));
            }
            lines.RemoveAll(l => l.Debit == 0 && l.Credit == 0);
            await Ledger.PostAsync(new JournalDraft(year.EndDate, JournalKind.Closing, $"Closing {year.Name}: income and expenses to retained earnings", lines)
            {
                Source = $"close:{year.Id:N}:{closings + 1}", Reference = year.Name,
            });
        }
        await using (var db = data.Create())
        {
            var row = await db.FiscalYears.Include(y => y.Periods).SingleAsync(y => y.Id == yearId);
            row.Status = FiscalStatus.Closed;
            row.ClosedAtUtc = SyncColumns.Now();
            row.ClosedBy = FinanceNumbers.Cut(data.CurrentUser?.UserName, 50);
            foreach (var p in row.Periods) p.Status = FiscalStatus.Closed;
            db.Audit(AuditActions.YearClosed, $"{row.Name} closed", nameof(FiscalYear), row.Id);
            await db.SaveChangesAsync();
            return row;
        }
    }

    /// <summary>Opens a closed year again: its periods are open and its closing entry is reversed (closing it again makes a new one).</summary>
    public async Task<FiscalYear> ReopenAsync(Guid yearId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Say why the year is opened again.");
        FiscalYear year;
        await using (var db = data.Create())
        {
            var row = await db.FiscalYears.Include(y => y.Periods).SingleOrDefaultAsync(y => y.Id == yearId) ?? throw new BusinessRuleException("That year no longer exists.");
            if (row.Status != FiscalStatus.Closed) throw new BusinessRuleException($"{row.Name} is not closed.");
            row.Status = FiscalStatus.Open;
            row.ClosedAtUtc = null;
            row.ClosedBy = null;
            foreach (var p in row.Periods) p.Status = FiscalStatus.Open;
            db.Audit(AuditActions.YearReopened, $"{row.Name} opened again: {reason.Trim()}", nameof(FiscalYear), row.Id);
            await db.SaveChangesAsync();
            year = row;
        }
        Guid? closing;
        await using (var db = data.Create())
            closing = await db.JournalEntries.AsNoTracking().Where(e => e.Source != null && e.Source.StartsWith($"close:{year.Id:N}") && e.Status == JournalStatus.Posted)
                .OrderByDescending(e => e.Number).Select(e => (Guid?)e.Id).FirstOrDefaultAsync();
        if (closing is Guid id) await Ledger.ReverseAsync(id, year.EndDate, "Year opened again: " + reason.Trim());
        return year;
    }

    /// <summary>
    /// Posts the opening balances of a company (or one store) that starts its books here on <paramref name="date"/>: an
    /// Opening entry, replaced when posted again for the same date and store while its period is open. A difference
    /// between debits and credits goes to retained earnings when <paramref name="balanceToRetainedEarnings"/>, else it is refused.
    /// </summary>
    public async Task<JournalEntry> PostOpeningAsync(DateOnly date, Guid? store, IReadOnlyList<OpeningLine> lines, bool balanceToRetainedEarnings)
    {
        if (lines.Count == 0) throw new BusinessRuleException("Enter the accounts' balances.");
        var codes = await CodesAsync();
        var draft = new List<JournalDraftLine>();
        foreach (var l in lines.Where(l => l.Debit != 0 || l.Credit != 0))
        {
            if (!codes.TryGetValue(l.AccountId, out var code)) throw new BusinessRuleException("An account chosen no longer exists.");
            draft.Add(JournalDraftLine.Code(code, l.Debit, l.Credit, l.StoreId ?? store, "Opening balance", l.PartyKind, l.PartyId));
        }
        decimal difference = Money.Round(draft.Sum(l => l.Debit) - draft.Sum(l => l.Credit));
        if (difference != 0)
        {
            if (!balanceToRetainedEarnings) throw new BusinessRuleException($"The balances do not agree: debits are {difference:N2} more than credits.");
            draft.Add(JournalDraftLine.Role(LedgerRoles.RetainedEarnings, difference < 0 ? -difference : 0, difference > 0 ? difference : 0, store, "Opening balances difference"));
        }
        return await Ledger.PostAsync(new JournalDraft(date, JournalKind.Opening, $"Opening balances on {date:yyyy-MM-dd}", draft)
        {
            Source = $"opening:{(store is Guid s ? s.ToString("N") : "company")}:{date:yyyy-MM-dd}", StoreId = store, Reference = "Opening",
        });
    }

    private async Task<Dictionary<Guid, string>> CodesAsync()
    {
        await using var db = data.Create();
        return await db.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);
    }
}
