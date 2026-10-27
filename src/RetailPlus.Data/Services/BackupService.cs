using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace RetailPlus.Data.Services;

/// <summary>
/// Tools / Backup, Restore and Rebuild. A till's SQLite file is copied with SQLite's online backup, so the program
/// keeps running; the server's database uses SQL Server's own backup.
/// </summary>
public sealed class BackupService(DataContext data)
{
    private bool IsSqlite => data.Connection.Provider == DatabaseProvider.Sqlite;

    /// <summary>Writes a full backup to <paramref name="folder"/> and returns the file name.</summary>
    public async Task<string> BackupAsync(string folder, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        if (IsSqlite)
        {
            var file = Path.Combine(Path.GetFullPath(folder), $"{Path.GetFileNameWithoutExtension(data.DatabaseName)}_{stamp}.db");
            await using var source = new SqliteConnection(data.Connection.ConnectionString);
            await using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
            await source.OpenAsync(cancellationToken);
            await target.OpenAsync(cancellationToken);
            source.BackupDatabase(target);
            await AuditAsync(AuditActions.Backup, "Written to " + file);
            return file;
        }
        var database = data.DatabaseName;
        var bak = Path.Combine(Path.GetFullPath(folder), $"{database}_{stamp}.bak");
        await using var connection = new SqlConnection(data.Connection.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "BACKUP DATABASE " + Quote(database) + " TO DISK = @file WITH INIT, COPY_ONLY, NAME = @name";
        command.Parameters.AddWithValue("@file", bak);
        command.Parameters.AddWithValue("@name", "SMA RETAIL backup");
        command.CommandTimeout = 0;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await AuditAsync(AuditActions.Backup, "Written to " + bak);
        return bak;
    }

    /// <summary>Replaces the database with a backup. Every other connection is closed first.</summary>
    public async Task RestoreAsync(string backupFile, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupFile)) throw new BusinessRuleException("The backup file was not found.");
        if (IsSqlite)
        {
            // The backup API copies into the live file page by page, under a lock, so the write-ahead log stays right.
            SqliteConnection.ClearAllPools();
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await using var target = new SqliteConnection(data.Connection.ConnectionString);
            await source.OpenAsync(cancellationToken);
            await target.OpenAsync(cancellationToken);
            source.BackupDatabase(target);
            SqliteConnection.ClearAllPools();
            await AuditAsync(AuditActions.Restore, "From " + backupFile);
            return;
        }
        var database = data.DatabaseName;
        var builder = new SqlConnectionStringBuilder(data.Connection.ConnectionString) { InitialCatalog = "master" };
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        async Task Run(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@file", backupFile);
            command.CommandTimeout = 0;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await Run("ALTER DATABASE " + Quote(database) + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        try
        {
            await Run("RESTORE DATABASE " + Quote(database) + " FROM DISK = @file WITH REPLACE");
        }
        finally
        {
            await Run("ALTER DATABASE " + Quote(database) + " SET MULTI_USER");
        }
        await AuditAsync(AuditActions.Restore, "From " + backupFile);
    }

    /// <summary>
    /// Writes the backup or restore to the audit log. A restored database from before the audit log keeps working:
    /// the line is then left out.
    /// </summary>
    private async Task AuditAsync(string action, string details)
    {
        try
        {
            await using var db = data.Create();
            db.Audit(action, details);
            await db.SaveChangesAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
        }
        catch (System.Data.Common.DbException)
        {
        }
    }

    /// <summary>Tools / Rebuild: checks the database, rebuilds every index and refreshes statistics.</summary>
    public async Task<string> RebuildAsync(CancellationToken cancellationToken = default)
    {
        if (IsSqlite)
        {
            await using var file = new SqliteConnection(data.Connection.ConnectionString);
            await file.OpenAsync(cancellationToken);
            await using (var check = file.CreateCommand())
            {
                check.CommandText = "PRAGMA integrity_check";
                var result = Convert.ToString(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new BusinessRuleException("The database file is damaged: " + result + ". Restore the last backup.");
            }
            await using (var rebuild = file.CreateCommand())
            {
                rebuild.CommandText = "REINDEX; ANALYZE; VACUUM;";
                rebuild.CommandTimeout = 0;
                await rebuild.ExecuteNonQueryAsync(cancellationToken);
            }
            return "Checked the database file, rebuilt its indexes and compacted it.";
        }
        await using var connection = new SqlConnection(data.Connection.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT QUOTENAME(s.name) + '.' + QUOTENAME(t.name) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id";
            await using var reader = await list.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) tables.Add(reader.GetString(0));
        }
        async Task Exec(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 0;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await Exec("DBCC CHECKDB WITH NO_INFOMSGS");
        foreach (var t in tables) await Exec("ALTER INDEX ALL ON " + t + " REBUILD");
        await Exec("EXEC sp_updatestats");
        return $"Checked the database and rebuilt the indexes of {tables.Count} tables.";
    }

    private static string Quote(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
