using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Customers;
using Retail25.Domain.Sales;

namespace Retail25.Application.Reports;

// ---------------------------------------------------------------------------------------------
// Tax report (guide p.56)
// ---------------------------------------------------------------------------------------------

public sealed record TaxReportRow(
    string TaxName,
    decimal Rate,
    decimal TaxableBase,
    decimal TaxCollected,
    int TransactionCount);

public sealed record TaxReportResult(
    IReadOnlyList<TaxReportRow> Rows,
    decimal TotalTaxCollected,
    decimal TotalNetSales,
    string? RegistrationNumber);

/// <summary>
/// What was collected, per rate, for a filing period.
/// <para>
/// Grouped by name <em>and</em> rate rather than name alone: a rate change mid-period is normal, and
/// merging 5% and 7% GST into one "GST" line would produce a figure that reconciles against nothing.
/// The rate comes from each sale's own tax snapshot, so a reprint and this report always agree.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Reports.Financial)]
public sealed record GetTaxReportQuery(
    long LocationId,
    DateOnly From,
    DateOnly To,
    bool IncludeVoided = false) : IRequest<TaxReportResult>;

[RequiresPermission(PermissionKeys.Reports.Financial)]
public sealed record ExportTaxReportQuery(GetTaxReportQuery Filter) : IRequest<string>;

// ---------------------------------------------------------------------------------------------
// Reward points activity (guide p.83–84)
// ---------------------------------------------------------------------------------------------

public sealed record RewardPointsRow(
    long CustomerId,
    string CustomerName,
    int Earned,
    int Redeemed,
    int Adjusted,
    int NetChange,
    int CurrentBalance);

public sealed record RewardPointsResult(
    IReadOnlyList<RewardPointsRow> Rows,
    int TotalEarned,
    int TotalRedeemed);

/// <summary>
/// Points earned and spent in a window, per customer.
/// <para>
/// <c>CurrentBalance</c> is today's real balance, not the window's running total — a customer who
/// earned nothing this month still has whatever they were carrying, and a report that implied
/// otherwise would have staff telling people their points had vanished.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record GetRewardPointsActivityQuery(
    long LocationId,
    DateOnly From,
    DateOnly To,
    long? CustomerId = null) : IRequest<RewardPointsResult>;

[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record ExportRewardPointsActivityQuery(GetRewardPointsActivityQuery Filter) : IRequest<string>;

// ---------------------------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------------------------

public sealed class FinancialReportHandlers
    : IRequestHandler<GetTaxReportQuery, TaxReportResult>,
      IRequestHandler<ExportTaxReportQuery, string>,
      IRequestHandler<GetRewardPointsActivityQuery, RewardPointsResult>,
      IRequestHandler<ExportRewardPointsActivityQuery, string>
{
    private readonly IApplicationDbContext _db;

    public FinancialReportHandlers(IApplicationDbContext db) => _db = db;

    public async Task<TaxReportResult> Handle(GetTaxReportQuery request, CancellationToken ct)
    {
        var transactions = _db.SalesTransactions.AsNoTracking()
            .Where(t => t.LocationId == request.LocationId)
            .Where(t => t.BusinessDate >= request.From && t.BusinessDate <= request.To)
            .Where(t => !t.IsTraining);

        if (!request.IncludeVoided)
        {
            transactions = transactions.Where(t => t.Status == TransactionStatus.Completed);
        }

        var facts = await (
            from transaction in transactions
            join snapshot in _db.SaleTaxSnapshots.AsNoTracking()
                on transaction.Id equals snapshot.TransactionId
            select new
            {
                transaction.Id,
                transaction.Subtotal,
                transaction.DiscountTotal,
                transaction.Tax1Total,
                transaction.Tax2Total,
                transaction.AddOnChargeTotal,
                snapshot.Tax1Name,
                snapshot.Tax1Rate,
                snapshot.Tax2Name,
                snapshot.Tax2Rate,
                snapshot.AddOnName,
                snapshot.AddOnRate,
                snapshot.TaxRegistrationNumber,
            }).ToListAsync(ct);

        if (facts.Count == 0)
        {
            return new TaxReportResult([], 0m, 0m, null);
        }

        var rows = new List<TaxReportRow>();

        rows.AddRange(facts
            .Where(f => f.Tax1Total != 0m)
            .GroupBy(f => new { Name = f.Tax1Name, Rate = f.Tax1Rate })
            .Select(g => new TaxReportRow(
                string.IsNullOrWhiteSpace(g.Key.Name) ? "Tax 1" : g.Key.Name,
                g.Key.Rate,
                g.Sum(f => f.Subtotal - f.DiscountTotal),
                g.Sum(f => f.Tax1Total),
                g.Count())));

        rows.AddRange(facts
            .Where(f => f.Tax2Total != 0m)
            .GroupBy(f => new { Name = f.Tax2Name, Rate = f.Tax2Rate })
            .Select(g => new TaxReportRow(
                string.IsNullOrWhiteSpace(g.Key.Name) ? "Tax 2" : g.Key.Name,
                g.Key.Rate,
                g.Sum(f => f.Subtotal - f.DiscountTotal),
                g.Sum(f => f.Tax2Total),
                g.Count())));

        // The add-on charge rides in the same section of the legacy report even though it is a
        // charge rather than a tax — separating them here would just make the totals harder to tie out.
        rows.AddRange(facts
            .Where(f => f.AddOnChargeTotal != 0m)
            .GroupBy(f => new { Name = f.AddOnName, Rate = f.AddOnRate })
            .Select(g => new TaxReportRow(
                string.IsNullOrWhiteSpace(g.Key.Name) ? "Add-on charge" : g.Key.Name,
                g.Key.Rate,
                g.Sum(f => f.Subtotal - f.DiscountTotal),
                g.Sum(f => f.AddOnChargeTotal),
                g.Count())));

        return new TaxReportResult(
            rows.OrderBy(r => r.TaxName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Rate).ToList(),
            rows.Sum(r => r.TaxCollected),
            facts.Sum(f => f.Subtotal - f.DiscountTotal),
            facts.Select(f => f.TaxRegistrationNumber).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)));
    }

    public async Task<string> Handle(ExportTaxReportQuery request, CancellationToken ct)
    {
        var result = await Handle(request.Filter, ct);

        var csv = new CsvWriter().Header("Tax", "Rate", "TaxableBase", "Collected", "Transactions");

        foreach (var row in result.Rows)
        {
            csv.Row(row.TaxName, row.Rate, row.TaxableBase, row.TaxCollected, row.TransactionCount);
        }

        return csv.ToString();
    }

    public async Task<RewardPointsResult> Handle(GetRewardPointsActivityQuery request, CancellationToken ct)
    {
        // UTC-anchored: see InventoryReportHandlers.DayRangeUtc — an unspecified-kind DateTime picks
        // up the server's local offset and Npgsql rejects it outright for a timestamptz column.
        var (from, to) = InventoryReportHandlers.DayRangeUtc(request.From, request.To);

        var customerIds = await _db.Customers.AsNoTracking()
            .Where(c => c.LocationId == request.LocationId && !c.IsDeleted)
            .Where(c => request.CustomerId == null || c.Id == request.CustomerId)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (customerIds.Count == 0)
        {
            return new RewardPointsResult([], 0, 0);
        }

        var entries = await _db.LoyaltyLedgerEntries.AsNoTracking()
            .Where(e => customerIds.Contains(e.CustomerId))
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to)
            .Select(e => new { e.CustomerId, e.EntryType, e.Points })
            .ToListAsync(ct);

        if (entries.Count == 0)
        {
            return new RewardPointsResult([], 0, 0);
        }

        var activeIds = entries.Select(e => e.CustomerId).Distinct().ToList();

        var names = await _db.Customers.AsNoTracking()
            .Where(c => activeIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.FullName, ct);

        var balances = await _db.CustomerPricingProfiles.AsNoTracking()
            .Where(p => activeIds.Contains(p.CustomerId))
            .ToDictionaryAsync(p => p.CustomerId, p => p.RewardPoints, ct);

        var rows = entries
            .GroupBy(e => e.CustomerId)
            .Select(g => new RewardPointsRow(
                g.Key,
                names.GetValueOrDefault(g.Key) ?? string.Empty,
                g.Where(e => e.EntryType == LoyaltyEntryType.Earned).Sum(e => e.Points),
                Math.Abs(g.Where(e => e.EntryType == LoyaltyEntryType.Redeemed).Sum(e => e.Points)),
                g.Where(e => e.EntryType is not (LoyaltyEntryType.Earned or LoyaltyEntryType.Redeemed)).Sum(e => e.Points),
                g.Sum(e => e.Points),
                balances.GetValueOrDefault(g.Key)))
            .OrderByDescending(r => r.Earned)
            .ToList();

        return new RewardPointsResult(rows, rows.Sum(r => r.Earned), rows.Sum(r => r.Redeemed));
    }

    public async Task<string> Handle(ExportRewardPointsActivityQuery request, CancellationToken ct)
    {
        var result = await Handle(request.Filter, ct);

        var csv = new CsvWriter().Header(
            "Customer", "Earned", "Redeemed", "Adjusted", "NetChange", "CurrentBalance");

        foreach (var row in result.Rows)
        {
            csv.Row(row.CustomerName, row.Earned, row.Redeemed, row.Adjusted, row.NetChange, row.CurrentBalance);
        }

        return csv.ToString();
    }
}
