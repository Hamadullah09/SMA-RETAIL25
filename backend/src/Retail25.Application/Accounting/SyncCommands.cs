using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Accounting;
using Retail25.Domain.Common;

namespace Retail25.Application.Accounting;

// ---------------------------------------------------------------------------------------------
// Running a sync
// ---------------------------------------------------------------------------------------------

/// <summary>What can be pushed or pulled. Named rather than free text so a typo in the URL is a
/// 400 rather than a silent no-op.</summary>
public enum SyncEntity
{
    Customers = 0,
    Items = 1,
    Vendors = 2,
    Invoices = 3,
    PosRevenue = 4,
    Bill = 5,
}

[RequiresPermission(PermissionKeys.System.SyncRun)]
public sealed record TriggerAccountingSyncCommand(
    long LocationId,
    SyncEntity Entity,
    bool Pull = false,
    DateOnly? BusinessDate = null,
    long? PurchaseOrderId = null,
    DateOnly? DueOn = null) : IRequest<Result<SyncResult>>;

// ---------------------------------------------------------------------------------------------
// Pre-flight
// ---------------------------------------------------------------------------------------------

public sealed record PreflightCheck(string Requirement, bool Satisfied, string Detail);

public sealed record PreflightReport(IReadOnlyList<PreflightCheck> Checks, bool Ready);

/// <summary>
/// What must be mapped before the first real sync (doc 09 §1, "pre-flight validation").
/// <para>
/// This exists because the legacy integration failed silently on exactly these four things, which is
/// why its manual has a troubleshooting chapter. Failing loudly up front is the whole point.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.System.SyncRun)]
public sealed record PreflightAccountingSyncQuery(long LocationId) : IRequest<PreflightReport>;

// ---------------------------------------------------------------------------------------------
// Log and mappings
// ---------------------------------------------------------------------------------------------

public sealed record SyncLogRow(
    long Id,
    string Provider,
    SyncDirection Direction,
    string Entity,
    SyncStatus Status,
    int RecordCount,
    string? ErrorMessage,
    DateTimeOffset OccurredAt,
    long DurationMs);

public sealed record SyncLogPage(IReadOnlyList<SyncLogRow> Rows, int TotalCount);

[RequiresPermission(PermissionKeys.System.SyncRun)]
public sealed record GetSyncLogQuery(
    string? Entity = null,
    SyncStatus? Status = null,
    int Skip = 0,
    int Take = 100) : IRequest<SyncLogPage>;

/// <summary>The request and response of one attempt, for the troubleshooting drill-down.</summary>
[RequiresPermission(PermissionKeys.System.SyncRun)]
public sealed record GetSyncLogDetailQuery(long Id) : IRequest<Result<SyncLog>>;

public sealed record ExternalMapRow(
    long Id,
    string EntityType,
    long? LocalId,
    string? LocalKey,
    string RemoteId,
    string? RemoteName,
    DateTimeOffset? LastSyncedAt);

[RequiresPermission(PermissionKeys.Settings.Read)]
public sealed record GetExternalMapsQuery(string Provider = "csv") : IRequest<IReadOnlyList<ExternalMapRow>>;

[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record UpsertExternalMapCommand(
    string Provider,
    string EntityType,
    long? LocalId,
    string? LocalKey,
    string RemoteId,
    string? RemoteName) : IRequest<Result<ExternalMapRow>>;

// ---------------------------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------------------------

public sealed class SyncHandlers
    : IRequestHandler<TriggerAccountingSyncCommand, Result<SyncResult>>,
      IRequestHandler<PreflightAccountingSyncQuery, PreflightReport>,
      IRequestHandler<GetSyncLogQuery, SyncLogPage>,
      IRequestHandler<GetSyncLogDetailQuery, Result<SyncLog>>,
      IRequestHandler<GetExternalMapsQuery, IReadOnlyList<ExternalMapRow>>,
      IRequestHandler<UpsertExternalMapCommand, Result<ExternalMapRow>>
{
    public static readonly Error BusinessDateRequired =
        new("sync.business_date_required", "Posting POS revenue needs the business date to post.");

    public static readonly Error PurchaseOrderRequired =
        new("sync.purchase_order_required", "Posting a bill needs the purchase order it is for.");

    public static readonly Error NotFound = new("sync.log_not_found", "No such sync attempt.");

    private readonly IApplicationDbContext _db;
    private readonly IAccountingConnector _connector;
    private readonly IDateTime _clock;

    public SyncHandlers(IApplicationDbContext db, IAccountingConnector connector, IDateTime clock)
    {
        _db = db;
        _connector = connector;
        _clock = clock;
    }

    public async Task<Result<SyncResult>> Handle(TriggerAccountingSyncCommand request, CancellationToken ct)
    {
        var scope = new SyncScope(request.LocationId);

        if (request.Pull)
        {
            return Result.Success(request.Entity switch
            {
                SyncEntity.Customers => await _connector.PullCustomersAsync(request.LocationId, ct),
                SyncEntity.Items => await _connector.PullItemsAsync(request.LocationId, ct),
                SyncEntity.Vendors => await _connector.PullVendorsAsync(request.LocationId, ct),
                _ => SyncResult.Failed($"{request.Entity} cannot be pulled — it is something we post, not something we read back."),
            });
        }

        switch (request.Entity)
        {
            case SyncEntity.PosRevenue when request.BusinessDate is null:
                return Result.Failure<SyncResult>(BusinessDateRequired);

            case SyncEntity.Bill when request.PurchaseOrderId is null:
                return Result.Failure<SyncResult>(PurchaseOrderRequired);
        }

        return Result.Success(request.Entity switch
        {
            SyncEntity.Customers => await _connector.PushCustomersAsync(scope, ct),
            SyncEntity.Items => await _connector.PushItemsAsync(scope, ct),
            SyncEntity.Vendors => await _connector.PushVendorsAsync(scope, ct),
            SyncEntity.Invoices => await _connector.PushInvoicesAsync(scope, ct),
            SyncEntity.PosRevenue => await _connector.PostPosRevenueAsync(request.LocationId, request.BusinessDate!.Value, ct),
            SyncEntity.Bill => await _connector.PostBillAsync(
                request.PurchaseOrderId!.Value,
                request.DueOn ?? _clock.Today().AddDays(30),
                ct),
            _ => SyncResult.Failed("Unknown entity."),
        });
    }

    public async Task<PreflightReport> Handle(PreflightAccountingSyncQuery request, CancellationToken ct)
    {
        var maps = await _db.ExternalEntityMaps.AsNoTracking()
            .Where(m => m.Provider == _connector.Provider)
            .ToListAsync(ct);

        bool Mapped(string entityType, string? localKey = null, long? localId = null) =>
            maps.Any(m => m.EntityType == entityType
                && (localKey is null || m.LocalKey == localKey)
                && (localId is null || m.LocalId == localId));

        var checks = new List<PreflightCheck>
        {
            new("Bank account", Mapped("Account", "BankAccount"),
                "Where a day's takings are deposited. Without it the revenue journal has nowhere to debit."),
            new("Sales income account", Mapped("Account", "SalesAccount"),
                "What the day's net sales credit."),
            new("Discount item", Mapped("DiscountItem"),
                "A subtotal discount needs its own line item on the other side, or the totals will not agree (guide p.110)."),
        };

        // Every active tender and tax rate needs somewhere to land — this is the check the legacy
        // integration didn't have, and matching silently by name is how it went wrong (guide p.109–110).
        var tenders = await _db.TenderTypes.AsNoTracking().Where(t => t.IsActive).ToListAsync(ct);
        var unmappedTenders = tenders.Where(t => !Mapped("TenderType", localId: t.Id)).Select(t => t.DisplayName).ToList();

        checks.Add(new PreflightCheck(
            "Payment methods",
            unmappedTenders.Count == 0,
            unmappedTenders.Count == 0
                ? $"All {tenders.Count} active tender types are mapped."
                : $"Not mapped: {string.Join(", ", unmappedTenders)}."));

        var taxes = await _db.TaxConfigurations.AsNoTracking()
            .Where(t => t.LocationId == request.LocationId)
            .ToListAsync(ct);

        var currentTax = taxes.OrderByDescending(t => t.EffectiveFrom).FirstOrDefault();

        checks.Add(new PreflightCheck(
            "Tax rates",
            currentTax is null || Mapped("TaxRate", localId: currentTax.Id),
            currentTax is null
                ? "No tax configuration to map."
                : Mapped("TaxRate", localId: currentTax.Id)
                    ? "The current tax configuration is mapped."
                    : "The current tax configuration has no remote tax item."));

        return new PreflightReport(checks, checks.All(c => c.Satisfied));
    }

    public async Task<SyncLogPage> Handle(GetSyncLogQuery request, CancellationToken ct)
    {
        var query = _db.SyncLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Entity))
        {
            query = query.Where(l => l.Entity == request.Entity);
        }

        if (request.Status is { } status)
        {
            query = query.Where(l => l.Status == status);
        }

        var totalCount = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(l => l.OccurredAt)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 500))
            .Select(l => new SyncLogRow(
                l.Id, l.Provider, l.Direction, l.Entity, l.Status,
                l.RecordCount, l.ErrorMessage, l.OccurredAt, l.DurationMs))
            .ToListAsync(ct);

        return new SyncLogPage(rows, totalCount);
    }

    public async Task<Result<SyncLog>> Handle(GetSyncLogDetailQuery request, CancellationToken ct)
    {
        var log = await _db.SyncLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.Id, ct);

        return log is null ? Result.Failure<SyncLog>(NotFound) : Result.Success(log);
    }

    public async Task<IReadOnlyList<ExternalMapRow>> Handle(GetExternalMapsQuery request, CancellationToken ct)
        => await _db.ExternalEntityMaps.AsNoTracking()
            .Where(m => m.Provider == request.Provider)
            .OrderBy(m => m.EntityType).ThenBy(m => m.LocalKey)
            .Select(m => new ExternalMapRow(m.Id, m.EntityType, m.LocalId, m.LocalKey, m.RemoteId, m.RemoteName, m.LastSyncedAt))
            .ToListAsync(ct);

    public async Task<Result<ExternalMapRow>> Handle(UpsertExternalMapCommand request, CancellationToken ct)
    {
        var existing = await _db.ExternalEntityMaps.FirstOrDefaultAsync(
            m => m.Provider == request.Provider
                && m.EntityType == request.EntityType
                && m.LocalId == request.LocalId
                && m.LocalKey == request.LocalKey,
            ct);

        if (existing is null)
        {
            existing = new ExternalEntityMap
            {
                Provider = request.Provider,
                EntityType = request.EntityType,
                LocalId = request.LocalId,
                LocalKey = request.LocalKey,
            };

            _db.ExternalEntityMaps.Add(existing);
        }

        existing.RemoteId = request.RemoteId;
        existing.RemoteName = request.RemoteName;
        existing.LastSyncedAt = _clock.Now;

        await _db.SaveChangesAsync(ct);

        return Result.Success(new ExternalMapRow(
            existing.Id, existing.EntityType, existing.LocalId, existing.LocalKey,
            existing.RemoteId, existing.RemoteName, existing.LastSyncedAt));
    }
}
