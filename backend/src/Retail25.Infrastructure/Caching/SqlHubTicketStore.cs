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

    // The expiry is in the WHERE clause, not merely checked afterwards: an expired ticket must not
    // be redeemable even in the moment between the sweep that should have removed it and the next.
    private const string RedeemSql =
        """
        DELETE FROM cached_hub_ticket
        OUTPUT deleted.payload
        WHERE ticket = @ticket AND expires_at > SYSDATETIMEOFFSET();
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

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RedeemSql;
        command.AddParameter("@ticket", ticket);

        var payload = await command.ExecuteScalarAsync(ct) as string;
        return payload is null ? null : JsonSerializer.Deserialize<HubTicket>(payload);
    }
}
