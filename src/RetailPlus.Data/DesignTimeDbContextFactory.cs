using Microsoft.EntityFrameworkCore.Design;

namespace RetailPlus.Data;

/// <summary>
/// Used by the dotnet-ef tool. Defaults to a LocalDB development database; set RETAILPLUS_DESIGN_DB to a full
/// connection string to point the tools elsewhere.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RetailPlusDbContext>
{
    public RetailPlusDbContext CreateDbContext(string[] args) =>
        RetailPlusDb.CreateContext(
            Environment.GetEnvironmentVariable("RETAILPLUS_DESIGN_DB") is { Length: > 0 } connection
                ? connection
                : RetailPlusDb.LocalDbConnectionString("RetailPlus_Dev"));
}
