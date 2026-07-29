using Retail25.Application.Abstractions;
using StackExchange.Redis;

namespace Retail25.Infrastructure.Caching;

/// <summary>
/// Cross-station tag arbitration on top of <c>SET key value NX PX</c> (doc 06 §2).
/// <para>
/// <c>NX</c> makes the claim atomic, so two tills reading the same basket cannot both win. The
/// expiry makes it self-healing: if a till crashes mid-sale the tag frees itself rather than becoming
/// permanently unsellable, which is what a claim without a TTL would do.
/// </para>
/// </summary>
public sealed class RedisTagDebouncer : ITagDebouncer
{
    private const string KeyPrefix = "tag:";

    private readonly IConnectionMultiplexer _redis;

    public RedisTagDebouncer(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<bool> TryClaimAsync(string epc, Guid stationId, TimeSpan window, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = Key(epc);
        var owner = stationId.ToString("N");

        if (await db.StringSetAsync(key, owner, window, When.NotExists))
        {
            return true;
        }

        // Re-reading a tag the same till already holds is normal — a reader reports it many times a
        // second. Refresh the window and treat it as a success so it is not reported as a conflict.
        var current = await db.StringGetAsync(key);
        if (current.IsNullOrEmpty || current != owner)
        {
            return false;
        }

        await db.KeyExpireAsync(key, window);
        return true;
    }

    public async Task ReleaseAsync(string epc, Guid stationId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = Key(epc);

        // Only the holder may release, or one till would be able to free another's tag.
        var current = await db.StringGetAsync(key);
        if (!current.IsNullOrEmpty && current == stationId.ToString("N"))
        {
            await db.KeyDeleteAsync(key);
        }
    }

    public async Task<Guid?> GetHolderAsync(string epc, CancellationToken ct = default)
    {
        var value = await _redis.GetDatabase().StringGetAsync(Key(epc));
        return value.IsNullOrEmpty || !Guid.TryParse(value!, out var stationId) ? null : stationId;
    }

    private static RedisKey Key(string epc) => KeyPrefix + epc.Trim().ToUpperInvariant();
}

/// <summary>
/// Idempotency replay store (doc 05). A repeated <c>Idempotency-Key</c> returns the original
/// response rather than taking the money twice — which is exactly what a cashier pressing Pay again
/// after a timeout would otherwise cause.
/// </summary>
public sealed class RedisIdempotencyStore : Retail25.Application.Behaviors.IIdempotencyStore
{
    private const string KeyPrefix = "idem:";

    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly IConnectionMultiplexer _redis;

    public RedisIdempotencyStore(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<T?> GetResponseAsync<T>(string key, CancellationToken ct = default)
    {
        var value = await _redis.GetDatabase().StringGetAsync(KeyPrefix + key);
        return value.IsNullOrEmpty ? default : System.Text.Json.JsonSerializer.Deserialize<T>(value!);
    }

    public async Task StoreResponseAsync<T>(string key, T response, CancellationToken ct = default)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(response);
        await _redis.GetDatabase().StringSetAsync(KeyPrefix + key, payload, Retention);
    }
}
