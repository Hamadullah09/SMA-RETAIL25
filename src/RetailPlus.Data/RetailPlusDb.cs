using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace RetailPlus.Data;

/// <summary>
/// Shared database options, so the app, the importer, the tests and the EF tools configure each engine alike. The model
/// is the same on both engines; each has its own migrations, in RetailPlus.Data.Sqlite and RetailPlus.Data.SqlServer.
/// </summary>
public static class RetailPlusDb
{
    /// <summary>
    /// SQL Server 2019 compatibility level: the lowest version the schema is tested on (LocalDB 2019 in development).
    /// The server runs SQL Server 2022 or 2025.
    /// </summary>
    public const int CompatibilityLevel = 150;

    /// <summary>SQL Server collation: case-insensitive, like the original's upper-case index tags.</summary>
    public const string Collation = "Latin1_General_100_CI_AS";

    /// <summary>SQLite's case-insensitive collation, set on every text column.</summary>
    public const string SqliteCollation = "NOCASE";

    public const string SqliteMigrationsAssembly = "RetailPlus.Data.Sqlite";
    public const string SqlServerMigrationsAssembly = "RetailPlus.Data.SqlServer";

    public static DbContextOptionsBuilder<RetailPlusDbContext> Configure(
        DbContextOptionsBuilder<RetailPlusDbContext> builder, DatabaseConnection connection, int? commandTimeout = null) =>
        connection.Provider switch
        {
            DatabaseProvider.Sqlite => builder.UseSqlite(connection.ConnectionString, o => o.MigrationsAssembly(SqliteMigrationsAssembly).CommandTimeout(commandTimeout)),
            _ => builder.UseSqlServer(connection.ConnectionString, o => o.UseCompatibilityLevel(CompatibilityLevel)
                .MigrationsAssembly(SqlServerMigrationsAssembly).CommandTimeout(commandTimeout)),
        };

    /// <summary>Options on an open connection, so several contexts can share one transaction (the importer).</summary>
    public static DbContextOptionsBuilder<RetailPlusDbContext> Configure(
        DbContextOptionsBuilder<RetailPlusDbContext> builder, DbConnection connection, DatabaseProvider provider, int? commandTimeout = null) =>
        provider switch
        {
            DatabaseProvider.Sqlite => builder.UseSqlite(connection, o => o.MigrationsAssembly(SqliteMigrationsAssembly).CommandTimeout(commandTimeout)),
            _ => builder.UseSqlServer(connection, o => o.UseCompatibilityLevel(CompatibilityLevel)
                .MigrationsAssembly(SqlServerMigrationsAssembly).CommandTimeout(commandTimeout)),
        };

    public static RetailPlusDbContext CreateContext(DatabaseConnection connection, StationIdentity? station = null) =>
        new(Configure(new DbContextOptionsBuilder<RetailPlusDbContext>(), connection).Options) { Station = station };

    /// <summary>A till's database file when nothing else is set: smaretail.db in %LOCALAPPDATA%\RetailPlus.</summary>
    public static string DefaultSqliteFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetailPlus", "smaretail.db");

    /// <summary>The database a tool's --db / --connection / --localdb options name; the default SQLite file when none.</summary>
    public static DatabaseConnection FromOptions(string? sqliteFile, string? connectionString, string? localDb) =>
        connectionString is { Length: > 0 } ? DatabaseConnection.SqlServer(connectionString)
        : localDb is { Length: > 0 } ? DatabaseConnection.LocalDb(localDb)
        : DatabaseConnection.SqliteFile(sqliteFile is { Length: > 0 } ? sqliteFile : DefaultSqliteFile);

    /// <summary>A LocalDB connection string for development and tests.</summary>
    public static string LocalDbConnectionString(string database) =>
        $@"Server=(localdb)\MSSQLLocalDB;Database={database};Trusted_Connection=True;TrustServerCertificate=True";
}

/// <summary>The store and station a database session works for; rows it adds are stamped with them.</summary>
public sealed record StationIdentity(Guid StoreId, int StationNumber);
