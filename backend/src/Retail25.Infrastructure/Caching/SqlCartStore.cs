using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Retail25.Application.Abstractions;
using Retail25.Domain.Sales;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Infrastructure.Caching;

/// <summary>
/// The cart in SQL Server, for deployments with no Redis (doc 06 §2).
/// <para>
/// The stored shape is the same <see cref="PersistedCart"/> JSON the Redis store writes, so the two
/// providers are interchangeable and a shop can move between them without a data migration.
/// </para>
/// <para>
/// Redis held two keys per cart — the snapshot and a station pointer. Here it is one row, and the
/// pointer is a flag on it. That removes a failure the two-key version had: a crash between the two
/// writes left a station pointing at a cart whose snapshot said it was finished.
/// </para>
/// </summary>
public sealed class SqlCartStore : ICartStore
{
    /// <summary>
    /// Twelve hours, matching the Redis TTL. An abandoned cart evaporates rather than accumulating,
    /// and a suspended one is gone from here entirely because SQL Server's own cart tables are its
    /// home by then.
    /// </summary>
    private const int TtlHours = 12;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string GetSql =
        """
        SELECT payload FROM cached_cart
        WHERE cart_id = @cartId AND expires_at > SYSDATETIMEOFFSET();
        """;

    private const string GetByStationSql =
        """
        SELECT TOP (1) payload FROM cached_cart
        WHERE station_id = @stationId AND is_active = 1 AND expires_at > SYSDATETIMEOFFSET()
        ORDER BY saved_at DESC;
        """;

    // HOLDLOCK is what makes this an upsert rather than a race. Without it two concurrent saves of
    // the same cart can both find no row and both insert, and one loses to a primary key violation
    // mid-sale.
    private const string SaveSql =
        """
        MERGE cached_cart WITH (HOLDLOCK) AS target
        USING (SELECT @cartId AS cart_id) AS source ON target.cart_id = source.cart_id
        WHEN MATCHED THEN UPDATE SET
            station_id = @stationId,
            is_active = @isActive,
            payload = @payload,
            saved_at = SYSDATETIMEOFFSET(),
            expires_at = DATEADD(hour, @ttlHours, SYSDATETIMEOFFSET())
        WHEN NOT MATCHED THEN INSERT (cart_id, station_id, is_active, payload, saved_at, expires_at)
            VALUES (@cartId, @stationId, @isActive, @payload, SYSDATETIMEOFFSET(), DATEADD(hour, @ttlHours, SYSDATETIMEOFFSET()));
        """;

    // Two statements because they mean two different things: forget this cart, and release the
    // till. The second is the analogue of deleting the Redis station key — it must not delete
    // another cart's snapshot, only its claim on the station.
    private const string RemoveSql =
        """
        DELETE FROM cached_cart WHERE cart_id = @cartId;
        UPDATE cached_cart SET is_active = 0 WHERE station_id = @stationId AND is_active = 1;
        """;

    private readonly ApplicationDbContext _context;
    private readonly CacheSweeper _sweeper;
    private readonly ILogger<SqlCartStore> _logger;

    public SqlCartStore(ApplicationDbContext context, CacheSweeper sweeper, ILogger<SqlCartStore> logger)
    {
        _context = context;
        _sweeper = sweeper;
        _logger = logger;
    }

    public async Task<CartSnapshot?> GetAsync(long cartId, CancellationToken ct = default)
    {
        var (connection, transaction) = await SqlCacheSession.OpenAsync(_context, ct);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = GetSql;
        command.AddParameter("@cartId", cartId);

        var payload = await command.ExecuteScalarAsync(ct) as string;
        return payload is null ? null : Deserialize(payload, cartId);
    }

    public async Task<CartSnapshot?> GetByStationAsync(long stationId, CancellationToken ct = default)
    {
        var (connection, transaction) = await SqlCacheSession.OpenAsync(_context, ct);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = GetByStationSql;
        command.AddParameter("@stationId", stationId);

        var payload = await command.ExecuteScalarAsync(ct) as string;

        // No cart id to name in the log if this fails to parse, so borrow the station's. A cart
        // that cannot be read is discarded either way.
        return payload is null ? null : Deserialize(payload, stationId);
    }

    public async Task SaveAsync(CartSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var payload = JsonSerializer.Serialize(PersistedCart.From(snapshot), SerializerOptions);
        var (connection, transaction) = await SqlCacheSession.OpenAsync(_context, ct);

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = SaveSql;
            command.AddParameter("@cartId", snapshot.Cart.Id);
            command.AddParameter("@stationId", snapshot.Cart.StationId);

            // Only an active cart owns its station; a completed or suspended one releases it so the
            // next customer can start a sale.
            command.AddParameter("@isActive", snapshot.Cart.IsActive);
            command.AddParameter("@payload", payload);
            command.AddParameter("@ttlHours", TtlHours);

            await command.ExecuteNonQueryAsync(ct);
        }

        await _sweeper.MaybeSweepAsync(connection, transaction, ct);
    }

    public async Task RemoveAsync(long cartId, long stationId, CancellationToken ct = default)
    {
        var (connection, transaction) = await SqlCacheSession.OpenAsync(_context, ct);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RemoveSql;
        command.AddParameter("@cartId", cartId);
        command.AddParameter("@stationId", stationId);

        await command.ExecuteNonQueryAsync(ct);
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
            _logger.LogError(ex, "Discarding unreadable cart {CartId} from the cache table", cartId);
            return null;
        }
    }
}
