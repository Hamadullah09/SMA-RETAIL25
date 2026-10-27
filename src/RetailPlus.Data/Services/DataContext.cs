using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace RetailPlus.Data.Services;

/// <summary>
/// Hands out a short-lived DbContext per operation, which is how the desktop app, the RFID hub and the services use
/// EF. The contexts come from a pool (reset and reused rather than built each time) and the connections from the
/// provider's connection pool, so a busy hub or many windows do not pay set-up costs on every query.
/// </summary>
public sealed class DataContext
{
    private readonly PooledDbContextFactory<RetailPlusDbContext> _pool;

    public DataContext(DatabaseConnection connection, StationIdentity? station = null)
    {
        Connection = connection;
        Station = station;
        var options = RetailPlusDb.Configure(new DbContextOptionsBuilder<RetailPlusDbContext>(), connection).Options;
        _pool = new PooledDbContextFactory<RetailPlusDbContext>(options, poolSize: 256);
    }

    public DatabaseConnection Connection { get; }

    /// <summary>The store and station this program works for; set by <see cref="SetupService.SeedAsync"/>.</summary>
    public StationIdentity? Station { get; set; }

    public RetailPlusDbContext Create()
    {
        var db = _pool.CreateDbContext();
        db.Station = Station;
        return db;
    }

    /// <summary>
    /// Applies pending migrations (creating the database if needed). A SQLite file is switched to write-ahead logging,
    /// so the RFID hub and the app can read while the other writes.
    /// </summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        if (Connection.Provider == DatabaseProvider.Sqlite && Path.GetDirectoryName(Connection.DatabaseName) is { Length: > 0 } folder)
            Directory.CreateDirectory(folder);
        await using var db = Create();
        await db.Database.MigrateAsync(cancellationToken);
        if (Connection.Provider == DatabaseProvider.Sqlite)
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
    }

    public string DatabaseName => Connection.DatabaseName;
}

/// <summary>Who is working where: the station, its location, and the logged-in user with their access level.</summary>
public sealed class SessionInfo
{
    public int StationNumber { get; set; } = 1;
    public Guid LocationId { get; set; }
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
