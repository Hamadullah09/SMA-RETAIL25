using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Security;

namespace Retail25.Infrastructure.Persistence;

/// <summary>The request-scoped facts an audit row needs that the application layer cannot see.</summary>
public sealed class HttpRequestContext : IRequestContext
{
    public HttpRequestContext(IHttpContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);

        var context = accessor.HttpContext;

        IpAddress = context?.Connection.RemoteIpAddress?.ToString();
        UserAgent = context?.Request.Headers.UserAgent.ToString();

        // Prefer the caller's correlation id so a trace spans the BFF and the API; fall back to the
        // ASP.NET trace identifier so a row is never left without one.
        CorrelationId = context?.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                        ?? context?.TraceIdentifier;
    }

    public string? IpAddress { get; }

    public string? CorrelationId { get; }

    public string? UserAgent { get; }
}

/// <summary>
/// Writes the audit rows that are not a single entity change — sign-ins, refusals, step-ups
/// (doc 07 §Audit).
/// <para>
/// The interceptor covers entity writes automatically. This covers the rest, which is most of what a
/// loss-prevention review actually asks about: who tried, who approved, and who was turned away.
/// </para>
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IRequestContext _requestContext;
    private readonly IDateTime _clock;

    public AuditWriter(
        ApplicationDbContext db,
        ICurrentUser currentUser,
        IRequestContext requestContext,
        IDateTime clock)
    {
        _db = db;
        _currentUser = currentUser;
        _requestContext = requestContext;
        _clock = clock;
    }

    public async Task RecordAsync(
        AuditAction action,
        string entityType,
        string? entityId = null,
        string? operation = null,
        string? beforeJson = null,
        string? afterJson = null,
        Guid? approverStaffId = null,
        string? reason = null,
        CancellationToken ct = default)
    {
        var actorName = _currentUser.StaffId is { } staffId
            ? await _db.StaffProfiles.AsNoTracking()
                .Where(s => s.Id == staffId)
                .Select(s => s.FullName)
                .FirstOrDefaultAsync(ct)
            : null;

        var entry = AuditLogEntry
            .For(action, entityType, _clock.Now, entityId, operation)
            .WithActor(
                _currentUser.UserId,
                _currentUser.StaffId,
                actorName,
                _currentUser.StationId,
                _currentUser.LocationId,
                _requestContext.IpAddress,
                _requestContext.CorrelationId);

        entry.BeforeJson = beforeJson;
        entry.AfterJson = afterJson;
        entry.ApproverStaffId = approverStaffId;
        entry.Reason = reason;

        _db.AuditLogEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
    }
}
