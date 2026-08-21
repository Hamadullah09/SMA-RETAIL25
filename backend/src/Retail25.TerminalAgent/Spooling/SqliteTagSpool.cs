using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retail25.Contracts.Terminals;

namespace Retail25.TerminalAgent.Spooling;

/// <summary>
/// Local durable queue for reads taken while the server was unreachable (doc 06 §6).
/// </summary>
public interface ITagSpool
{
    /// <summary>
    /// Spools a batch together with the reader that saw it.
    /// <para>
    /// The reader travels with the tags because the station is resolved from reader and antenna. A
    /// spool that stored only the reads had to guess on replay, and guessed the machine's own till —
    /// so a batch that failed to send once came back addressed to the wrong checkout.
    /// </para>
    /// </summary>
    Task EnqueueAsync(long readerId, IReadOnlyList<TagRead> tags, CancellationToken ct = default);

    /// <summary>Oldest entries first, capped. Returns the rows and the ids needed to acknowledge them.</summary>
    Task<IReadOnlyList<SpooledBatch>> PeekAsync(int maxBatches, CancellationToken ct = default);

    Task AcknowledgeAsync(IEnumerable<long> ids, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);
}

public sealed record SpooledBatch(long Id, DateTimeOffset SpooledAt, IReadOnlyList<TagRead> Tags, long ReaderId);

/// <summary>
/// SQLite because the spool has to survive a power cut, not just a process restart, and a till loses
/// power more often than anything else in the building.
/// <para>
/// It is deliberately bounded, by both age and row count. An unbounded spool on a machine that has
/// been offline all weekend fills the disk and then takes the till down for a reason that has nothing
/// to do with the outage — and reads from three days ago are worthless anyway, because the basket
/// they came from left long ago. Old entries are dropped and the drop is logged, never silent.
/// </para>
/// </summary>
public sealed class SqliteTagSpool : ITagSpool, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AgentOptions _options;
    private readonly ILogger<SqliteTagSpool> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteTagSpool(IOptions<AgentOptions> options, ILogger<SqliteTagSpool> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;

        var path = _options.ResolveSpoolPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());

        _connection.Open();
        Initialise();
    }

    private void Initialise()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS tag_spool (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                spooled_at  TEXT NOT NULL,
                payload     TEXT NOT NULL,

                -- Which reader saw these. Nullable, because a spool written by an older agent has
                -- no answer and must still replay: 0 there means "no reader identity", which is
                -- exactly what the per-station path is for.
                reader_id   INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_tag_spool_spooled_at ON tag_spool (spooled_at);
            """;
        command.ExecuteNonQuery();

        // The table above is only created on a fresh till. A spool file already on disk keeps the
        // shape it was made with, so the column has to be added to it as well — and SQLite has no
        // "add column if missing", hence asking.
        using var columns = _connection.CreateCommand();
        columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('tag_spool') WHERE name = 'reader_id'";

        if (Convert.ToInt64(columns.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            using var add = _connection.CreateCommand();
            add.CommandText = "ALTER TABLE tag_spool ADD COLUMN reader_id INTEGER NOT NULL DEFAULT 0";
            add.ExecuteNonQuery();
        }
    }

    public async Task EnqueueAsync(long readerId, IReadOnlyList<TagRead> tags, CancellationToken ct = default)
    {
        if (tags is null || tags.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(ct);

        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "INSERT INTO tag_spool (spooled_at, payload, reader_id) VALUES ($at, $payload, $reader)";
            command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(tags));
            command.Parameters.AddWithValue("$reader", readerId);

            await command.ExecuteNonQueryAsync(ct);
            await TrimAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SpooledBatch>> PeekAsync(int maxBatches, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);

        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT id, spooled_at, payload, reader_id FROM tag_spool ORDER BY id LIMIT $limit";
            command.Parameters.AddWithValue("$limit", Math.Max(1, maxBatches));

            var batches = new List<SpooledBatch>();
            await using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var tags = JsonSerializer.Deserialize<List<TagRead>>(reader.GetString(2)) ?? [];
                batches.Add(new SpooledBatch(
                    reader.GetInt64(0),
                    DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                    tags,
                    reader.GetInt64(3)));
            }

            return batches;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AcknowledgeAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        var list = ids?.ToList() ?? [];
        if (list.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(ct);

        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = $"DELETE FROM tag_spool WHERE id IN ({string.Join(',', list)})";
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);

        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM tag_spool";
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drops anything past the age or size bound, and says how much it dropped.</summary>
    private async Task TrimAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-_options.SpoolRetentionHours)
            .ToString("O", CultureInfo.InvariantCulture);

        await using var byAge = _connection.CreateCommand();
        byAge.CommandText = "DELETE FROM tag_spool WHERE spooled_at < $cutoff";
        byAge.Parameters.AddWithValue("$cutoff", cutoff);
        var expired = await byAge.ExecuteNonQueryAsync(ct);

        await using var bySize = _connection.CreateCommand();
        bySize.CommandText = """
            DELETE FROM tag_spool
            WHERE id NOT IN (SELECT id FROM tag_spool ORDER BY id DESC LIMIT $keep)
            """;
        bySize.Parameters.AddWithValue("$keep", _options.SpoolMaxBatches);
        var overflowed = await bySize.ExecuteNonQueryAsync(ct);

        if (expired + overflowed > 0)
        {
            _logger.LogWarning(
                "Dropped {Expired} expired and {Overflowed} overflowing spooled tag batches",
                expired,
                overflowed);
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
        _gate.Dispose();
    }
}
