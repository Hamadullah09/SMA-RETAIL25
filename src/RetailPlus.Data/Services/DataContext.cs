using Microsoft.EntityFrameworkCore;

namespace RetailPlus.Data.Services;

/// <summary>Creates a short-lived DbContext per operation, which is how the desktop app and the services use EF.</summary>
public sealed class DataContext(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    public RetailPlusDbContext Create() => RetailPlusDb.CreateContext(ConnectionString);

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
