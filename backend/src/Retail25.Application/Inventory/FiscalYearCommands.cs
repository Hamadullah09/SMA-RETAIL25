using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Inventory;
using Retail25.Domain.Sales;

namespace Retail25.Application.Inventory;

public sealed record FiscalYearDto(
    Guid Id,
    Guid LocationId,
    int Year,
    DateOnly StartsOn,
    DateOnly EndsOn,
    FiscalYearStatus Status,
    DateTimeOffset? ClosedAt,
    int ArchivedRows,
    decimal ArchivedNetSales,
    string? Notes);

/// <summary>One month of one item, as the archive holds it.</summary>
public sealed record ArchiveRowDto(
    int Year,
    int Month,
    string StockCode,
    string Name,
    decimal QuantitySold,
    decimal NetSales,
    decimal CostOfGoodsSold,
    decimal GrossMargin,
    int TransactionCount);

/// <summary>
/// What a close would do, or did. The same shape either way, so a dry run and a real one are
/// comparable at a glance.
/// </summary>
public sealed record FiscalYearCloseResult(
    int Year,
    bool WasDryRun,
    int ArchiveRows,
    int ProductsCheckpointed,
    decimal NetSales,
    decimal CostOfGoodsSold,
    decimal GrossMargin,
    int TransactionsCovered,
    IReadOnlyList<string> Warnings);

[RequiresPermission(PermissionKeys.Inventory.YearEnd)]
public sealed record ListFiscalYearsQuery(Guid LocationId) : IRequest<IReadOnlyList<FiscalYearDto>>;

/// <summary>Opens a year so it can be traded in and later closed. Calendar years unless told otherwise.</summary>
[RequiresPermission(PermissionKeys.Inventory.YearEnd)]
public sealed record OpenFiscalYearCommand(
    Guid LocationId,
    int Year,
    DateOnly? StartsOn = null,
    DateOnly? EndsOn = null,
    string? Notes = null) : IRequest<Result<FiscalYearDto>>;

/// <summary>
/// The year-end close (guide p.29).
/// <para>
/// <paramref name="DryRun"/> does every calculation and writes nothing, which is how this should
/// always be run the first time. The figures it reports are the figures the real close will write.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.YearEnd)]
public sealed record RunFiscalYearCloseCommand(Guid FiscalYearId, bool DryRun = false)
    : IRequest<Result<FiscalYearCloseResult>>;

/// <summary>
/// Undoes a close (doc 03: "undo the year-end close"). Safe because the close destroyed nothing —
/// this drops the archive rows and the checkpoints, and the ledger they came from is untouched.
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.YearEnd)]
public sealed record ReopenFiscalYearCommand(Guid FiscalYearId) : IRequest<Result<FiscalYearDto>>;

/// <summary>The archive, which is the point of having closed the year at all.</summary>
[RequiresPermission(PermissionKeys.Reports.Sales)]
public sealed record GetSalesHistoryQuery(
    Guid LocationId,
    int? Year = null,
    Guid? ProductId = null,
    int Take = 500) : IRequest<IReadOnlyList<ArchiveRowDto>>;

[RequiresPermission(PermissionKeys.Reports.Sales)]
public sealed record ExportSalesHistoryQuery(GetSalesHistoryQuery Inner) : IRequest<string>;

/// <summary>
/// Fiscal years and the year-end close (guide p.29).
/// <para>
/// The legacy close cleared histories and rolled this year's monthly figures into last year's. This
/// one destroys nothing: it rolls the year up into <c>SalesHistoryArchive</c>, writes a
/// zero-quantity <c>YearEnd</c> checkpoint per item, and marks the year closed. Every figure stays
/// derivable from the transactions, and every previous year keeps its own archive rows instead of
/// being overwritten each January.
/// </para>
/// </summary>
public sealed class FiscalYearHandlers :
    IRequestHandler<ListFiscalYearsQuery, IReadOnlyList<FiscalYearDto>>,
    IRequestHandler<OpenFiscalYearCommand, Result<FiscalYearDto>>,
    IRequestHandler<RunFiscalYearCloseCommand, Result<FiscalYearCloseResult>>,
    IRequestHandler<ReopenFiscalYearCommand, Result<FiscalYearDto>>,
    IRequestHandler<GetSalesHistoryQuery, IReadOnlyList<ArchiveRowDto>>,
    IRequestHandler<ExportSalesHistoryQuery, string>
{
    public static readonly Error YearNotFound = new("fiscal_year.not_found", "No such fiscal year.");

    public static readonly Error EndsInTheFuture = new(
        "fiscal_year.ends_in_the_future",
        "That year has not finished yet.");

    /// <summary>Rows written between SaveChanges calls on a whole-catalogue close.</summary>
    private const int ChunkSize = 500;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    public FiscalYearHandlers(IApplicationDbContext db, ICurrentUser currentUser, IDateTime clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<IReadOnlyList<FiscalYearDto>> Handle(ListFiscalYearsQuery request, CancellationToken ct)
        => await _db.FiscalYears.AsNoTracking()
            .Where(y => y.LocationId == request.LocationId)
            .OrderByDescending(y => y.Year)
            .Select(y => new FiscalYearDto(
                y.Id, y.LocationId, y.Year, y.StartsOn, y.EndsOn, y.Status,
                y.ClosedAt, y.ArchivedRows, y.ArchivedNetSales, y.Notes))
            .ToListAsync(ct);

    public async Task<Result<FiscalYearDto>> Handle(OpenFiscalYearCommand request, CancellationToken ct)
    {
        var created = request.StartsOn is { } starts && request.EndsOn is { } ends
            ? FiscalYear.Create(request.LocationId, request.Year, starts, ends, request.Notes)
            : FiscalYear.Calendar(request.LocationId, request.Year);

        if (created.IsFailure)
        {
            return Result.Failure<FiscalYearDto>(created.Error);
        }

        var year = created.Value;

        // Overlap is checked in memory: a store has a handful of these, and expressing "these two
        // date ranges intersect" in a predicate reads worse than it computes.
        var existing = await _db.FiscalYears.AsNoTracking()
            .Where(y => y.LocationId == request.LocationId)
            .Select(y => new { y.Year, y.StartsOn, y.EndsOn })
            .ToListAsync(ct);

        if (existing.Any(y => y.Year == request.Year || (y.StartsOn <= year.EndsOn && year.StartsOn <= y.EndsOn)))
        {
            return Result.Failure<FiscalYearDto>(FiscalYear.Overlaps.With("year", request.Year));
        }

        _db.FiscalYears.Add(year);
        await _db.SaveChangesAsync(ct);

        return Result.Success(ToDto(year));
    }

    public async Task<Result<FiscalYearCloseResult>> Handle(RunFiscalYearCloseCommand request, CancellationToken ct)
    {
        var year = await _db.FiscalYears.FirstOrDefaultAsync(y => y.Id == request.FiscalYearId, ct);

        if (year is null)
        {
            return Result.Failure<FiscalYearCloseResult>(YearNotFound);
        }

        if (year.Status == FiscalYearStatus.Closed)
        {
            return Result.Failure<FiscalYearCloseResult>(FiscalYear.AlreadyClosed.With("year", year.Year));
        }

        var warnings = new List<string>();

        // Closing a year that has not finished would archive a partial year under a whole year's
        // heading, and the figure would be wrong forever after without anything saying so.
        if (year.EndsOn >= ((IDateTime)_clock).Today())
        {
            return Result.Failure<FiscalYearCloseResult>(EndsInTheFuture.With("endsOn", year.EndsOn));
        }

        // Years close in order. Closing 2026 while 2025 is still open would leave a gap that nobody
        // notices until someone asks for a five-year comparison.
        var earlierOpen = await _db.FiscalYears.AsNoTracking()
            .Where(y => y.LocationId == year.LocationId && y.Year < year.Year && y.Status == FiscalYearStatus.Open)
            .OrderBy(y => y.Year)
            .Select(y => y.Year)
            .FirstOrDefaultAsync(ct);

        if (earlierOpen != 0)
        {
            return Result.Failure<FiscalYearCloseResult>(FiscalYear.EarlierYearStillOpen.With("openYear", earlierOpen));
        }

        var lines = await YearLinesAsync(year, ct);

        var grouped = lines
            .GroupBy(l => (l.BusinessDate.Month, l.ProductId))
            .Select(group => new
            {
                group.Key.Month,
                group.Key.ProductId,
                StockCode = group.First().StockCode ?? string.Empty,
                Name = group.First().Name ?? string.Empty,
                Quantity = group.Sum(l => l.Quantity),
                NetSales = group.Sum(l => l.LineNet),
                Cogs = group.Sum(l => l.Cost),
                Transactions = group.Select(l => l.TransactionId).Distinct().Count(),
            })
            .ToList();

        var productIds = grouped.Select(g => g.ProductId).Distinct().ToList();

        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        var missing = productIds.Count(id => !products.ContainsKey(id));

        if (missing > 0)
        {
            // The archive still gets its rows — the snapshot columns are exactly for this — but the
            // checkpoint cannot be written for an item that no longer exists.
            warnings.Add($"{missing} item(s) sold in {year.Year} no longer exist and were archived without a checkpoint.");
        }

        var result = new FiscalYearCloseResult(
            year.Year,
            request.DryRun,
            grouped.Count,
            products.Count,
            grouped.Sum(g => g.NetSales),
            grouped.Sum(g => g.Cogs),
            grouped.Sum(g => g.NetSales - g.Cogs),
            lines.Select(l => l.TransactionId).Distinct().Count(),
            warnings);

        if (request.DryRun)
        {
            return Result.Success(result);
        }

        var archivedAt = _clock.Now;
        var written = 0;

        foreach (var row in grouped)
        {
            _db.SalesHistoryArchives.Add(new SalesHistoryArchive
            {
                FiscalYearId = year.Id,
                LocationId = year.LocationId,
                Year = year.Year,
                Month = row.Month,
                ProductId = row.ProductId,
                StockCodeSnapshot = row.StockCode,
                NameSnapshot = row.Name,
                DepartmentId = products.GetValueOrDefault(row.ProductId)?.DepartmentId,
                QuantitySold = row.Quantity,
                NetSales = row.NetSales,
                CostOfGoodsSold = row.Cogs,
                TransactionCount = row.Transactions,
                ArchivedAt = archivedAt,
            });

            if (++written % ChunkSize == 0)
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        // A checkpoint per item that traded. Quantity is zero on purpose: the ledger has to still
        // replay to the same on-hand, so this is a marker rather than a movement. The on-hand at the
        // moment of close goes in the reason, which is the only field that can carry it and is what
        // anyone reading the ledger at that point actually wants to see.
        foreach (var (productId, product) in products)
        {
            _db.StockLedgerEntries.Add(new StockLedgerEntry
            {
                ProductId = productId,
                LocationId = year.LocationId,
                MovementType = MovementType.YearEnd,
                Quantity = 0m,
                UnitCost = product.AvgCost,
                ReferenceType = nameof(FiscalYear),
                ReferenceId = year.Id,
                Reason = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Year-end {year.Year}: on hand {product.OnHand}, average cost {product.AvgCost}"),

                // Stamped at the last moment of the year, not at the moment the button was pressed,
                // so the checkpoint sorts inside the year it belongs to.
                OccurredAt = new DateTimeOffset(year.EndsOn.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero),
                StaffId = _currentUser.StaffId,
            });
        }

        var closed = year.Close(archivedAt, _currentUser.StaffId, grouped.Count, result.NetSales);

        if (closed.IsFailure)
        {
            return Result.Failure<FiscalYearCloseResult>(closed.Error);
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(result);
    }

    public async Task<Result<FiscalYearDto>> Handle(ReopenFiscalYearCommand request, CancellationToken ct)
    {
        var year = await _db.FiscalYears.FirstOrDefaultAsync(y => y.Id == request.FiscalYearId, ct);

        if (year is null)
        {
            return Result.Failure<FiscalYearDto>(YearNotFound);
        }

        var reopened = year.Reopen();

        if (reopened.IsFailure)
        {
            return Result.Failure<FiscalYearDto>(reopened.Error);
        }

        // Both derived artefacts go with it, so a re-close starts from nothing and cannot double the
        // figures. Nothing here is a source of truth — the sale lines and the ledger are.
        var archives = await _db.SalesHistoryArchives.Where(a => a.FiscalYearId == year.Id).ToListAsync(ct);
        _db.SalesHistoryArchives.RemoveRange(archives);

        var checkpoints = await _db.StockLedgerEntries
            .Where(e => e.ReferenceType == nameof(FiscalYear) && e.ReferenceId == year.Id)
            .ToListAsync(ct);

        _db.StockLedgerEntries.RemoveRange(checkpoints);

        await _db.SaveChangesAsync(ct);

        return Result.Success(ToDto(year));
    }

    public async Task<IReadOnlyList<ArchiveRowDto>> Handle(GetSalesHistoryQuery request, CancellationToken ct)
    {
        var query = _db.SalesHistoryArchives.AsNoTracking().Where(a => a.LocationId == request.LocationId);

        if (request.Year is { } year)
        {
            query = query.Where(a => a.Year == year);
        }

        if (request.ProductId is { } productId)
        {
            query = query.Where(a => a.ProductId == productId);
        }

        var rows = await query
            .OrderByDescending(a => a.Year)
            .ThenBy(a => a.Month)
            .ThenBy(a => a.StockCodeSnapshot)
            .Take(Math.Clamp(request.Take, 1, 5000))
            .ToListAsync(ct);

        return rows.Select(a => new ArchiveRowDto(
            a.Year,
            a.Month,
            a.StockCodeSnapshot,
            a.NameSnapshot,
            a.QuantitySold,
            a.NetSales,
            a.CostOfGoodsSold,
            a.GrossMargin,
            a.TransactionCount)).ToList();
    }

    public async Task<string> Handle(ExportSalesHistoryQuery request, CancellationToken ct)
    {
        var rows = await Handle(request.Inner, ct);

        var csv = new CsvWriter().Header(
            "Year", "Month", "Code", "Description", "Quantity", "Net sales", "Cost", "Margin", "Transactions");

        foreach (var row in rows)
        {
            csv.Row(
                row.Year, row.Month, row.StockCode, row.Name,
                row.QuantitySold, row.NetSales, row.CostOfGoodsSold, row.GrossMargin, row.TransactionCount);
        }

        return csv.ToString();
    }

    /// <summary>
    /// The year's sale lines, flattened to what the rollup needs.
    /// <para>
    /// Voided sales and practice sales are both left out. A void is money that never changed hands,
    /// and a training sale never happened at all — archiving either would bake a wrong figure into
    /// the one place nobody re-derives.
    /// </para>
    /// </summary>
    private async Task<List<YearLine>> YearLinesAsync(FiscalYear year, CancellationToken ct)
        => await (from line in _db.SaleLines.AsNoTracking()
                  join transaction in _db.SalesTransactions.AsNoTracking() on line.TransactionId equals transaction.Id
                  where transaction.LocationId == year.LocationId
                        && transaction.BusinessDate >= year.StartsOn
                        && transaction.BusinessDate <= year.EndsOn
                        && transaction.Status == TransactionStatus.Completed
                        && !transaction.IsTraining
                  select new YearLine(
                      transaction.Id,
                      transaction.BusinessDate,
                      line.ProductId,
                      line.StockCodeSnapshot,
                      line.NameSnapshot,
                      line.Quantity,
                      line.ExtendedNet,
                      line.UnitCostSnapshot * line.Quantity))
            .ToListAsync(ct);

    private static FiscalYearDto ToDto(FiscalYear year) => new(
        year.Id, year.LocationId, year.Year, year.StartsOn, year.EndsOn, year.Status,
        year.ClosedAt, year.ArchivedRows, year.ArchivedNetSales, year.Notes);

    /// <summary>
    /// The snapshot columns are nullable on <c>SaleLine</c>, so they are here too. An archive row
    /// with a blank code still carries the right money, and substituting a placeholder would make a
    /// gap in the data look like a real item.
    /// </summary>
    private sealed record YearLine(
        Guid TransactionId,
        DateOnly BusinessDate,
        Guid ProductId,
        string? StockCode,
        string? Name,
        decimal Quantity,
        decimal LineNet,
        decimal Cost);
}
