using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Security;

namespace Retail25.Application.Audit;

public sealed record AuditLogRow(
    Guid Id,
    DateTimeOffset OccurredAt,
    AuditAction Action,
    string? ActorName,
    Guid? ActorStaffId,
    Guid? StationId,
    string? IpAddress,
    string EntityType,
    string? EntityId,
    string? Operation,
    string? BeforeJson,
    string? AfterJson,
    string? ApproverName,
    string? Reason,
    string? CorrelationId);

public sealed record AuditLogPage(IReadOnlyList<AuditLogRow> Rows, int TotalCount);

/// <summary>
/// Reads the audit trail (doc 07 §Audit). Read-only and permission-gated: an audit log a user can
/// edit is not an audit log, and one everybody can read is a map of who handles the money.
/// </summary>
[RequiresPermission(PermissionKeys.System.AuditRead)]
public sealed record AuditLogQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? ActorStaffId = null,
    Guid? StationId = null,
    string? EntityType = null,
    string? EntityId = null,
    AuditAction? Action = null,
    string? CorrelationId = null,
    int Skip = 0,
    int Take = 100) : IRequest<AuditLogPage>;

/// <summary>
/// Everything one request did, by correlation id. This is the question an investigation actually
/// asks: not "what changed" but "what happened when that void was authorised".
/// </summary>
[RequiresPermission(PermissionKeys.System.AuditRead)]
public sealed record AuditTrailForRequestQuery(string CorrelationId) : IRequest<IReadOnlyList<AuditLogRow>>;

public sealed class AuditLogHandlers
    : IRequestHandler<AuditLogQuery, AuditLogPage>,
      IRequestHandler<AuditTrailForRequestQuery, IReadOnlyList<AuditLogRow>>
{
    private readonly IApplicationDbContext _db;

    public AuditLogHandlers(IApplicationDbContext db) => _db = db;

    public async Task<AuditLogPage> Handle(AuditLogQuery request, CancellationToken ct)
    {
        var query = _db.AuditLogEntries.AsNoTracking().AsQueryable();

        if (request.From is { } from)
        {
            query = query.Where(e => e.OccurredAt >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(e => e.OccurredAt <= to);
        }

        if (request.ActorStaffId is { } staffId)
        {
            query = query.Where(e => e.ActorStaffId == staffId);
        }

        if (request.StationId is { } stationId)
        {
            query = query.Where(e => e.StationId == stationId);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            query = query.Where(e => e.EntityType == request.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityId))
        {
            query = query.Where(e => e.EntityId == request.EntityId);
        }

        if (request.Action is { } action)
        {
            query = query.Where(e => e.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            query = query.Where(e => e.CorrelationId == request.CorrelationId);
        }

        var total = await query.CountAsync(ct);

        var entries = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 500))
            .ToListAsync(ct);

        return new AuditLogPage(await ProjectAsync(entries, ct), total);
    }

    public async Task<IReadOnlyList<AuditLogRow>> Handle(AuditTrailForRequestQuery request, CancellationToken ct)
    {
        var entries = await _db.AuditLogEntries.AsNoTracking()
            .Where(e => e.CorrelationId == request.CorrelationId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(ct);

        return await ProjectAsync(entries, ct);
    }

    private async Task<List<AuditLogRow>> ProjectAsync(List<AuditLogEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        // Approver names are resolved separately because the row stores an id: the name at the time
        // is not what a reviewer wants six months later if the person has since been renamed.
        var approverIds = entries.Where(e => e.ApproverStaffId.HasValue)
            .Select(e => e.ApproverStaffId!.Value)
            .Distinct()
            .ToList();

        var approvers = approverIds.Count == 0
            ? []
            : await _db.StaffProfiles.AsNoTracking()
                .Where(s => approverIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        return entries.Select(e => new AuditLogRow(
            e.Id,
            e.OccurredAt,
            e.Action,
            e.ActorName,
            e.ActorStaffId,
            e.StationId,
            e.IpAddress,
            e.EntityType,
            e.EntityId,
            e.Operation,
            e.BeforeJson,
            e.AfterJson,
            e.ApproverStaffId is { } id && approvers.TryGetValue(id, out var name) ? name : null,
            e.Reason,
            e.CorrelationId)).ToList();
    }
}
