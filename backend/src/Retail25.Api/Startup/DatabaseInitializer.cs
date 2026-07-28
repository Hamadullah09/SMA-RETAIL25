using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Retail25.Infrastructure.Persistence;
using Retail25.Infrastructure.Persistence.Seeding;

namespace Retail25.Api.Startup;

/// <summary>
/// Brings the database up to date at start-up.
/// <para>
/// Migrations are applied only when <c>Database:AutoMigrate</c> is set, which is the developer
/// convenience. In production, schema changes are a deliberate deployment step run before the new
/// code starts — an application that migrates itself on boot will happily half-migrate a database
/// during a rolling restart.
/// </para>
/// <para>
/// Seeding is separate and safe to repeat: it only ever fills gaps, so a store whose settings have
/// been edited is never reset by a restart.
/// </para>
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));

        var database = services.GetRequiredService<ApplicationDbContext>();

        if (app.Configuration.GetValue("Database:AutoMigrate", false))
        {
            logger.LogInformation("Applying database migrations.");
            await database.Database.MigrateAsync();
        }
        else if (!await database.Database.CanConnectAsync())
        {
            logger.LogWarning(
                "Cannot reach the database. The API will start, but every request that touches data will fail. " +
                "Check ConnectionStrings:DefaultConnection.");
            return;
        }

        var seedOptions = services.GetRequiredService<IOptions<SeedOptions>>().Value;
        if (!seedOptions.Enabled)
        {
            return;
        }

        // Seeding needs the schema to exist. If migrations have not been applied yet, say so
        // plainly rather than failing inside the seeder with a missing-table error.
        var pending = await database.Database.GetPendingMigrationsAsync();
        if (pending.Any())
        {
            logger.LogWarning(
                "Skipping seed: {Count} migration(s) have not been applied. Run 'dotnet ef database update' " +
                "or set Database:AutoMigrate to true.", pending.Count());
            return;
        }

        var seeder = services.GetRequiredService<ReferenceDataSeeder>();
        await seeder.SeedAsync(seedOptions);
    }
}
