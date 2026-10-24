using Microsoft.EntityFrameworkCore;

namespace RetailPlus.Data;

/// <summary>Shared database options, so the app, the importer, the tests and the EF tools configure SQL Server alike.</summary>
public static class RetailPlusDb
{
    /// <summary>
    /// SQL Server 2019 compatibility level: the lowest version the schema is tested on (LocalDB 2019 in development).
    /// Stores run SQL Server 2022 or 2025 Express.
    /// </summary>
    public const int CompatibilityLevel = 150;

    public const string Collation = "Latin1_General_100_CI_AS";

    public static DbContextOptionsBuilder<RetailPlusDbContext> Configure(
        DbContextOptionsBuilder<RetailPlusDbContext> builder, string connectionString) =>
        builder.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(CompatibilityLevel));

    public static RetailPlusDbContext CreateContext(string connectionString) =>
        new(Configure(new DbContextOptionsBuilder<RetailPlusDbContext>(), connectionString).Options);

    /// <summary>A LocalDB connection string for development and tests.</summary>
    public static string LocalDbConnectionString(string database) =>
        $@"Server=(localdb)\MSSQLLocalDB;Database={database};Trusted_Connection=True;TrustServerCertificate=True";
}
