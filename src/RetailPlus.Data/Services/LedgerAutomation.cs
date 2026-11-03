using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Services;

/// <summary>What one run of the automatic posting did.</summary>
public sealed record AutoPostResult(int Entries, IReadOnlyList<string> Problems);

/// <summary>
/// Posts the stores' business days to the ledger by itself: the days a package from a store's tills touched, as soon as
/// it is applied, and every night the last days of every store again (late packages). "Post days" on the web runs the
/// same. One run at a time on a server, so two never post the same day at once. Each run first gives stores and tenders
/// added since the server started the standard accounts of the daily journal's mapping (<see cref="LedgerService.SeedAsync"/>).
/// </summary>
public sealed class LedgerAutomation(DataContext data)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Posts the days from <paramref name="from"/> to <paramref name="to"/> of one store or (null) every store.</summary>
    public async Task<AutoPostResult> PostDaysAsync(DateOnly from, DateOnly to, Guid? store = null, CancellationToken cancellationToken = default)
    {
        if (to < from) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > 400) throw new BusinessRuleException("Post at most 400 days at a time.");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            // A store or tender added since the server started gets the standard accounts of its daily-journal mapping first.
            await new LedgerService(data).SeedAsync(cancellationToken);
            var problems = new List<string>();
            int entries = 0;
            List<Guid> stores = store is Guid one ? [one] : await StoresAsync(cancellationToken);
            foreach (var s in stores)
            {
                try
                {
                    entries += await new LedgerService(data).PostDailyAsync(from, to, s, cancellationToken);
                }
                catch (BusinessRuleException ex)
                {
                    problems.Add($"{await StoreCodeAsync(s, cancellationToken)}: {ex.Message}");
                }
            }
            if (entries > 0) await AuditAsync(store, $"Posted {entries} business days, {from:yyyy-MM-dd} to {to:yyyy-MM-dd}", cancellationToken);
            return new AutoPostResult(entries, problems);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// The days that packages applied since the last look touched (sales, payments on account, stock movements and gift
    /// card transactions of the store that sent them), posted. The first look only sets where to start from.
    /// </summary>
    public async Task<AutoPostResult> PostSyncedDaysAsync(CancellationToken cancellationToken = default)
    {
        var problems = new List<string>();
        int entries = 0;
        var work = new List<(Guid Store, DateOnly From, DateOnly To)>();
        long high;
        await using (var db = data.Create())
        {
            var settings = await db.FinanceSettings.FirstOrDefaultAsync(cancellationToken);
            if (settings is null || !settings.AutoPostOnSync) return new AutoPostResult(0, []);
            high = await Changes.HighMarkAsync(db, cancellationToken);
            long cursor = settings.SyncCursor;
            if (cursor != 0 && high > cursor)
            {
                var stores = await Changes.Between(db, db.SyncBatches.AsNoTracking().Where(b => b.Direction == SyncDirection.Upload && b.Status == SyncBatchStatus.Applied), cursor, high)
                    .Where(b => b.StoreId != null).Select(b => b.StoreId!.Value).Distinct().ToListAsync(cancellationToken);
                foreach (var store in stores)
                {
                    var days = new List<DateTime?>
                    {
                        await Changes.Between(db, db.Sales.AsNoTracking().Where(s => s.StoreId == store), cursor, high).MinAsync(s => (DateTime?)s.SoldAt, cancellationToken),
                        await Changes.Between(db, db.Sales.AsNoTracking().Where(s => s.StoreId == store), cursor, high).MaxAsync(s => (DateTime?)s.SoldAt, cancellationToken),
                        await Changes.Between(db, db.StockMovements.AsNoTracking().Where(m => m.StoreId == store), cursor, high).MinAsync(m => (DateTime?)m.At, cancellationToken),
                        await Changes.Between(db, db.StockMovements.AsNoTracking().Where(m => m.StoreId == store), cursor, high).MaxAsync(m => (DateTime?)m.At, cancellationToken),
                        await Changes.Between(db, db.GiftCardTransactions.AsNoTracking().Where(t => t.StoreId == store), cursor, high).MinAsync(t => (DateTime?)t.At, cancellationToken),
                        await Changes.Between(db, db.GiftCardTransactions.AsNoTracking().Where(t => t.StoreId == store), cursor, high).MaxAsync(t => (DateTime?)t.At, cancellationToken),
                    };
                    var paid = Changes.Between(db, db.InvoicePayments.AsNoTracking().Where(p => p.StoreId == store), cursor, high);
                    var paidFrom = await paid.MinAsync(p => (DateOnly?)p.PaidOn, cancellationToken);
                    var paidTo = await paid.MaxAsync(p => (DateOnly?)p.PaidOn, cancellationToken);
                    var dates = days.Where(d => d is not null).Select(d => DateOnly.FromDateTime(d!.Value)).ToList();
                    if (paidFrom is DateOnly pf) dates.Add(pf);
                    if (paidTo is DateOnly pt) dates.Add(pt);
                    if (dates.Count > 0) work.Add((store, dates.Min(), dates.Max()));
                }
            }
            settings.SyncCursor = high;
            await db.SaveChangesAsync(cancellationToken);
        }
        foreach (var (store, from, to) in work)
        {
            // A very old row changed (a late correction) is posted from its day; at most a year back in one go.
            var start = to.DayNumber - from.DayNumber > 366 ? to.AddDays(-366) : from;
            var result = await PostDaysAsync(start, to, store, cancellationToken);
            entries += result.Entries;
            problems.AddRange(result.Problems);
        }
        return new AutoPostResult(entries, problems);
    }

    /// <summary>
    /// Once a day, from the settings' hour on: the last <see cref="FinanceSettings.NightlyDays"/> days of every store, posted
    /// again. Returns null when it is not time yet (or it ran today already).
    /// </summary>
    public async Task<AutoPostResult?> PostNightlyAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(now);
        int days;
        await using (var db = data.Create())
        {
            var settings = await db.FinanceSettings.FirstOrDefaultAsync(cancellationToken);
            if (settings is null) return null;
            if (settings.LastNightlyRunOn == today || now.Hour < settings.NightlyHour) return null;
            settings.LastNightlyRunOn = today;
            days = settings.NightlyDays;
            await db.SaveChangesAsync(cancellationToken);
        }
        return await PostDaysAsync(today.AddDays(1 - days), today, null, cancellationToken);
    }

    private async Task<List<Guid>> StoresAsync(CancellationToken cancellationToken)
    {
        await using var db = data.Create();
        return await db.Stores.AsNoTracking().OrderBy(s => s.Code).Select(s => s.Id).ToListAsync(cancellationToken);
    }

    private async Task<string> StoreCodeAsync(Guid store, CancellationToken cancellationToken)
    {
        await using var db = data.Create();
        return await db.Stores.AsNoTracking().Where(s => s.Id == store).Select(s => s.Code).FirstOrDefaultAsync(cancellationToken) ?? "?";
    }

    private async Task AuditAsync(Guid? store, string details, CancellationToken cancellationToken)
    {
        var target = store ?? data.Station?.StoreId;
        if (target is null)
        {
            await using var read = data.Create();
            target = await read.Stores.AsNoTracking().OrderBy(s => s.Code).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(cancellationToken);
        }
        if (target is not Guid id) return;
        await using var db = data.For(new StationIdentity(id, 0), data.CurrentUser).Create();
        db.Audit(AuditActions.DaysPosted, details);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// What changed in the database between two looks. On SQL Server (the server) that is the rows' change number, raised
/// whenever a row is written, also by a till's package (whose rows keep the till's own times); elsewhere the rows' change
/// time in ticks, which is right for a database that no till sends to.
/// </summary>
internal static class Changes
{
    public static async Task<long> HighMarkAsync(RetailPlusDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer()) return SyncColumns.Now().Ticks;
        // Numbers below MIN_ACTIVE_ROWVERSION belong to committed rows only (as the sync's downloads read them).
        return await db.Database.SqlQueryRaw<long>("SELECT CAST(MIN_ACTIVE_ROWVERSION() AS bigint) - 1 AS [Value]").SingleAsync(cancellationToken);
    }

    public static IQueryable<T> Between<T>(RetailPlusDbContext db, IQueryable<T> rows, long after, long upTo) where T : class, ITracked
    {
        if (db.Database.IsSqlServer())
            return rows.Where(r => EF.Property<long>(r, SyncColumns.ChangeNumber) > after && EF.Property<long>(r, SyncColumns.ChangeNumber) <= upTo);
        var from = new DateTime(after, DateTimeKind.Utc);
        var to = new DateTime(upTo, DateTimeKind.Utc);
        return rows.Where(r => r.UpdatedAtUtc > from && r.UpdatedAtUtc <= to);
    }
}
