using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Retail25.Application.Abstractions;
using Retail25.Domain.Configuration;

namespace Retail25.Infrastructure.Persistence;

/// <summary>
/// Document numbers from SQL Server sequences, one per location and document kind.
/// <para>
/// The legacy system kept a "next number" setting per workstation, which is why two tills selling at
/// the same moment could produce the same invoice number. A sequence is monotonic and unaffected by
/// rollback — a rolled-back sale burns a number, which is the correct trade: a gap is auditable, a
/// duplicate is not. That property is the same on both engines, which is why this class survived the
/// move from PostgreSQL with its behaviour intact.
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
    private readonly ApplicationDbContext _db;

    public SequenceGenerator(ApplicationDbContext db) => _db = db;

    public Task<long> NextTransactionNumberAsync(long locationId, CancellationToken ct = default)
        => NextAsync(SequenceKind.Transaction, locationId, ct);

    public Task<long> NextInvoiceNumberAsync(long locationId, CancellationToken ct = default)
        => NextAsync(SequenceKind.Invoice, locationId, ct);

    public async Task<long> NextAsync(SequenceKind kind, long locationId, CancellationToken ct = default)
    {
        // The name is built from a fixed prefix, a known enum name and a numeric id, so it contains
        // only letters, digits and underscores. Nothing here is user-supplied, which is what makes
        // the raw interpolation safe — an identifier cannot be a bound parameter in PostgreSQL.
        var name = SequenceName(kind, locationId);

        // Created unconditionally, every time, before the number is drawn.
        //
        // Two rejected alternatives, both of which were tried here. A process-wide "already created"
        // set is an assumption about a database made by a process that outlives it: with integer keys
        // every fresh database's location id is 1, so one database's sequence name masked another's,
        // and a test that recreated its database under the same name hit the same thing. And catching
        // "no such sequence" to create it on demand was written against PostgreSQL, which aborts the
        // whole transaction on any failed statement — the recovery ran against a connection that
        // refused everything until rollback. SQL Server would tolerate that shape, but the reason to
        // avoid it is the same on both: a recovery path that only runs on first use is a recovery
        // path nobody exercises.
        //
        // So: one extra idempotent statement per number issued. That is a real cost on a till that
        // rings a sale a second, and it is still the right trade against issuing a duplicate invoice
        // number or failing a sale outright.
        await EnsureSequenceAsync(kind, locationId, name, ct);

        return await NextValueAsync(name, ct);
    }

    private async Task<long> NextValueAsync(string name, CancellationToken ct)
    {
        if (_db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
        {
            await _db.Database.OpenConnectionAsync(ct);
        }

        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT NEXT VALUE FOR [" + name + "]";
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();

        var value = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Creates the sequence, starting from the administered <see cref="NumberSequence"/> row so a
    /// migrated store's numbering continues rather than restarting at 1.
    /// </summary>
    private async Task EnsureSequenceAsync(SequenceKind kind, long locationId, string name, CancellationToken ct)
    {
        var start = await _db.NumberSequences.AsNoTracking()
            .Where(s => s.LocationId == locationId && s.Kind == kind)
            .Select(s => (long?)s.NextNumber)
            .FirstOrDefaultAsync(ct) ?? 1L;

        var startAt = Math.Max(1L, start).ToString(CultureInfo.InvariantCulture);

#pragma warning disable EF1002
        await _db.Database.ExecuteSqlRawAsync(CreateIfAbsent(name, startAt), ct);
#pragma warning restore EF1002
    }

    public async Task RestartAsync(SequenceKind kind, long locationId, long nextNumber, CancellationToken ct = default)
    {
        var name = SequenceName(kind, locationId);
        var startAt = Math.Max(1L, nextNumber).ToString(CultureInfo.InvariantCulture);

#pragma warning disable EF1002
        await _db.Database.ExecuteSqlRawAsync(CreateIfAbsent(name, startAt), ct);

        await _db.Database.ExecuteSqlRawAsync(
            "ALTER SEQUENCE [" + name + "] RESTART WITH " + startAt,
            ct);
#pragma warning restore EF1002
    }

    /// <summary>
    /// SQL Server has no <c>CREATE SEQUENCE IF NOT EXISTS</c>, so the guard is explicit.
    /// <para>
    /// <c>OBJECT_ID(…, 'SO')</c> rather than a query against <c>sys.sequences</c> by name: the
    /// former resolves through the default schema the way every other statement in this connection
    /// does, and the latter would match a sequence of the same name in a schema this code will never
    /// use — reporting "already there" about an object it cannot draw from.
    /// </para>
    /// </summary>
    private static string CreateIfAbsent(string name, string startAt)
        => "IF OBJECT_ID(N'[" + name + "]', 'SO') IS NULL "
           + "CREATE SEQUENCE [" + name + "] AS bigint START WITH " + startAt + " INCREMENT BY 1";

    private static string SequenceName(SequenceKind kind, long locationId)
        => string.Create(CultureInfo.InvariantCulture, $"seq_{kind.ToString().ToLowerInvariant()}_{locationId}");
}
