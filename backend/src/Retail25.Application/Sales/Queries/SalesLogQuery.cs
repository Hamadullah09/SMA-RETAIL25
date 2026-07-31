using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Sales.Queries;

public sealed record SalesLogRow(
    Guid Id,
    long TransactionNumber,
    DateTimeOffset CompletedAt,
    DateOnly BusinessDate,
    string StationCode,
    string StaffName,
    string? CustomerName,
    int LineCount,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal Tax1Total,
    decimal Tax2Total,
    decimal GrandTotal,
    TransactionStatus Status);

public sealed record SalesLogPage(IReadOnlyList<SalesLogRow> Rows, int TotalCount, decimal PageTotal, decimal GrandTotal);

/// <summary>
/// The itemized sales log (guide p.14–15): every sale in a window, filterable, exportable.
/// <para>
/// Voided sales stay in the list by default with their status showing. Hiding them would make the
/// log disagree with the ledger, and reconciling a drawer against a log that quietly drops rows is
/// how shortages become unexplainable.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Reports.Sales)]
public sealed record SalesLogQuery(
    Guid LocationId,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? StationId = null,
    Guid? StaffId = null,
    Guid? CustomerId = null,
    bool IncludeVoided = true,
    int Skip = 0,
    int Take = 100) : IRequest<SalesLogPage>;

/// <summary>The same rows as CSV, standing in for the legacy "Open In MS-Excel" button (guide p.101).</summary>
[RequiresPermission(PermissionKeys.Reports.Sales)]
public sealed record ExportSalesLogQuery(SalesLogQuery Filter) : IRequest<string>;

/// <summary>One sale in full, for the drill-down and the reprint preview.</summary>
[RequiresPermission(PermissionKeys.Reports.Sales)]
public sealed record GetSaleQuery(Guid TransactionId) : IRequest<Result<SaleDetailDto>>;

public sealed record SaleDetailLineDto(
    int Sequence,
    string StockCode,
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPct,
    decimal ExtendedNet,
    decimal Tax1Amount,
    decimal Tax2Amount,
    PriceOrigin PriceOrigin,
    LineType LineType,
    string? Epc);

public sealed record SaleDetailTenderDto(string TenderName, decimal Amount, decimal AmountTendered, decimal ChangeGiven, string? Reference);

public sealed record SaleDetailDto(
    Guid Id,
    long TransactionNumber,
    DateTimeOffset CompletedAt,
    TransactionStatus Status,
    string StationCode,
    string StaffName,
    string? CustomerName,
    IReadOnlyList<SaleDetailLineDto> Lines,
    IReadOnlyList<SaleDetailTenderDto> Tenders,
    decimal Subtotal,
    decimal DiscountTotal,
    string Tax1Name,
    decimal Tax1Total,
    string Tax2Name,
    decimal Tax2Total,
    decimal AddOnCharge,
    decimal GrandTotal,
    decimal ChangeGiven,
    Guid? ReversesTransactionId,
    Guid? VoidedByTransactionId,
    string? VoidReason);

public sealed class SalesLogHandlers
    : IRequestHandler<SalesLogQuery, SalesLogPage>,
      IRequestHandler<ExportSalesLogQuery, string>,
      IRequestHandler<GetSaleQuery, Result<SaleDetailDto>>
{
    private readonly IApplicationDbContext _db;

    public SalesLogHandlers(IApplicationDbContext db) => _db = db;

    public async Task<SalesLogPage> Handle(SalesLogQuery request, CancellationToken ct)
    {
        var query = Filter(request);

        var totalCount = await query.CountAsync(ct);
        var grandTotal = await query.SumAsync(t => (decimal?)t.GrandTotal, ct) ?? 0m;

        var transactions = await query
            .OrderByDescending(t => t.CompletedAt)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 1000))
            .ToListAsync(ct);

        var rows = await ProjectAsync(transactions, ct);

        return new SalesLogPage(rows, totalCount, rows.Sum(r => r.GrandTotal), grandTotal);
    }

    public async Task<string> Handle(ExportSalesLogQuery request, CancellationToken ct)
    {
        var transactions = await Filter(request.Filter)
            .OrderBy(t => t.CompletedAt)
            .ToListAsync(ct);

        var rows = await ProjectAsync(transactions, ct);

        var csv = new CsvWriter().Header(
            "Number", "Completed", "BusinessDate", "Station", "Staff", "Customer",
            "Lines", "Subtotal", "Discount", "Tax1", "Tax2", "Total", "Status");

        foreach (var row in rows)
        {
            csv.Row(
                row.TransactionNumber, row.CompletedAt, row.BusinessDate,
                row.StationCode, row.StaffName, row.CustomerName, row.LineCount,
                row.Subtotal, row.DiscountTotal, row.Tax1Total, row.Tax2Total,
                row.GrandTotal, row.Status);
        }

        return csv.ToString();
    }

    public async Task<Result<SaleDetailDto>> Handle(GetSaleQuery request, CancellationToken ct)
    {
        var transaction = await _db.SalesTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, ct);

        if (transaction is null)
        {
            return Result.Failure<SaleDetailDto>(new Error("sale.not_found", "No such sale."));
        }

        var lines = await _db.SaleLines.AsNoTracking()
            .Where(l => l.TransactionId == transaction.Id)
            .OrderBy(l => l.Sequence)
            .ToListAsync(ct);

        var tenders = await _db.SaleTenders.AsNoTracking()
            .Where(t => t.TransactionId == transaction.Id)
            .ToListAsync(ct);

        var tenderNames = await _db.TenderTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.DisplayName, ct);
        var taxSnapshot = await _db.SaleTaxSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.TransactionId == transaction.Id, ct);

        var stationCode = await _db.Stations.AsNoTracking()
            .Where(s => s.Id == transaction.StationId).Select(s => s.StationCode).FirstOrDefaultAsync(ct);

        var staffName = await _db.StaffProfiles.AsNoTracking()
            .Where(s => s.Id == transaction.StaffId).Select(s => s.FullName).FirstOrDefaultAsync(ct);

        var customerName = transaction.CustomerId is { } customerId
            ? await _db.Customers.AsNoTracking().Where(c => c.Id == customerId).Select(c => c.FullName).FirstOrDefaultAsync(ct)
            : null;

        return Result.Success(new SaleDetailDto(
            transaction.Id,
            transaction.TransactionNumber,
            transaction.CompletedAt,
            transaction.Status,
            stationCode ?? string.Empty,
            staffName ?? string.Empty,
            customerName,
            lines.Select(l => new SaleDetailLineDto(
                l.Sequence,
                l.StockCodeSnapshot ?? string.Empty,
                l.NameSnapshot ?? string.Empty,
                l.Quantity,
                l.UnitPrice,
                l.DiscountPct,
                l.ExtendedNet,
                l.Tax1Amount,
                l.Tax2Amount,
                l.PriceOrigin,
                l.LineType,
                l.Epc)).ToList(),
            tenders.Select(t => new SaleDetailTenderDto(
                tenderNames.TryGetValue(t.TenderTypeId, out var name) ? name : "Tender",
                t.Amount,
                t.AmountTendered,
                t.ChangeGiven,
                t.Reference ?? t.AuthCode)).ToList(),
            transaction.Subtotal,
            transaction.DiscountTotal,
            taxSnapshot?.Tax1Name ?? string.Empty,
            transaction.Tax1Total,
            taxSnapshot?.Tax2Name ?? string.Empty,
            transaction.Tax2Total,
            transaction.AddOnChargeTotal,
            transaction.GrandTotal,
            transaction.ChangeGiven,
            transaction.ReversesTransactionId,
            transaction.VoidedByTransactionId,
            transaction.VoidReason));
    }

    private IQueryable<SalesTransaction> Filter(SalesLogQuery request)
    {
        var query = _db.SalesTransactions.AsNoTracking().Where(t => t.LocationId == request.LocationId);

        if (request.From is { } from)
        {
            query = query.Where(t => t.BusinessDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(t => t.BusinessDate <= to);
        }

        if (request.StationId is { } stationId)
        {
            query = query.Where(t => t.StationId == stationId);
        }

        if (request.StaffId is { } staffId)
        {
            query = query.Where(t => t.StaffId == staffId);
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(t => t.CustomerId == customerId);
        }

        if (!request.IncludeVoided)
        {
            query = query.Where(t => t.Status == TransactionStatus.Completed);
        }

        return query;
    }

    private async Task<List<SalesLogRow>> ProjectAsync(List<SalesTransaction> transactions, CancellationToken ct)
    {
        if (transactions.Count == 0)
        {
            return [];
        }

        var ids = transactions.Select(t => t.Id).ToList();

        var lineCounts = await _db.SaleLines.AsNoTracking()
            .Where(l => ids.Contains(l.TransactionId))
            .GroupBy(l => l.TransactionId)
            .Select(g => new { TransactionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TransactionId, x => x.Count, ct);

        var stationIds = transactions.Select(t => t.StationId).Distinct().ToList();
        var stations = await _db.Stations.AsNoTracking()
            .Where(s => stationIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.StationCode, ct);

        var staffIds = transactions.Select(t => t.StaffId).Distinct().ToList();
        var staff = await _db.StaffProfiles.AsNoTracking()
            .Where(s => staffIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        var customerIds = transactions.Where(t => t.CustomerId.HasValue).Select(t => t.CustomerId!.Value).Distinct().ToList();
        var customers = customerIds.Count == 0
            ? []
            : await _db.Customers.AsNoTracking()
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FullName, ct);

        return transactions.Select(t => new SalesLogRow(
            t.Id,
            t.TransactionNumber,
            t.CompletedAt,
            t.BusinessDate,
            stations.TryGetValue(t.StationId, out var code) ? code : string.Empty,
            staff.TryGetValue(t.StaffId, out var name) ? name : string.Empty,
            t.CustomerId is { } cid && customers.TryGetValue(cid, out var customer) ? customer : null,
            lineCounts.TryGetValue(t.Id, out var lineCount) ? lineCount : 0,
            t.Subtotal,
            t.DiscountTotal,
            t.Tax1Total,
            t.Tax2Total,
            t.GrandTotal,
            t.Status)).ToList();
    }
}
