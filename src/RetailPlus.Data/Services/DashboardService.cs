using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Services;

/// <summary>One day of the week chart.</summary>
public sealed record DaySales(DateOnly Day, decimal Total, int Customers);

/// <summary>A share of the payments (cash, card, account …).</summary>
public sealed record TenderShare(string Name, decimal Amount, decimal Percent);

/// <summary>An item that needs ordering.</summary>
public sealed record LowStockItem(int Id, string Plu, string Name, decimal OnHand, decimal ReorderPoint);

/// <summary>A recent sale for the dashboard list.</summary>
public sealed record RecentSale(long InvoiceNumber, DateTime SoldAt, string? ClientName, decimal Total, int Lines, string Tenders);

/// <summary>Everything the Dashboard shows, read in one go.</summary>
public sealed record DashboardData(
    decimal SalesToday,
    int CustomersToday,
    decimal SalesYesterday,
    decimal AverageSaleToday,
    IReadOnlyList<DaySales> Week,
    IReadOnlyList<TenderShare> Payments,
    bool PaymentsAreToday,
    int ItemCount,
    int ItemsInStock,
    int ItemsLow,
    int ItemsOut,
    IReadOnlyList<LowStockItem> ToReorder,
    int ClientCount,
    decimal OwedToStore,
    int OpenInvoices,
    int OpenPurchaseOrders,
    int OpenCustomerOrders,
    int SuspendedSales,
    DateTime? DrawerOpenedAt,
    decimal DrawerReceipts,
    decimal ExpectedCash,
    IReadOnlyList<RecentSale> Recent,
    IReadOnlyList<(string Name, decimal Units, decimal Sales)> BestSellers);

/// <summary>Figures for the Dashboard screen.</summary>
public sealed class DashboardService(DataContext data)
{
    public async Task<DashboardData> LoadAsync(SessionInfo session, DateTime? now = null)
    {
        var clock = now ?? DateTime.Now;
        var today = clock.Date;
        var weekStart = today.AddDays(-6);
        await using var db = data.Create();
        var sales = db.Sales.AsNoTracking().Where(s => s.LocationId == session.LocationId && s.Status == SaleStatus.Completed);

        var byDay = await sales.Where(s => s.SoldAt >= weekStart && s.SoldAt < today.AddDays(1))
            .GroupBy(s => s.SoldAt.Date).Select(g => new { Day = g.Key, Total = g.Sum(s => s.Total), Count = g.Count() }).ToListAsync();
        var week = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i))
            .Select(d => byDay.FirstOrDefault(x => x.Day == d) is { } x ? new DaySales(DateOnly.FromDateTime(d), x.Total, x.Count) : new DaySales(DateOnly.FromDateTime(d), 0, 0))
            .ToList();
        var todayRow = week[^1];
        var yesterday = week[^2];

        // How customers paid: today, or the last 30 days when nothing was sold yet today.
        bool paymentsToday = todayRow.Customers > 0;
        var since = paymentsToday ? today : today.AddDays(-30);
        var paid = await db.SalePayments.AsNoTracking()
            .Where(p => sales.Any(s => s.Id == p.SaleId && s.SoldAt >= since))
            .GroupBy(p => p.TenderType).Select(g => new { Name = g.Key, Amount = g.Sum(p => p.Amount) }).ToListAsync();
        decimal paidTotal = paid.Sum(p => Math.Abs(p.Amount));
        var payments = paid.Where(p => p.Amount != 0).OrderByDescending(p => p.Amount)
            .Select(p => new TenderShare(p.Name, p.Amount, paidTotal == 0 ? 0 : Math.Round(Math.Abs(p.Amount) / paidTotal * 100m, 0)))
            .ToList();

        var items = db.Items.AsNoTracking().Where(i => i.LocationId == session.LocationId && !i.IsDeleted
            && (i.ItemType == ItemType.Standard || i.ItemType == ItemType.Matrix || i.ItemType == ItemType.Serialized || i.ItemType == ItemType.Kit));
        int itemCount = await db.Items.CountAsync(i => i.LocationId == session.LocationId && !i.IsDeleted);
        int stocked = await items.CountAsync();
        int outOfStock = await items.CountAsync(i => i.OnHand <= 0);
        int low = await items.CountAsync(i => i.OnHand > 0 && i.ReorderPoint > 0 && i.OnHand <= i.ReorderPoint);
        var toReorder = await items.Where(i => (i.ReorderPoint > 0 && i.OnHand <= i.ReorderPoint) || i.OnHand <= 0)
            .OrderBy(i => i.OnHand).ThenBy(i => i.Plu).Take(8)
            .Select(i => new LowStockItem(i.Id, i.Plu, i.Name, i.OnHand, i.ReorderPoint)).ToListAsync();

        int clients = await db.Clients.CountAsync(c => !c.IsDeleted);
        var openInvoices = db.Invoices.AsNoTracking().Where(i => i.Balance > 0 && !i.IsVoided && i.Kind != InvoiceKind.Quote);
        decimal owed = await openInvoices.SumAsync(i => (decimal?)i.Balance) ?? 0;
        int openInvoiceCount = await openInvoices.CountAsync();
        int openPos = await db.PurchaseOrders.CountAsync(p => p.LocationId == session.LocationId && p.Status != PurchaseOrderStatus.Received);
        int customerOrders = await db.CustomerOrders.CountAsync(o => !o.Paid);
        int suspended = await db.SuspendedSales.CountAsync();

        var drawer = await db.DrawerSessions.AsNoTracking().Include(d => d.Tenders)
            .Where(d => d.StationNumber == session.StationNumber && d.ClosedAt == null).OrderByDescending(d => d.Id).FirstOrDefaultAsync();
        decimal expectedCash = 0;
        if (drawer is not null)
        {
            var cashNames = await db.TenderTypes.AsNoTracking().Where(t => t.Kind == TenderKind.Cash).Select(t => t.Name).ToListAsync();
            expectedCash = drawer.OpeningFloat + drawer.Tenders.Where(t => cashNames.Contains(t.TenderType)).Sum(t => t.Amount) + drawer.PayIns - drawer.PayOuts;
        }

        var recentRows = await sales.OrderByDescending(s => s.SoldAt).Take(6)
            .Select(s => new { s.InvoiceNumber, s.SoldAt, s.ClientName, s.Total, Lines = s.Lines.Count, Tenders = s.Payments.Select(p => p.TenderType).ToList() })
            .ToListAsync();
        var recent = recentRows.Select(r => new RecentSale(r.InvoiceNumber, r.SoldAt, r.ClientName, r.Total, r.Lines, string.Join(", ", r.Tenders.Distinct()))).ToList();

        var best = await db.SaleLines.AsNoTracking()
            .Where(l => l.Kind == SaleLineKind.Sale && sales.Any(s => s.Id == l.SaleId && s.SoldAt >= weekStart))
            .GroupBy(l => l.Description).Select(g => new { Name = g.Key, Units = g.Sum(l => l.Quantity), Sales = g.Sum(l => l.LineTotal) })
            .OrderByDescending(x => x.Sales).Take(5).ToListAsync();

        return new DashboardData(
            todayRow.Total, todayRow.Customers, yesterday.Total, todayRow.Customers == 0 ? 0 : Math.Round(todayRow.Total / todayRow.Customers, 2),
            week, payments, paymentsToday, itemCount, stocked - outOfStock - low, low, outOfStock, toReorder, clients, owed, openInvoiceCount,
            openPos, customerOrders, suspended, drawer?.OpenedAt, drawer?.TotalReceipts ?? 0, expectedCash, recent,
            best.Select(b => (b.Name, b.Units, b.Sales)).ToList());
    }
}
