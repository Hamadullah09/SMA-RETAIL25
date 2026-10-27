using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;

namespace RetailPlus.Server;

public sealed record DashboardTotals(decimal NetSales, decimal Tax, decimal Total, int Transactions, decimal AverageSale, decimal Units, decimal Returns,
    decimal Discounts, decimal GrossProfit, int Voids);
public sealed record DashboardStore(Guid StoreId, string Code, string Name, decimal NetSales, int Transactions, decimal AverageSale, DateTime? LastUploadAtUtc);
public sealed record DashboardDay(DateOnly Date, decimal NetSales, int Transactions);
public sealed record DashboardHour(int Hour, decimal NetSales, int Transactions);
public sealed record DashboardTender(string Tender, decimal Amount, int Count);
public sealed record DashboardItem(Guid? ItemId, string? Plu, string Name, decimal Units, decimal NetSales);
public sealed record DashboardCashier(Guid? UserId, string UserName, decimal NetSales, int Transactions);
public sealed record DashboardStation(Guid StoreId, string StoreCode, int StationNumber, decimal NetSales, int Transactions);
public sealed record DashboardDrawer(string StoreCode, int StationNumber, DateTime? ClosedAt, string? ClosedBy, decimal Expected, decimal? Counted, decimal? Variance);
public sealed record Dashboard(DateOnly From, DateOnly To, DashboardTotals Totals, DashboardTotals Previous, IReadOnlyList<DashboardStore> ByStore,
    IReadOnlyList<DashboardDay> ByDay, IReadOnlyList<DashboardHour> ByHour, IReadOnlyList<DashboardTender> PaymentMix, IReadOnlyList<DashboardItem> TopItems,
    IReadOnlyList<DashboardCashier> ByCashier, IReadOnlyList<DashboardStation> ByStation, IReadOnlyList<DashboardDrawer> Drawers);

public sealed record TillInfo(Guid StationId, Guid StoreId, string StoreCode, string StoreName, int Number, string? Name, bool Enrolled, bool Retired,
    string? AppVersion, DateTime? LastUploadAtUtc, DateTime? LastDownloadAtUtc, DateTime? LastSaleAt, decimal TodayNetSales, int TodayTransactions,
    bool DrawerOpen, int FailedBatches, string Status);

public static partial class Endpoints
{
    private static void MapDashboard(RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", DashboardAsync).RequireAuthorization(Need(Permissions.DashboardView));
        api.MapGet("/tills", TillsAsync).RequireAuthorization(Need(Permissions.WebSignIn));
    }

    /// <summary>Net sales as the reports count them: after line and sale discounts, before taxes.</summary>
    private static IQueryable<Sale> SalesOf(RetailPlusDbContext db, Guid? store, DateOnly from, DateOnly to)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var sales = db.Sales.AsNoTracking().Where(s => s.SoldAt >= start && s.SoldAt < end);
        return store is { } s ? sales.Where(x => x.StoreId == s) : sales;
    }

    private static async Task<DashboardTotals> TotalsAsync(RetailPlusDbContext db, Guid? store, DateOnly from, DateOnly to)
    {
        var sales = SalesOf(db, store, from, to);
        var completed = sales.Where(s => s.Status == SaleStatus.Completed);
        var sums = await completed.GroupBy(_ => 1).Select(g => new
        {
            Net = g.Sum(s => s.Subtotal - s.SubtotalDiscount), Tax = g.Sum(s => s.Tax1 + s.Tax2), Total = g.Sum(s => s.Total), Count = g.Count(),
            SaleDiscounts = g.Sum(s => s.SubtotalDiscount), Cogs = g.Sum(s => s.Cogs),
        }).FirstOrDefaultAsync();
        var lines = db.SaleLines.AsNoTracking().Where(l => completed.Any(s => s.Id == l.SaleId));
        var lineSums = await lines.GroupBy(_ => 1).Select(g => new
        {
            Units = g.Where(l => l.Kind == SaleLineKind.Sale || l.Kind == SaleLineKind.Return).Sum(l => l.Quantity),
            Returns = g.Where(l => l.Kind == SaleLineKind.Return).Sum(l => -l.LineTotal),
            Discounts = g.Sum(l => l.DiscountAmount),
        }).FirstOrDefaultAsync();
        int voids = await sales.CountAsync(s => s.Status == SaleStatus.Voided);
        decimal net = sums?.Net ?? 0, total = sums?.Total ?? 0;
        int count = sums?.Count ?? 0;
        return new DashboardTotals(net, sums?.Tax ?? 0, total, count, count == 0 ? 0 : Math.Round(total / count, 2), lineSums?.Units ?? 0, lineSums?.Returns ?? 0,
            (lineSums?.Discounts ?? 0) + (sums?.SaleDiscounts ?? 0), net - (sums?.Cogs ?? 0), voids);
    }

    private static async Task<IResult> DashboardAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        int days = to.DayNumber - from.DayNumber + 1;
        await using var db = api.Root.Create();
        var totals = await TotalsAsync(db, store, from, to);
        var previous = await TotalsAsync(db, store, from.AddDays(-days), from.AddDays(-1));

        var completed = SalesOf(db, store, from, to).Where(s => s.Status == SaleStatus.Completed);
        var stores = await (store is { } one ? db.Stores.Where(s => s.Id == one) : api.User.Visible(db.Stores)).AsNoTracking().OrderBy(s => s.Code).ToListAsync();
        var perStore = await completed.GroupBy(s => s.StoreId).Select(g => new { g.Key, Net = g.Sum(s => s.Subtotal - s.SubtotalDiscount), Total = g.Sum(s => s.Total), Count = g.Count() })
            .ToDictionaryAsync(x => x.Key);
        var uploads = await db.StationLinks.AsNoTracking().GroupBy(l => l.StoreId).Select(g => new { g.Key, Last = g.Max(l => l.LastUploadAtUtc) }).ToDictionaryAsync(x => x.Key, x => x.Last);
        var byStore = stores.Select(s => perStore.TryGetValue(s.Id, out var x)
            ? new DashboardStore(s.Id, s.Code, s.Name, x.Net, x.Count, x.Count == 0 ? 0 : Math.Round(x.Total / x.Count, 2), ApiRequest.Utc(uploads.GetValueOrDefault(s.Id)))
            : new DashboardStore(s.Id, s.Code, s.Name, 0, 0, 0, ApiRequest.Utc(uploads.GetValueOrDefault(s.Id)))).ToList();

        var perDay = await completed.GroupBy(s => s.SoldAt.Date).Select(g => new { Day = g.Key, Net = g.Sum(s => s.Subtotal - s.SubtotalDiscount), Count = g.Count() })
            .ToDictionaryAsync(x => DateOnly.FromDateTime(x.Day));
        var byDay = Enumerable.Range(0, days).Select(i => from.AddDays(i))
            .Select(d => perDay.TryGetValue(d, out var x) ? new DashboardDay(d, x.Net, x.Count) : new DashboardDay(d, 0, 0)).ToList();
        var perHour = await completed.GroupBy(s => s.SoldAt.Hour).Select(g => new { Hour = g.Key, Net = g.Sum(s => s.Subtotal - s.SubtotalDiscount), Count = g.Count() })
            .ToDictionaryAsync(x => x.Hour);
        var byHour = Enumerable.Range(0, 24).Select(h => perHour.TryGetValue(h, out var x) ? new DashboardHour(h, x.Net, x.Count) : new DashboardHour(h, 0, 0)).ToList();

        var paymentMix = (await db.SalePayments.AsNoTracking().Where(p => completed.Any(s => s.Id == p.SaleId))
                .GroupBy(p => p.TenderType).Select(g => new { g.Key, Amount = g.Sum(p => p.Amount), Count = g.Count() }).ToListAsync())
            .OrderByDescending(x => x.Amount).Select(x => new DashboardTender(x.Key, x.Amount, x.Count)).ToList();
        var topItems = (await db.SaleLines.AsNoTracking().Where(l => (l.Kind == SaleLineKind.Sale || l.Kind == SaleLineKind.Return) && completed.Any(s => s.Id == l.SaleId))
                .GroupBy(l => new { l.ItemId, l.Plu, l.Description }).Select(g => new { g.Key, Units = g.Sum(l => l.Quantity), Net = g.Sum(l => l.LineTotal) })
                .OrderByDescending(x => x.Net).Take(10).ToListAsync())
            .Select(x => new DashboardItem(x.Key.ItemId, x.Key.Plu, x.Key.Description, x.Units, x.Net)).ToList();
        var byCashier = (await completed.GroupBy(s => new { s.StaffId, s.Staff }).Select(g => new { g.Key, Net = g.Sum(s => s.Subtotal - s.SubtotalDiscount), Count = g.Count() }).ToListAsync())
            .GroupBy(x => x.Key.StaffId?.ToString() ?? "name:" + (x.Key.Staff ?? "").ToUpperInvariant())
            .Select(g => new DashboardCashier(g.First().Key.StaffId, g.First().Key.Staff ?? "(none)", g.Sum(x => x.Net), g.Sum(x => x.Count)))
            .OrderByDescending(x => x.NetSales).ToList();
        var codes = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code);
        var byStation = (await completed.GroupBy(s => new { s.StoreId, s.StationNumber }).Select(g => new { g.Key, Net = g.Sum(s => s.Subtotal - s.SubtotalDiscount), Count = g.Count() }).ToListAsync())
            .Select(x => new DashboardStation(x.Key.StoreId, codes.GetValueOrDefault(x.Key.StoreId) ?? "", x.Key.StationNumber, x.Net, x.Count))
            .OrderBy(x => x.StoreCode, StringComparer.Ordinal).ThenBy(x => x.StationNumber).ToList();

        // Drawers closed in the period, with the cash that should be in them. Tills do not record a counted amount.
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var drawerRows = db.DrawerSessions.AsNoTracking().Include(d => d.Tenders).Where(d => d.ClosedAt >= start && d.ClosedAt < end);
        if (store is { } ds) drawerRows = drawerRows.Where(d => d.StoreId == ds);
        var cash = await db.TenderTypes.AsNoTracking().Where(t => t.Kind == TenderKind.Cash).Select(t => t.Name).ToListAsync();
        var drawers = (await drawerRows.OrderBy(d => d.ClosedAt).ToListAsync())
            .Select(d => new DashboardDrawer(codes.GetValueOrDefault(d.StoreId) ?? "", d.StationNumber, d.ClosedAt, d.ClosedBy,
                d.OpeningFloat + d.Tenders.Where(t => cash.Contains(t.TenderType)).Sum(t => t.Amount) + d.PayIns - d.PayOuts, null, null)).ToList();

        return Results.Ok(new Dashboard(from, to, totals, previous, byStore, byDay, byHour, paymentMix, topItems, byCashier, byStation, drawers));
    }

    private static async Task<IResult> TillsAsync(ApiRequest api, IOptions<ServerOptions> options)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var stores = await (store is { } one ? db.Stores.Where(s => s.Id == one) : api.User.Visible(db.Stores)).AsNoTracking().ToDictionaryAsync(s => s.Id);
        var ids = stores.Keys.ToList();
        var stations = await db.Stations.AsNoTracking().Where(s => ids.Contains(s.StoreId)).ToListAsync();
        var links = await db.StationLinks.AsNoTracking().Where(l => ids.Contains(l.StoreId)).ToDictionaryAsync(l => l.StationId);
        var lastSales = await db.Sales.AsNoTracking().Where(s => ids.Contains(s.StoreId)).GroupBy(s => new { s.StoreId, s.StationNumber })
            .Select(g => new { g.Key, Last = g.Max(s => s.SoldAt) }).ToDictionaryAsync(x => (x.Key.StoreId, x.Key.StationNumber), x => x.Last);
        var open = (await db.DrawerSessions.AsNoTracking().Where(d => ids.Contains(d.StoreId) && d.ClosedAt == null).Select(d => new { d.StoreId, d.StationNumber }).ToListAsync())
            .Select(d => (d.StoreId, d.StationNumber)).ToHashSet();
        var list = new List<TillInfo>();
        foreach (var station in stations.OrderBy(s => stores[s.StoreId].Code, StringComparer.Ordinal).ThenBy(s => s.Number))
        {
            var info = await StationInfoAsync(db, station, options.Value);
            var today = await api.TodayAsync(station.StoreId);
            var start = today.ToDateTime(TimeOnly.MinValue);
            var todaySales = await db.Sales.AsNoTracking()
                .Where(s => s.StoreId == station.StoreId && s.StationNumber == station.Number && s.Status == SaleStatus.Completed && s.SoldAt >= start && s.SoldAt < start.AddDays(1))
                .GroupBy(_ => 1).Select(g => new { Net = g.Sum(s => s.Subtotal - s.SubtotalDiscount), Count = g.Count() }).FirstOrDefaultAsync();
            var link = links.GetValueOrDefault(station.Id);
            string status = info.Retired ? "retired"
                : !info.Enrolled || link?.LastUploadAtUtc is null ? "never"
                : info.FailedBatches > 0 ? "failed"
                : info.Behind ? "behind"
                : "ok";
            var s = stores[station.StoreId];
            list.Add(new TillInfo(station.Id, s.Id, s.Code, s.Name, station.Number, station.Name, info.Enrolled, info.Retired, info.AppVersion,
                ApiRequest.Utc(info.LastUploadAtUtc), ApiRequest.Utc(info.LastDownloadAtUtc), lastSales.TryGetValue((s.Id, station.Number), out var last) ? last : null,
                todaySales?.Net ?? 0, todaySales?.Count ?? 0, open.Contains((s.Id, station.Number)), info.FailedBatches, status));
        }
        return Results.Ok(list);
    }
}
