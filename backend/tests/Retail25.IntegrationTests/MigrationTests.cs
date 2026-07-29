using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// Phase 0's exit criterion, stated as a test: <i>a migration applies to a clean database</i>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrationTests
{
    private readonly PostgresFixture _postgres;

    public MigrationTests(PostgresFixture postgres) => _postgres = postgres;

    [RequiresDockerFact]
    public async Task The_migration_applies_to_a_clean_database()
    {
        var connection = await _postgres.CreateEmptyDatabaseAsync("migration_clean");

        await using var db = _postgres.CreateContext(connection);

        await db.Database.MigrateAsync();

        var applied = await db.Database.GetAppliedMigrationsAsync();

        // Not just "no exception": the history table has to exist and name what ran, because that
        // row is the only thing that makes the *next* migration possible.
        applied.Should().NotBeEmpty();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [RequiresDockerFact]
    public async Task Applying_the_migration_twice_is_a_no_op()
    {
        var connection = await _postgres.CreateEmptyDatabaseAsync("migration_twice");

        await using var db = _postgres.CreateContext(connection);

        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();

        // A container that restarts, or two API replicas booting together, both hit this path.
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [RequiresDockerFact]
    public void The_migrations_match_the_model()
    {
        using var db = _postgres.CreateContext();

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot;
        snapshot.Should().NotBeNull("the model snapshot is what the next migration is generated against");

        // The snapshot is a design-time model and has to be finalised before it can be compared with
        // the live one, or every table reads as a difference.
        var snapshotModel = db.GetService<IModelRuntimeInitializer>()
            .Initialize(((IMutableModel)snapshot!.Model).FinalizeModel(), designTime: true, validationLogger: null);

        var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(
            snapshotModel.GetRelationalModel(),
            db.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        // The failure this catches: someone edits an entity, the unit tests still pass because the
        // in-memory provider builds its model from code, and the schema silently drifts from the
        // migration until the first deployment against a real database fails.
        differences.Should().BeEmpty("an entity changed without a migration — run `dotnet ef migrations add`");
    }

    [RequiresDockerFact]
    public async Task The_seeder_can_run_twice_without_duplicating_a_store()
    {
        var connection = await _postgres.CreateEmptyDatabaseAsync("migration_seed");

        await using var db = _postgres.CreateContext(connection);
        await db.Database.MigrateAsync();

        var clock = Substitute.For<IDateTime>();
        clock.Now.Returns(new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero));

        var seeder = new DatabaseSeeder(db, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseSeeder>.Instance);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        // The API seeds on every boot. A seeder that is not idempotent gives a restarted container a
        // second location, a second set of tenders, and a till that cannot decide which to use.
        (await db.Locations.CountAsync()).Should().Be(1);
        (await db.Currencies.CountAsync(c => c.IsBaseCurrency)).Should().Be(1);
        (await db.PricingRuleSettings.CountAsync()).Should().Be(Retail25.Domain.Configuration.PricingRuleKeys.DefaultOrder.Count);
        (await db.NumberSequences.CountAsync()).Should().Be(Enum.GetValues<Retail25.Domain.Configuration.SequenceKind>().Length);
    }
}
