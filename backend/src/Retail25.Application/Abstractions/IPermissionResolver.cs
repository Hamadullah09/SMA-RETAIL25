using Retail25.Domain.Security;

namespace Retail25.Application.Abstractions;

/// <summary>
/// Resolves what a user may do, from their roles' <c>role_permission</c> rows.
/// <para>
/// A port rather than a query so the result can be cached: the authorisation behaviour runs on every
/// command, and a database round trip there would sit inside the till's quote budget.
/// </para>
/// </summary>
public interface IPermissionResolver
{
    Task<IReadOnlySet<string>> ResolveForUserAsync(long userId, CancellationToken ct = default);

    /// <summary>Drops the cached set after a role or grant changes, so it takes effect immediately.</summary>
    Task InvalidateAsync(long userId, CancellationToken ct = default);

    /// <summary>Drops every cached set. Used when a role's grants change and many users are affected.</summary>
    Task InvalidateAllAsync(CancellationToken ct = default);
}

/// <summary>
/// Writes audit rows for things that are not a single entity change — a sign-in, a refused command,
/// a step-up (doc 07 §Audit).
/// <para>
/// The interceptor covers entity writes automatically. This covers the rest, which is most of what a
/// loss-prevention review actually asks about: who tried, who approved, who was turned away.
/// </para>
/// </summary>
public interface IAuditWriter
{
    Task RecordAsync(
        AuditAction action,
        string entityType,
        string? entityId = null,
        string? operation = null,
        string? beforeJson = null,
        string? afterJson = null,
        long? approverStaffId = null,
        string? reason = null,
        CancellationToken ct = default);
}
