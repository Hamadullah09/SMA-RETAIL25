using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Drawer;

public sealed record DrawerTenderTotal(string TenderName, decimal Amount, int Count);

/// <summary>
/// The drawer report (guide p.10–11, p.15). <see cref="ExpectedCash"/> is always the replayed ledger
/// sum, never a stored running figure.
/// </summary>
public sealed record DrawerTotalsDto(
    long SessionId,
    long StationId,
    DrawerSessionStatus Status,
    DateOnly BusinessDate,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal OpeningFloat,
    decimal CashSales,
    decimal CashRefunds,
    decimal PayIns,
    decimal PayOuts,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? Variance,
    decimal NetSales,
    decimal Tax1Collected,
    decimal Tax2Collected,
    decimal CostOfGoodsSold,
    int TransactionCount,
    IReadOnlyList<DrawerTenderTotal> TenderTotals);

/// <summary>Opens the drawer for the day with a counted float (guide p.10).</summary>
[RequiresPermission(PermissionKeys.Drawer.OpenFloat)]
public sealed record OpenDrawerSessionCommand(long StationId, decimal OpeningFloat) : IRequest<Result<DrawerTotalsDto>>;

/// <summary>Money into the drawer that is not a sale (guide p.11).</summary>
[RequiresPermission(PermissionKeys.Drawer.PayIn)]
public sealed record PayInCommand(long StationId, decimal Amount, string Reason) : IRequest<Result<DrawerTotalsDto>>;

/// <summary>Money out of the drawer that is not a refund — petty cash, a bank drop (guide p.11).</summary>
[RequiresPermission(PermissionKeys.Drawer.PayOut)]
public sealed record PayOutCommand(long StationId, decimal Amount, string Reason) : IRequest<Result<DrawerTotalsDto>>;

/// <summary>
/// No-sale drawer pop (guide p.11). It changes no money but is always recorded, because an
/// unexplained open drawer is exactly what a loss-prevention review needs to be able to see.
/// </summary>
[RequiresPermission(PermissionKeys.Drawer.Pop)]
public sealed record PopDrawerCommand(long StationId, string? Reason = null) : IRequest<Result<DrawerTotalsDto>>;

/// <summary>Closes against a physical count and produces the variance (guide p.11).</summary>
[RequiresPermission(PermissionKeys.Drawer.Close)]
public sealed record CloseDrawerSessionCommand(long StationId, decimal CountedCash) : IRequest<Result<DrawerTotalsDto>>;

[RequiresPermission(PermissionKeys.Drawer.Read)]
public sealed record GetDrawerTotalsQuery(long StationId, long? SessionId = null) : IRequest<Result<DrawerTotalsDto>>;

public sealed class DrawerHandlers
    : IRequestHandler<OpenDrawerSessionCommand, Result<DrawerTotalsDto>>,
      IRequestHandler<PayInCommand, Result<DrawerTotalsDto>>,
      IRequestHandler<PayOutCommand, Result<DrawerTotalsDto>>,
      IRequestHandler<PopDrawerCommand, Result<DrawerTotalsDto>>,
      IRequestHandler<CloseDrawerSessionCommand, Result<DrawerTotalsDto>>,
      IRequestHandler<GetDrawerTotalsQuery, Result<DrawerTotalsDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly PosContextLoader _contextLoader;
    private readonly IPosNotifier _notifier;
    private readonly ITerminalNotifier _terminals;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    public DrawerHandlers(
        IApplicationDbContext db,
        PosContextLoader contextLoader,
        IPosNotifier notifier,
        ITerminalNotifier terminals,
        ICurrentUser currentUser,
        IDateTime clock)
    {
        _db = db;
        _contextLoader = contextLoader;
        _notifier = notifier;
        _terminals = terminals;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<DrawerTotalsDto>> Handle(OpenDrawerSessionCommand request, CancellationToken ct)
    {
        var existing = await FindOpenSessionAsync(request.StationId, ct);
        if (existing is not null)
        {
            return Result.Failure<DrawerTotalsDto>(DrawerSession.AlreadyOpen.With("sessionId", existing.Id));
        }

        var contextResult = await _contextLoader.LoadAsync(request.StationId, ct);
        if (contextResult.IsFailure)
        {
            return Result.Failure<DrawerTotalsDto>(contextResult.Error);
        }

        var context = contextResult.Value;
        var staffId = _currentUser.StaffId ?? 0L;

        var created = DrawerSession.Open(
            request.StationId,
            context.Location.Id,
            staffId,
            request.OpeningFloat,
            context.BusinessDate,
            _clock.Now);

        if (created.IsFailure)
        {
            return Result.Failure<DrawerTotalsDto>(created.Error);
        }

        var session = created.Value;
        _db.DrawerSessions.Add(session);

        // Saved before its id is read: the opening-float entry has to point at a real session, and
        // the id is the database's to assign. One transaction still, courtesy of the pipeline.
        await _db.SaveChangesAsync(ct);

        _db.DrawerLedgerEntries.Add(DrawerLedgerEntry.Create(
            session.Id,
            DrawerEntryType.OpeningFloat,
            request.OpeningFloat,
            staffId,
            _clock.Now,
            "Opening float"));

        await _db.SaveChangesAsync(ct);
        await _terminals.OpenDrawerAsync(request.StationId, ct);

        return await PublishAsync(session, ct);
    }

    public Task<Result<DrawerTotalsDto>> Handle(PayInCommand request, CancellationToken ct)
        => RecordMovementAsync(
            request.StationId,
            session => DrawerLedgerEntry.PayIn(session.Id, request.Amount, request.Reason, _currentUser.StaffId ?? 0L, _clock.Now),
            popDrawer: true,
            ct);

    public Task<Result<DrawerTotalsDto>> Handle(PayOutCommand request, CancellationToken ct)
        => RecordMovementAsync(
            request.StationId,
            session => DrawerLedgerEntry.PayOut(session.Id, request.Amount, request.Reason, _currentUser.StaffId ?? 0L, _clock.Now),
            popDrawer: true,
            ct);

    public Task<Result<DrawerTotalsDto>> Handle(PopDrawerCommand request, CancellationToken ct)
        => RecordMovementAsync(
            request.StationId,
            session => Result.Success(DrawerLedgerEntry.Create(
                session.Id,
                DrawerEntryType.NoSalePop,
                0m,
                _currentUser.StaffId ?? 0L,
                _clock.Now,
                request.Reason ?? "No sale")),
            popDrawer: true,
            ct);

    public async Task<Result<DrawerTotalsDto>> Handle(CloseDrawerSessionCommand request, CancellationToken ct)
    {
        var session = await FindOpenSessionAsync(request.StationId, ct);
        if (session is null)
        {
            return Result.Failure<DrawerTotalsDto>(DrawerSession.NotOpen.With("stationId", request.StationId));
        }

        var totals = await BuildTotalsAsync(session, ct);

        var closed = session.Close(
            request.CountedCash,
            totals.ExpectedCash,
            _currentUser.StaffId ?? 0L,
            _clock.Now,
            JsonSerializer.Serialize(totals.TenderTotals),
            await BuildDepartmentSalesJsonAsync(session, ct));

        if (closed.IsFailure)
        {
            return Result.Failure<DrawerTotalsDto>(closed.Error);
        }

        await _db.SaveChangesAsync(ct);
        return await PublishAsync(session, ct);
    }

    public async Task<Result<DrawerTotalsDto>> Handle(GetDrawerTotalsQuery request, CancellationToken ct)
    {
        var session = request.SessionId is { } sessionId
            ? await _db.DrawerSessions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == sessionId, ct)
            : await FindOpenSessionAsync(request.StationId, ct);

        return session is null
            ? Result.Failure<DrawerTotalsDto>(DrawerSession.NotOpen.With("stationId", request.StationId))
            : Result.Success(await BuildTotalsAsync(session, ct));
    }

    private async Task<Result<DrawerTotalsDto>> RecordMovementAsync(
        long stationId,
        Func<DrawerSession, Result<DrawerLedgerEntry>> build,
        bool popDrawer,
        CancellationToken ct)
    {
        var session = await FindOpenSessionAsync(stationId, ct);
        if (session is null)
        {
            return Result.Failure<DrawerTotalsDto>(DrawerSession.NotOpen.With("stationId", stationId));
        }

        var entry = build(session);
        if (entry.IsFailure)
        {
            return Result.Failure<DrawerTotalsDto>(entry.Error);
        }

        _db.DrawerLedgerEntries.Add(entry.Value);
        await _db.SaveChangesAsync(ct);

        if (popDrawer)
        {
            await _terminals.OpenDrawerAsync(stationId, ct);
        }

        return await PublishAsync(session, ct);
    }

    private Task<DrawerSession?> FindOpenSessionAsync(long stationId, CancellationToken ct)
        => _db.DrawerSessions.FirstOrDefaultAsync(d => d.StationId == stationId && d.Status == DrawerSessionStatus.Open, ct);

    private async Task<Result<DrawerTotalsDto>> PublishAsync(DrawerSession session, CancellationToken ct)
    {
        var totals = await BuildTotalsAsync(session, ct);
        await _notifier.DrawerStateChangedAsync(session.StationId, totals, ct);
        return Result.Success(totals);
    }

    /// <summary>
    /// Replays the ledger for this session. Deriving rather than storing is what lets a supervisor
    /// audit a variance line by line instead of arguing with a number.
    /// </summary>
    private async Task<DrawerTotalsDto> BuildTotalsAsync(DrawerSession session, CancellationToken ct)
    {
        var entries = await _db.DrawerLedgerEntries.AsNoTracking()
            .Where(e => e.DrawerSessionId == session.Id)
            .ToListAsync(ct);

        var cashSales = entries.Where(e => e.EntryType == DrawerEntryType.Sale).Sum(e => e.Amount);
        var cashRefunds = entries.Where(e => e.EntryType == DrawerEntryType.Refund).Sum(e => e.Amount);
        var payIns = entries.Where(e => e.EntryType == DrawerEntryType.PayIn).Sum(e => e.Amount);
        var payOuts = entries.Where(e => e.EntryType == DrawerEntryType.PayOut).Sum(e => e.Amount);
        var corrections = entries.Where(e => e.EntryType == DrawerEntryType.Correction).Sum(e => e.Amount);

        var expected = session.OpeningFloat + cashSales + cashRefunds + payIns + payOuts + corrections;

        var tenderTotals = await BuildTenderTotalsAsync(session, ct);

        return new DrawerTotalsDto(
            session.Id,
            session.StationId,
            session.Status,
            session.BusinessDate,
            session.OpenedAt,
            session.ClosedAt,
            session.OpeningFloat,
            cashSales,
            cashRefunds,
            payIns,
            payOuts,
            expected,
            session.CountedCash,
            session.CountedCash is { } counted ? counted - expected : null,
            session.NetSales,
            session.Tax1Collected,
            session.Tax2Collected,
            session.CostOfGoodsSold,
            session.TransactionCount,
            tenderTotals);
    }

    private async Task<IReadOnlyList<DrawerTenderTotal>> BuildTenderTotalsAsync(DrawerSession session, CancellationToken ct)
    {
        var transactionIds = await _db.SalesTransactions.AsNoTracking()
            .Where(t => t.DrawerSessionId == session.Id)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (transactionIds.Count == 0)
        {
            return [];
        }

        var grouped = await _db.SaleTenders.AsNoTracking()
            .Where(t => transactionIds.Contains(t.TransactionId))
            .GroupBy(t => t.TenderTypeId)
            .Select(g => new { TenderTypeId = g.Key, Amount = g.Sum(t => t.Amount), Count = g.Count() })
            .ToListAsync(ct);

        var names = await _db.TenderTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.DisplayName, ct);

        return grouped
            .Select(g => new DrawerTenderTotal(
                names.TryGetValue(g.TenderTypeId, out var name) ? name : "Tender",
                g.Amount,
                g.Count))
            .OrderByDescending(g => g.Amount)
            .ToList();
    }

    /// <summary>Net sales by department for the close report (guide p.15).</summary>
    private async Task<string> BuildDepartmentSalesJsonAsync(DrawerSession session, CancellationToken ct)
    {
        var transactionIds = await _db.SalesTransactions.AsNoTracking()
            .Where(t => t.DrawerSessionId == session.Id && t.Status != TransactionStatus.Voided)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (transactionIds.Count == 0)
        {
            return "[]";
        }

        var rows = await (from line in _db.SaleLines.AsNoTracking()
                          where transactionIds.Contains(line.TransactionId)
                          join product in _db.Products.AsNoTracking() on line.ProductId equals product.Id
                          group line by product.DepartmentId into grouped
                          select new { DepartmentId = grouped.Key, Net = grouped.Sum(l => l.ExtendedNet) })
            .ToListAsync(ct);

        var departmentNames = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var result = rows.Select(r => new
        {
            Department = r.DepartmentId is { } id && departmentNames.TryGetValue(id, out var name) ? name : "Unassigned",
            r.Net,
        });

        return JsonSerializer.Serialize(result);
    }
}
