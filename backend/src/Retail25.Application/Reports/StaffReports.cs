using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Staff;

namespace Retail25.Application.Reports;

public sealed record HoursRow(
    long StaffId,
    string StaffCode,
    string StaffName,
    int Shifts,
    decimal HoursWorked,
    /// <summary>Shifts with no clock-out. Their hours are not counted, so the figure is honest.</summary>
    int OpenShifts,
    DateTimeOffset? FirstIn,
    DateTimeOffset? LastOut);

public sealed record HoursReportResult(
    IReadOnlyList<HoursRow> Rows,
    decimal TotalHours,
    int TotalShifts,
    int TotalOpenShifts);

/// <summary>Hours worked over a period (guide p.75–76).</summary>
[RequiresPermission(PermissionKeys.Reports.Hours)]
public sealed record HoursReportQuery(
    long LocationId,
    DateOnly From,
    DateOnly To,
    long? StaffId = null) : IRequest<HoursReportResult>;

[RequiresPermission(PermissionKeys.Reports.Hours)]
public sealed record ExportHoursReportQuery(HoursReportQuery Inner) : IRequest<string>;

public sealed record CommissionRow(
    long StaffId,
    string StaffCode,
    string StaffName,
    int Lines,
    decimal SalesNet,
    decimal Commission,
    int CappedLines);

public sealed record CommissionDetailRow(
    long TransactionId,
    long TransactionNumber,
    DateOnly BusinessDate,
    string StockCode,
    decimal Quantity,
    decimal LineNet,
    CommissionType CommissionType,
    decimal RateApplied,
    decimal Amount,
    bool WasCapped);

public sealed record CommissionReportResult(
    IReadOnlyList<CommissionRow> Rows,
    IReadOnlyList<CommissionDetailRow> Detail,
    decimal TotalCommission,
    decimal TotalSalesNet);

/// <summary>
/// Commission earned over a period (guide p.33, p.76).
/// <para>
/// Read straight off the commission ledger rather than recomputed from today's rules — what someone
/// was paid must not change because a rate was edited afterwards.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Reports.Commissions)]
public sealed record CommissionReportQuery(
    long LocationId,
    DateOnly From,
    DateOnly To,
    long? StaffId = null,
    /// <summary>Line-by-line detail. Only meaningful for one person, so it needs <see cref="StaffId"/>.</summary>
    bool IncludeDetail = false,
    int DetailTake = 500) : IRequest<CommissionReportResult>;

[RequiresPermission(PermissionKeys.Reports.Commissions)]
public sealed record ExportCommissionReportQuery(CommissionReportQuery Inner) : IRequest<string>;

public sealed class StaffReportHandlers :
    IRequestHandler<HoursReportQuery, HoursReportResult>,
    IRequestHandler<ExportHoursReportQuery, string>,
    IRequestHandler<CommissionReportQuery, CommissionReportResult>,
    IRequestHandler<ExportCommissionReportQuery, string>
{
    private readonly IApplicationDbContext _db;

    public StaffReportHandlers(IApplicationDbContext db) => _db = db;

    public async Task<HoursReportResult> Handle(HoursReportQuery request, CancellationToken ct)
    {
        var (from, to) = InventoryReportHandlers.DayRangeUtc(request.From, request.To);

        var query = _db.TimeClockEntries.AsNoTracking()
            .Where(e => e.LocationId == request.LocationId && e.ClockIn >= from && e.ClockIn <= to);

        if (request.StaffId is { } staffId)
        {
            query = query.Where(e => e.StaffId == staffId);
        }

        var entries = await query.ToListAsync(ct);
        var staff = await StaffAsync(entries.Select(e => e.StaffId), ct);

        var rows = entries
            .GroupBy(e => e.StaffId)
            .Select(group =>
            {
                var closed = group.Where(e => e.ClockOut is not null).ToList();
                var profile = staff.GetValueOrDefault(group.Key);

                return new HoursRow(
                    group.Key,
                    profile?.StaffCode ?? "—",
                    profile?.FullName ?? "—",
                    closed.Count,
                    decimal.Round(closed.Sum(e => e.HoursWorked ?? 0m), 2, MidpointRounding.AwayFromZero),

                    // An open shift is counted but its hours are not. Guessing at "now minus clock-in"
                    // for someone who forgot to clock out three days ago would put a 72-hour shift on
                    // a payroll report.
                    group.Count(e => e.ClockOut is null),
                    group.Min(e => e.ClockIn),
                    closed.Count == 0 ? null : closed.Max(e => e.ClockOut));
            })
            .OrderBy(r => r.StaffCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new HoursReportResult(
            rows,
            rows.Sum(r => r.HoursWorked),
            rows.Sum(r => r.Shifts),
            rows.Sum(r => r.OpenShifts));
    }

    public async Task<string> Handle(ExportHoursReportQuery request, CancellationToken ct)
    {
        var report = await Handle(request.Inner, ct);

        var csv = new CsvWriter().Header("Code", "Name", "Shifts", "Hours", "Open shifts", "First in", "Last out");

        foreach (var row in report.Rows)
        {
            csv.Row(row.StaffCode, row.StaffName, row.Shifts, row.HoursWorked, row.OpenShifts, row.FirstIn, row.LastOut);
        }

        return csv.ToString();
    }

    public async Task<CommissionReportResult> Handle(CommissionReportQuery request, CancellationToken ct)
    {
        var query = _db.CommissionLedgerEntries.AsNoTracking()
            .Where(e => e.LocationId == request.LocationId
                        && e.BusinessDate >= request.From
                        && e.BusinessDate <= request.To);

        if (request.StaffId is { } staffId)
        {
            query = query.Where(e => e.StaffId == staffId);
        }

        var entries = await query.ToListAsync(ct);
        var staff = await StaffAsync(entries.Select(e => e.StaffId), ct);

        var rows = entries
            .GroupBy(e => e.StaffId)
            .Select(group =>
            {
                var profile = staff.GetValueOrDefault(group.Key);

                return new CommissionRow(
                    group.Key,
                    profile?.StaffCode ?? "—",
                    profile?.FullName ?? "—",
                    group.Count(),
                    group.Sum(e => e.LineNet),
                    group.Sum(e => e.Amount),
                    group.Count(e => e.WasCapped));
            })
            .OrderByDescending(r => r.Commission)
            .ToList();

        var detail = new List<CommissionDetailRow>();

        if (request.IncludeDetail && entries.Count > 0)
        {
            var transactionIds = entries.Select(e => e.TransactionId).Distinct().ToList();

            var numbers = await _db.SalesTransactions.AsNoTracking()
                .Where(t => transactionIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.TransactionNumber, ct);

            detail = entries
                .OrderByDescending(e => e.OccurredAt)
                .Take(Math.Clamp(request.DetailTake, 1, 2000))
                .Select(e => new CommissionDetailRow(
                    e.TransactionId,
                    numbers.GetValueOrDefault(e.TransactionId),
                    e.BusinessDate,
                    e.StockCodeSnapshot,
                    e.Quantity,
                    e.LineNet,
                    e.CommissionType,
                    e.RateApplied,
                    e.Amount,
                    e.WasCapped))
                .ToList();
        }

        return new CommissionReportResult(
            rows,
            detail,
            rows.Sum(r => r.Commission),
            rows.Sum(r => r.SalesNet));
    }

    public async Task<string> Handle(ExportCommissionReportQuery request, CancellationToken ct)
    {
        var report = await Handle(request.Inner, ct);

        var csv = new CsvWriter().Header("Code", "Name", "Lines", "Sales net", "Commission", "Capped lines");

        foreach (var row in report.Rows)
        {
            csv.Row(row.StaffCode, row.StaffName, row.Lines, row.SalesNet, row.Commission, row.CappedLines);
        }

        return csv.ToString();
    }

    private async Task<Dictionary<long, StaffProfile>> StaffAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();

        if (distinct.Count == 0)
        {
            return [];
        }

        return await _db.StaffProfiles.AsNoTracking()
            .Where(s => distinct.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s, ct);
    }
}
