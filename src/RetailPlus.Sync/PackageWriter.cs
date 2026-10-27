using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Data;

namespace RetailPlus.Sync;

/// <summary>What goes into a till's package for the server.</summary>
public sealed record UploadOptions(TillScope Till, byte[] Key, DateTime? ChangedAfterUtc, long Cursor, string? AppVersion)
{
    /// <summary>A copy of the database file to put in the package (made with SQLite's online backup).</summary>
    public string? DatabaseCopy { get; init; }
    /// <summary>Write a package even when nothing changed.</summary>
    public bool EvenIfEmpty { get; init; }
}

/// <summary>What goes into the server's package for a till.</summary>
public sealed record DownloadOptions(TillScope Till, byte[] Key, long Cursor, string? AppVersion);

/// <summary>
/// Writes packages from the EF model: every table the policy lets travel in that direction, parents before children,
/// and the deletions. A till sends what changed since its last package; the server sends a till what changed since its
/// last download (by the server's change numbers) among the rows that till receives.
/// </summary>
public sealed class PackageWriter(SyncPolicy? policy = null)
{
    /// <summary>
    /// A till's package goes back this far before the last one, so a row saved by a transaction that was still running
    /// when the last package was written is not missed. Rows sent twice change nothing.
    /// </summary>
    public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(10);

    public SyncPolicy Policy { get; } = policy ?? SyncPolicy.Default;

    /// <summary>
    /// Writes a till's package to <paramref name="output"/>; null (and nothing written) when nothing changed. The manifest's
    /// <see cref="PackageManifest.PackagedThroughUtc"/> is where the next package starts.
    /// </summary>
    public async Task<PackageManifest?> WriteUploadAsync(RetailPlusDbContext db, Stream output, UploadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        var model = SyncModel.For(db.Model, Policy);
        DateTime? since = options.ChangedAfterUtc is { } after && after > DateTime.MinValue + Overlap ? after - Overlap : null;
        var tables = new List<(SyncTable Table, List<object?[]> Rows)>();
        List<Tombstone> tombstones;
        DateTime through;
        // On SQLite the transaction takes the write lock, so saves already under way finish first and their rows are read.
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            through = SyncColumns.Now();
            foreach (var table in model.Tables.Where(t => t.Flow.HasFlag(SyncFlow.Up)))
            {
                var rows = await ReadRowsAsync(db, table, since is { } s ? ChangedAfter(table, s) : null, cancellationToken);
                if (rows.Count > 0) tables.Add((table, rows));
            }
            var names = model.Tables.Where(t => t.Flow.HasFlag(SyncFlow.Up)).Select(t => t.Name).ToList();
            tombstones = await db.Tombstones.AsNoTracking()
                .Where(t => (since == null || t.UpdatedAtUtc > since) && t.MergedIntoId == null && names.Contains(t.EntityName))
                .OrderBy(t => t.DeletedAtUtc).ToListAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        if (tables.Count == 0 && tombstones.Count == 0 && options.DatabaseCopy is null && !options.EvenIfEmpty) return null;

        var manifest = new PackageManifest
        {
            BatchId = Guid.CreateVersion7(), Kind = SyncDirection.Upload, StoreId = options.Till.StoreId, StationNumber = options.Till.StationNumber,
            AppVersion = options.AppVersion, CreatedAtUtc = through, ChangedAfterUtc = since, PackagedThroughUtc = through, Cursor = options.Cursor,
        };
        await BuildAsync(output, manifest, tables, tombstones, options.DatabaseCopy, options.Key, cancellationToken);
        return manifest;
    }

    /// <summary>
    /// Writes the server's package for a till: the rows it receives that changed after <see cref="DownloadOptions.Cursor"/>,
    /// up to the newest change number every running transaction is past. Server (SQL Server) only.
    /// </summary>
    public async Task<PackageManifest> WriteDownloadAsync(RetailPlusDbContext db, Stream output, DownloadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        if (!db.Database.IsSqlServer()) throw new InvalidOperationException("Packages for tills are written by the server, on SQL Server.");
        var model = SyncModel.For(db.Model, Policy);
        // Change numbers below MIN_ACTIVE_ROWVERSION belong to committed rows only, and no transaction still running can
        // commit one below it; so nothing below this mark can turn up later behind the till's cursor.
        long high = await db.Database.SqlQueryRaw<long>("SELECT CAST(MIN_ACTIVE_ROWVERSION() AS bigint) - 1 AS [Value]").SingleAsync(cancellationToken);
        long cursor = options.Cursor > high ? 0 : Math.Max(0, options.Cursor);   // a cursor from another database starts over
        var tables = new List<(SyncTable Table, List<object?[]> Rows)>();
        foreach (var table in model.Tables.Where(t => t.Flow.HasFlag(SyncFlow.Down)))
        {
            var filter = And(Between(table.ClrType, cursor, high), model.DownloadFilter(table, db, options.Till));
            var rows = await ReadRowsAsync(db, table, filter, cancellationToken);
            if (rows.Count > 0) tables.Add((table, rows));
        }
        var names = model.Tables.Where(t => t.Flow.HasFlag(SyncFlow.Down)).Select(t => t.Name).ToList();
        var store = options.Till.StoreId;
        var tombstones = await db.Tombstones.AsNoTracking()
            .Where(t => EF.Property<long>(t, SyncColumns.ChangeNumber) > cursor && EF.Property<long>(t, SyncColumns.ChangeNumber) <= high)
            .Where(t => (t.StoreId == null || t.StoreId == store) && names.Contains(t.EntityName))
            .OrderBy(t => t.DeletedAtUtc).ToListAsync(cancellationToken);

        var manifest = new PackageManifest
        {
            BatchId = Guid.CreateVersion7(), Kind = SyncDirection.Download, StoreId = store, StationNumber = options.Till.StationNumber,
            AppVersion = options.AppVersion, CreatedAtUtc = SyncColumns.Now(), Cursor = cursor, CursorTo = high,
        };
        await BuildAsync(output, manifest, tables, tombstones, null, options.Key, cancellationToken);
        return manifest;
    }

    private static async Task BuildAsync(Stream output, PackageManifest manifest, List<(SyncTable Table, List<object?[]> Rows)> tables,
        List<Tombstone> tombstones, string? databaseCopy, byte[] key, CancellationToken cancellationToken)
    {
        using var builder = new PackageBuilder(output, manifest);
        foreach (var (table, rows) in tables)
            manifest.Tables.Add(builder.Add(table.Name, $"tables/{table.Name}.json", RowJson.Write(table.Columns, rows), rows.Count));
        if (tombstones.Count > 0)
        {
            var rows = tombstones.Select(t => new TombstoneRow(t.EntityName, t.RowId, t.DeletedAtUtc, t.StoreId, t.MergedIntoId)).ToList();
            manifest.Tombstones = builder.Add(nameof(Tombstone), SyncPackage.TombstonesFile,
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(rows, SyncPackage.Json), rows.Count);
        }
        if (databaseCopy is not null)
            manifest.Database = await builder.AddFileAsync("Database", SyncPackage.DatabaseFile, databaseCopy, cancellationToken);
        builder.Finish(key);
    }

    /// <summary>The rows of a table that pass the filter, each as its column values in the table's column order.</summary>
    internal static async Task<List<object?[]>> ReadRowsAsync(RetailPlusDbContext db, SyncTable table, LambdaExpression? filter, CancellationToken cancellationToken)
    {
        var rows = SyncModel.Rows(db, table.ClrType);
        if (filter is not null) rows = SyncModel.Where(rows, filter);
        var e = Expression.Parameter(table.ClrType, "e");
        var values = Expression.NewArrayInit(typeof(object),
            table.Columns.Select(c => Expression.Convert(SyncModel.Column(e, c.Name, c.ClrType), typeof(object))));
        var select = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [table.ClrType, typeof(object[])],
            rows.Expression, Expression.Quote(Expression.Lambda(values, e)));
        return (await rows.Provider.CreateQuery<object?[]>(select).ToListAsync(cancellationToken))!;
    }

    private static LambdaExpression ChangedAfter(SyncTable table, DateTime since)
    {
        var e = Expression.Parameter(table.ClrType, "e");
        return Expression.Lambda(Expression.GreaterThan(SyncModel.Column<DateTime>(e, nameof(ITracked.UpdatedAtUtc)), SyncModel.Value(since)), e);
    }

    private static LambdaExpression Between(Type type, long after, long upTo)
    {
        var e = Expression.Parameter(type, "e");
        var number = SyncModel.Column<long>(e, SyncColumns.ChangeNumber);
        return Expression.Lambda(Expression.AndAlso(Expression.GreaterThan(number, SyncModel.Value(after)), Expression.LessThanOrEqual(number, SyncModel.Value(upTo))), e);
    }

    /// <summary>Both conditions on the same row.</summary>
    internal static LambdaExpression And(LambdaExpression first, LambdaExpression? second)
    {
        if (second is null) return first;
        var body = new Rebind(second.Parameters[0], first.Parameters[0]).Visit(second.Body);
        return Expression.Lambda(Expression.AndAlso(first.Body, body), first.Parameters);
    }

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
