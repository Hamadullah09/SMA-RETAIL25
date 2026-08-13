using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Retail25.Application.Abstractions;
using Retail25.Application.Maintenance;
using Retail25.Domain.Common;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Infrastructure.Maintenance;

/// <summary>
/// A backup the application takes itself, table by table, into a file it can actually hand back.
/// <para>
/// <b>Why this exists.</b> <c>BACKUP DATABASE … TO DISK</c> writes to a path on the machine running
/// SQL Server. On this deployment that is a different machine from the web app, so the file lands
/// somewhere the application cannot see, cannot list and cannot offer for download — and shared
/// database hosting rarely grants the BACKUP permission in the first place. Native backup is the
/// better tool when the database is your own; it cannot work at all when it is not.
/// </para>
/// <para>
/// So this reads every table through the connection the application already has and writes them as
/// JSON lines inside a zip. It needs no server-side file access and no elevated database
/// permission: if the app can read its own data, it can back it up.
/// </para>
/// <para>
/// <b>What it is not.</b> This is the data, not the database — no indexes, no permissions, no
/// Hangfire scheduling state. It restores into a schema created by migrations, which is how this
/// application builds a database anyway. And <b>restoring is not implemented yet</b>: writing rows
/// back in dependency order with identity columns preserved is the dangerous half, and a
/// half-tested restore is worse than none because it is discovered during an outage. The archive is
/// verified on the way out so what is here can be trusted; see <c>docs/runbooks/restore.md</c>.
/// </para>
/// </summary>
public sealed class PortableDatabaseBackupService : IDatabaseBackupService
{
    private static readonly Error Failed = new("backup.failed", "The backup could not be taken.");
    private static readonly Error InvalidName = new("backup.invalid_name", "That is not the name of a backup file.");
    private static readonly Error NotFound = new("backup.not_found", "No backup file with that name exists.");

    private static readonly JsonSerializerOptions ManifestFormat = new() { WriteIndented = true };

    private static readonly Error RestoreNotSupported = new(
        "backup.restore_not_supported",
        "This archive is restored by following the restore runbook, not from this screen.");

    private readonly ApplicationDbContext _db;
    private readonly string _directory;
    private readonly IDateTime _clock;
    private readonly ILogger<PortableDatabaseBackupService> _logger;

    public PortableDatabaseBackupService(
        ApplicationDbContext db,
        IConfiguration configuration,
        IHostEnvironment environment,
        IDateTime clock,
        ILogger<PortableDatabaseBackupService> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        _db = db;
        _clock = clock;
        _logger = logger;

        // Inside the application's own folder by default, which is the only place a shared pool
        // identity is certain to be able to write — the same rule the logging configuration states
        // for its sinks. The previous default was under the user profile, which on this host the
        // pool has no rights to, so listing backups returned an empty list forever.
        _directory = configuration["Backup:Directory"]
            ?? Path.Combine(environment.ContentRootPath, "App_Data", "backups");
    }

    public async Task<Result<BackupFileDto>> BackupAsync(CancellationToken ct)
    {
        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"retail25_{_clock.Now.UtcDateTime:yyyyMMdd_HHmmss}.r25bak.zip");

        var path = Path.Combine(_directory, fileName);

        try
        {
            Directory.CreateDirectory(_directory);

            var counts = new Dictionary<string, long>(StringComparer.Ordinal);

            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                foreach (var table in TableNames())
                {
                    counts[table] = await WriteTableAsync(archive, table, ct);
                }

                await WriteManifestAsync(archive, counts, ct);
            }

            // Read back what was just written. A backup nobody has opened is a hope, and the cheapest
            // moment to discover a truncated archive is now, while the original data is still here.
            var verified = Verify(path, counts);
            if (verified.IsFailure)
            {
                return Result.Failure<BackupFileDto>(verified.Error);
            }

            var info = new FileInfo(path);
            _logger.LogInformation(
                "Backed up {Tables} tables, {Rows} rows, to {Path} ({Bytes} bytes).",
                counts.Count,
                counts.Values.Sum(),
                path,
                info.Length);

            return Result.Success(new BackupFileDto(fileName, info.Length, info.CreationTimeUtc));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.SqlClient.SqlException)
        {
            _logger.LogError(ex, "Backup to {Path} failed.", path);

            // Left behind, a part-written archive is indistinguishable from a good one in a listing.
            TryDelete(path);

            return Result.Failure<BackupFileDto>(Failed.With("detail", ex.Message));
        }
    }

    public Task<Result<IReadOnlyList<BackupFileDto>>> ListAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_directory))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<BackupFileDto>>([]));
        }

        IReadOnlyList<BackupFileDto> files = new DirectoryInfo(_directory)
            .GetFiles("*.r25bak.zip")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupFileDto(f.Name, f.Length, f.CreationTimeUtc))
            .ToList();

        return Task.FromResult(Result.Success(files));
    }

    /// <summary>
    /// Deliberately refused rather than half-done. See the summary: an untested restore is
    /// discovered during an outage, which is the worst moment to find out.
    /// </summary>
    public Task<Result> RestoreAsync(string fileName, CancellationToken ct)
        => Task.FromResult(Result.Failure(RestoreNotSupported));

    /// <summary>Opens a finished archive for download, refusing anything that is not one of ours.</summary>
    public Result<string> ResolvePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains("..", StringComparison.Ordinal)
            || Path.GetFileName(fileName) != fileName
            || !fileName.EndsWith(".r25bak.zip", StringComparison.Ordinal))
        {
            return Result.Failure<string>(InvalidName.With("fileName", fileName));
        }

        var path = Path.Combine(_directory, fileName);

        return File.Exists(path)
            ? Result.Success(path)
            : Result.Failure<string>(NotFound.With("fileName", fileName));
    }

    /// <summary>Every table the model knows about, in a stable order so two archives can be compared.</summary>
    private IEnumerable<string> TableNames()
        => _db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

    /// <summary>
    /// One table, streamed. Rows are written as they are read so a large sales history costs a
    /// buffer per row rather than a copy of the table in memory.
    /// </summary>
    private async Task<long> WriteTableAsync(ZipArchive archive, string table, CancellationToken ct)
    {
        var entry = archive.CreateEntry($"tables/{table}.jsonl", CompressionLevel.Optimal);

        await using var target = entry.Open();
        await using var writer = new StreamWriter(target);

        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();

        // The name comes from the EF model, not from a caller, so it contains only what a mapped
        // table name can contain. It cannot be a parameter either — an identifier never can be.
        command.CommandText = $"SELECT * FROM [{table}]";
        command.CommandTimeout = 600;

        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = 0L;

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);

            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
            }

            await writer.WriteLineAsync(JsonSerializer.Serialize(row).AsMemory(), ct);
            rows++;
        }

        await writer.FlushAsync(ct);
        return rows;
    }

    private async Task WriteManifestAsync(ZipArchive archive, Dictionary<string, long> counts, CancellationToken ct)
    {
        var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);

        await using var target = entry.Open();

        // The migration the schema was at. A restore into a database at a different migration is
        // the one thing that silently produces wrong data rather than an error, so the archive
        // carries the answer with it.
        var applied = (await _db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault();

        await JsonSerializer.SerializeAsync(
            target,
            new
            {
                format = "retail25.portable.v1",
                takenAtUtc = _clock.Now.UtcDateTime,
                schemaMigration = applied,
                tables = counts.OrderBy(c => c.Key, StringComparer.Ordinal).ToDictionary(c => c.Key, c => c.Value),
                totalRows = counts.Values.Sum(),
            },
            ManifestFormat,
            ct);
    }

    /// <summary>Re-opens the finished file and checks every table is present with the rows claimed.</summary>
    private static Result Verify(string path, Dictionary<string, long> counts)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);

            if (archive.GetEntry("manifest.json") is null)
            {
                return Result.Failure(Failed.With("detail", "the archive has no manifest"));
            }

            foreach (var (table, expected) in counts)
            {
                var entry = archive.GetEntry($"tables/{table}.jsonl");
                if (entry is null)
                {
                    return Result.Failure(Failed.With("detail", $"'{table}' is missing from the archive"));
                }

                using var reader = new StreamReader(entry.Open());
                var actual = 0L;
                while (reader.ReadLine() is not null)
                {
                    actual++;
                }

                if (actual != expected)
                {
                    return Result.Failure(Failed.With(
                        "detail",
                        $"'{table}' holds {actual} rows but {expected} were written"));
                }
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return Result.Failure(Failed.With("detail", $"the archive could not be re-opened: {ex.Message}"));
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "A failed backup was left at {Path}.", path);
        }
    }
}
