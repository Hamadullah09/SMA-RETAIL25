using Microsoft.EntityFrameworkCore.Design;

namespace RetailPlus.Data.SqlServer;

/// <summary>
/// Used by the dotnet-ef tool. Defaults to a LocalDB development database; set RETAILPLUS_DESIGN_DB to a full
/// connection string to point the tools elsewhere.
/// </summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<RetailPlusDbContext>
{
    public RetailPlusDbContext CreateDbContext(string[] args) =>
        RetailPlusDb.CreateContext(Environment.GetEnvironmentVariable("RETAILPLUS_DESIGN_DB") is { Length: > 0 } connection
            ? DatabaseConnection.SqlServer(connection)
            : DatabaseConnection.LocalDb("RetailPlus_Dev"));
}
