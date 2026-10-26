using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace RetailPlus.Data.Services;

/// <summary>
/// Hands out a short-lived DbContext per operation, which is how the desktop app, the RFID hub and the services use
/// EF. The contexts come from a pool (reset and reused rather than built each time) and the SQL connections from
/// SqlClient's connection pool, so a busy hub or many stations do not pay set-up costs on every query.
/// </summary>
public sealed class DataContext
{
    private readonly PooledDbContextFactory<RetailPlusDbContext> _pool;

    public DataContext(string connectionString)
    {
        ConnectionString = connectionString;
        var options = RetailPlusDb.Configure(new DbContextOptionsBuilder<RetailPlusDbContext>(), connectionString).Options;
        _pool = new PooledDbContextFactory<RetailPlusDbContext>(options, poolSize: 256);
    }

    public string ConnectionString { get; }

    public RetailPlusDbContext Create() => _pool.CreateDbContext();

    /// <summary>Applies pending migrations (creating the database if needed).</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = Create();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public string DatabaseName
    {
        get
        {
            using var db = Create();
            return db.Database.GetDbConnection().Database;
        }
    }
}

/// <summary>Who is working where: the station, its location, and the logged-in user with their access level.</summary>
public sealed class SessionInfo
{
    public int StationNumber { get; set; } = 1;
    public int LocationId { get; set; }
    public string LocationCode { get; set; } = "";
    public string UserName { get; set; } = "Admin";
    /// <summary>0 trainee, 1 sales only, 2 add/change, 3 supervisor, 4 administrator.</summary>
    public int AccessLevel { get; set; } = 4;
    public bool IsTrainee => AccessLevel == 0;
}

/// <summary>A save was rejected because someone else changed the record first.</summary>
public sealed class ConcurrencyConflictException(string message) : Exception(message);

/// <summary>A business rule stopped an operation; the message is meant for the user.</summary>
public sealed class BusinessRuleException(string message) : Exception(message);
