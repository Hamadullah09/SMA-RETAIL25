using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Reports;

/// <summary>What the rows are grouped by. One query answers several legacy reports (guide p.15–18).</summary>
public enum SalesAnalysisGroupBy
{
    Product = 0,
    Department = 1,
    Client = 2,
    Day = 3,
    Week = 4,
    Month = 5,
}

public sealed record SalesAnalysisRow(
    string GroupKey,
    string GroupLabel,
    decimal Quantity,
    decimal NetSales,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal? Cogs,
    decimal? GrossMargin,
    decimal? GrossMarginPct,
    int TransactionCount);

public sealed record SalesAnalysisResult(
    IReadOnlyList<SalesAnalysisRow> Rows,
    decimal GrandQuantity,
    decimal GrandNetSales,
    decimal? GrandCogs,
    decimal? GrandGrossMargin);

/// <summary>
/// Sales by product, department, client or period — and, with <c>Top</c> set, the top-sellers list
/// (guide p.15–18). One query rather than four because they differ only by what the rows are
/// grouped on; four handlers would be four places for the same margin arithmetic to drift.
/// <para>
/// <paramref name="HideCost"/> is set by the controller, not the caller: the cost-free route is
/// gated on <see cref="PermissionKeys.Reports.Sales"/> and the full one on
/// <see cref="PermissionKeys.Reports.CostVisibility"/>, so a user without cost visibility gets a
/// response that never contained the numbers rather than one that hides them client-side.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Reports.Sales)]
public sealed record SalesAnalysisQuery(
    long LocationId,
    DateOnly From,
    DateOnly To,
    SalesAnalysisGroupBy GroupBy = SalesAnalysisGroupBy.Product,
    long? DepartmentId = null,
    long? ProductId = null,
    long? CustomerId = null,
    bool IncludeVoided = false,
    int? Top = null,
    string? SortBy = null,
    bool HideCost = false) : IRequest<SalesAnalysisResult>;

[RequiresPermission(PermissionKeys.Reports.Sales)]
public sealed record ExportSalesAnalysisQuery(SalesAnalysisQuery Filter) : IRequest<string>;

/// <summary>
/// The same analysis with cost and margin included. A separate request type purely so the
/// permission attribute can differ — the arithmetic is shared, but seeing what things cost is a
/// different grant from seeing what they sold for.
/// </summary>
[RequiresPermission(PermissionKeys.Reports.CostVisibility)]
public sealed record MarginAnalysisQuery(SalesAnalysisQuery Inner) : IRequest<SalesAnalysisResult>;

[RequiresPermission(PermissionKeys.Reports.CostVisibility)]
public sealed record ExportMarginAnalysisQuery(SalesAnalysisQuery Filter) : IRequest<string>;

public sealed class SalesAnalysisHandlers
    : IRequestHandler<SalesAnalysisQuery, SalesAnalysisResult>,
      IRequestHandler<ExportSalesAnalysisQuery, string>,
      IRequestHandler<MarginAnalysisQuery, SalesAnalysisResult>,
      IRequestHandler<ExportMarginAnalysisQuery, string>
{
    private readonly IApplicationDbContext _db;

    public SalesAnalysisHandlers(IApplicationDbContext db) => _db = db;

    public async Task<SalesAnalysisResult> Handle(SalesAnalysisQuery request, CancellationToken ct)
    {
        var facts = await LoadAsync(request, ct);

        if (facts.Count == 0)
        {
            return new SalesAnalysisResult([], 0m, 0m, request.HideCost ? null : 0m, request.HideCost ? null : 0m);
        }

        var labels = await LabelsAsync(request.GroupBy, facts, ct);

        var rows = facts
            .GroupBy(f => f.GroupKey)
            .Select(g =>
            {
                var net = g.Sum(f => f.NetSales);
                var cogs = g.Sum(f => f.Cogs);
                var margin = net - cogs;

                return new SalesAnalysisRow(
                    g.Key,
                    labels.TryGetValue(g.Key, out var label) ? label : g.Key,
                    g.Sum(f => f.Quantity),
                    net,
                    g.Sum(f => f.Discount),
                    g.Sum(f => f.Tax),
                    request.HideCost ? null : cogs,
                    request.HideCost ? null : margin,
                    request.HideCost ? null : (net == 0m ? 0m : Math.Round(margin / net * 100m, 2)),
                    g.Select(f => f.TransactionId).Distinct().Count());
            })
            .ToList();

        rows = Sort(rows, request.SortBy, request.GroupBy);

        if (request.Top is { } top && top > 0)
        {
            rows = rows.Take(top).ToList();
        }

        var grandNet = facts.Sum(f => f.NetSales);
        var grandCogs = facts.Sum(f => f.Cogs);

        return new SalesAnalysisResult(
            rows,
            facts.Sum(f => f.Quantity),
            grandNet,
            request.HideCost ? null : grandCogs,
            request.HideCost ? null : grandNet - grandCogs);
    }

    /// <summary>Cost forced on — the permission attribute, not the caller, decides who reaches this.</summary>
    public Task<SalesAnalysisResult> Handle(MarginAnalysisQuery request, CancellationToken ct)
        => Handle(request.Inner with { HideCost = false }, ct);

    public Task<string> Handle(ExportMarginAnalysisQuery request, CancellationToken ct)
        => Handle(new ExportSalesAnalysisQuery(request.Filter with { HideCost = false }), ct);

    public async Task<string> Handle(ExportSalesAnalysisQuery request, CancellationToken ct)
    {
        var result = await Handle(request.Filter with { Top = null }, ct);
        var hideCost = request.Filter.HideCost;

        var csv = new CsvWriter();

        if (hideCost)
        {
            csv.Header("Group", "Quantity", "NetSales", "Discount", "Tax", "Transactions");

            foreach (var row in result.Rows)
            {
                csv.Row(row.GroupLabel, row.Quantity, row.NetSales, row.DiscountTotal, row.TaxTotal, row.TransactionCount);
            }
        }
        else
        {
            csv.Header("Group", "Quantity", "NetSales", "Discount", "Tax", "Cogs", "GrossMargin", "GrossMarginPct", "Transactions");

            foreach (var row in result.Rows)
            {
                csv.Row(
                    row.GroupLabel, row.Quantity, row.NetSales, row.DiscountTotal, row.TaxTotal,
                    row.Cogs, row.GrossMargin, row.GrossMarginPct, row.TransactionCount);
            }
        }

        return csv.ToString();
    }

    /// <summary>
    /// One flattened line-plus-parent fact per sale line, pulled once. Grouping happens in memory
    /// because the period buckets (ISO week, month) and the department lookup are C# concerns —
    /// the same reason <c>SalesLogQuery</c> projects its lookups rather than joining in SQL.
    /// </summary>
    private async Task<List<Fact>> LoadAsync(SalesAnalysisQuery request, CancellationToken ct)
    {
        var transactions = _db.SalesTransactions.AsNoTracking()
            .Where(t => t.LocationId == request.LocationId)
            .Where(t => t.BusinessDate >= request.From && t.BusinessDate <= request.To)
            .Where(t => !t.IsTraining);

        if (!request.IncludeVoided)
        {
            transactions = transactions.Where(t => t.Status == TransactionStatus.Completed);
        }

        if (request.CustomerId is { } customerId)
        {
            transactions = transactions.Where(t => t.CustomerId == customerId);
        }

        var joined = await (
            from line in _db.SaleLines.AsNoTracking()
            join transaction in transactions on line.TransactionId equals transaction.Id
            where line.LineType == LineType.Sale || line.LineType == LineType.Return
            select new
            {
                line.ProductId,
                line.Quantity,
                line.ExtendedNet,
                line.ProratedAdjustment,
                line.UnitPrice,
                line.DiscountPct,
                line.UnitCostSnapshot,
                line.Tax1Amount,
                line.Tax2Amount,
                TransactionId = transaction.Id,
                transaction.BusinessDate,
                transaction.CustomerId,
            }).ToListAsync(ct);

        if (joined.Count == 0)
        {
            return [];
        }

        var productIds = joined.Select(j => j.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.DepartmentId })
            .ToDictionaryAsync(p => p.Id, p => p.DepartmentId, ct);

        var facts = new List<Fact>(joined.Count);

        foreach (var row in joined)
        {
            var departmentId = products.TryGetValue(row.ProductId, out var d) ? d : null;

            if (request.DepartmentId is { } wantedDepartment && departmentId != wantedDepartment)
            {
                continue;
            }

            if (request.ProductId is { } wantedProduct && row.ProductId != wantedProduct)
            {
                continue;
            }

            // The line's own discount is the gap between what it would have listed at and what it
            // actually settled at — the sale-level adjustment is already prorated onto the line.
            var gross = row.UnitPrice * row.Quantity;
            var discount = gross - row.ExtendedNet;

            facts.Add(new Fact(
                GroupKeyFor(request.GroupBy, row.ProductId, departmentId, row.CustomerId, row.BusinessDate),
                row.Quantity,
                row.ExtendedNet,
                discount < 0m ? 0m : discount,
                row.Tax1Amount + row.Tax2Amount,
                row.UnitCostSnapshot * row.Quantity,
                row.TransactionId));
        }

        return facts;
    }

    private static string GroupKeyFor(
        SalesAnalysisGroupBy groupBy,
        long productId,
        long? departmentId,
        long? customerId,
        DateOnly businessDate) => groupBy switch
        {
            SalesAnalysisGroupBy.Product => productId.ToString(CultureInfo.InvariantCulture),
            SalesAnalysisGroupBy.Department => departmentId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            SalesAnalysisGroupBy.Client => customerId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            SalesAnalysisGroupBy.Day => businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SalesAnalysisGroupBy.Week => WeekKey(businessDate),
            SalesAnalysisGroupBy.Month => businessDate.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            _ => productId.ToString(CultureInfo.InvariantCulture),
        };

    private static string WeekKey(DateOnly date)
    {
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        var week = ISOWeek.GetWeekOfYear(dateTime);
        return $"{ISOWeek.GetYear(dateTime)}-W{week:D2}";
    }

    /// <summary>Turns the grouping key into something a person reads, in one batched lookup.</summary>
    private async Task<Dictionary<string, string>> LabelsAsync(
        SalesAnalysisGroupBy groupBy,
        List<Fact> facts,
        CancellationToken ct)
    {
        var keys = facts.Select(f => f.GroupKey).Distinct().ToList();

        switch (groupBy)
        {
            case SalesAnalysisGroupBy.Product:
            {
                var ids = keys.Where(k => long.TryParse(k, out _)).Select(long.Parse).ToList();
                var products = await _db.Products.AsNoTracking()
                    .Where(p => ids.Contains(p.Id))
                    .Select(p => new { p.Id, p.StockCode, p.Name })
                    .ToListAsync(ct);

                return products.ToDictionary(p => p.Id.ToString(CultureInfo.InvariantCulture), p => $"{p.StockCode} — {p.Name}");
            }

            case SalesAnalysisGroupBy.Department:
            {
                var ids = keys.Where(k => long.TryParse(k, out _)).Select(long.Parse).ToList();
                var departments = await _db.Departments.AsNoTracking()
                    .Where(d => ids.Contains(d.Id))
                    .Select(d => new { d.Id, d.Name })
                    .ToListAsync(ct);

                var labels = departments.ToDictionary(d => d.Id.ToString(CultureInfo.InvariantCulture), d => d.Name);
                labels[string.Empty] = "(no department)";
                return labels;
            }

            case SalesAnalysisGroupBy.Client:
            {
                var ids = keys.Where(k => long.TryParse(k, out _)).Select(long.Parse).ToList();
                var customers = await _db.Customers.AsNoTracking()
                    .Where(c => ids.Contains(c.Id))
                    .Select(c => new { c.Id, c.FullName })
                    .ToListAsync(ct);

                var labels = customers.ToDictionary(c => c.Id.ToString(CultureInfo.InvariantCulture), c => c.FullName);
                labels[string.Empty] = "(walk-in)";
                return labels;
            }

            default:
                // Period keys are already readable.
                return keys.ToDictionary(k => k, k => k);
        }
    }

    private static List<SalesAnalysisRow> Sort(
        List<SalesAnalysisRow> rows,
        string? sortBy,
        SalesAnalysisGroupBy groupBy) => sortBy?.ToLowerInvariant() switch
        {
            "quantity" => rows.OrderByDescending(r => r.Quantity).ToList(),
            "margin" => rows.OrderByDescending(r => r.GrossMargin ?? 0m).ToList(),
            "cogs" => rows.OrderByDescending(r => r.Cogs ?? 0m).ToList(),
            "label" => rows.OrderBy(r => r.GroupLabel, StringComparer.OrdinalIgnoreCase).ToList(),
            // A period report reads forwards in time; everything else leads with the biggest number.
            _ when groupBy is SalesAnalysisGroupBy.Day or SalesAnalysisGroupBy.Week or SalesAnalysisGroupBy.Month
                => rows.OrderBy(r => r.GroupKey, StringComparer.Ordinal).ToList(),
            _ => rows.OrderByDescending(r => r.NetSales).ToList(),
        };

    private sealed record Fact(
        string GroupKey,
        decimal Quantity,
        decimal NetSales,
        decimal Discount,
        decimal Tax,
        decimal Cogs,
        long TransactionId);
}
