using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Retail25.Domain.Configuration;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// Document numbering against a real PostgreSQL sequence.
/// <para>
/// None of this is testable in memory: <see cref="SequenceGenerator"/> issues raw
/// <c>CREATE SEQUENCE</c> and <c>nextval</c>, and the whole point of using a sequence is behaviour the
/// database provides — monotonic under concurrency, unaffected by rollback. The legacy system's
/// per-workstation counter did collide, and this is the test that says ours does not.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SequenceGeneratorTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private ApplicationDbContextScope _scope = null!;

    public SequenceGeneratorTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync() => _scope = await ApplicationDbContextScope.CreateAsync(_postgres, "sequences");

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return Task.CompletedTask;
    }

    [RequiresIsolatedDatabaseFact]
    public async Task A_sequence_starts_from_the_administered_next_number()
    {
        // What a migration does: write the legacy counter into the row before anything issues a number.
        var row = await _scope.Db.NumberSequences.FirstAsync(s => s.Kind == SequenceKind.Customer);
        row.SetNext(4182).IsSuccess.Should().BeTrue();
        await _scope.Db.SaveChangesAsync();

        var generator = new SequenceGenerator(_scope.Db);

        // Customer 4,182 has to be followed by 4,183, because staff and paper records refer to
        // those numbers.
        (await generator.NextAsync(SequenceKind.Customer, _scope.LocationId)).Should().Be(4182);
        (await generator.NextAsync(SequenceKind.Customer, _scope.LocationId)).Should().Be(4183);
    }

    [RequiresIsolatedDatabaseFact]
    public async Task Numbers_are_never_issued_twice_under_concurrency()
    {
        var generator = new SequenceGenerator(_scope.Db);

        // Warm the sequence on one connection first; the parallel callers below each need their own.
        await generator.NextAsync(SequenceKind.Transaction, _scope.LocationId);

        var issued = await Task.WhenAll(Enumerable.Range(0, 25).Select(async _ =>
        {
            await using var db = _postgres.CreateContext(_scope.Db.Database.GetConnectionString());
            return await new SequenceGenerator(db).NextAsync(SequenceKind.Transaction, _scope.LocationId);
        }));

        // Two tills completing a sale in the same millisecond must not produce the same transaction
        // number. This is the defect the legacy "next number" setting had.
        issued.Should().OnlyHaveUniqueItems();
        issued.Should().HaveCount(25);
    }

    [RequiresIsolatedDatabaseFact]
    public async Task Repointing_a_counter_restarts_the_live_sequence()
    {
        var generator = new SequenceGenerator(_scope.Db);

        await generator.NextAsync(SequenceKind.Invoice, _scope.LocationId);
        await generator.NextAsync(SequenceKind.Invoice, _scope.LocationId);

        await generator.RestartAsync(SequenceKind.Invoice, _scope.LocationId, 9000);

        // Saving the administered row alone changes nothing that issues numbers — the sequence was
        // created from that row the first time it was used and never reads it again.
        (await generator.NextAsync(SequenceKind.Invoice, _scope.LocationId)).Should().Be(9000);
    }

    [RequiresIsolatedDatabaseFact]
    public async Task Each_location_numbers_independently()
    {
        var second = Location.Create("Second Store", "SEC", "CAD", "UTC", TimeOnly.MinValue).Value;
        _scope.Db.Locations.Add(second);
        await _scope.Db.SaveChangesAsync();

        var generator = new SequenceGenerator(_scope.Db);

        await generator.NextAsync(SequenceKind.Transaction, _scope.LocationId);
        await generator.NextAsync(SequenceKind.Transaction, _scope.LocationId);

        // A second shop's numbering is its own; sharing one would make every printed number ambiguous
        // across the two.
        (await generator.NextAsync(SequenceKind.Transaction, second.Id)).Should().Be(1);
    }
}
