using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Data;

namespace RetailPlus.Cutover.Tests;

/// <summary>
/// The Retail25 database the cutover is tested on: a copy of the LocalDB database Retail25_Source (Retail25's demo seed),
/// restored under a new name from a copy-only backup (the original is only read), then enriched with
/// Fixtures/retail25-enrich.sql. Set RETAILPLUS_R25_SOURCE to copy another database, and RETAILPLUS_TEST_SQL to a connection
/// string without a database name to use another server than LocalDB. Without the server or the database, the tests skip.
/// </summary>
public sealed class Retail25SourceFixture : IAsyncLifetime
{
    private readonly string _database = "R25_CutoverTest_" + Guid.NewGuid().ToString("N")[..10];
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RetailPlus_CutoverTest_" + Guid.NewGuid().ToString("N")[..10]);
    private bool _created;

    public static string Server => Environment.GetEnvironmentVariable("RETAILPLUS_TEST_SQL") is { Length: > 0 } server
        ? server
        : @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";

    public static string SourceName => Environment.GetEnvironmentVariable("RETAILPLUS_R25_SOURCE") is { Length: > 0 } name ? name : "Retail25_Source";

    public string ConnectionString => new SqlConnectionStringBuilder(Server) { InitialCatalog = _database }.ConnectionString;

    public bool Available { get; private set; }
    public string UnavailableReason { get; private set; } = "";

    public async Task InitializeAsync()
    {
        try
        {
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(Server) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            if (await ScalarAsync(master, "SELECT DB_ID(@name)", ("@name", SourceName)) is null or DBNull)
            {
                UnavailableReason = $"No database {SourceName} on {master.DataSource} to copy the Retail25 source from.";
                return;
            }
            Directory.CreateDirectory(_folder);
            var backup = Path.Combine(_folder, "retail25.bak");
            await ExecAsync(master, $"BACKUP DATABASE [{SourceName}] TO DISK = @file WITH COPY_ONLY, INIT", ("@file", backup));
            var files = new List<(string Logical, string Type)>();
            await using (var list = master.CreateCommand())
            {
                list.CommandText = "RESTORE FILELISTONLY FROM DISK = @file";
                list.Parameters.AddWithValue("@file", backup);
                await using var reader = await list.ExecuteReaderAsync();
                while (await reader.ReadAsync()) files.Add((reader.GetString(reader.GetOrdinal("LogicalName")), reader.GetString(reader.GetOrdinal("Type"))));
            }
            var moves = string.Join(", ", files.Select((f, i) => $"MOVE @l{i} TO @p{i}"));
            var parameters = files.SelectMany((f, i) => new[]
            {
                ($"@l{i}", (object)f.Logical),
                ($"@p{i}", Path.Combine(_folder, $"{_database}_{i}{(f.Type == "L" ? ".ldf" : ".mdf")}")),
            }).Append(("@file", backup)).ToArray();
            await ExecAsync(master, $"RESTORE DATABASE [{_database}] FROM DISK = @file WITH {moves}", parameters);
            _created = true;

            await using var copy = new SqlConnection(ConnectionString);
            await copy.OpenAsync();
            await ExecAsync(copy, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "retail25-enrich.sql")));
            Available = true;
        }
        catch (SqlException ex)
        {
            UnavailableReason = "No SQL Server to build the Retail25 source on: " + ex.Message;
        }
    }

    /// <summary>Runs SQL on the source copy (a change made in Retail25 between two runs).</summary>
    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecAsync(connection, sql);
    }

    private static async Task ExecAsync(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 600;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteScalarAsync();
    }

    public async Task DisposeAsync()
    {
        if (_created)
        {
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(Server) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            await ExecAsync(master, $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}];");
        }
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}

[CollectionDefinition(Name)]
public sealed class Retail25SourceGroup : ICollectionFixture<Retail25SourceFixture>
{
    public const string Name = "Retail25 source";
}

/// <summary>New, empty target databases of one engine, removed when the tests are done.</summary>
public abstract class CutoverTargets : IAsyncLifetime
{
    protected List<DatabaseConnection> Made { get; } = [];

    public abstract DatabaseConnection Create();

    /// <summary>Whether the database was created at all (a dry run must not create it).</summary>
    public abstract Task<bool> ExistsAsync(DatabaseConnection target);

    public Task InitializeAsync() => Task.CompletedTask;

    public abstract Task DisposeAsync();
}

public sealed class SqliteTargets : CutoverTargets
{
    public override DatabaseConnection Create()
    {
        var target = DatabaseConnection.SqliteFile(Path.Combine(Path.GetTempPath(), "RetailPlus_Cutover_" + Guid.NewGuid().ToString("N")[..12] + ".db"));
        Made.Add(target);
        return target;
    }

    public override Task<bool> ExistsAsync(DatabaseConnection target) => Task.FromResult(File.Exists(target.DatabaseName));

    public override Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in Made.SelectMany(t => new[] { t.DatabaseName, t.DatabaseName + "-wal", t.DatabaseName + "-shm" }))
            if (File.Exists(file)) File.Delete(file);
        return Task.CompletedTask;
    }
}

public sealed class SqlServerTargets : CutoverTargets
{
    public override DatabaseConnection Create()
    {
        var target = DatabaseConnection.SqlServer(new SqlConnectionStringBuilder(Retail25SourceFixture.Server)
        {
            InitialCatalog = "RetailPlus_Cutover_" + Guid.NewGuid().ToString("N")[..12],
        }.ConnectionString);
        Made.Add(target);
        return target;
    }

    public override async Task<bool> ExistsAsync(DatabaseConnection target)
    {
        await using var master = new SqlConnection(new SqlConnectionStringBuilder(target.ConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = "SELECT DB_ID(@name)";
        command.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = target.DatabaseName;
        return await command.ExecuteScalarAsync() is not (null or DBNull);
    }

    public override async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        foreach (var target in Made)
        {
            try
            {
                await using var db = RetailPlusDb.CreateContext(target);
                await db.Database.EnsureDeletedAsync();
            }
            catch (SqlException)
            {
                // No server: nothing was made.
            }
        }
    }
}
