using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Services;

/// <summary>Net sales of a month: per store, and per salesperson at each store.</summary>
public sealed record MonthActuals(IReadOnlyDictionary<Guid, decimal> ByStore, IReadOnlyDictionary<(Guid StoreId, Guid UserId), decimal> ByPerson);

/// <summary>
/// Sales targets per store and per salesperson and month, against the month's net sales (completed sales after line and
/// sale discounts, before taxes, as the dashboard counts them).
/// </summary>
public sealed class TargetService(DataContext data)
{
    /// <summary>
    /// Adds a target (one per store, or salesperson at a store, and month) or changes its amount (a stale version is a
    /// conflict).
    /// </summary>
    public async Task<SalesTarget> SaveAsync(SalesTarget t)
    {
        if (t.Amount < 0) throw new BusinessRuleException("A target cannot be below zero.");
        await using var db = data.Create();
        if (t.Id == Guid.Empty)
        {
            if (t.Month is < 1 or > 12 || t.Year is < 2000 or > 2100) throw new BusinessRuleException("Choose the month of the target.");
            if (t.StoreId != Guid.Empty && !await db.Stores.AnyAsync(s => s.Id == t.StoreId)) throw new BusinessRuleException("That store does not exist.");
            if (t.UserId is { } user && !await db.UserAccounts.AnyAsync(u => u.Id == user)) throw new BusinessRuleException("That salesperson does not exist.");
            var store = t.StoreId == Guid.Empty ? db.Station?.StoreId ?? Guid.Empty : t.StoreId;
            if (await db.SalesTargets.AnyAsync(x => x.StoreId == store && x.Year == t.Year && x.Month == t.Month && x.UserId == t.UserId))
                throw new BusinessRuleException("That target is already set for the month; change it instead.");
            db.SalesTargets.Add(t);
        }
        else
        {
            var row = await db.SalesTargets.SingleOrDefaultAsync(x => x.Id == t.Id) ?? throw new BusinessRuleException("That target no longer exists.");
            if (row.Version != t.Version) throw new ConcurrencyConflictException("Someone else changed this target. Load it again and make your changes again.");
            db.Entry(row).Property(x => x.Version).OriginalValue = t.Version;
            row.Amount = t.Amount;
            t = row;
        }
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("Someone else changed this target. Load it again and make your changes again.");
        }
        return t;
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.SalesTargets.SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessRuleException("That target no longer exists.");
        db.SalesTargets.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>The targets of a month at some stores (null: every store), the stores' own first.</summary>
    public async Task<List<SalesTarget>> ListAsync(int year, int month, IReadOnlyCollection<Guid>? stores = null)
    {
        await using var db = data.Create();
        var q = db.SalesTargets.AsNoTracking().Where(t => t.Year == year && t.Month == month);
        if (stores is not null) q = q.Where(t => stores.Contains(t.StoreId));
        return [.. (await q.ToListAsync()).OrderBy(t => t.UserId is not null).ThenBy(t => t.StoreId)];
    }

    /// <summary>The month's net sales at some stores (null: every store), per store and per salesperson.</summary>
    public async Task<MonthActuals> ActualsAsync(int year, int month, IReadOnlyCollection<Guid>? stores = null)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);
        await using var db = data.Create();
        var sales = db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Completed && s.SoldAt >= start && s.SoldAt < end);
        if (stores is not null) sales = sales.Where(s => stores.Contains(s.StoreId));
        var rows = await sales.GroupBy(s => new { s.StoreId, s.StaffId }).Select(g => new { g.Key.StoreId, g.Key.StaffId, Net = g.Sum(s => s.Subtotal - s.SubtotalDiscount) })
            .ToListAsync();
        var byStore = rows.GroupBy(r => r.StoreId).ToDictionary(g => g.Key, g => g.Sum(r => r.Net));
        var byPerson = rows.Where(r => r.StaffId is not null).GroupBy(r => (r.StoreId, r.StaffId!.Value)).ToDictionary(g => g.Key, g => g.Sum(r => r.Net));
        return new MonthActuals(byStore, byPerson);
    }

    /// <summary>The share of a target reached, in percent (0 for no target).</summary>
    public static decimal Percent(decimal actual, decimal target) => target <= 0 ? 0 : Math.Round(actual * 100m / target, 1);
}
