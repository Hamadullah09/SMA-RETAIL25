using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using RetailPlus.Core.Model;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Sync;

/// <summary>Which database applies a package.</summary>
public enum SyncSide
{
    /// <summary>The server, applying a till's upload. Where the same code was made twice, the server's row stays.</summary>
    Server,
    /// <summary>A till, applying the server's download. The server's rows win.</summary>
    Till,
}

/// <summary>How a package is applied.</summary>
public sealed class ApplyOptions
{
    public required SyncSide Side { get; init; }
    /// <summary>The till the package comes from (server) or is for (till); the manifest must name it.</summary>
    public required TillScope Till { get; init; }
    public string? FileName { get; init; }
    public string? FileSha256 { get; init; }
    /// <summary>Runs inside the package's transaction before any row is applied (a till joining a server re-points its rows).</summary>
    public Func<RetailPlusDbContext, Task>? BeforeApply { get; init; }
    /// <summary>Runs inside the package's transaction after the rows (a till moves its download cursor).</summary>
    public Func<RetailPlusDbContext, PackageManifest, Task>? AfterApply { get; init; }
}

/// <summary>What became of a package.</summary>
public sealed record ApplyResult(SyncBatchStatus Status, Guid BatchId, int Inserted, int Updated, int Deleted, int Unchanged, IReadOnlyList<string> Notes, string? Error)
{
    public int Changed => Inserted + Updated + Deleted;
}

/// <summary>
/// Applies a package in one transaction, or not at all: checks it (signature, checksums, row counts, the station it
/// names, not applied before), deletes what its tombstones name, then inserts its new rows and updates rows whose copy
/// in the package is newer (the later edit wins). Columns the system keeps are never taken for a row already here; the
/// derived ones are worked out again afterwards. Every package is recorded in SyncBatches, also refused ones.
/// </summary>
public sealed partial class PackageApplier(SyncPolicy? policy = null)
{
    public SyncPolicy Policy { get; } = policy ?? SyncPolicy.Default;

    public async Task<ApplyResult> ApplyAsync(DataContext data, PackageReader package, byte[] key, ApplyOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);
        var manifest = package.Manifest;
        SyncModel model;
        await using (var probe = data.Create()) model = SyncModel.For(probe.Model, Policy);
        try
        {
            package.Verify(key);
            Check(manifest, options, model);
        }
        catch (PackageRejectedException ex)
        {
            return await RecordFailureAsync(data, manifest, options, ex.Message, cancellationToken);
        }

        await using var db = data.Create();
        db.ApplyingSync = true;
        if (await db.SyncBatches.AnyAsync(b => b.BatchId == manifest.BatchId && b.Status == SyncBatchStatus.Applied, cancellationToken))
        {
            db.ApplyingSync = false;
            db.SyncBatches.Add(Batch(manifest, options, SyncBatchStatus.Duplicate, null, "This package was applied before; nothing was done."));
            await db.SaveChangesAsync(cancellationToken);
            return new ApplyResult(SyncBatchStatus.Duplicate, manifest.BatchId, 0, 0, 0, 0, [], null);
        }

        var run = new Run(db, model, options, Policy.NewDerivedRules(), cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // On a till, references are checked at the commit, so a row merged into the server's can be replaced in any order.
            if (db.Database.IsSqlite()) await db.Database.ExecuteSqlRawAsync("PRAGMA defer_foreign_keys = ON;", cancellationToken);
            if (options.BeforeApply is { } before) await before(db);
            await ApplyTombstonesAsync(run, package.ReadTombstones());
            foreach (var table in model.Tables)
            {
                if (manifest.Tables.FirstOrDefault(t => t.Entity == table.Name) is not { } entry) continue;
                if (!table.Flow.HasFlag(run.Direction))
                {
                    run.Notes.Add($"{entry.Rows} {table.Name} rows were not taken: they do not travel this way.");
                    continue;
                }
                using var rows = package.ReadTable(entry);
                await (Task)ApplyTableMethod.MakeGenericMethod(table.ClrType).Invoke(this, [run, table, rows.RootElement])!;
            }
            if (options.Side == SyncSide.Server) await CheckStoresAsync(run, manifest);
            foreach (var rule in run.Derived) await rule.ApplyAsync(db, cancellationToken);
            if (options.AfterApply is { } after) await after(db, manifest);
            db.SyncBatches.Add(Batch(manifest, options, SyncBatchStatus.Applied, null, run.Notes.Count == 0 ? null : string.Join(Environment.NewLine, run.Notes)));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is PackageRejectedException or DbUpdateException or DbException or InvalidOperationException or JsonException
                                       or InvalidCastException or FormatException or TargetInvocationException)
        {
            db.ChangeTracker.Clear();
            return await RecordFailureAsync(data, manifest, options, Explain(ex), cancellationToken);
        }
        return new ApplyResult(SyncBatchStatus.Applied, manifest.BatchId, run.Inserted, run.Updated, run.Deleted, run.Unchanged, run.Notes, null);
    }

    private static void Check(PackageManifest manifest, ApplyOptions options, SyncModel model)
    {
        var kind = options.Side == SyncSide.Server ? SyncDirection.Upload : SyncDirection.Download;
        if (manifest.Kind != kind)
            throw new PackageRejectedException(kind == SyncDirection.Upload ? "This is a package for a till, not one from a till." : "This is a package from a till, not one for a till.");
        if (manifest.StoreId != options.Till.StoreId || manifest.StationNumber != options.Till.StationNumber)
            throw new PackageRejectedException($"The package is for station {manifest.StationNumber} of another store or station, not station {options.Till.StationNumber}.");
        if (manifest.Tables.FirstOrDefault(t => model.Find(t.Entity) is null) is { } unknown)
            throw new PackageRejectedException($"The package holds {unknown.Entity} rows, which this program does not know. Update the program.");
    }

    private static SyncBatch Batch(PackageManifest manifest, ApplyOptions options, SyncBatchStatus status, string? error, string? notes) => new()
    {
        BatchId = manifest.BatchId, StoreId = manifest.StoreId == Guid.Empty ? null : manifest.StoreId, StationNumber = manifest.StationNumber,
        Direction = manifest.Kind, FileName = options.FileName, FileSha256 = options.FileSha256, Rows = manifest.Rows, AppVersion = manifest.AppVersion,
        ReceivedAtUtc = SyncColumns.Now(), AppliedAtUtc = status == SyncBatchStatus.Applied ? SyncColumns.Now() : null, Status = status,
        Error = error, Notes = notes, UpdatedAtUtc = SyncColumns.Now(),
    };

    private static async Task<ApplyResult> RecordFailureAsync(DataContext data, PackageManifest manifest, ApplyOptions options, string error, CancellationToken cancellationToken)
    {
        await using var db = data.Create();
        db.SyncBatches.Add(Batch(manifest, options, SyncBatchStatus.Failed, error, null));
        await db.SaveChangesAsync(cancellationToken);
        return new ApplyResult(SyncBatchStatus.Failed, manifest.BatchId, 0, 0, 0, 0, [], error);
    }

    private static string Explain(Exception ex)
    {
        if (ex is TargetInvocationException { InnerException: { } inner }) return Explain(inner);
        if (ex is PackageRejectedException) return ex.Message;
        var root = ex;
        while (root.InnerException is not null) root = root.InnerException;
        return ex is DbUpdateException ? "The rows could not be saved: " + root.Message : root.Message;
    }

    /// <summary>The state of one package being applied.</summary>
    private sealed class Run(RetailPlusDbContext db, SyncModel model, ApplyOptions options, IReadOnlyList<IDerivedRule> derived, CancellationToken cancellationToken)
    {
        public RetailPlusDbContext Db { get; } = db;
        public SyncModel Model { get; } = model;
        public ApplyOptions Options { get; } = options;
        public SyncSide Side => Options.Side;
        public SyncFlow Direction => Side == SyncSide.Server ? SyncFlow.Up : SyncFlow.Down;
        public IReadOnlyList<IDerivedRule> Derived { get; } = derived;
        public CancellationToken Token { get; } = cancellationToken;
        public List<string> Notes { get; } = [];
        public int Inserted, Updated, Deleted, Unchanged;
        /// <summary>Per table: row ids merged into another row's id.</summary>
        public Dictionary<string, Dictionary<Guid, Guid>> Merged { get; } = new(StringComparer.Ordinal);
        /// <summary>Per table: the rows this package inserted or changed.</summary>
        public Dictionary<string, HashSet<Guid>> Touched { get; } = new(StringComparer.Ordinal);

        public void Touch(SyncTable table, Guid id) => (Touched.TryGetValue(table.Name, out var set) ? set : Touched[table.Name] = []).Add(id);

        public void Map(SyncTable table, Guid from, Guid to) =>
            (Merged.TryGetValue(table.Name, out var map) ? map : Merged[table.Name] = [])[from] = to;

        public Guid Resolve(string table, Guid id) => Merged.TryGetValue(table, out var map) && map.TryGetValue(id, out var to) ? to : id;
    }

    // Tombstones --------------------------------------------------------------------------------------------------------

    private static async Task ApplyTombstonesAsync(Run run, IReadOnlyList<TombstoneRow> tombstones)
    {
        var order = run.Model.Tables.Select((t, i) => (t.Name, i)).ToDictionary(x => x.Name, x => x.i, StringComparer.Ordinal);
        // The server's merges first: rows here that point at a merged row point at the row it was merged into.
        foreach (var t in tombstones.Where(t => t.MergedIntoId is not null))
        {
            if (run.Side == SyncSide.Server || run.Model.Find(t.EntityName) is not { } table || !table.Flow.HasFlag(run.Direction)) continue;
            run.Map(table, t.RowId, t.MergedIntoId!.Value);
            if (await RepointAsync(run.Db, table.EntityType, t.RowId, t.MergedIntoId.Value, run.Token) | await DeleteRowAsync(run.Db, table, t.RowId, run.Token) > 0)
                run.Notes.Add($"{table.Name} {t.RowId} was merged into {t.MergedIntoId} by the server.");
        }
        // Then the deletions, children before parents.
        foreach (var t in tombstones.Where(t => t.MergedIntoId is null).OrderByDescending(t => order.GetValueOrDefault(t.EntityName, -1)))
        {
            if (run.Model.Find(t.EntityName) is not { } table || !table.Flow.HasFlag(run.Direction)) continue;
            var id = run.Resolve(table.Name, t.RowId);
            // A till deletes only rows of its own store.
            if (run.Side == SyncSide.Server && run.Model.StoreFilter(table, run.Db, run.Options.Till) is { } filter
                && await (Task<int>)CountOutsideMethod.MakeGenericMethod(table.ClrType).Invoke(null, [run, table, filter, new[] { id }])! > 0)
            {
                run.Notes.Add($"{table.Name} {id} belongs to another store; the station cannot delete it.");
                continue;
            }
            await ReleaseReferencesAsync(run.Db, table, id, run.Token);
            if (await DeleteRowAsync(run.Db, table, id, run.Token) == 0) continue;
            run.Deleted++;
            // The server passes the deletion on to the other tills.
            if (run.Side == SyncSide.Server)
                run.Db.Tombstones.Add(new Tombstone
                {
                    EntityName = t.EntityName, RowId = id, DeletedAtUtc = t.DeletedAtUtc, StoreId = t.StoreId, UpdatedAtUtc = SyncColumns.Now(),
                });
        }
        await run.Db.SaveChangesAsync(run.Token);
    }

    /// <summary>
    /// Before a row is deleted: optional references to it are cleared (as the app does); a required reference that the
    /// database would not remove with it refuses the package.
    /// </summary>
    private static async Task ReleaseReferencesAsync(RetailPlusDbContext db, SyncTable table, Guid id, CancellationToken cancellationToken)
    {
        foreach (var fk in table.EntityType.GetReferencingForeignKeys().Where(fk => fk.Properties.Count == 1))
        {
            var dependent = fk.DeclaringEntityType;
            string rows = Sql.Table(db, dependent), column = Sql.Name(db, Sql.Column(dependent, fk.Properties[0]));
            if (fk.DeleteBehavior is DeleteBehavior.Cascade) continue;
            if (fk.DeleteBehavior is DeleteBehavior.ClientSetNull or DeleteBehavior.SetNull && fk.Properties[0].IsNullable)
            {
                string clear = "UPDATE " + rows + " SET " + column + " = NULL WHERE " + column + " = {0}";
                await db.Database.ExecuteSqlRawAsync(clear, [id], cancellationToken);
                continue;
            }
            string count = "SELECT COUNT(*) AS " + Sql.Name(db, "Value") + " FROM " + rows + " WHERE " + column + " = {0}";
            var used = await db.Database.SqlQueryRaw<int>(count, id).SingleAsync(cancellationToken);
            if (used > 0)
                throw new PackageRejectedException($"{table.Name} {id} was deleted elsewhere, but {used} {dependent.ClrType.Name} rows here still use it.");
        }
    }

    private static Task<int> DeleteRowAsync(RetailPlusDbContext db, SyncTable table, Guid id, CancellationToken cancellationToken)
    {
        string delete = "DELETE FROM " + Sql.Table(db, table.EntityType) + " WHERE " + Sql.Name(db, Sql.Column(table.EntityType, table.Key)) + " = {0}";
        return db.Database.ExecuteSqlRawAsync(delete, [id], cancellationToken);
    }

    /// <summary>
    /// Moves every reference to row <paramref name="from"/> over to row <paramref name="to"/> (rows of any table, also the
    /// local ones). Returns true when anything pointed at it.
    /// </summary>
    internal static async Task<bool> RepointAsync(RetailPlusDbContext db, IEntityType type, Guid from, Guid to, CancellationToken cancellationToken)
    {
        int moved = 0;
        foreach (var fk in type.GetReferencingForeignKeys().Where(fk => fk.Properties.Count == 1))
        {
            var dependent = fk.DeclaringEntityType;
            string table = Sql.Table(db, dependent), column = Sql.Name(db, Sql.Column(dependent, fk.Properties[0]));
            // A one-to-one row (an item's picture) that both rows have: the one of the row merged away goes.
            if (fk.Properties[0].IsPrimaryKey())
            {
                string drop = "DELETE FROM " + table + " WHERE " + column + " = {0} AND EXISTS (SELECT 1 FROM " + table + " WHERE " + column + " = {1})";
                await db.Database.ExecuteSqlRawAsync(drop, [from, to], cancellationToken);
            }
            string move = "UPDATE " + table + " SET " + column + " = {0} WHERE " + column + " = {1}";
            moved += await db.Database.ExecuteSqlRawAsync(move, [to, from], cancellationToken);
        }
        return moved > 0;
    }

    // Rows --------------------------------------------------------------------------------------------------------------

    private static readonly MethodInfo ApplyTableMethod = typeof(PackageApplier).GetMethod(nameof(ApplyTableAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private async Task ApplyTableAsync<T>(Run run, SyncTable table, JsonElement json) where T : class
    {
        var db = run.Db;
        string key = table.Key.Name;
        var rows = json.EnumerateArray().Select(r => RowJson.Read(r, table.Columns)).Where(r => r.ContainsKey(key)).ToList();
        if (rows.Count == 0) return;
        if (run.Side == SyncSide.Server) await LoadMergesAsync(run, table, rows);
        foreach (var row in rows)
        {
            row[key] = run.Resolve(table.Name, (Guid)row[key]!);
            foreach (var fk in table.References)
            {
                var column = fk.Properties[0].Name;
                if (row.GetValueOrDefault(column) is Guid target) row[column] = run.Resolve(fk.PrincipalEntityType.ClrType.Name, target);
            }
        }

        var existing = await LoadAsync<T>(db, table, rows.Select(r => (Guid)r[key]!).Distinct().ToList(), run.Token);
        var rule = run.Side == SyncSide.Server ? Policy.UploadRuleOf(table.ClrType) : null;
        var inserts = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            if (!existing.TryGetValue((Guid)row[key]!, out var entity))
            {
                inserts.Add(row);
                continue;
            }
            var entry = db.Entry(entity);
            if (!IsNewer(row, entry))
            {
                run.Unchanged++;
                continue;
            }
            if (rule is not null && !await rule(new UploadCheck { Db = db, Till = run.Options.Till, Values = row, Existing = entity, Notes = run.Notes }))
                continue;
            var before = entry.CurrentValues.Clone();
            foreach (var column in table.Columns)
            {
                if (column.IsPrimaryKey() || SyncColumns.IsKept(table.ClrType, column.Name) || !row.TryGetValue(column.Name, out var value)) continue;
                var property = entry.Property(column.Name);
                if (!Equals(property.CurrentValue, value)) property.CurrentValue = value;
            }
            run.Updated++;
            run.Touch(table, (Guid)row[key]!);
            foreach (var derived in run.Derived) derived.Observe(table, row, false, before);
        }

        foreach (var row in await ResolveClashesAsync<T>(run, table, await WithoutDeletedAsync(run, table, inserts)))
        {
            if (rule is not null && !await rule(new UploadCheck { Db = db, Till = run.Options.Till, Values = row, Existing = null, Notes = run.Notes }))
                continue;
            var entity = Activator.CreateInstance<T>();
            foreach (var column in table.Columns.Where(c => c.PropertyInfo is not null))
                if (row.TryGetValue(column.Name, out var value) && (value is not null || column.IsNullable))
                    column.PropertyInfo!.SetValue(entity, value);
            var entry = db.Add(entity);
            foreach (var column in table.Columns.Where(c => c.IsShadowProperty()))
                if (row.TryGetValue(column.Name, out var value) && value is not null) entry.Property(column.Name).CurrentValue = value;
            run.Inserted++;
            run.Touch(table, (Guid)row[key]!);
            foreach (var derived in run.Derived) derived.Observe(table, row, true, null);
        }
        await db.SaveChangesAsync(run.Token);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Rows deleted here are not brought back by a copy that is older than the deletion: a till may send again a row it
    /// received (its packages overlap a little) after the server deleted it. A copy edited after the deletion wins.
    /// </summary>
    private static async Task<List<Dictionary<string, object?>>> WithoutDeletedAsync(Run run, SyncTable table, List<Dictionary<string, object?>> inserts)
    {
        if (inserts.Count == 0) return inserts;
        string key = table.Key.Name;
        var deleted = new Dictionary<Guid, DateTime>();
        foreach (var chunk in inserts.Select(r => (Guid)r[key]!).Chunk(500))
        {
            var ids = chunk.ToList();
            foreach (var t in await run.Db.Tombstones.AsNoTracking().Where(t => t.EntityName == table.Name && t.MergedIntoId == null && ids.Contains(t.RowId))
                         .Select(t => new { t.RowId, t.DeletedAtUtc }).ToListAsync(run.Token))
                deleted[t.RowId] = deleted.TryGetValue(t.RowId, out var other) && other > t.DeletedAtUtc ? other : t.DeletedAtUtc;
        }
        if (deleted.Count == 0) return inserts;
        return inserts.Where(row =>
        {
            if (!deleted.TryGetValue((Guid)row[key]!, out var at)) return true;
            var edited = row.GetValueOrDefault(SyncColumns.EditedAtUtc) as DateTime? ?? row.GetValueOrDefault(nameof(ITracked.UpdatedAtUtc)) as DateTime? ?? DateTime.MinValue;
            if (edited > at) return true;
            run.Unchanged++;
            return false;
        }).ToList();
    }

    /// <summary>The later edit wins; between equal edits, the later change (a count the system keeps changes nothing here).</summary>
    private static bool IsNewer(Dictionary<string, object?> row, EntityEntry entry)
    {
        var updated = row.GetValueOrDefault(nameof(ITracked.UpdatedAtUtc)) as DateTime? ?? DateTime.MinValue;
        var edited = row.GetValueOrDefault(SyncColumns.EditedAtUtc) as DateTime? ?? updated;
        var localUpdated = (DateTime)entry.Property(nameof(ITracked.UpdatedAtUtc)).CurrentValue!;
        var localEdited = entry.Metadata.FindProperty(SyncColumns.EditedAtUtc) is null ? localUpdated : (DateTime)entry.Property(SyncColumns.EditedAtUtc).CurrentValue!;
        return edited > localEdited || (edited == localEdited && updated > localUpdated);
    }

    private static async Task<Dictionary<Guid, T>> LoadAsync<T>(RetailPlusDbContext db, SyncTable table, List<Guid> ids, CancellationToken cancellationToken) where T : class
    {
        var found = new Dictionary<Guid, T>();
        var getter = table.Key.GetGetter();
        foreach (var chunk in ids.Chunk(500))
        {
            var e = Expression.Parameter(typeof(T), "e");
            var predicate = Expression.Lambda<Func<T, bool>>(
                Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)], SyncModel.Value(chunk), SyncModel.Column<Guid>(e, table.Key.Name)), e);
            foreach (var row in await db.Set<T>().Where(predicate).ToListAsync(cancellationToken))
                found[(Guid)getter.GetClrValue(row)!] = row;
        }
        return found;
    }

    /// <summary>
    /// Merges recorded by earlier packages: a till that has not yet heard of a merge still sends the id merged away, and
    /// its rows still point at it.
    /// </summary>
    private static async Task LoadMergesAsync(Run run, SyncTable table, List<Dictionary<string, object?>> rows)
    {
        var wanted = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal) { [table.Name] = [.. rows.Select(r => (Guid)r[table.Key.Name]!)] };
        foreach (var fk in table.References)
        {
            var name = fk.PrincipalEntityType.ClrType.Name;
            if (!wanted.TryGetValue(name, out var set)) wanted[name] = set = [];
            foreach (var row in rows)
                if (row.GetValueOrDefault(fk.Properties[0].Name) is Guid id) set.Add(id);
        }
        foreach (var (name, ids) in wanted)
            foreach (var chunk in ids.Chunk(500))
            {
                var list = chunk.ToList();
                var merges = await run.Db.Tombstones.AsNoTracking().Where(t => t.EntityName == name && t.MergedIntoId != null && list.Contains(t.RowId))
                    .Select(t => new { t.RowId, To = t.MergedIntoId!.Value }).ToListAsync(run.Token);
                foreach (var m in merges) (run.Merged.TryGetValue(name, out var map) ? map : run.Merged[name] = [])[m.RowId] = m.To;
            }
    }

    /// <summary>
    /// New rows whose unique values (a stock code, a name) are already taken by another row here. Where the policy allows,
    /// the rows are merged: on the server the server's row stays and the new one becomes it; on a till the server's row
    /// replaces the till's. Otherwise the package is refused, naming the rows. Returns the rows still to insert.
    /// </summary>
    private async Task<List<Dictionary<string, object?>>> ResolveClashesAsync<T>(Run run, SyncTable table, List<Dictionary<string, object?>> inserts) where T : class
    {
        if (inserts.Count == 0) return inserts;
        string key = table.Key.Name;
        var keyGetter = table.Key.GetGetter();
        foreach (var index in table.UniqueIndexes.Where(i => !i.Properties.Any(p => p.IsPrimaryKey())))
        {
            var columns = index.Properties;
            var filter = index.GetFilter();
            object? Incoming(Dictionary<string, object?> row, string column) => row.GetValueOrDefault(Property(table, column)?.Name ?? column);
            var candidates = inserts.Where(r => InFilter(filter, c => Incoming(r, c)) == true && r.GetValueOrDefault(columns[0].Name) is not null).ToList();
            if (candidates.Count == 0) continue;
            var others = new List<T>();
            foreach (var chunk in candidates.Select(r => r[columns[0].Name]!).Distinct().Chunk(500))
                others.AddRange(await FindAsync<T>(run.Db, columns[0], chunk, run.Token));
            foreach (var row in candidates)
            {
                var clash = others.FirstOrDefault(o => (Guid)keyGetter.GetClrValue(o)! != (Guid)row[key]!
                    && columns.All(c => Same(row.GetValueOrDefault(c.Name), c.GetGetter().GetClrValue(o)))
                    && InFilter(filter, c => Property(table, c)?.GetGetter().GetClrValue(o)) == true);
                if (clash is null) continue;
                var names = columns.Select(c => c.Name).ToList();
                Guid incomingId = (Guid)row[key]!, existingId = (Guid)keyGetter.GetClrValue(clash)!;
                string what = $"{table.Name} with {string.Join(", ", columns.Select(c => $"{c.Name} {row.GetValueOrDefault(c.Name)}"))}";
                bool serverOwned = table.Flow == SyncFlow.Down;
                if (!(Policy.MayMerge(table.ClrType, names, row, clash, run.Options.Till) || (run.Side == SyncSide.Till && serverOwned)))
                    throw new PackageRejectedException($"{what}: the package's row ({incomingId}) and the row already here ({existingId}) have the same {string.Join(" and ", names)}. Change one of them and send again.");
                if (run.Side == SyncSide.Server)
                {
                    // The server's row stays; the till's becomes it, now and in its later packages, and the till is told.
                    run.Map(table, incomingId, existingId);
                    inserts.Remove(row);
                    run.Db.Tombstones.Add(new Tombstone
                    {
                        EntityName = table.Name, RowId = incomingId, MergedIntoId = existingId, DeletedAtUtc = SyncColumns.Now(), UpdatedAtUtc = SyncColumns.Now(),
                        StoreId = row.GetValueOrDefault(nameof(IStoreScoped.StoreId)) as Guid?,
                    });
                    run.Notes.Add($"{what} was made here too; the station's row was merged into the server's.");
                }
                else
                {
                    // The server's row replaces this till's: what pointed at the till's row points at the server's.
                    await RepointAsync(run.Db, table.EntityType, existingId, incomingId, run.Token);
                    await DeleteRowAsync(run.Db, table, existingId, run.Token);
                    others.Remove(clash);
                    run.Notes.Add($"{what} was also made on the server; this till's row was merged into the server's.");
                }
            }
        }
        return inserts;
    }

    private static async Task<List<T>> FindAsync<T>(RetailPlusDbContext db, IProperty column, object[] values, CancellationToken cancellationToken) where T : class
    {
        var typed = Array.CreateInstance(column.ClrType, values.Length);
        for (int i = 0; i < values.Length; i++) typed.SetValue(values[i], i);
        var box = Activator.CreateInstance(typeof(SyncModel.Box<>).MakeGenericType(typed.GetType()), typed)!;
        var e = Expression.Parameter(typeof(T), "e");
        var contains = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [column.ClrType],
            Expression.Property(Expression.Constant(box), "Value"), SyncModel.Column(e, column.Name, column.ClrType));
        return await db.Set<T>().AsNoTracking().Where(Expression.Lambda<Func<T, bool>>(contains, e)).ToListAsync(cancellationToken);
    }

    private static IProperty? Property(SyncTable table, string column) =>
        table.EntityType.GetProperties().FirstOrDefault(p => string.Equals(p.GetColumnName(), column, StringComparison.OrdinalIgnoreCase));

    /// <summary>Equal as the databases compare them: text without regard to case.</summary>
    private static bool Same(object? a, object? b) =>
        a is string x && b is string y ? string.Equals(x, y, StringComparison.OrdinalIgnoreCase) : Equals(a, b);

    /// <summary>
    /// Whether a row falls under a filtered index ("[IsDeleted] = 0", "[Number] IS NOT NULL AND ..."); null when the
    /// filter is not understood, in which case the database itself decides.
    /// </summary>
    internal static bool? InFilter(string? filter, Func<string, object?> value)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        foreach (var part in AndSplit().Split(filter))
        {
            var clause = part.Trim().TrimStart('(').TrimEnd(')').Trim();
            if (NullClause().Match(clause) is { Success: true } isNull)
            {
                bool none = value(isNull.Groups[1].Value) is null;
                if (isNull.Groups[2].Success == none) return false;
                continue;
            }
            if (CompareClause().Match(clause) is not { Success: true } compare) return null;
            var actual = value(compare.Groups[1].Value);
            var literal = compare.Groups[3].Value;
            bool equal = literal.EndsWith('\'')
                ? string.Equals(Convert.ToString(actual, CultureInfo.InvariantCulture), literal[(literal.IndexOf('\'') + 1)..^1].Replace("''", "'", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase)
                : actual is not null && decimal.Parse(literal, CultureInfo.InvariantCulture) == (actual is bool b ? (b ? 1 : 0) : Convert.ToDecimal(actual, CultureInfo.InvariantCulture));
            if (equal != (compare.Groups[2].Value == "=")) return false;
        }
        return true;
    }

    [GeneratedRegex(@"\s+AND\s+", RegexOptions.IgnoreCase)]
    private static partial Regex AndSplit();

    [GeneratedRegex(@"^\[(\w+)\]\s+IS\s+(NOT\s+)?NULL$", RegexOptions.IgnoreCase)]
    private static partial Regex NullClause();

    [GeneratedRegex(@"^\[(\w+)\]\s*(=|<>|!=)\s*(N?'(?:[^']|'')*'|-?\d+(?:\.\d+)?)$")]
    private static partial Regex CompareClause();

    // Stores ------------------------------------------------------------------------------------------------------------

    /// <summary>A till may only send rows of its own store; one row of another store refuses the package.</summary>
    private static async Task CheckStoresAsync(Run run, PackageManifest manifest)
    {
        foreach (var entry in manifest.Tables)
        {
            if (run.Model.Find(entry.Entity) is not { } table || !table.Flow.HasFlag(SyncFlow.Up)) continue;
            if (run.Model.StoreFilter(table, run.Db, run.Options.Till) is not { } filter || !run.Touched.TryGetValue(table.Name, out var ids)) continue;
            var count = await (Task<int>)CountOutsideMethod.MakeGenericMethod(table.ClrType).Invoke(null, [run, table, filter, ids.ToArray()])!;
            if (count > 0)
                throw new PackageRejectedException($"The package holds {count} {table.Name} rows of another store; a till may only send its own store's rows.");
        }
    }

    private static readonly MethodInfo CountOutsideMethod = typeof(PackageApplier).GetMethod(nameof(CountOutsideAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>How many of these rows exist here outside the till's store.</summary>
    private static async Task<int> CountOutsideAsync<T>(Run run, SyncTable table, LambdaExpression filter, Guid[] ids) where T : class
    {
        int outside = 0;
        foreach (var chunk in ids.Chunk(500))
        {
            var e = Expression.Parameter(typeof(T), "e");
            var mine = Expression.Lambda<Func<T, bool>>(
                Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)], SyncModel.Value(chunk), SyncModel.Column<Guid>(e, table.Key.Name)), e);
            var rows = run.Db.Set<T>().Where(mine);
            int all = await rows.CountAsync(run.Token);
            int inStore = await ((IQueryable<T>)SyncModel.Where(rows, filter)).CountAsync(run.Token);
            outside += all - inStore;
        }
        return outside;
    }

    /// <summary>Quoting table and column names for the few statements written by hand.</summary>
    private static class Sql
    {
        public static string Table(RetailPlusDbContext db, IEntityType type) =>
            db.GetService<ISqlGenerationHelper>().DelimitIdentifier(type.GetTableName()!, type.GetSchema());

        public static string Column(IEntityType type, IProperty property) =>
            property.GetColumnName(StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema()))!;

        public static string Name(RetailPlusDbContext db, string name) => db.GetService<ISqlGenerationHelper>().DelimitIdentifier(name);
    }
}
