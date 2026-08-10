using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Infrastructure.Caching;

/// <summary>
/// Shared plumbing for the SQL-backed stores.
/// <para>
/// Commands run on <see cref="ApplicationDbContext"/>'s own connection and enlist in its ambient
/// transaction when there is one. That is the point of borrowing the connection rather than opening
/// a second: a sale that writes its ledger rows and clears its cart does both or neither. Two
/// connections would leave a paid transaction with a cart still sitting on the till.
/// </para>
/// </summary>
internal static class SqlCacheSession
{
    /// <summary>
    /// The connection to run on, already open, and the transaction to enlist in if one is running.
    /// </summary>
    internal static async Task<(DbConnection Connection, DbTransaction? Transaction)> OpenAsync(
        ApplicationDbContext context,
        CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();

        // EF opens and closes this connection around its own work, so its state here depends on
        // whether a query is in flight. Opening an already-open connection throws.
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return (connection, context.Database.CurrentTransaction?.GetDbTransaction());
    }

    /// <summary>Adds a parameter. Null becomes DBNull, which is not the same value to ADO.NET.</summary>
    internal static void AddParameter(this DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Milliseconds, clamped to what DATEADD can take, as an int parameter.</summary>
    internal static int ToMilliseconds(TimeSpan span)
    {
        // DATEADD(millisecond, …) takes an int, which caps a window at about 24 days. Anything
        // longer is a caller mistake rather than a real intent, and silently overflowing to a
        // negative expiry would make a claim that is already expired the moment it is written.
        var total = span.TotalMilliseconds;
        return total <= 0 ? 0 : total >= int.MaxValue ? int.MaxValue : (int)total;
    }
}

/// <summary>
/// Deletes expired rows, occasionally.
/// <para>
/// Redis expired keys itself. SQL Server does not, and the obvious replacement — a background
/// timer, or a Hangfire recurring job — does not survive this deployment: shared IIS recycles the
/// pool on a schedule and unloads it when idle, so anything that has to still be running at 2am
/// is not running. Sweeping from the write path is the version that works, because a shop that is
/// writing is a shop that is open.
/// </para>
/// <para>
/// Correctness never depends on this. Every read filters on <c>expires_at</c>, so an unswept row
/// is invisible rather than wrong; the sweep is only there to stop the tables growing forever.
/// </para>
/// </summary>
public sealed class CacheSweeper
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Bounded so the unlucky request that triggers a sweep pays a predictable cost. Whatever is
    /// left over is collected by the next sweep.
    /// <para>
    /// Written out as a constant string rather than composed: CA2100 treats any command text that
    /// is not a compile-time constant as a possible injection, and it is right to — a batch size
    /// interpolated today is a table name interpolated later.
    /// </para>
    /// </summary>
    private const string SweepSql =
        """
        DELETE TOP (500) FROM cached_cart WHERE expires_at <= SYSDATETIMEOFFSET();
        DELETE TOP (500) FROM cached_tag_claim WHERE expires_at <= SYSDATETIMEOFFSET();
        DELETE TOP (500) FROM cached_idempotency_entry WHERE expires_at <= SYSDATETIMEOFFSET();
        DELETE TOP (500) FROM cached_hub_ticket WHERE expires_at <= SYSDATETIMEOFFSET();
        """;

    private long _nextSweepTicks;

    /// <summary>
    /// Sweeps if it is due. Never throws: a failed sweep is housekeeping that did not happen, and
    /// taking a sale down for it would be trading a real failure for a cosmetic one.
    /// </summary>
    internal async Task MaybeSweepAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.Ticks;
        var next = Interlocked.Read(ref _nextSweepTicks);

        if (now < next)
        {
            return;
        }

        // Claim the sweep before doing it, so concurrent requests do not all sweep at once.
        if (Interlocked.CompareExchange(ref _nextSweepTicks, now + Interval.Ticks, next) != next)
        {
            return;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = SweepSql;

            await command.ExecuteNonQueryAsync(ct);
        }
        catch (DbException)
        {
            // Deliberately swallowed. See the summary.
        }
    }
}
