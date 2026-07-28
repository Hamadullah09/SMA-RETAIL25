using System.Text.Json;
using Retail25.Application.Abstractions;
using Retail25.Domain.Sales;
using StackExchange.Redis;

namespace Retail25.Infrastructure.Carts;

/// <summary>
/// Cart storage in Redis — the deployment default.
/// <para>
/// The cart lives on the server because RFID reads arrive from a daemon rather than from the
/// browser, and because a second station must be able to see and resume a suspended sale. Redis is
/// used rather than the database because a cart is touched on every tag read and never needs to
/// survive as a permanent record; only the committed sale does.
/// </para>
/// </summary>
public sealed class RedisCartStore : ICartStore
{
    private const string CartKeyPrefix = "retail25:cart:";
    private const string LinesKeyPrefix = "retail25:cart-lines:";
    private const string StationKeyPrefix = "retail25:station-cart:";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _idleExpiry;

    /// <param name="redis">Shared connection multiplexer.</param>
    /// <param name="idleExpiry">
    /// How long an untouched cart survives. Every write refreshes it, so a cart a cashier is still
    /// working on never expires; one abandoned at the end of a shift does.
    /// </param>
    public RedisCartStore(IConnectionMultiplexer redis, TimeSpan idleExpiry)
    {
        _redis = redis;
        _idleExpiry = idleExpiry;
    }

    private IDatabase Db => _redis.GetDatabase();

    public async Task<Cart?> GetAsync(Guid cartId, CancellationToken ct = default)
    {
        var value = await Db.StringGetAsync(CartKey(cartId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<Cart>(value!, SerializerOptions);
    }

    public async Task<Cart?> GetByStationAsync(Guid stationId, CancellationToken ct = default)
    {
        var cartId = await Db.StringGetAsync(StationKey(stationId));
        if (cartId.IsNullOrEmpty || !Guid.TryParse(cartId!, out var id))
        {
            return null;
        }

        var cart = await GetAsync(id, ct);
        return cart?.Status == CartStatus.Active ? cart : null;
    }

    public async Task SetAsync(Cart cart, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cart);

        var payload = JsonSerializer.Serialize(cart, SerializerOptions);
        await Db.StringSetAsync(CartKey(cart.Id), payload, _idleExpiry);

        // The station pointer lets a reconnecting till find the sale it was in the middle of
        // without the browser having to remember a cart id across a refresh.
        if (cart.Status == CartStatus.Active)
        {
            await Db.StringSetAsync(StationKey(cart.StationId), cart.Id.ToString(), _idleExpiry);
        }
        else
        {
            await Db.KeyDeleteAsync(StationKey(cart.StationId));
        }
    }

    public async Task RemoveAsync(Guid cartId, CancellationToken ct = default)
    {
        var cart = await GetAsync(cartId, ct);

        await Db.KeyDeleteAsync(CartKey(cartId));
        await Db.KeyDeleteAsync(LinesKey(cartId));

        if (cart is not null)
        {
            await Db.KeyDeleteAsync(StationKey(cart.StationId));
        }
    }

    public async Task<IReadOnlyList<CartLine>> GetLinesAsync(Guid cartId, CancellationToken ct = default)
    {
        var value = await Db.StringGetAsync(LinesKey(cartId));
        if (value.IsNullOrEmpty)
        {
            return [];
        }

        var lines = JsonSerializer.Deserialize<List<CartLine>>(value!, SerializerOptions) ?? [];
        return lines.OrderBy(l => l.Sequence).ToList();
    }

    public async Task SetLinesAsync(Guid cartId, IReadOnlyList<CartLine> lines, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var payload = JsonSerializer.Serialize(lines, SerializerOptions);
        await Db.StringSetAsync(LinesKey(cartId), payload, _idleExpiry);

        // Keep the cart alive as long as its lines are being worked on.
        await Db.KeyExpireAsync(CartKey(cartId), _idleExpiry);
    }

    public async Task<int> IncrementRevisionAsync(Guid cartId, CancellationToken ct = default)
    {
        var cart = await GetAsync(cartId, ct);
        if (cart is null)
        {
            return 0;
        }

        cart.Revision++;
        await SetAsync(cart, ct);
        return cart.Revision;
    }

    private static string CartKey(Guid cartId) => CartKeyPrefix + cartId.ToString("N");

    private static string LinesKey(Guid cartId) => LinesKeyPrefix + cartId.ToString("N");

    private static string StationKey(Guid stationId) => StationKeyPrefix + stationId.ToString("N");
}
