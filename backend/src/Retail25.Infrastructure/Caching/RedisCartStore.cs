using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Retail25.Application.Abstractions;
using Retail25.Domain.Sales;
using StackExchange.Redis;

namespace Retail25.Infrastructure.Caching;

/// <summary>
/// The cart in Redis (doc 06 §2).
/// <para>
/// Two keys per cart: <c>cart:{id}</c> holds the serialized snapshot, <c>station:{id}:cart</c> points
/// a till at the cart it is running. The TTL is 12 hours so an abandoned cart eventually evaporates
/// without a sweeper job, and a suspended cart is removed from Redis entirely because its home is
/// Postgres by then.
/// </para>
/// </summary>
public sealed class RedisCartStore : ICartStore
{
    private const string CartKeyPrefix = "cart:";
    private const string StationKeyPrefix = "station:";

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(12);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCartStore> _logger;

    public RedisCartStore(IConnectionMultiplexer redis, ILogger<RedisCartStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<CartSnapshot?> GetAsync(long cartId, CancellationToken ct = default)
    {
        var value = await _redis.GetDatabase().StringGetAsync(CartKey(cartId));
        return value.IsNullOrEmpty ? null : Deserialize(value!, cartId);
    }

    public async Task<CartSnapshot?> GetByStationAsync(long stationId, CancellationToken ct = default)
    {
        var cartId = await _redis.GetDatabase().StringGetAsync(StationKey(stationId));
        if (cartId.IsNullOrEmpty || !long.TryParse(cartId!, out var id))
        {
            return null;
        }

        return await GetAsync(id, ct);
    }

    public async Task SaveAsync(CartSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var db = _redis.GetDatabase();
        var payload = JsonSerializer.Serialize(PersistedCart.From(snapshot), SerializerOptions);

        var batch = db.CreateBatch();
        var tasks = new List<Task>
        {
            batch.StringSetAsync(CartKey(snapshot.Cart.Id), payload, Ttl),
        };

        // Only an active cart owns its station key; a completed or suspended one must release it so
        // the next customer can start a sale.
        if (snapshot.Cart.IsActive)
        {
            tasks.Add(batch.StringSetAsync(StationKey(snapshot.Cart.StationId), snapshot.Cart.Id.ToString(CultureInfo.InvariantCulture), Ttl));
        }
        else
        {
            tasks.Add(batch.KeyDeleteAsync(StationKey(snapshot.Cart.StationId)));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task RemoveAsync(long cartId, long stationId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync([CartKey(cartId), StationKey(stationId)]);
    }

    private CartSnapshot? Deserialize(string payload, long cartId)
    {
        try
        {
            return JsonSerializer.Deserialize<PersistedCart>(payload, SerializerOptions)?.ToSnapshot();
        }
        catch (JsonException ex)
        {
            // A cart that cannot be read is worse than no cart: returning null makes the till start
            // a fresh sale rather than showing half a basket.
            _logger.LogError(ex, "Discarding unreadable cart {CartId} from Redis", cartId);
            return null;
        }
    }

    private static RedisKey CartKey(long cartId) => CartKeyPrefix + cartId.ToString(CultureInfo.InvariantCulture);

    private static RedisKey StationKey(long stationId) => $"{StationKeyPrefix}{stationId}:cart";
}
