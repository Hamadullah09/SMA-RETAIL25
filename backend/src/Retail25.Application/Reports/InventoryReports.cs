using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Inventory;
using Retail25.Domain.Purchasing;

namespace Retail25.Application.Reports;

// ---------------------------------------------------------------------------------------------
// Stock valuation (guide p.24)
// ---------------------------------------------------------------------------------------------

public sealed record StockValuationRow(
    long? DepartmentId,
    string DepartmentName,
    int ProductCount,
    decimal UnitsOnHand,
    decimal CostValue,
    decimal RetailValue,
    decimal PotentialMargin);

public sealed record StockValuationResult(
    IReadOnlyList<StockValuationRow> Rows,
    decimal TotalUnits,
    decimal TotalCostValue,
    decimal TotalRetailValue);

/// <summary>
/// What the shelves are worth right now, at cost and at retail, by department.
/// <para>
/// Deliberately point-in-time rather than as-at-a-date: <c>OnHand</c> and <c>AvgCost</c> are current
/// state, and reconstructing a historical valuation would mean replaying the whole stock ledger
/// against a moving average — a different, much heavier report than the one the guide describes.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Reports.CostVisibility)]
public sealed record GetStockValuationQuery(long LocationId, long? DepartmentId = null)
    : IRequest<StockValuationResult>;

public sealed record StockValuationDetailRow(
    long ProductId,
    string StockCode,
    string Name,
    string DepartmentName,
    decimal OnHand,
    decimal AvgCost,
    decimal ExtendedCost,
    decimal RegularPrice,
    decimal ExtendedRetail);

public sealed record StockValuationDetailPage(IReadOnlyList<StockValuationDetailRow> Rows, int TotalCount);

/// <summary>The line-by-line drill-down behind the department summary.</summary>
[RequiresPermission(PermissionKeys.Reports.CostVisibility)]
public sealed record GetStockValuationDetailQuery(
    long LocationId,
    long? DepartmentId = null,
    int Skip = 0,
    int Take = 200) : IRequest<StockValuationDetailPage>;

[RequiresPermission(PermissionKeys.Reports.CostVisibility)]
public sealed record ExportStockValuationQuery(long LocationId, long? DepartmentId = null) : IRequest<string>;

// ---------------------------------------------------------------------------------------------
// Understock / overstock (guide p.25–27)
// ---------------------------------------------------------------------------------------------

public enum StockPosition
{
    Normal = 0,

    /// <summary>Already short: cover has fallen past the reorder point, or below zero.</summary>
    Understock = 1,

    Overstock = 2,

    /// <summary>
    /// Exactly on the reorder point — order now, but nothing has run out yet.
    /// <para>
    /// Split out from <see cref="Understock"/> because collapsing the two is what made the alert
    /// unreadable. Every product in the seeded catalogue holds one and reorders at one, so the
    /// dashboard reported 180 of 195 as understocked and there was no way to see which of them had
    /// actually run short. Both still need buying, and the purchase-order generator still orders on
    /// the same boundary — this only says which kind.
    /// </para>
    /// </summary>
    AtReorderPoint = 3,
}

public sealed record StockPositionRow(
    long ProductId,
    string StockCode,
    string Name,
    string DepartmentName,
    decimal OnHand,
    decimal OnOrder,
    int ReorderPoint,
    int BaseStock,
    decimal AvgWeeklySales,
    decimal WeeksOfSupply,
    StockPosition Position);

/// <summary>
/// Which items are running out and which are drowning the shelf, using the legacy heuristic the
/// parity matrix names: three weeks of sales, what is already on order, and the base stock level.
/// <para>
/// Understock is the reorder point being reached — the same trigger the purchase-order generator
/// already uses, so the two screens never disagree about what needs ordering.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Reports.Inventory)]
public sealed record GetStockPositionQuery(
    long LocationId,
    long? DepartmentId = null,
    StockPosition? Only = null) : IRequest<IReadOnlyList<StockPositionRow>>;

[RequiresPermission(PermissionKeys.Reports.Inventory)]
public sealed record ExportStockPositionQuery(GetStockPositionQuery Filter) : IRequest<string>;

// ---------------------------------------------------------------------------------------------
// On order (guide p.19)
// ---------------------------------------------------------------------------------------------

public sealed record OnOrderRow(
    long ProductId,
    string StockCode,
    string Name,
    string SupplierName,
    long PoNumber,
    decimal OrderQty,
    decimal QtyReceived,
    decimal QtyOutstanding,
    decimal CostEach,
    decimal ExpectedValue,
    DateOnly? PostedOn,
    DateOnly? DueOn);

/// <summary>Everything bought but not yet on the shelf — the other half of the reorder picture.</summary>
[RequiresPermission(PermissionKeys.Reports.Inventory)]
public sealed record GetOnOrderQuery(
    long LocationId,
    long? SupplierId = null,
    long? DepartmentId = null) : IRequest<IReadOnlyList<OnOrderRow>>;

[RequiresPermission(PermissionKeys.Reports.Inventory)]
public sealed record ExportOnOrderQuery(GetOnOrderQuery Filter) : IRequest<string>;

// ---------------------------------------------------------------------------------------------
// Stock received (guide p.21)
// ---------------------------------------------------------------------------------------------

public sealed record StockReceivedRow(
    DateTimeOffset OccurredAt,
    long? PoNumber,
    string SupplierName,
    string StockCode,
    string ProductName,
    decimal QtyReceived,
    decimal UnitCost,
    decimal ExtendedCost,
    decimal ReceiptFreightTotal);

public sealed record StockReceivedPage(
    IReadOnlyList<StockReceivedRow> Rows,
    int TotalCount,
    decimal TotalCost);

/// <summary>
/// What actually arrived, in a window, from the stock ledger rather than the purchase orders — a
/// receipt that was posted is a fact about stock, and the ledger is where facts about stock live.
/// <para>
/// Freight is shown as the receipt's total rather than allocated per line: the allocation is
/// computed transiently when a receipt posts and only ever lands in the item's moving-average cost,
/// so re-deriving it here would be a second, differently-rounded answer to the same question.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Reports.Inventory)]
public sealed record GetStockReceivedQuery(
    long LocationId,
    DateOnly From,
    DateOnly To,
    long? SupplierId = null,
    int Skip = 0,
    int Take = 200) : IRequest<StockReceivedPage>;

[RequiresPermission(PermissionKeys.Reports.Inventory)]
public sealed record ExportStockReceivedQuery(GetStockReceivedQuery Filter) : IRequest<string>;

// ---------------------------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------------------------

public sealed class InventoryReportHandlers
    : IRequestHandler<GetStockValuationQuery, StockValuationResult>,
      IRequestHandler<GetStockValuationDetailQuery, StockValuationDetailPage>,
      IRequestHandler<ExportStockValuationQuery, string>,
      IRequestHandler<GetStockPositionQuery, IReadOnlyList<StockPositionRow>>,
      IRequestHandler<ExportStockPositionQuery, string>,
      IRequestHandler<GetOnOrderQuery, IReadOnlyList<OnOrderRow>>,
      IRequestHandler<ExportOnOrderQuery, string>,
      IRequestHandler<GetStockReceivedQuery, StockReceivedPage>,
      IRequestHandler<ExportStockReceivedQuery, string>
{
    /// <summary>The legacy overstock window: three weeks of demand (parity matrix, guide p.25–27).</summary>
    private const int OverstockWeeks = 3;

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public InventoryReportHandlers(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<StockValuationResult> Handle(GetStockValuationQuery request, CancellationToken ct)
    {
        var products = await ValuationProducts(request.LocationId, request.DepartmentId).ToListAsync(ct);
        var departments = await DepartmentNamesAsync(ct);

        var rows = products
            .GroupBy(p => p.DepartmentId)
            .Select(g => new StockValuationRow(
                g.Key,
                g.Key is { } id && departments.TryGetValue(id, out var name) ? name : "(no department)",
                g.Count(),
                g.Sum(p => p.OnHand),
                g.Sum(p => p.OnHand * p.AvgCost),
                g.Sum(p => p.OnHand * p.RegularPrice),
                g.Sum(p => p.OnHand * p.RegularPrice) - g.Sum(p => p.OnHand * p.AvgCost)))
            .OrderByDescending(r => r.CostValue)
            .ToList();

        return new StockValuationResult(
            rows,
            rows.Sum(r => r.UnitsOnHand),
            rows.Sum(r => r.CostValue),
            rows.Sum(r => r.RetailValue));
    }

    public async Task<StockValuationDetailPage> Handle(GetStockValuationDetailQuery request, CancellationToken ct)
    {
        var query = ValuationProducts(request.LocationId, request.DepartmentId);

        var totalCount = await query.CountAsync(ct);
        var products = await query
            .OrderBy(p => p.StockCode)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 1000))
            .ToListAsync(ct);

        var departments = await DepartmentNamesAsync(ct);

        return new StockValuationDetailPage(products.Select(p => ToDetailRow(p, departments)).ToList(), totalCount);
    }

    public async Task<string> Handle(ExportStockValuationQuery request, CancellationToken ct)
    {
        var products = await ValuationProducts(request.LocationId, request.DepartmentId)
            .OrderBy(p => p.StockCode)
            .ToListAsync(ct);

        var departments = await DepartmentNamesAsync(ct);

        var csv = new CsvWriter().Header(
            "StockCode", "Name", "Department", "OnHand", "AvgCost", "ExtendedCost", "RegularPrice", "ExtendedRetail");

        foreach (var row in products.Select(p => ToDetailRow(p, departments)))
        {
            csv.Row(row.StockCode, row.Name, row.DepartmentName, row.OnHand, row.AvgCost, row.ExtendedCost, row.RegularPrice, row.ExtendedRetail);
        }

        return csv.ToString();
    }

    public async Task<IReadOnlyList<StockPositionRow>> Handle(GetStockPositionQuery request, CancellationToken ct)
    {
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.LocationId == request.LocationId && !p.IsDeleted)
            .Where(p => request.DepartmentId == null || p.DepartmentId == request.DepartmentId)
            .Select(p => new
            {
                p.Id,
                p.StockCode,
                p.Name,
                p.DepartmentId,
                p.OnHand,
                p.OnOrder,
                p.ReorderPoint,
                p.BaseStock,
            })
            .ToListAsync(ct);

        if (products.Count == 0)
        {
            return [];
        }

        // Three weeks of actual sale movements, read from the ledger rather than the sales tables:
        // a case-break or a transfer changes what the shelf can supply just as a sale does.
        var since = _clock.Now.AddDays(-7 * OverstockWeeks);
        var productIds = products.Select(p => p.Id).ToList();

        var sold = await _db.StockLedgerEntries.AsNoTracking()
            .Where(e => e.LocationId == request.LocationId)
            .Where(e => e.MovementType == MovementType.Sale)
            .Where(e => e.OccurredAt >= since)
            .Where(e => productIds.Contains(e.ProductId))
            .GroupBy(e => e.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(e => e.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => Math.Abs(x.Quantity), ct);

        var departments = await DepartmentNamesAsync(ct);
        var rows = new List<StockPositionRow>();

        foreach (var product in products)
        {
            var soldInWindow = sold.GetValueOrDefault(product.Id);
            var weekly = soldInWindow / OverstockWeeks;

            var position = StockPosition.Normal;

            // Was `OnHand <= ReorderPoint`, which ignored stock already on order and collapsed
            // "order now" into "already short". Both still need buying — the standing says which.
            var standing = ReorderPolicy.Assess(product.OnHand, product.OnOrder, 0m, product.ReorderPoint);

            if (standing == ReorderStanding.Below)
            {
                position = StockPosition.Understock;
            }
            else if (standing == ReorderStanding.AtPoint)
            {
                position = StockPosition.AtReorderPoint;
            }
            else if (product.OnHand + product.OnOrder > product.BaseStock + (weekly * OverstockWeeks))
            {
                position = StockPosition.Overstock;
            }

            if (request.Only is { } only && position != only)
            {
                continue;
            }

            if (request.Only is null && position == StockPosition.Normal)
            {
                continue;
            }

            rows.Add(new StockPositionRow(
                product.Id,
                product.StockCode,
                product.Name,
                product.DepartmentId is { } id && departments.TryGetValue(id, out var name) ? name : "(no department)",
                product.OnHand,
                product.OnOrder,
                product.ReorderPoint,
                product.BaseStock,
                Math.Round(weekly, 2),
                weekly == 0m ? 0m : Math.Round(product.OnHand / weekly, 1),
                position));
        }

        return rows.OrderBy(r => r.Position).ThenBy(r => r.StockCode, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<string> Handle(ExportStockPositionQuery request, CancellationToken ct)
    {
        var rows = await Handle(request.Filter, ct);

        var csv = new CsvWriter().Header(
            "Position", "StockCode", "Name", "Department", "OnHand", "OnOrder", "ReorderPoint",
            "BaseStock", "AvgWeeklySales", "WeeksOfSupply");

        foreach (var row in rows)
        {
            csv.Row(
                row.Position, row.StockCode, row.Name, row.DepartmentName, row.OnHand, row.OnOrder,
                row.ReorderPoint, row.BaseStock, row.AvgWeeklySales, row.WeeksOfSupply);
        }

        return csv.ToString();
    }

    public async Task<IReadOnlyList<OnOrderRow>> Handle(GetOnOrderQuery request, CancellationToken ct)
    {
        var orders = await _db.PurchaseOrders.AsNoTracking()
            .Where(o => o.LocationId == request.LocationId)
            .Where(o => o.Status == PurchaseOrderStatus.Posted || o.Status == PurchaseOrderStatus.PartiallyReceived)
            .Where(o => request.SupplierId == null || o.SupplierId == request.SupplierId)
            .Select(o => new { o.Id, o.PoNumber, o.SupplierId, o.PostedOn, o.DueOn })
            .ToListAsync(ct);

        if (orders.Count == 0)
        {
            return [];
        }

        var orderIds = orders.Select(o => o.Id).ToList();

        var lines = await _db.PurchaseOrderLines.AsNoTracking()
            .Where(l => orderIds.Contains(l.PurchaseOrderId))
            .Where(l => l.OrderQty > l.QtyReceived)
            .ToListAsync(ct);

        if (lines.Count == 0)
        {
            return [];
        }

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.StockCode, p.Name, p.DepartmentId })
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        var supplierIds = orders.Select(o => o.SupplierId).Distinct().ToList();
        var suppliers = await _db.Suppliers.AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Company, ct);

        var ordersById = orders.ToDictionary(o => o.Id);
        var rows = new List<OnOrderRow>();

        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                continue;
            }

            if (request.DepartmentId is { } department && product.DepartmentId != department)
            {
                continue;
            }

            var order = ordersById[line.PurchaseOrderId];
            var outstanding = line.OrderQty - line.QtyReceived;

            rows.Add(new OnOrderRow(
                product.Id,
                product.StockCode,
                product.Name,
                suppliers.GetValueOrDefault(order.SupplierId) ?? string.Empty,
                order.PoNumber,
                line.OrderQty,
                line.QtyReceived,
                outstanding,
                line.CostEach,
                outstanding * line.CostEach,
                order.PostedOn,
                order.DueOn));
        }

        return rows.OrderBy(r => r.SupplierName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.StockCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string> Handle(ExportOnOrderQuery request, CancellationToken ct)
    {
        var rows = await Handle(request.Filter, ct);

        var csv = new CsvWriter().Header(
            "Supplier", "PoNumber", "StockCode", "Name", "Ordered", "Received", "Outstanding",
            "CostEach", "ExpectedValue", "PostedOn", "DueOn");

        foreach (var row in rows)
        {
            csv.Row(
                row.SupplierName, row.PoNumber, row.StockCode, row.Name, row.OrderQty, row.QtyReceived,
                row.QtyOutstanding, row.CostEach, row.ExpectedValue, row.PostedOn, row.DueOn);
        }

        return csv.ToString();
    }

    public async Task<StockReceivedPage> Handle(GetStockReceivedQuery request, CancellationToken ct)
    {
        var (entries, totalCount, totalCost) = await ReceiptEntriesAsync(request, paged: true, ct);
        var rows = await ProjectReceiptsAsync(entries, request.SupplierId, ct);

        return new StockReceivedPage(rows, totalCount, totalCost);
    }

    public async Task<string> Handle(ExportStockReceivedQuery request, CancellationToken ct)
    {
        var (entries, _, _) = await ReceiptEntriesAsync(request.Filter, paged: false, ct);
        var rows = await ProjectReceiptsAsync(entries, request.Filter.SupplierId, ct);

        var csv = new CsvWriter().Header(
            "Received", "PoNumber", "Supplier", "StockCode", "Product", "Quantity",
            "UnitCost", "ExtendedCost", "ReceiptFreight");

        foreach (var row in rows)
        {
            csv.Row(
                row.OccurredAt, row.PoNumber, row.SupplierName, row.StockCode, row.ProductName,
                row.QtyReceived, row.UnitCost, row.ExtendedCost, row.ReceiptFreightTotal);
        }

        return csv.ToString();
    }

    // -----------------------------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A whole-days window as UTC instants. Shared because every date-filtered report needs it and
    /// getting it wrong fails loudly at the database rather than quietly in the numbers.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) DayRangeUtc(DateOnly from, DateOnly to)
        => (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero));

    private IQueryable<Domain.Catalog.Product> ValuationProducts(long locationId, long? departmentId)
        => _db.Products.AsNoTracking()
            .Where(p => p.LocationId == locationId && !p.IsDeleted)
            .Where(p => departmentId == null || p.DepartmentId == departmentId);

    private async Task<Dictionary<long, string>> DepartmentNamesAsync(CancellationToken ct)
        => await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);

    private static StockValuationDetailRow ToDetailRow(
        Domain.Catalog.Product product,
        IReadOnlyDictionary<long, string> departments)
        => new(
            product.Id,
            product.StockCode,
            product.Name,
            product.DepartmentId is { } id && departments.TryGetValue(id, out var name) ? name : "(no department)",
            product.OnHand,
            product.AvgCost,
            product.OnHand * product.AvgCost,
            product.RegularPrice,
            product.OnHand * product.RegularPrice);

    private async Task<(List<StockLedgerEntry> Entries, int TotalCount, decimal TotalCost)> ReceiptEntriesAsync(
        GetStockReceivedQuery request,
        bool paged,
        CancellationToken ct)
    {
        // Anchored to UTC deliberately. DateOnly.ToDateTime yields Kind=Unspecified, which converts
        // to DateTimeOffset using the *server's* local offset — so a report run on a machine set to
        // anything but UTC would answer for a window shifted by that offset. It read as a quiet
        // wrong answer rather than an error, which is the worse of the two.
        //
        // Under Npgsql this also threw outright, because timestamptz accepts no offset but zero.
        // SQL Server's datetimeoffset accepts any of them, so the engine no longer catches this —
        // the anchoring is now the only thing that does.
        var (from, to) = DayRangeUtc(request.From, request.To);

        var query = _db.StockLedgerEntries.AsNoTracking()
            .Where(e => e.LocationId == request.LocationId)
            .Where(e => e.MovementType == MovementType.Receipt)
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to);

        var totalCount = await query.CountAsync(ct);
        var totalCost = await query.SumAsync(e => (decimal?)(e.Quantity * e.UnitCost), ct) ?? 0m;

        var ordered = query.OrderByDescending(e => e.OccurredAt);

        var entries = paged
            ? await ordered
                .Skip(Math.Max(0, request.Skip))
                .Take(Math.Clamp(request.Take, 1, 1000))
                .ToListAsync(ct)
            : await ordered.ToListAsync(ct);

        return (entries, totalCount, totalCost);
    }

    private async Task<List<StockReceivedRow>> ProjectReceiptsAsync(
        List<StockLedgerEntry> entries,
        long? supplierFilter,
        CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var receiptIds = entries.Where(e => e.ReferenceId.HasValue).Select(e => e.ReferenceId!.Value).Distinct().ToList();

        var receipts = await _db.PurchaseOrderReceipts.AsNoTracking()
            .Where(r => receiptIds.Contains(r.Id))
            .Select(r => new { r.Id, r.PurchaseOrderId, r.FreightTotal })
            .ToDictionaryAsync(r => r.Id, r => r, ct);

        var orderIds = receipts.Values.Select(r => r.PurchaseOrderId).Distinct().ToList();
        var orders = await _db.PurchaseOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.PoNumber, o.SupplierId })
            .ToDictionaryAsync(o => o.Id, o => o, ct);

        var supplierIds = orders.Values.Select(o => o.SupplierId).Distinct().ToList();
        var suppliers = await _db.Suppliers.AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Company, ct);

        var productIds = entries.Select(e => e.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.StockCode, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        var rows = new List<StockReceivedRow>();

        foreach (var entry in entries)
        {
            var receipt = entry.ReferenceId is { } referenceId ? receipts.GetValueOrDefault(referenceId) : null;
            var order = receipt is not null ? orders.GetValueOrDefault(receipt.PurchaseOrderId) : null;

            if (supplierFilter is { } wanted && order?.SupplierId != wanted)
            {
                continue;
            }

            var product = products.GetValueOrDefault(entry.ProductId);

            rows.Add(new StockReceivedRow(
                entry.OccurredAt,
                order?.PoNumber,
                order is not null ? suppliers.GetValueOrDefault(order.SupplierId) ?? string.Empty : string.Empty,
                product?.StockCode ?? string.Empty,
                product?.Name ?? string.Empty,
                entry.Quantity,
                entry.UnitCost,
                entry.Quantity * entry.UnitCost,
                receipt?.FreightTotal ?? 0m));
        }

        return rows;
    }
}
