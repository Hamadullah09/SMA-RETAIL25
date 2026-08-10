using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Infrastructure.Caching;

/// <summary>
/// Cross-station tag arbitration in SQL Server (doc 06 §2).
/// <para>
/// The Redis version is <c>SET key value NX PX</c>: atomic, self-expiring. Both properties have to
/// survive the move, because this is the thing that stops two tills selling the same tagged garment.
/// </para>
/// <para>
/// Atomicity comes from a single <c>MERGE</c> under <c>HOLDLOCK</c> — one statement, one lock on
/// the key, so two tills reading the same basket cannot both win. Expiry is a column rather than a
/// TTL, checked on every read and in the claim condition itself, which is what makes a claim
/// self-healing: a till that crashes mid-sale leaves a row that stops blocking anyone the moment
/// its window passes, instead of making the tag permanently unsellable.
/// </para>
/// </summary>
public sealed class SqlTagDebouncer : ITagDebouncer
{
    // Three outcomes in one statement:
    //   no row              -> INSERT, claimed
    //   row expired         -> UPDATE, claimed
    //   row held by us      -> UPDATE, window refreshed (a reader reports the same tag many times
    //                          a second; that is not a conflict)
    //   row held by another -> no action, and @@ROWCOUNT is 0
    private const string ClaimSql =
        """
        MERGE cached_tag_claim WITH (HOLDLOCK) AS target
        USING (SELECT @epc AS epc) AS source ON target.epc = source.epc
        WHEN MATCHED AND (target.expires_at <= SYSDATETIMEOFFSET() OR target.station_id = @stationId)
            THEN UPDATE SET
                station_id = @stationId,
                expires_at = DATEADD(millisecond, @windowMs, SYSDATETIMEOFFSET())
        WHEN NOT MATCHED THEN INSERT (epc, station_id, expires_at)
            VALUES (@epc, @stationId, DATEADD(millisecond, @windowMs, SYSDATETIMEOFFSET()));
        """;

    // Only the holder may release, or one till could free another's tag.
    private const string ReleaseSql =
        """
        DELETE FROM cached_tag_claim WHERE epc = @epc AND station_id = @stationId;
        """;

    private const string HolderSql =
        """
        SELECT station_id FROM cached_tag_claim
        WHERE epc = @epc AND expires_at > SYSDATETIMEOFFSET();
        """;

    private readonly ApplicationDbContext _context;

    public SqlTagDebouncer(ApplicationDbContext context) => _context = context;

    public async Task<bool> TryClaimAsync(string epc, long stationId, TimeSpan window, CancellationToken ct = default)
    {
        var (connection, transaction) = await SqlCacheSession.OpenAsync(_context, ct);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ClaimSql;
        command.AddParameter("@epc", Normalise(epc));
        command.AddParameter("@stationId", stationId);
        command.AddParameter("@windowMs", SqlCacheSession.ToMilliseconds(window));

        // MERGE reports the rows it inserted or updated. Matched-but-not-eligible does neither, so
        // zero means the tag is held by another till.
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task ReleaseAsync(string epc, long stationId, CancellationToken ct = default)
    {
        var (connection, transaction) = await SqlCacheSession.OpenAsync(_context, ct);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ReleaseSql;
        command.AddParameter("@epc", Normalise(epc));
        command.AddParameter("@stationId", stationId);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<long?> GetHolderAsync(string epc, CancellationToken ct = default)
    {
        var (connection, transaction) = await SqlCacheSession.OpenAsync(_context, ct);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = HolderSql;
        command.AddParameter("@epc", Normalise(epc));

        var value = await command.ExecuteScalarAsync(ct);
        return value is long stationId ? stationId : null;
    }

    /// <summary>
    /// The same normalisation the Redis key used. A reader that reports lower-case hex and one that
    /// reports upper-case are reporting the same tag, and they must collide on the same row.
    /// </summary>
    private static string Normalise(string epc) => epc.Trim().ToUpperInvariant();
}
