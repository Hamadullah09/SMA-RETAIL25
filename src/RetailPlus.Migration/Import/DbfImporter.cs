using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Data;
using RetailPlus.Data.Import;
using RetailPlus.Migration.Dbf;

namespace RetailPlus.Migration.Import;

/// <summary>What to import and where to.</summary>
public sealed class ImportOptions
{
    /// <summary>The Retail Plus data root (the folder holding SETUP.DBF and one folder per location).</summary>
    public required string SourceFolder { get; init; }
    public required string ConnectionString { get; init; }
    /// <summary>Empty a database that already holds data instead of refusing to import into it.</summary>
    public bool Overwrite { get; init; }
    /// <summary>Optional Plus50.INI of a workstation, imported as that station's settings.</summary>
    public string? StationIniPath { get; init; }
    /// <summary>
    /// Year the monthly and weekly sales columns of the inventory belong to (LASTYR is the year before). Defaults to
    /// the year the inventory table was last written.
    /// </summary>
    public int? SalesYear { get; init; }
    public Action<string>? Progress { get; init; }
}

/// <summary>The import was not started, for example because the database already holds data.</summary>
public sealed class ImportRefusedException : Exception
{
    public ImportRefusedException() { }
    public ImportRefusedException(string message) : base(message) { }
    public ImportRefusedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// One-time import of a Retail Plus 5.2 data folder into the SQL Server database (docs/DATA_LAYER_PLAN.md). Reads
/// copies of the DBF/FPT files (never writes them), maps every known table per docs/DATA_FORMATS.md, keeps each
/// source record as JSON in <see cref="ImportSourceRow"/>, and checks control totals afterwards. All rows are written
/// in one transaction, so a failed import leaves the database as it was.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The connection only lives during RunAsync, which disposes it.")]
public sealed partial class DbfImporter(ImportOptions options)
{
    private const int ChunkSize = 500;

    private readonly ImportLog _log = new(options.Progress);
    private readonly ImportReport _report = new();
    private readonly HashSet<string> _filesRead = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Name, decimal Source, Func<RetailPlusDbContext, CancellationToken, Task<decimal>> Query)> _checks = [];
    private readonly Dictionary<object, string> _where = new(ReferenceEqualityComparer.Instance);
    private SqlConnection _connection = null!;
    private SqlTransaction? _transaction;
    private string _root = "";
    private int _batchId;
    private DateTime _now;

    public static string ToolVersion => typeof(DbfImporter).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Runs the import. Throws <see cref="ImportRefusedException"/> before touching anything if refused.</summary>
    public async Task<ImportReport> RunAsync(CancellationToken cancellationToken = default)
    {
        _now = DateTime.Now;
        _now = _now.AddTicks(-(_now.Ticks % TimeSpan.TicksPerSecond));
        _root = Path.GetFullPath(options.SourceFolder);
        if (!Directory.Exists(_root)) throw new ImportRefusedException($"Source folder {_root} does not exist.");
        if (FindFile(_root, "SETUP.DBF") is null && FindLocations().Count == 0)
            throw new ImportRefusedException($"{_root} does not look like a Retail Plus data folder (no SETUP.DBF and no inventory).");

        var csb = new SqlConnectionStringBuilder(options.ConnectionString);
        _report.StartedAt = _now;
        _report.SourceFolder = _root;
        _report.Database = $"{csb.DataSource}/{csb.InitialCatalog}";
        _report.ToolVersion = ToolVersion;

        _log.Info($"Preparing database {_report.Database}");
        await using (var db = RetailPlusDb.CreateContext(options.ConnectionString))
        {
            db.Database.SetCommandTimeout(600);
            await db.Database.MigrateAsync(cancellationToken);
            bool hasData = await db.Items.AnyAsync(cancellationToken) || await db.Clients.AnyAsync(cancellationToken);
            if (hasData && !options.Overwrite)
                throw new ImportRefusedException($"Database {_report.Database} already holds items or clients. Use the overwrite option to replace them.");
            var batch = new ImportBatch { StartedAt = _now, SourcePath = Truncate(_root, 260), ToolVersion = ToolVersion, Status = "Running" };
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync(cancellationToken);
            _batchId = batch.Id;
            _report.ImportBatchId = _batchId;
        }

        _connection = new SqlConnection(options.ConnectionString);
        try
        {
            await _connection.OpenAsync(cancellationToken);
            _transaction = (SqlTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            if (options.Overwrite) await WipeAsync(cancellationToken);
            await ImportAllAsync(cancellationToken);
            await _transaction.CommitAsync(cancellationToken);
            _report.Succeeded = true;
            _log.Info("Import committed; checking control totals");
            await VerifyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _report.Error = ex.Message;
            if (_transaction?.Connection is not null)
            {
                try { await _transaction.RollbackAsync(CancellationToken.None); }
                catch (InvalidOperationException) { /* already completed */ }
            }
            throw;
        }
        finally
        {
            if (_transaction is not null) await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
            _report.FinishedAt = DateTime.Now;
            _report.Warnings.AddRange(_log.Warnings);
            await FinishBatchAsync();
        }
        return _report;
    }

    private async Task ImportAllAsync(CancellationToken ct)
    {
        await ImportSetupAsync(ct);
        var numbers = ReadNumbers();
        var locations = FindLocations();
        var inventoryNames = CollectInventoryNames(locations);
        await ImportDepartmentsAndCategoriesAsync(inventoryNames, ct);
        await ImportPartiesAsync(inventoryNames.Suppliers, ct);
        var locationIds = await ImportLocationsAsync(locations, ct);
        foreach (var location in locations)
        {
            int locationId = locationIds[location.Code];
            if (location.InventoryPath is not null) await ImportInventoryAsync(location.Code, locationId, location.InventoryPath, ct);
            if (location.Folder is not null) await ImportLocationFolderAsync(location.Code, locationId, location.Folder, ct);
        }
        await ImportInvoicesAsync(ct);
        await ImportRmasAsync(ct);
        await ImportUsersAsync(ct);
        await ImportStaffAsync(ct);
        await ImportTextsAsync(ct);
        await ImportFastKeysAsync(ct);
        await ImportStationsAsync(ct);
        await SeedTenderTypesAsync(ct);
        await RestartSequencesAsync(numbers, ct);
        NoteUnimportedFiles();
    }

    // Database helpers -----------------------------------------------------------------------------------------

    /// <summary>A context on the import connection, enlisted in the import transaction.</summary>
    private RetailPlusDbContext NewContext()
    {
        var builder = new DbContextOptionsBuilder<RetailPlusDbContext>()
            .UseSqlServer(_connection, sql => sql.UseCompatibilityLevel(RetailPlusDb.CompatibilityLevel).CommandTimeout(600));
        var db = new RetailPlusDbContext(builder.Options);
        db.Database.UseTransaction(_transaction);
        return db;
    }

    private async Task<int> ExecAsync(string sql, CancellationToken ct)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        command.CommandTimeout = 600;
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Empties every table of the model except the current import batch, children before parents, for the
    /// overwrite option. Sequences are restarted later from NUMBERS.DBF.
    /// </summary>
    private async Task WipeAsync(CancellationToken ct)
    {
        _log.Info("Emptying the existing database (overwrite)");
        await using var db = NewContext();
        var types = db.Model.GetEntityTypes().Where(t => t.GetTableName() is not null && t.FindOwnership() is null).ToList();
        string Name(Microsoft.EntityFrameworkCore.Metadata.IEntityType t) => $"[{t.GetSchema() ?? "dbo"}].[{t.GetTableName()}]";
        var remaining = types.Select(Name).ToHashSet(StringComparer.Ordinal);
        var references = types
            .SelectMany(t => t.GetForeignKeys().Select(fk => (Dependent: Name(t), Principal: Name(fk.PrincipalEntityType))))
            .Where(r => r.Dependent != r.Principal)
            .ToList();
        while (remaining.Count > 0)
        {
            var next = remaining.Where(t => !references.Any(r => r.Principal == t && remaining.Contains(r.Dependent)))
                .OrderBy(t => t, StringComparer.Ordinal).ToList();
            if (next.Count == 0) throw new InvalidOperationException("Foreign keys form a cycle; cannot empty the database.");
            foreach (var table in next)
            {
                var sql = table == "[dbo].[ImportBatches]"
                    ? "DELETE FROM [dbo].[ImportBatches] WHERE [Id] <> " + _batchId.ToString(CultureInfo.InvariantCulture)
                    : "DELETE FROM " + table;
                await ExecAsync(sql, ct);
                remaining.Remove(table);
            }
        }
    }

    /// <summary>A root entity to insert with the source records it came from.</summary>
    private sealed class Pending(object? root)
    {
        public object? Root { get; } = root;
        public List<(object? Entity, string EntityName, SourceRecord Source, string Json)> Traces { get; } = [];
    }

    private Pending One(object entity, string entityName, SourceRecord source, params string[] exclude)
    {
        var pending = new Pending(entity);
        pending.Traces.Add((entity, entityName, source, source.ToJson(exclude)));
        _where[entity] = source.Where;
        return pending;
    }

    /// <summary>A source record that is kept in the trace only (deleted records of tables without soft delete).</summary>
    private static Pending TraceOnly(string entityName, SourceRecord source)
    {
        var pending = new Pending(null);
        pending.Traces.Add((null, entityName, source, source.ToJson()));
        return pending;
    }

    private T Remember<T>(T entity, SourceRecord source) where T : class
    {
        _where[entity] = source.Where;
        return entity;
    }

    /// <summary>Inserts the entities in chunks, then the trace rows pointing at their new ids.</summary>
    private async Task<int> SaveAsync(IReadOnlyList<Pending> rows, CancellationToken ct)
    {
        int written = 0;
        for (int start = 0; start < rows.Count; start += ChunkSize)
        {
            var part = rows.Skip(start).Take(ChunkSize).ToList();
            await using var db = NewContext();
            foreach (var p in part)
                if (p.Root is not null) db.Add(p.Root);
            TruncateStrings(db);
            await db.SaveChangesAsync(ct);
            written += part.Count(p => p.Root is not null);
            foreach (var p in part)
                foreach (var (entity, name, source, json) in p.Traces)
                {
                    int? id = entity is null ? null : Convert.ToInt32(db.Entry(entity).Property("Id").CurrentValue, CultureInfo.InvariantCulture);
                    db.ImportSourceRows.Add(new ImportSourceRow
                    {
                        ImportBatchId = _batchId,
                        Entity = name,
                        EntityId = id,
                        SourceFile = Truncate(source.Table.RelativePath, 260),
                        SourceRecordNumber = source.Number,
                        RawJson = json,
                    });
                }
            await db.SaveChangesAsync(ct);
        }
        return written;
    }

    /// <summary>Cuts strings to the column sizes of the model, with a warning for each cut.</summary>
    private void TruncateStrings(DbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            foreach (var property in entry.Properties)
            {
                if (property.CurrentValue is not string text) continue;
                var max = property.Metadata.GetMaxLength();
                if (max is null || text.Length <= max) continue;
                property.CurrentValue = text[..max.Value];
                var where = _where.GetValueOrDefault(entry.Entity, entry.Metadata.ClrType.Name);
                _log.Warn(where, $"{entry.Metadata.ClrType.Name}.{property.Metadata.Name} cut from {text.Length} to {max} characters");
            }
        }
    }

    private void Check(string name, decimal source, Func<RetailPlusDbContext, CancellationToken, Task<decimal>> query) =>
        _checks.Add((name, source, query));

    private async Task VerifyAsync(CancellationToken ct)
    {
        await using var db = RetailPlusDb.CreateContext(options.ConnectionString);
        db.Database.SetCommandTimeout(600);
        foreach (var (name, source, query) in _checks)
            _report.Totals.Add(new ControlTotal(name, source, await query(db, ct)));
    }

    private async Task FinishBatchAsync()
    {
        if (_batchId == 0) return;
        var manifest = _filesRead.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(path =>
        {
            var info = new FileInfo(path);
            using var stream = File.OpenRead(path);
            return new { File = Path.GetRelativePath(_root, path), info.Length, Sha256 = Convert.ToHexString(SHA256.HashData(stream)) };
        }).ToList();
        await using var db = RetailPlusDb.CreateContext(options.ConnectionString);
        var batch = await db.ImportBatches.FindAsync(_batchId);
        if (batch is null) return;
        batch.FinishedAt = _report.FinishedAt.AddTicks(-(_report.FinishedAt.Ticks % TimeSpan.TicksPerSecond));
        batch.Status = _report.Succeeded ? "Succeeded" : "Failed";
        batch.ManifestJson = JsonSerializer.Serialize(manifest);
        batch.ReportJson = _report.ToJson();
        await db.SaveChangesAsync();
    }

    // Source helpers ---------------------------------------------------------------------------------------------

    /// <summary>Finds a file by name in a folder, ignoring case (copies from Windows keep any case).</summary>
    private static string? FindFile(string folder, string name) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
            : null;

    private SourceTable Open(string path)
    {
        var dbf = DbfTable.Open(path);
        var relative = Path.GetRelativePath(_root, path);
        foreach (var warning in dbf.Warnings) _log.Warn(relative, warning);
        _filesRead.Add(path);
        if (dbf.MemoFilePath is not null) _filesRead.Add(dbf.MemoFilePath);
        _log.Info($"Reading {relative} ({dbf.RecordsPresent} records)");
        return new SourceTable(dbf, relative, _log);
    }

    private TableResult Result(SourceTable table, string entity)
    {
        var result = new TableResult(table.RelativePath, entity) { Records = table.Dbf.RecordsPresent };
        _report.Tables.Add(result);
        return result;
    }

    private string? ReadText(string path)
    {
        _filesRead.Add(path);
        var text = Cp1252.Decode(File.ReadAllBytes(path)).TrimEnd(' ', '\0', '\r', '\n', '\t', '\x1a');
        return text.Length == 0 ? null : text;
    }

    private void Warn(SourceRecord record, IEnumerable<string> messages)
    {
        foreach (var m in messages) _log.Warn(record.Where, m);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private static string AppendNote(string? notes, string line) =>
        string.IsNullOrEmpty(notes) ? line : notes + "\r\n" + line;
}
