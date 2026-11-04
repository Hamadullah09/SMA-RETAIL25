using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A budget amount to save: an account, the month (its first day) and the amount (debit positive).</summary>
public sealed record BudgetAmount(Guid AccountId, DateOnly Month, decimal Amount);

/// <summary>An account's budget against what was posted, over a period.</summary>
public sealed record BudgetVsActual(Guid AccountId, string Code, string Name, AccountType Type, decimal Budget, decimal Actual)
{
    public decimal Difference => Actual - Budget;
}

/// <summary>
/// Budgets per account, month and store (or the company), entered by hand or copied from last year's actuals or budget,
/// and compared with what was posted.
/// </summary>
public sealed class BudgetService(DataContext data)
{
    /// <summary>The budget lines of a year for one store, or (null) the company's own.</summary>
    public async Task<List<BudgetLine>> GetAsync(Guid yearId, Guid? store)
    {
        await using var db = data.Create();
        return await db.BudgetLines.AsNoTracking().Where(b => b.FiscalYearId == yearId && b.StoreId == store).OrderBy(b => b.Month).ToListAsync();
    }

    /// <summary>Saves amounts of a year's budget for a store (or the company): a zero amount removes the month.</summary>
    public async Task SaveAsync(Guid yearId, Guid? store, IReadOnlyList<BudgetAmount> amounts)
    {
        await using var db = data.Create();
        var year = await db.FiscalYears.AsNoTracking().SingleOrDefaultAsync(y => y.Id == yearId) ?? throw new BusinessRuleException("That year no longer exists.");
        var accounts = await db.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id);
        foreach (var a in amounts)
        {
            var month = new DateOnly(a.Month.Year, a.Month.Month, 1);
            if (month < new DateOnly(year.StartDate.Year, year.StartDate.Month, 1) || month > year.EndDate)
                throw new BusinessRuleException($"{month:yyyy-MM} is not in {year.Name}.");
            if (!accounts.TryGetValue(a.AccountId, out var account) || !account.IsPosting) throw new BusinessRuleException("Budgets are for accounts that are posted to.");
        }
        var existing = await db.BudgetLines.Where(b => b.FiscalYearId == yearId && b.StoreId == store).ToListAsync();
        foreach (var a in amounts)
        {
            var month = new DateOnly(a.Month.Year, a.Month.Month, 1);
            var row = existing.FirstOrDefault(b => b.AccountId == a.AccountId && b.Month == month);
            decimal amount = Money.Round(a.Amount);
            if (amount == 0)
            {
                if (row is not null) db.BudgetLines.Remove(row);
                continue;
            }
            if (row is null)
            {
                db.BudgetLines.Add(row = new BudgetLine { FiscalYearId = yearId, AccountId = a.AccountId, StoreId = store, Month = month });
                existing.Add(row);
            }
            row.Amount = amount;
        }
        db.Audit(AuditActions.Changed, $"Budget {year.Name}: {amounts.Count} amounts", nameof(BudgetLine));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Fills a year's budget for a store (or the company) from the year before: its posted income and expenses per account
    /// and month (<paramref name="fromBudget"/> false) or its budget, raised by <paramref name="percent"/>. What the year
    /// had is replaced. Returns the number of amounts.
    /// </summary>
    public async Task<int> CopyFromLastYearAsync(Guid yearId, Guid? store, bool fromBudget, decimal percent)
    {
        FiscalYear year;
        FiscalYear? previous;
        List<BudgetAmount> amounts;
        await using (var db = data.Create())
        {
            year = await db.FiscalYears.AsNoTracking().SingleOrDefaultAsync(y => y.Id == yearId) ?? throw new BusinessRuleException("That year no longer exists.");
            previous = await db.FiscalYears.AsNoTracking().Where(y => y.EndDate < year.StartDate).OrderByDescending(y => y.EndDate).FirstOrDefaultAsync();
            if (previous is null) throw new BusinessRuleException($"There is no year before {year.Name} to copy from.");
            int shift = (year.StartDate.Year - previous.StartDate.Year) * 12 + year.StartDate.Month - previous.StartDate.Month;
            decimal factor = 1 + percent / 100m;
            if (fromBudget)
            {
                amounts = (await db.BudgetLines.AsNoTracking().Where(b => b.FiscalYearId == previous.Id && b.StoreId == store).ToListAsync())
                    .Select(b => new BudgetAmount(b.AccountId, b.Month.AddMonths(shift), Money.Round(b.Amount * factor))).ToList();
            }
            else
            {
                var pl = await db.LedgerAccounts.AsNoTracking().Where(a => (a.Type == AccountType.Income || a.Type == AccountType.Expense) && a.IsPosting).Select(a => a.Id).ToListAsync();
                var lines = db.JournalLines.AsNoTracking().Where(l => pl.Contains(l.AccountId))
                    .Join(db.JournalEntries.AsNoTracking().Where(e => e.Status != JournalStatus.Draft && e.Kind != JournalKind.Closing && e.Date >= previous.StartDate && e.Date <= previous.EndDate),
                        l => l.JournalEntryId, e => e.Id, (l, e) => new { l.AccountId, l.StoreId, e.Date, l.Debit, l.Credit });
                if (store is Guid s) lines = lines.Where(l => l.StoreId == s);
                amounts = (await lines.ToListAsync()).GroupBy(l => (l.AccountId, new DateOnly(l.Date.Year, l.Date.Month, 1)))
                    .Select(g => new BudgetAmount(g.Key.AccountId, g.Key.Item2.AddMonths(shift), Money.Round(g.Sum(l => l.Debit - l.Credit) * factor)))
                    .Where(a => a.Amount != 0).ToList();
            }
            var old = await db.BudgetLines.Where(b => b.FiscalYearId == yearId && b.StoreId == store).ToListAsync();
            db.BudgetLines.RemoveRange(old);
            await db.SaveChangesAsync();
        }
        amounts = amounts.Where(a => a.Month <= year.EndDate).ToList();
        await SaveAsync(yearId, store, amounts);
        return amounts.Count;
    }

    /// <summary>
    /// The budget of each account over <paramref name="from"/> to <paramref name="to"/> (whole months), for a store, or for
    /// the company (every store's budgets and the company's own).
    /// </summary>
    public async Task<Dictionary<Guid, decimal>> AmountsAsync(DateOnly from, DateOnly to, Guid? store)
    {
        var first = new DateOnly(from.Year, from.Month, 1);
        await using var db = data.Create();
        var q = db.BudgetLines.AsNoTracking().Where(b => b.Month >= first && b.Month <= to);
        if (store is Guid s) q = q.Where(b => b.StoreId == s);
        return (await q.Select(b => new { b.AccountId, b.Amount }).ToListAsync()).GroupBy(b => b.AccountId).ToDictionary(g => g.Key, g => g.Sum(b => b.Amount));
    }

    /// <summary>Budget against actual per account (income and expenses, and any account with a budget) over the period.</summary>
    public async Task<List<BudgetVsActual>> CompareAsync(DateOnly from, DateOnly to, Guid? store)
    {
        var budget = await AmountsAsync(from, to, store);
        var actual = (await new LedgerService(data).BalancesAsync(from, to, store, exceptClosing: true)).ToDictionary(b => b.AccountId, b => b.Debit - b.Credit);
        await using var db = data.Create();
        var accounts = await db.LedgerAccounts.AsNoTracking().Where(a => a.IsPosting).OrderBy(a => a.Code).ToListAsync();
        return accounts.Where(a => budget.ContainsKey(a.Id) || ((a.Type is AccountType.Income or AccountType.Expense) && actual.GetValueOrDefault(a.Id) != 0))
            .Select(a => new BudgetVsActual(a.Id, a.Code, a.Name, a.Type, budget.GetValueOrDefault(a.Id), actual.GetValueOrDefault(a.Id)))
            .ToList();
    }
}
