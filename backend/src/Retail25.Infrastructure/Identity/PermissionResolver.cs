using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// Resolves a user's permissions from their roles' <c>role_permission</c> rows, with a short cache.
/// <para>
/// The authorisation behaviour runs on every command, so an uncached lookup would put a database
/// round trip inside the till's 120 ms quote budget. The cache is deliberately short-lived and
/// explicitly invalidated when a grant changes: a revoked permission that stays live for five
/// minutes is a revoked permission that did not work.
/// </para>
/// </summary>
public sealed class PermissionResolver : IPermissionResolver
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public PermissionResolver(ApplicationDbContext db, [FromKeyedServices("permissions")] IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlySet<string>> ResolveForUserAsync(long userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey(userId), out IReadOnlySet<string>? cached) && cached is not null)
        {
            return cached;
        }

        var roleIds = await _db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var permissions = roleIds.Count == 0
            ? []
            : await _db.RolePermissions
                .Where(rp => roleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionKey)
                .Distinct()
                .ToListAsync(ct);

        var set = new HashSet<string>(permissions, StringComparer.Ordinal);

        _cache.Set(CacheKey(userId), (IReadOnlySet<string>)set, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheLifetime,
            Size = 1,
        });

        return set;
    }

    public Task InvalidateAsync(long userId, CancellationToken ct = default)
    {
        _cache.Remove(CacheKey(userId));
        return Task.CompletedTask;
    }

    public Task InvalidateAllAsync(CancellationToken ct = default)
    {
        // A role's grants changing affects everyone who holds it, and the membership list is not
        // to hand. Compacting the whole cache is cheap next to getting authorisation wrong.
        if (_cache is MemoryCache memoryCache)
        {
            memoryCache.Compact(1.0);
        }

        return Task.CompletedTask;
    }

    private static string CacheKey(long userId) => $"permissions:{userId:N}";
}
