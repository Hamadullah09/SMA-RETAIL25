using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Data;

namespace RetailPlus.Data.Tests;

/// <summary>
/// Creates a throwaway database on LocalDB, applies all migrations, and drops it afterwards. Set RETAILPLUS_TEST_SQL
/// to a connection string without a database name to use another server. Tests skip when no server is reachable.
/// </summary>
public sealed class LocalDbFixture : IDisposable
{
    private int _codeCounter;

    public LocalDbFixture()
    {
        DatabaseName = "RetailPlus_Test_" + Guid.NewGuid().ToString("N")[..12];
        var server = Environment.GetEnvironmentVariable("RETAILPLUS_TEST_SQL");
        ConnectionString = server is { Length: > 0 }
            ? new SqlConnectionStringBuilder(server) { InitialCatalog = DatabaseName }.ConnectionString
            : RetailPlusDb.LocalDbConnectionString(DatabaseName);
        try
        {
            using var context = CreateContext();
            context.Database.Migrate();
            Available = true;
        }
        catch (SqlException ex)
        {
            UnavailableReason = "No SQL Server for integration tests: " + ex.Message;
        }
    }

    public string DatabaseName { get; }
    public string ConnectionString { get; }
    public bool Available { get; }
    public string UnavailableReason { get; } = "";

    public RetailPlusDbContext CreateContext() => RetailPlusDb.CreateContext(ConnectionString);

    /// <summary>A fresh three-character location code, so tests sharing the database don't collide.</summary>
    public string NextLocationCode() => "T" + Interlocked.Increment(ref _codeCounter).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

    public void Dispose()
    {
        if (!Available) return;
        using var context = CreateContext();
        context.Database.EnsureDeleted();
    }
}
