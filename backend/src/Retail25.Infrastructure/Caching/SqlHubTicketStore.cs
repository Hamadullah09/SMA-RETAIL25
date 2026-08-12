using System.Security.Cryptography;
using System.Text.Json;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Infrastructure.Caching;

/// <summary>
/// Hub tickets in SQL Server (doc 07 §Topology).
/// <para>
/// A ticket is single use, and that has to be a property of the operation rather than of a
/// follow-up delete a crash could skip. <c>DELETE … OUTPUT deleted.payload</c> is the equivalent of
/// Redis's <c>GETDEL</c>: the row is read and removed in one statement, so two connections racing
/// on the same ticket cannot both open a socket.
/// </para>
/// </summary>
public sealed class SqlHubTicketStore : IHubTicketStore
{
    private const string IssueSql =
        """
        INSERT INTO cached_hub_ticket (ticket, payload, expires_at)
        VALUES (@ticket, @payload, DATEADD(millisecond, @lifetimeMs, SYSDATETIMEOFFSET()));
        """;

    // Decrement-and-return, in one statement, for the same reason the delete was one statement:
    // two connections racing the same ticket must not both be served. UPDATE takes an exclusive
    // lock on the row, so the decrements serialise and the WHERE clause below is evaluated against
    // the value the previous one left behind.
    //
    // It counts down rather than deleting because one connection costs two exchanges — the
    // negotiate POST and the transport connection after it — and the SignalR client presents the
    // same token to both. Deleting on the first meant the WebSocket upgrade always arrived holding
    // a ticket that no longer existed, which is what forced every hub onto long polling.
    //
    // The expiry is in the WHERE clause, not merely checked afterwards: an expired ticket must not
    // be redeemable even in the moment between the sweep that should have removed it and the next.
    private const string RedeemSql =
        """
        UPDATE cached_hub_ticket
        SET redemptions_remaining = redemptions_remaining - 1
        OUTPUT inserted.payload
        WHERE ticket = @ticket
          AND expires_at > SYSDATETIMEOFFSET()
          AND redemptions_remaining > 0;
        """;

    // Rows that have nothing left are removed on the next redeem attempt and by the sweeper. Not
    // in the same statement as the decrement: an UPDATE that deletes its own row cannot also
    // OUTPUT it, and correctness here belongs to the decrement.
    private const string PurgeSpentSql =
        """
        DELETE FROM cached_hub_ticket
        WHERE ticket = @ticket AND redemptions_remaining <= 0;
        """;

    private readonly ApplicationDbContext _context;
    private readonly CacheSweeper _sweeper;

    public SqlHubTicketStore(ApplicationDbContext context, CacheSweeper sweeper)
    {
        _context = context;
        _sweeper = sweeper;
    }

    public async Task<string> IssueAsync(HubTicket ticket, TimeSpan lifetime, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        // 32 bytes of CSPRNG output: a ticket that could be guessed inside its own lifetime would be
        // worse than the token it replaces.
        var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var (connection, transaction) = await SqlCacheSession.OpenAsync(_context, ct);

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = IssueSql;
            command.AddParameter("@ticket", value);
            command.AddParameter("@payload", JsonSerializer.Serialize(ticket));
            command.AddParameter("@lifetimeMs", SqlCacheSession.ToMilliseconds(lifetime));

            await command.ExecuteNonQueryAsync(ct);
        }

        await _sweeper.MaybeSweepAsync(connection, transaction, ct);

        return value;
    }

    public async Task<HubTicket?> RedeemAsync(string ticket, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return null;
        }

        var (connection, transaction) = await SqlCacheSession.OpenAsync(_context, ct);

        string? payload;

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = RedeemSql;
            command.AddParameter("@ticket", ticket);

            payload = await command.ExecuteScalarAsync(ct) as string;
        }

        // Tidy the row away once it has nothing left. The redeem above has already decided the
        // answer, so this failing would cost a dead row until the sweeper runs, not a wrong result.
        using (var purge = connection.CreateCommand())
        {
            purge.Transaction = transaction;
            purge.CommandText = PurgeSpentSql;
            purge.AddParameter("@ticket", ticket);

            await purge.ExecuteNonQueryAsync(ct);
        }

        return payload is null ? null : JsonSerializer.Deserialize<HubTicket>(payload);
    }
}
