using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Reports;

/// <summary>
/// The production reports: orders by status, cost and variances, work in progress, material usage against standard,
/// output per work centre, scrap and capacity load. Each covers one store (null: every store); the dated ones cover the
/// days from <c>from</c> to <c>to</c>. Cost reports need <see cref="Permissions.ProductionCosting"/>, the rest
/// <see cref="Permissions.ProductionView"/>.
/// </summary>
public sealed class ProductionReports(DataContext data)
{
    public static readonly IReadOnlyList<ReportDefinition> All =
    [
        new("production-status", "Production", "Production orders by status", false, Permissions.ProductionView),
        new("production-costs", "Production", "Production cost and variances", true, Permissions.ProductionCosting),
        new("production-wip", "Production", "Work in progress value", false, Permissions.ProductionCosting),
        new("production-materials", "Production", "Material usage against standard", true, Permissions.ProductionView),
        new("production-output", "Production", "Output per work centre", true, Permissions.ProductionView),
        new("production-scrap", "Production", "Scrap", true, Permissions.ProductionView),
        new("production-capacity", "Production", "Capacity load by week", false, Permissions.ProductionView),
    ];

    public async Task<ReportTable> RunAsync(string key, Guid? store, DateOnly from, DateOnly to) => key switch
    {
        "production-status" => await StatusAsync(store),
        "production-costs" => await CostsAsync(store, from, to),
        "production-wip" => await WipAsync(store),
        "production-materials" => await MaterialsAsync(store, from, to),
        "production-output" => await OutputAsync(store, from, to),
        "production-scrap" => await ScrapAsync(store, from, to),
        "production-capacity" => await CapacityAsync(store, from),
        _ => throw new BusinessRuleException($"There is no production report {key}."),
    };

    private static string Period(DateOnly from, DateOnly to) => from == to ? $"{from:d}" : $"{from:d} to {to:d}";

    private async Task<ReportTable> StatusAsync(Guid? store)
    {
        var rows = await new ProductionService(data).ListAsync(store, take: 100_000);
        var table = new ReportTable
        {
            Title = "Production orders by status",
            Columns = [new("Status"), new("Orders", ColumnKind.Count, true), new("Quantity", ColumnKind.Quantity, true), new("Made", ColumnKind.Quantity, true),
                new("Oldest planned start", ColumnKind.Date)],
        };
        foreach (var g in rows.GroupBy(r => r.Status).OrderBy(g => g.Key))
            table.Rows.Add([ProductionService.Describe(g.Key), g.Count(), g.Sum(r => r.Quantity), g.Sum(r => r.Received), g.Min(r => r.PlannedStart)]);
        return table;
    }

    /// <summary>Orders completed or closed in the period, their standard against actual cost and the variances.</summary>
    private async Task<ReportTable> CostsAsync(Guid? store, DateOnly from, DateOnly to)
    {
        var table = new ReportTable
        {
            Title = "Production cost and variances", Subtitle = $"Orders completed {Period(from, to)}",
            Columns = [new("Order", ColumnKind.Count), new("Product"), new("Made", ColumnKind.Quantity, true), new("Standard", ColumnKind.Money, true),
                new("Actual", ColumnKind.Money, true), new("Material usage", ColumnKind.Money, true), new("Material price", ColumnKind.Money, true),
                new("Labour", ColumnKind.Money, true), new("Machine", ColumnKind.Money, true), new("Overhead", ColumnKind.Money, true),
                new("Total variance", ColumnKind.Money, true), new("Left in work in progress", ColumnKind.Money, true)],
        };
        foreach (var (order, progress) in await CompletedAsync(store, from, to))
        {
            var v = ProductionCostingService.Variances(order, progress);
            table.Rows.Add([order.Number, $"{order.ProductCode} {order.ProductName}", v.OutputQuantity, v.StandardTotal, v.ActualTotal, v.MaterialUsageVariance,
                v.MaterialPriceVariance, v.LabourVariance, v.MachineVariance, v.OverheadVariance, v.TotalVariance, progress.Costs.WorkInProgress]);
        }
        return table;
    }

    private async Task<ReportTable> WipAsync(Guid? store)
    {
        var table = new ReportTable
        {
            Title = "Work in progress value", Subtitle = "Released, started and completed orders not yet closed",
            Columns = [new("Order", ColumnKind.Count), new("Product"), new("Status"), new("Material", ColumnKind.Money, true), new("Labour, machine, overhead", ColumnKind.Money, true),
                new("Received", ColumnKind.Money, true), new("Work in progress", ColumnKind.Money, true)],
        };
        foreach (var r in await new ProductionCostingService(data).WorkInProgressAsync(store))
            table.Rows.Add([r.Number, $"{r.ProductCode} {r.ProductName}", ProductionService.Describe(r.Status), r.Material, r.Conversion, r.Received, r.WorkInProgress]);
        return table;
    }

    private async Task<ReportTable> MaterialsAsync(Guid? store, DateOnly from, DateOnly to)
    {
        var table = new ReportTable
        {
            Title = "Material usage against standard", Subtitle = $"Orders completed {Period(from, to)}",
            Columns = [new("Component"), new("Description"), new("Standard quantity", ColumnKind.Quantity, true), new("Used", ColumnKind.Quantity, true),
                new("Difference", ColumnKind.Quantity, true), new("Standard cost", ColumnKind.Money, true), new("Actual cost", ColumnKind.Money, true),
                new("Usage variance", ColumnKind.Money, true), new("Price variance", ColumnKind.Money, true)],
        };
        var all = new List<MaterialUsage>();
        foreach (var (order, progress) in await CompletedAsync(store, from, to)) all.AddRange(ProductionCostingService.Variances(order, progress).Materials);
        foreach (var g in all.GroupBy(m => m.StockCode, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            decimal std = g.Sum(m => m.StandardQuantity), used = g.Sum(m => m.ActualQuantity);
            table.Rows.Add([g.Key, g.First().Description, std, used, used - std, g.Sum(m => m.StandardCost), g.Sum(m => m.ActualCost), g.Sum(m => m.UsageVariance),
                g.Sum(m => m.PriceVariance)]);
        }
        return table;
    }

    private async Task<ReportTable> OutputAsync(Guid? store, DateOnly from, DateOnly to)
    {
        var (start, end) = (from.ToDateTime(TimeOnly.MinValue), to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        await using var db = data.Create();
        var reports = await db.ProductionReports.AsNoTracking().Where(r => r.At >= start && r.At < end && (store == null || r.StoreId == store)).ToListAsync();
        var orderIds = reports.Select(r => r.ProductionOrderId).Distinct().ToList();
        var ops = await db.ProductionOrderOperations.AsNoTracking().Where(o => orderIds.Contains(o.ProductionOrderId))
            .Select(o => new { o.ProductionOrderId, o.Sequence, o.WorkCentreId, o.WorkCentreName }).ToListAsync();
        var centres = await db.WorkCentres.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Code + " " + w.Name);
        var table = new ReportTable
        {
            Title = "Output per work centre", Subtitle = Period(from, to),
            Columns = [new("Work centre"), new("Reports", ColumnKind.Count, true), new("Hours", ColumnKind.Quantity, true), new("Good", ColumnKind.Quantity, true),
                new("Scrap", ColumnKind.Quantity, true), new("Scrap %", ColumnKind.Percent), new("Good an hour", ColumnKind.Quantity), new("Cost applied", ColumnKind.Money, true)],
        };
        var rows = reports.Select(r =>
        {
            var op = ops.FirstOrDefault(o => o.ProductionOrderId == r.ProductionOrderId && o.Sequence == r.OperationSequence);
            var name = op?.WorkCentreId is Guid id && centres.TryGetValue(id, out var n) ? n : op?.WorkCentreName ?? "(none)";
            return (Centre: name, Report: r);
        });
        foreach (var g in rows.GroupBy(r => r.Centre).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            decimal hours = Math.Round(g.Sum(r => r.Report.Minutes) / 60m, 2), good = g.Sum(r => r.Report.GoodQuantity), scrap = g.Sum(r => r.Report.ScrapQuantity);
            table.Rows.Add([g.Key, g.Count(), hours, good, scrap, good + scrap == 0 ? 0m : Math.Round(scrap / (good + scrap) * 100m, 1), hours == 0 ? 0m : Math.Round(good / hours, 2),
                g.Sum(r => r.Report.LabourCost + r.Report.MachineCost + r.Report.OverheadCost)]);
        }
        return table;
    }

    private async Task<ReportTable> ScrapAsync(Guid? store, DateOnly from, DateOnly to)
    {
        var (start, end) = (from.ToDateTime(TimeOnly.MinValue), to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        await using var db = data.Create();
        var orders = await db.ProductionOrders.AsNoTracking().Where(o => store == null || o.StoreId == store).ToDictionaryAsync(o => o.Id);
        var table = new ReportTable
        {
            Title = "Scrap", Subtitle = Period(from, to),
            Columns = [new("When", ColumnKind.Date), new("Order", ColumnKind.Count), new("What"), new("Stock code"), new("Quantity", ColumnKind.Quantity, true),
                new("Value", ColumnKind.Money, true), new("By"), new("Note")],
        };
        var reports = await db.ProductionReports.AsNoTracking().Where(r => r.At >= start && r.At < end && r.ScrapQuantity > 0).ToListAsync();
        var rows = new List<object?[]>();
        foreach (var r in reports.Where(r => orders.ContainsKey(r.ProductionOrderId)))
        {
            var o = orders[r.ProductionOrderId];
            rows.Add([r.At, o.Number, $"At operation {r.OperationSequence}", o.ProductCode, r.ScrapQuantity, Money.Round(r.ScrapQuantity * o.StandardUnitCost), r.UserName, r.Note]);
        }
        var moves = await db.StockMovements.AsNoTracking().Where(m => m.Kind == StockMovementKind.ProductionScrap && m.At >= start && m.At < end && m.ProductionOrderId != null)
            .Join(db.Items.AsNoTracking(), m => m.ItemId, i => i.Id, (m, i) => new { m.At, Order = m.ProductionOrderId!.Value, i.Plu, m.Quantity, m.UnitCost, m.User, m.Reason })
            .ToListAsync();
        foreach (var m in moves.Where(m => orders.ContainsKey(m.Order)))
        {
            var o = orders[m.Order];
            bool output = string.Equals(m.Plu, o.ProductCode, StringComparison.OrdinalIgnoreCase);
            rows.Add([m.At, o.Number, output ? "Output thrown away" : "Material thrown away", m.Plu, -m.Quantity, Money.Round(-m.Quantity * m.UnitCost), m.User, m.Reason]);
        }
        table.Rows.AddRange(rows.OrderBy(r => (DateTime)r[0]!));
        return table;
    }

    private async Task<ReportTable> CapacityAsync(Guid? store, DateOnly from)
    {
        var table = new ReportTable
        {
            Title = "Capacity load by week", Subtitle = $"From the week of {from:d}, open production orders",
            Columns = [new("Work centre"), new("Week of", ColumnKind.Date), new("Capacity hours", ColumnKind.Quantity, true), new("Load hours", ColumnKind.Quantity, true),
                new("Load %", ColumnKind.Percent)],
        };
        List<Guid> stores;
        await using (var db = data.Create())
            stores = store is Guid s ? [s] : await db.Stores.AsNoTracking().OrderBy(x => x.Code).Select(x => x.Id).ToListAsync();
        var planning = new MrpService(data);
        foreach (var id in stores)
            foreach (var r in await planning.CapacityAsync(id, from))
                table.Rows.Add([$"{r.Code} {r.Name}", r.WeekStart, r.CapacityHours, r.LoadHours, r.LoadPercent]);
        return table;
    }

    /// <summary>Orders completed (or closed) in the period, with their progress.</summary>
    private async Task<List<(ProductionOrder Order, OrderProgress Progress)>> CompletedAsync(Guid? store, DateOnly from, DateOnly to)
    {
        var (start, end) = (from.ToDateTime(TimeOnly.MinValue), to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var service = new ProductionService(data);
        List<Guid> ids;
        await using (var db = data.Create())
            ids = await db.ProductionOrders.AsNoTracking()
                .Where(o => (o.Status == ProductionOrderStatus.Completed || o.Status == ProductionOrderStatus.Closed) && o.CompletedAt >= start && o.CompletedAt < end
                            && (store == null || o.StoreId == store))
                .OrderBy(o => o.Number).Select(o => o.Id).ToListAsync();
        var list = new List<(ProductionOrder, OrderProgress)>();
        foreach (var id in ids)
            if (await service.ProgressAsync(id) is { } progress) list.Add((progress.Order, progress));
        return list;
    }
}
