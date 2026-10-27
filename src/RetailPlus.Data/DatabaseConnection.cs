using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace RetailPlus.Data;

/// <summary>Which database engine holds the data.</summary>
public enum DatabaseProvider
{
    /// <summary>A file on the till, so it keeps selling without a network or server.</summary>
    Sqlite,
    /// <summary>The central server (and LocalDB in development).</summary>
    SqlServer,
}

/// <summary>A database to open: the engine and its connection string.</summary>
public sealed record DatabaseConnection(DatabaseProvider Provider, string ConnectionString)
{
    public static DatabaseConnection SqliteFile(string path) =>
        new(DatabaseProvider.Sqlite, new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path) }.ToString());

    public static DatabaseConnection SqlServer(string connectionString) => new(DatabaseProvider.SqlServer, connectionString);

    /// <summary>A LocalDB database, for development and tests.</summary>
    public static DatabaseConnection LocalDb(string database) => SqlServer(RetailPlusDb.LocalDbConnectionString(database));

    /// <summary>The database file (SQLite), or the database name (SQL Server).</summary>
    public string DatabaseName => Provider == DatabaseProvider.Sqlite
        ? new SqliteConnectionStringBuilder(ConnectionString).DataSource
        : new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

    /// <summary>Where the data is, for messages and reports: the file, or server/database.</summary>
    public string Description => Provider == DatabaseProvider.Sqlite
        ? DatabaseName
        : $"{new SqlConnectionStringBuilder(ConnectionString).DataSource}/{DatabaseName}";

    public override string ToString() => $"{Provider}: {Description}";
}
