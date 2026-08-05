using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Retail25.Application.Abstractions;
using Retail25.Application.Maintenance;
using Retail25.Domain.Common;

namespace Retail25.Infrastructure.Maintenance;

/// <summary>
/// BACKUP DATABASE / RESTORE DATABASE against the configured SQL Server, following the procedure in
/// <c>docs/runbooks/restore.md</c>.
/// <para>
/// The backup directory must be writable by the SQL Server service account — with LocalDB that is
/// the signed-in user, so the default under the user profile works out of the box. A full server
/// would need <c>Backup:Directory</c> pointed somewhere its service account can write.
/// </para>
/// </summary>
public sealed class SqlServerDatabaseBackupService : IDatabaseBackupService
{
    private static readonly Error BackupFailed = new("backup.failed", "The backup could not be taken.");
    private static readonly Error InvalidName = new("backup.invalid_name", "That is not the name of a backup file.");
    private static readonly Error NotFound = new("backup.not_found", "No backup file with that name exists.");
    private static readonly Error RestoreFailed = new("backup.restore_failed", "The restore did not complete.");

    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly string _directory;
    private readonly IDateTime _clock;
    private readonly ILogger<SqlServerDatabaseBackupService> _logger;

    public SqlServerDatabaseBackupService(
        IConfiguration configuration,
        IDateTime clock,
        ILogger<SqlServerDatabaseBackupService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured.");
        _databaseName = new SqlConnectionStringBuilder(_connectionString).InitialCatalog;
        _directory = configuration["Backup:Directory"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Retail25",
            "Backups");
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<BackupFileDto>> BackupAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);

        var fileName = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{_databaseName}_{_clock.Now.UtcDateTime:yyyyMMdd_HHmmss}.bak");
        var path = Path.Combine(_directory, fileName);

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            // CHECKSUM makes silent page corruption fail the backup now, when the operator is
            // watching, rather than the restore months later, when the original is gone.
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"BACKUP DATABASE [{_databaseName}] TO DISK = @path WITH INIT, CHECKSUM, NAME = @name";
            command.CommandTimeout = 600;
            command.Parameters.AddWithValue("@path", path);
            command.Parameters.AddWithValue("@name", $"{_databaseName} full backup");
            await command.ExecuteNonQueryAsync(ct);

            var info = new FileInfo(path);
            _logger.LogInformation("Backed up {Database} to {Path} ({Bytes} bytes).", _databaseName, path, info.Length);

            return Result.Success(new BackupFileDto(fileName, info.Length, info.CreationTimeUtc));
        }
        catch (Exception ex) when (ex is SqlException or IOException)
        {
            _logger.LogError(ex, "Backup of {Database} to {Path} failed.", _databaseName, path);
            return Result.Failure<BackupFileDto>(BackupFailed.With("detail", ex.Message));
        }
    }

    public Task<Result<IReadOnlyList<BackupFileDto>>> ListAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_directory))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<BackupFileDto>>([]));
        }

        IReadOnlyList<BackupFileDto> files = new DirectoryInfo(_directory)
            .GetFiles("*.bak")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupFileDto(f.Name, f.Length, f.CreationTimeUtc))
            .ToList();

        return Task.FromResult(Result.Success(files));
    }

    public async Task<Result> RestoreAsync(string fileName, CancellationToken ct)
    {
        // The name came over HTTP. Anything that is not a bare *.bak file name inside the backup
        // directory is refused before it reaches a SQL statement.
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
            || !fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(InvalidName);
        }

        var path = Path.Combine(_directory, fileName);

        if (!File.Exists(path))
        {
            return Result.Failure(NotFound.With("fileName", fileName));
        }

        // Restore runs from master: the target database cannot be the one the connection sits in,
        // because SINGLE_USER is about to throw every other session out of it.
        var builder = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" };

        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(ct);

            await ExecuteAsync(connection, "RESTORE VERIFYONLY FROM DISK = @path", path, ct);

            // Pooled connections held by this process would otherwise re-enter the database between
            // SINGLE_USER and the restore and deadlock it.
            SqlConnection.ClearAllPools();

            await ExecuteAsync(
                connection,
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE",
                path: null,
                ct);

            try
            {
                await ExecuteAsync(
                    connection,
                    $"RESTORE DATABASE [{_databaseName}] FROM DISK = @path WITH REPLACE",
                    path,
                    ct);
            }
            finally
            {
                // Whatever happened, do not leave the shop's database in single-user mode.
                await ExecuteAsync(
                    connection,
                    $"ALTER DATABASE [{_databaseName}] SET MULTI_USER",
                    path: null,
                    CancellationToken.None);
            }

            SqlConnection.ClearAllPools();
            _logger.LogWarning("Restored {Database} from {Path}. All sessions were dropped.", _databaseName, path);

            return Result.Success();
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Restore of {Database} from {Path} failed.", _databaseName, path);
            return Result.Failure(RestoreFailed.With("detail", ex.Message));
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, string? path, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 600;

        if (path is not null)
        {
            command.Parameters.AddWithValue("@path", path);
        }

        await command.ExecuteNonQueryAsync(ct);
    }
}
