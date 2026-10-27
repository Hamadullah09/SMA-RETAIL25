using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>
/// A throwaway store database with all migrations applied and one store with station 1, the way the app starts one.
/// Every database test runs twice: on SQLite (a file in the temp folder, as on a till) and on SQL Server (as on the
/// server). Set RETAILPLUS_TEST_SQL to a connection string without a database name to use another SQL Server than
/// LocalDB; the SQL Server tests skip when no server is reachable.
/// </summary>
public abstract class DatabaseFixture : IAsyncLifetime
{
    private int _codeCounter;

    public abstract DatabaseConnection Connection { get; }
    public StationIdentity? Station { get; private set; }
    public bool Available { get; private set; }
    public string UnavailableReason { get; private set; } = "";

    /// <summary>Services for station 1 of the test store.</summary>
    public DataContext Data => new(Connection, Station);

    public RetailPlusDbContext CreateContext() => RetailPlusDb.CreateContext(Connection, Station);

    /// <summary>A fresh three-character location code, so tests sharing the database don't collide.</summary>
    public string NextLocationCode() => "T" + Interlocked.Increment(ref _codeCounter).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

    public async Task InitializeAsync()
    {
        try
        {
            var data = new DataContext(Connection);
            await data.MigrateAsync();
            Station = await new SetupService(data).SeedAsync(1);
            Available = true;
        }
        catch (SqlException ex) when (Connection.Provider == DatabaseProvider.SqlServer)
        {
            UnavailableReason = "No SQL Server for integration tests: " + ex.Message;
        }
    }

    public abstract Task DisposeAsync();
}

public sealed class SqliteFixture : DatabaseFixture
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "RetailPlus_Test_" + Guid.NewGuid().ToString("N")[..12] + ".db");

    public override DatabaseConnection Connection => DatabaseConnection.SqliteFile(_file);

    public override Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _file, _file + "-wal", _file + "-shm" })
            if (File.Exists(file)) File.Delete(file);
        return Task.CompletedTask;
    }
}

public sealed class SqlServerFixture : DatabaseFixture
{
    public override DatabaseConnection Connection { get; } = Connect("RetailPlus_Test_" + Guid.NewGuid().ToString("N")[..12]);

    private static DatabaseConnection Connect(string database) =>
        Environment.GetEnvironmentVariable("RETAILPLUS_TEST_SQL") is { Length: > 0 } server
            ? DatabaseConnection.SqlServer(new SqlConnectionStringBuilder(server) { InitialCatalog = database }.ConnectionString)
            : DatabaseConnection.LocalDb(database);

    public override async Task DisposeAsync()
    {
        if (!Available) return;
        SqlConnection.ClearAllPools();
        await using var db = RetailPlusDb.CreateContext(Connection);
        await db.Database.EnsureDeletedAsync();
    }
}
