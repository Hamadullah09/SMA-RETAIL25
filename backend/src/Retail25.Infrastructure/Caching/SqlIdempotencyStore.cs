using System.Text.Json;
using Retail25.Application.Behaviors;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Infrastructure.Caching;

/// <summary>
/// Idempotency replay store in SQL Server (doc 05). A repeated <c>Idempotency-Key</c> returns the
/// original response rather than taking the money twice — which is exactly what a cashier pressing
/// Pay again after a timeout would otherwise cause.
/// </summary>
public sealed class SqlIdempotencyStore : IIdempotencyStore
{
    /// <summary>
    /// Twenty-four hours, matching the Redis retention. Long enough to cover a till that was
    /// retried after an outage, short enough that the table does not become a second sales ledger.
    /// </summary>
    private const int RetentionHours = 24;

    private const string GetSql =
        """
        SELECT payload FROM cached_idempotency_entry
        WHERE idempotency_key = @key AND expires_at > SYSDATETIMEOFFSET();
        """;

    private const string StoreSql =
        """
        MERGE cached_idempotency_entry WITH (HOLDLOCK) AS target
        USING (SELECT @key AS idempotency_key) AS source ON target.idempotency_key = source.idempotency_key
        WHEN MATCHED THEN UPDATE SET
            payload = @payload,
            expires_at = DATEADD(hour, @retentionHours, SYSDATETIMEOFFSET())
        WHEN NOT MATCHED THEN INSERT (idempotency_key, payload, expires_at)
            VALUES (@key, @payload, DATEADD(hour, @retentionHours, SYSDATETIMEOFFSET()));
        """;

    private readonly ApplicationDbContext _context;
    private readonly CacheSweeper _sweeper;

    public SqlIdempotencyStore(ApplicationDbContext context, CacheSweeper sweeper)
    {
        _context = context;
        _sweeper = sweeper;
    }

    public async Task<T?> GetResponseAsync<T>(string key, CancellationToken ct = default)
    {
        var (connection, transaction) = await SqlCacheSession.OpenAsync(_context, ct);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = GetSql;
        command.AddParameter("@key", key);

        var payload = await command.ExecuteScalarAsync(ct) as string;
        return payload is null ? default : JsonSerializer.Deserialize<T>(payload);
    }

    public async Task StoreResponseAsync<T>(string key, T response, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(response);
        var (connection, transaction) = await SqlCacheSession.OpenAsync(_context, ct);

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = StoreSql;
            command.AddParameter("@key", key);
            command.AddParameter("@payload", payload);
            command.AddParameter("@retentionHours", RetentionHours);

            await command.ExecuteNonQueryAsync(ct);
        }

        await _sweeper.MaybeSweepAsync(connection, transaction, ct);
    }
}
