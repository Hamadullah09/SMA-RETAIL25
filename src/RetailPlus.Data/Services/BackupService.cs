using System.Globalization;
using Microsoft.Data.SqlClient;

namespace RetailPlus.Data.Services;

/// <summary>Tools / Backup, Restore and Rebuild, done with SQL Server's own backup instead of copying files.</summary>
public sealed class BackupService(DataContext data)
{
    /// <summary>Writes a full copy-only backup to <paramref name="folder"/> and returns the file name.</summary>
    public async Task<string> BackupAsync(string folder, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folder);
        var database = data.DatabaseName;
        var file = Path.Combine(Path.GetFullPath(folder), $"{database}_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.bak");
        await using var connection = new SqlConnection(data.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "BACKUP DATABASE " + Quote(database) + " TO DISK = @file WITH INIT, COPY_ONLY, NAME = @name";
        command.Parameters.AddWithValue("@file", file);
        command.Parameters.AddWithValue("@name", "Retail Plus backup");
        command.CommandTimeout = 0;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return file;
    }

    /// <summary>Replaces the database with a backup. Every other connection is closed first.</summary>
    public async Task RestoreAsync(string backupFile, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupFile)) throw new BusinessRuleException("The backup file was not found.");
        var database = data.DatabaseName;
        var builder = new SqlConnectionStringBuilder(data.ConnectionString) { InitialCatalog = "master" };
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
    }

    /// <summary>Tools / Rebuild: checks the database, rebuilds every index and refreshes statistics.</summary>
    public async Task<string> RebuildAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(data.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var messages = new List<string>();
        connection.InfoMessage += (_, e) => messages.Add(e.Message);
        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT QUOTENAME(s.name) + '.' + QUOTENAME(t.name) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id";
            await using var reader = await list.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) tables.Add(reader.GetString(0));
        }
        async Task Run(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 0;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await Run("DBCC CHECKDB WITH NO_INFOMSGS");
        foreach (var t in tables) await Run("ALTER INDEX ALL ON " + t + " REBUILD");
        await Run("EXEC sp_updatestats");
        return $"Checked the database and rebuilt the indexes of {tables.Count} tables.";
    }

    private static string Quote(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
