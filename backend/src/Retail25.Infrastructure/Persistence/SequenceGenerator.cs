using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Retail25.Application.Abstractions;
using Retail25.Domain.Configuration;

namespace Retail25.Infrastructure.Persistence;

/// <summary>
/// Document numbers from Postgres sequences, one per location and document kind.
/// <para>
/// The legacy system kept a "next number" setting per workstation, which is why two tills selling at
/// the same moment could produce the same invoice number. A sequence is transactional, monotonic and
/// unaffected by rollback — a rolled-back sale burns a number, which is the correct trade: a gap is
/// auditable, a duplicate is not.
/// </para>
/// <para>
/// Each sequence is created on first use and <b>started from the administered
/// <see cref="NumberSequence"/> row</b>. That is what carries a migrated store's numbering forward:
/// the importer writes the legacy counter into the row, and the first number this ever issues
/// continues from it rather than from 1.
/// </para>
/// </summary>
public sealed class SequenceGenerator : ISequenceGenerator
{
    private static readonly ConcurrentDictionary<string, byte> EnsuredSequences = new(StringComparer.Ordinal);

    private readonly ApplicationDbContext _db;

    public SequenceGenerator(ApplicationDbContext db) => _db = db;

    public Task<long> NextTransactionNumberAsync(long locationId, CancellationToken ct = default)
        => NextAsync(SequenceKind.Transaction, locationId, ct);

    public Task<long> NextInvoiceNumberAsync(long locationId, CancellationToken ct = default)
        => NextAsync(SequenceKind.Invoice, locationId, ct);

    public async Task<long> NextAsync(SequenceKind kind, long locationId, CancellationToken ct = default)
    {
        // The name is built from a fixed prefix, a known enum name and a GUID in "N" form, so it
        // contains only letters, digits and underscores. Nothing here is user-supplied, which is what
        // makes the raw interpolation safe — an identifier cannot be a bound parameter in PostgreSQL.
        var name = $"seq_{kind.ToString().ToLowerInvariant()}_{locationId:N}";

        // Sequences are created lazily so adding a location is a data operation, not a migration.
        if (EnsuredSequences.TryAdd(name, 0))
        {
            var start = await _db.NumberSequences.AsNoTracking()
                .Where(s => s.LocationId == locationId && s.Kind == kind)
                .Select(s => (long?)s.NextNumber)
                .FirstOrDefaultAsync(ct) ?? 1L;

            var startAt = Math.Max(1L, start).ToString(CultureInfo.InvariantCulture);

#pragma warning disable EF1002
            await _db.Database.ExecuteSqlRawAsync(
                "CREATE SEQUENCE IF NOT EXISTS \"" + name + "\" AS bigint START " + startAt + " INCREMENT 1",
                ct);
#pragma warning restore EF1002
        }

        if (_db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
        {
            await _db.Database.OpenConnectionAsync(ct);
        }

        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT nextval('\"" + name + "\"')";
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();

        var value = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public async Task RestartAsync(SequenceKind kind, long locationId, long nextNumber, CancellationToken ct = default)
    {
        var name = $"seq_{kind.ToString().ToLowerInvariant()}_{locationId:N}";
        var startAt = Math.Max(1L, nextNumber).ToString(CultureInfo.InvariantCulture);

#pragma warning disable EF1002
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE SEQUENCE IF NOT EXISTS \"" + name + "\" AS bigint START " + startAt + " INCREMENT 1",
            ct);

        await _db.Database.ExecuteSqlRawAsync(
            "ALTER SEQUENCE \"" + name + "\" RESTART WITH " + startAt,
            ct);
#pragma warning restore EF1002

        EnsuredSequences.TryAdd(name, 0);
    }
}
