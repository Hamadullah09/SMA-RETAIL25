using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RetailPlus.Core.Model;

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

    /// <summary>
    /// Who is working: the audit log names this user for changes saved through the contexts handed out, and services
    /// that record who did something without being given a session use it. Null in tools that run without a user.
    /// </summary>
    public SessionInfo? CurrentUser { get; set; }

    public RetailPlusDbContext Create()
    {
        var db = _pool.CreateDbContext();
        // Pooled contexts keep their properties between uses, so every one is set again.
        db.Station = Station;
        db.ActingUserId = CurrentUser?.UserId;
        db.ActingUserName = CurrentUser?.UserName;
        db.SuppressAudit = false;
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
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = "Admin";
    /// <summary>The name of the user's role (empty until someone signs in).</summary>
    public string RoleName { get; set; } = "";
    /// <summary>0 trainee, 1 sales only, 2 add/change, 3 supervisor, 4 administrator.</summary>
    public int AccessLevel { get; set; } = 4;
    public bool IsTrainee => AccessLevel == 0;
    /// <summary>What the user's role allows; everything until someone signs in (a database without users).</summary>
    public IReadOnlySet<string> Permissions { get; set; } = RetailPlus.Core.Rules.Permissions.All.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    public bool Can(string permission) => Permissions.Contains(permission);

    /// <summary>Works as the given user from now on.</summary>
    public void SignIn(UserAccount user)
    {
        UserId = user.Id;
        UserName = user.UserName;
        RoleName = user.Role?.Name ?? "";
        AccessLevel = user.AccessLevel;
        Permissions = SetupService.PermissionsOf(user);
    }

    /// <summary>The same station and location, worked by another user (a supervisor approving at this till).</summary>
    public SessionInfo As(UserAccount user)
    {
        var other = new SessionInfo { StationNumber = StationNumber, LocationId = LocationId, LocationCode = LocationCode };
        other.SignIn(user);
        return other;
    }
}

/// <summary>A save was rejected because someone else changed the record first.</summary>
public sealed class ConcurrencyConflictException(string message) : Exception(message);

/// <summary>A business rule stopped an operation; the message is meant for the user.</summary>
public sealed class BusinessRuleException(string message) : Exception(message);
