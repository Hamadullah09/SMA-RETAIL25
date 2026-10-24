using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Services;

/// <summary>The End of Day (Z) report of one drawer session.</summary>
public sealed record DrawerReport(DrawerSession Session, IReadOnlyList<DrawerSessionTender> Tenders, IReadOnlyList<DrawerMovement> Movements,
    decimal CashTendered, decimal ExpectedCash)
{
    public decimal GrossSales => Session.NetSales + Session.Tax1Total + Session.Tax2Total;
    public decimal GrossProfit => Session.NetSales - Session.Cogs;
}

/// <summary>Cash drawer: opening float, pay-ins and pay-outs, no-sale openings and End of Day.</summary>
public sealed class DrawerService(DataContext data)
{
    /// <summary>The station's open session; a new one is started when the last was closed by End of Day.</summary>
    internal static async Task<DrawerSession> GetOpenSessionAsync(RetailPlusDbContext db, int stationNumber, int locationId)
    {
        var session = await db.DrawerSessions.Include(s => s.Tenders)
            .Where(s => s.StationNumber == stationNumber && s.ClosedAt == null).OrderByDescending(s => s.Id).FirstOrDefaultAsync();
        if (session is null)
        {
            session = new DrawerSession { StationNumber = stationNumber, LocationId = locationId, OpenedAt = DateTime.Now };
            db.DrawerSessions.Add(session);
            await db.SaveChangesAsync();
        }
        return session;
    }

    public async Task<DrawerReport> GetCurrentAsync(SessionInfo info)
    {
        await using var db = data.Create();
        var session = await GetOpenSessionAsync(db, info.StationNumber, info.LocationId);
        return await BuildReportAsync(db, session);
    }

    public async Task<DrawerReport?> GetAsync(int sessionId)
    {
        await using var db = data.Create();
        var session = await db.DrawerSessions.Include(s => s.Tenders).SingleOrDefaultAsync(s => s.Id == sessionId);
        return session is null ? null : await BuildReportAsync(db, session);
    }

    private static async Task<DrawerReport> BuildReportAsync(RetailPlusDbContext db, DrawerSession session)
    {
        var movements = await db.DrawerMovements.AsNoTracking().Where(m => m.DrawerSessionId == session.Id).OrderBy(m => m.At).ToListAsync();
        var cashNames = await db.TenderTypes.AsNoTracking().Where(t => t.Kind == TenderKind.Cash).Select(t => t.Name).ToListAsync();
        decimal cash = session.Tenders.Where(t => cashNames.Contains(t.TenderType)).Sum(t => t.Amount);
        decimal expected = session.OpeningFloat + cash + session.PayIns - session.PayOuts;
        return new DrawerReport(session, session.Tenders.OrderBy(t => t.TenderType).ToList(), movements, cash, expected);
    }

    public async Task AddMovementAsync(SessionInfo info, DrawerMovementKind kind, decimal amount, string? reason)
    {
        if (kind != DrawerMovementKind.NoSale && amount <= 0) throw new BusinessRuleException("Enter an amount above zero.");
        if (info.IsTrainee) return;
        await using var db = data.Create();
        var session = await GetOpenSessionAsync(db, info.StationNumber, info.LocationId);
        switch (kind)
        {
            case DrawerMovementKind.OpeningFloat: session.OpeningFloat += amount; break;
            case DrawerMovementKind.PayIn: session.PayIns += amount; break;
            case DrawerMovementKind.PayOut: session.PayOuts += amount; break;
        }
        db.DrawerMovements.Add(new DrawerMovement { DrawerSessionId = session.Id, Kind = kind, Amount = kind == DrawerMovementKind.NoSale ? 0 : amount, At = DateTime.Now, Reason = reason, User = info.UserName });
        await db.SaveChangesAsync();
    }

    /// <summary>End of Day: closes the session and returns its report. The next sale starts a new session.</summary>
    public async Task<DrawerReport> EndOfDayAsync(SessionInfo info)
    {
        await using var db = data.Create();
        var session = await GetOpenSessionAsync(db, info.StationNumber, info.LocationId);
        session.ClosedAt = DateTime.Now;
        await db.SaveChangesAsync();
        return await BuildReportAsync(db, session);
    }

    public async Task<List<DrawerSession>> ListAsync(int? stationNumber = null, int take = 200)
    {
        await using var db = data.Create();
        var q = db.DrawerSessions.AsNoTracking().AsQueryable();
        if (stationNumber is int st) q = q.Where(s => s.StationNumber == st);
        return await q.OrderByDescending(s => s.OpenedAt).Take(take).ToListAsync();
    }
}
