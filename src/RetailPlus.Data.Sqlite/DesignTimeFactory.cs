using Microsoft.EntityFrameworkCore.Design;

namespace RetailPlus.Data.Sqlite;

/// <summary>Used by the dotnet-ef tool to build the SQLite migrations, against a scratch file in the temp folder.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<RetailPlusDbContext>
{
    public RetailPlusDbContext CreateDbContext(string[] args) =>
        RetailPlusDb.CreateContext(DatabaseConnection.SqliteFile(Path.Combine(Path.GetTempPath(), "smaretail-design.db")));
}
