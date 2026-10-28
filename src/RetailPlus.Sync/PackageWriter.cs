using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    /// <summary>
    /// At most this many rows and deletions in one package: more are split into a series of packages, sent and applied
    /// one after another. Null for one package however large (a package saved to a file).
    /// </summary>
    public int? MaxRowsPerPackage { get; init; }
}

/// <summary>What goes into the server's package for a till.</summary>
public sealed record DownloadOptions(TillScope Till, byte[] Key, long Cursor, string? AppVersion);

/// <summary>
/// Writes packages from the EF model: every table the policy lets travel in that direction, parents before children,
/// and the deletions. A till sends what changed since its last package; the server sends a till what changed since its
/// last download (by the server's change numbers) among the rows that till receives. Rows go from the database into the
/// package as they are read, so neither side holds a package in memory.
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
    /// Writes a till's changes to <paramref name="output"/> as one package; null (and nothing written) when nothing
    /// changed. The manifest's <see cref="PackageManifest.PackagedThroughUtc"/> is where the next package starts.
    /// </summary>
    public async Task<PackageManifest?> WriteUploadAsync(RetailPlusDbContext db, Stream output, UploadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        var parts = await WriteUploadAsync(db, _ => output, false, options with { MaxRowsPerPackage = null }, cancellationToken);
        return parts.Count == 0 ? null : parts[0];
    }

    /// <summary>
    /// Writes a till's changes as a series of packages of at most <see cref="UploadOptions.MaxRowsPerPackage"/> rows, each
    /// into the stream <paramref name="openPart"/> gives for its number (from 1), which is closed when that package is
    /// done. Deletions come first (children before parents), then the tables parents before children, so applying the
    /// packages in order never needs a row of a later one. Returns the manifests in order; none when nothing changed.
    /// </summary>
    public Task<IReadOnlyList<PackageManifest>> WriteUploadAsync(RetailPlusDbContext db, Func<int, Stream> openPart, UploadOptions options, CancellationToken cancellationToken = default) =>
        WriteUploadAsync(db, openPart, true, options, cancellationToken);

    private async Task<IReadOnlyList<PackageManifest>> WriteUploadAsync(RetailPlusDbContext db, Func<int, Stream> openPart, bool ownsStreams, UploadOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(openPart);
        ArgumentNullException.ThrowIfNull(options);
        var model = SyncModel.For(db.Model, Policy);
        DateTime? since = options.ChangedAfterUtc is { } after && after > DateTime.MinValue + Overlap ? after - Overlap : null;
        var up = model.Tables.Where(t => t.Flow.HasFlag(SyncFlow.Up)).ToList();
        Guid? series = options.MaxRowsPerPackage is null ? null : Guid.CreateVersion7();
        DateTime through = default;
        using var parts = new PackageParts(openPart, ownsStreams, options.Key, options.MaxRowsPerPackage ?? int.MaxValue, () => new PackageManifest
        {
            Kind = SyncDirection.Upload, StoreId = options.Till.StoreId, StationNumber = options.Till.StationNumber, AppVersion = options.AppVersion,
            CreatedAtUtc = through, ChangedAfterUtc = since, Cursor = options.Cursor, SeriesId = series,
        });
        // On SQLite the transaction takes the write lock, so saves already under way finish first and their rows are read,
        // and nothing changes while the rows are read.
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            through = SyncColumns.Now();
            for (int i = up.Count - 1; i >= 0; i--)
            {
                var name = up[i].Name;
                var deleted = db.Tombstones.AsNoTracking()
                    .Where(t => t.EntityName == name && t.MergedIntoId == null && (since == null || t.UpdatedAtUtc > since))
                    .OrderBy(t => t.DeletedAtUtc).AsAsyncEnumerable();
                await foreach (var tombstone in deleted.WithCancellation(cancellationToken)) parts.AddTombstone(tombstone);
            }
            foreach (var table in up)
            {
                // Rows that point at rows of their own table are never split over two packages.
                if (table.RefersToItself) parts.Break();
                await foreach (var row in StreamRowsAsync(db, table, since is { } s ? ChangedAfter(table, s) : null, cancellationToken))
                    parts.AddRow(table, row, mayBreak: !table.RefersToItself);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        if (parts.IsEmpty && options.DatabaseCopy is null && !options.EvenIfEmpty) return [];
        if (options.DatabaseCopy is not null) await parts.AddDatabaseAsync(options.DatabaseCopy, cancellationToken);
        return parts.Finish(packagedThrough: through);
    }

    /// <summary>
    /// Writes the server's package for a till: the rows it receives that changed after <see cref="DownloadOptions.Cursor"/>,
    /// up to the newest change number every running transaction is past. Server (SQL Server) only.
    /// </summary>
    public async Task<PackageManifest> WriteDownloadAsync(RetailPlusDbContext db, Stream output, DownloadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        if (!db.Database.IsSqlServer()) throw new InvalidOperationException("Packages for tills are written by the server, on SQL Server.");
        var model = SyncModel.For(db.Model, Policy);
        // Change numbers below MIN_ACTIVE_ROWVERSION belong to committed rows only, and no transaction still running can
        // commit one below it; so nothing below this mark can turn up later behind the till's cursor.
        long high = await db.Database.SqlQueryRaw<long>("SELECT CAST(MIN_ACTIVE_ROWVERSION() AS bigint) - 1 AS [Value]").SingleAsync(cancellationToken);
        long cursor = options.Cursor > high ? 0 : Math.Max(0, options.Cursor);   // a cursor from another database starts over
        var store = options.Till.StoreId;
        var created = SyncColumns.Now();
        using var parts = new PackageParts(_ => output, false, options.Key, int.MaxValue, () => new PackageManifest
        {
            Kind = SyncDirection.Download, StoreId = store, StationNumber = options.Till.StationNumber, AppVersion = options.AppVersion,
            CreatedAtUtc = created, Cursor = cursor, CursorTo = high,
        });
        var down = model.Tables.Where(t => t.Flow.HasFlag(SyncFlow.Down)).ToList();
        var names = down.Select(t => t.Name).ToList();
        var tombstones = db.Tombstones.AsNoTracking()
            .Where(t => EF.Property<long>(t, SyncColumns.ChangeNumber) > cursor && EF.Property<long>(t, SyncColumns.ChangeNumber) <= high)
            .Where(t => (t.StoreId == null || t.StoreId == store) && names.Contains(t.EntityName))
            .OrderBy(t => t.DeletedAtUtc).AsAsyncEnumerable();
        await foreach (var tombstone in tombstones.WithCancellation(cancellationToken)) parts.AddTombstone(tombstone);
        foreach (var table in down)
        {
            var filter = And(Between(table.ClrType, cursor, high), model.DownloadFilter(table, db, options.Till));
            await foreach (var row in StreamRowsAsync(db, table, filter, cancellationToken)) parts.AddRow(table, row, mayBreak: false);
        }
        return parts.Finish(packagedThrough: null)[0];
    }

    /// <summary>The rows of a table that pass the filter, each as its column values in the table's column order, as they are read.</summary>
    internal static async IAsyncEnumerable<object?[]> StreamRowsAsync(RetailPlusDbContext db, SyncTable table, LambdaExpression? filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = SyncModel.Rows(db, table.ClrType);
        if (filter is not null) rows = SyncModel.Where(rows, filter);
        var e = Expression.Parameter(table.ClrType, "e");
        var values = Expression.NewArrayInit(typeof(object),
            table.Columns.Select(c => Expression.Convert(SyncModel.Column(e, c.Name, c.ClrType), typeof(object))));
        var select = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [table.ClrType, typeof(object[])],
            rows.Expression, Expression.Quote(Expression.Lambda(values, e)));
        await foreach (var row in rows.Provider.CreateQuery<object?[]>(select).AsAsyncEnumerable().WithCancellation(cancellationToken))
            yield return row;
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

/// <summary>
/// Rows written into one package, or into a series of packages of at most <c>maxRows</c> rows each, as they are read.
/// Each package of a series has its own batch id and names the one before it, so the receiver applies them in order;
/// only the last one says how far the changes are packaged.
/// </summary>
internal sealed class PackageParts(Func<int, Stream> open, bool ownsStreams, byte[] key, int maxRows, Func<PackageManifest> newManifest) : IDisposable
{
    private readonly List<PackageManifest> _done = [];
    private Stream? _stream;
    private PackageBuilder? _builder;
    private RowsWriter? _rows;
    private int _rowsInPart;

    /// <summary>True while no row has been written.</summary>
    public bool IsEmpty => _done.Count == 0 && _builder is null;

    public void AddRow(SyncTable table, object?[] row, bool mayBreak) =>
        Add(table.Name, $"tables/{table.Name}.json", w => RowJson.WriteRow(w, table.Columns, row), mayBreak);

    public void AddTombstone(Tombstone t) =>
        Add(nameof(Tombstone), SyncPackage.TombstonesFile,
            w => JsonSerializer.Serialize(w, new TombstoneRow(t.EntityName, t.RowId, t.DeletedAtUtc, t.StoreId, t.MergedIntoId), SyncPackage.Json), mayBreak: true);

    /// <summary>The rows that follow start a new package, unless the current one is still empty.</summary>
    public void Break()
    {
        if (_builder is not null && _rowsInPart > 0) FinishPart(last: false);
    }

    public async Task AddDatabaseAsync(string path, CancellationToken cancellationToken)
    {
        _builder ??= Start();
        CloseRows();
        _builder.Manifest.Database = await _builder.AddFileAsync("Database", SyncPackage.DatabaseFile, path, cancellationToken);
    }

    /// <summary>Finishes the last package (an empty one when nothing was written) and returns every manifest, in order.</summary>
    public IReadOnlyList<PackageManifest> Finish(DateTime? packagedThrough)
    {
        _builder ??= Start();
        _builder.Manifest.PackagedThroughUtc = packagedThrough;
        FinishPart(last: true);
        return _done;
    }

    private void Add(string entity, string file, Action<Utf8JsonWriter> write, bool mayBreak)
    {
        if (_builder is not null && mayBreak && _rowsInPart >= maxRows) FinishPart(last: false);
        _builder ??= Start();
        if (_rows is { } open && open.Entity != entity) CloseRows();
        _rows ??= _builder.BeginRows(entity, file);
        _rows.Write(write);
        _rowsInPart++;
    }

    private PackageBuilder Start()
    {
        var manifest = newManifest();
        manifest.BatchId = Guid.CreateVersion7();
        manifest.Part = _done.Count + 1;
        manifest.PreviousBatchId = _done.Count == 0 ? null : _done[^1].BatchId;
        _stream = open(manifest.Part);
        _rowsInPart = 0;
        return new PackageBuilder(_stream, manifest);
    }

    private void CloseRows()
    {
        if (_rows is null) return;
        var entry = _rows.Finish();
        _rows = null;
        if (entry.Entity == nameof(Tombstone)) _builder!.Manifest.Tombstones = entry;
        else _builder!.Manifest.Tables.Add(entry);
    }

    private void FinishPart(bool last)
    {
        CloseRows();
        var builder = _builder!;
        builder.Manifest.LastPart = last;
        if (!last) builder.Manifest.PackagedThroughUtc = null;
        builder.Finish(key);
        if (ownsStreams) _stream!.Dispose();
        _done.Add(builder.Manifest);
        _builder = null;
        _stream = null;
    }

    public void Dispose()
    {
        _rows?.Dispose();
        _builder?.Dispose();
        if (ownsStreams) _stream?.Dispose();
    }
}
