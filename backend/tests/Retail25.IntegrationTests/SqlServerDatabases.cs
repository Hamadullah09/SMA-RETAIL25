using Microsoft.Data.SqlClient;

namespace Retail25.IntegrationTests;

/// <summary>
/// Dropping and recreating a test database on a server the suite does not own.
/// <para>
/// Shared by all three fixtures because the SQL Server form of it has two sharp edges, and a copy
/// of this in three files is two copies that will eventually only get one of them.
/// </para>
/// </summary>
internal static class SqlServerDatabases
{
    /// <summary>SQL Server's "CREATE DATABASE permission denied in database 'master'".</summary>
    public const int PermissionDenied = 262;

    /// <summary>Raised when the login is valid but holds nothing at server level.</summary>
    public const int InsufficientPrivilege = 15247;

    public static bool IsPermissionError(SqlException error)
        => error.Number is PermissionDenied or InsufficientPrivilege;

    public static string WithDatabase(string connectionString, string database)
        => new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database }.ConnectionString;

    /// <summary>
    /// Drops <paramref name="name"/> if it exists and creates it empty, returning its connection
    /// string. Throws <see cref="SqlException"/> if the login may not create databases — the caller
    /// decides whether that is fatal or a reason to share.
    /// </summary>
    public static async Task<string> RecreateAsync(string adminConnection, string name)
    {
        await using var connection = new SqlConnection(WithDatabase(adminConnection, "master"));
        await connection.OpenAsync();

        // SINGLE_USER WITH ROLLBACK IMMEDIATE, not a bare DROP.
        //
        // PostgreSQL had `DROP DATABASE ... WITH (FORCE)` for this; SQL Server has no such clause and
        // instead refuses outright while any session is connected. A previous test class's pooled
        // connections are exactly such sessions, so a bare DROP fails on the second suite to run and
        // on nothing before it — the worst possible distribution for a failure.
        //
        // The database is put back to MULTI_USER by being dropped, so there is nothing to restore.
        await using (var drop = new SqlCommand(
            $"""
             IF DB_ID(N'{name}') IS NOT NULL
             BEGIN
                 ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                 DROP DATABASE [{name}];
             END
             """,
            connection))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await using (var create = new SqlCommand($"CREATE DATABASE [{name}]", connection))
        {
            await create.ExecuteNonQueryAsync();
        }

        var target = WithDatabase(adminConnection, name);

        // The pool is keyed by connection string, and a dropped-and-recreated database leaves
        // pooled connections that look alive to the client and refer to a database that no longer
        // exists server-side. Without this the next checkout fails on a connection error rather
        // than anything to do with the test.
        using (var pooled = new SqlConnection(target))
        {
            SqlConnection.ClearPool(pooled);
        }

        return target;
    }
}
