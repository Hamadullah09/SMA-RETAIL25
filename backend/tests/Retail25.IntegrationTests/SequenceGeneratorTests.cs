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
[Collection(SqlServerCollection.Name)]
public sealed class SequenceGeneratorTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sqlServer;
    private ApplicationDbContextScope _scope = null!;

    public SequenceGeneratorTests(SqlServerFixture sqlServer) => _sqlServer = sqlServer;

    public async Task InitializeAsync() => _scope = await ApplicationDbContextScope.CreateAsync(_sqlServer, "sequences");

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
            await using var db = _sqlServer.CreateContext(_scope.Db.Database.GetConnectionString());
            return await new SequenceGenerator(db).NextAsync(SequenceKind.Transaction, _scope.LocationId);
        }));

        // Two tills completing a sale in the same millisecond must not produce the same transaction
        // number. This is the defect the legacy "next number" setting had.
        issued.Should().OnlyHaveUniqueItems();
        issued.Should().HaveCount(25);
    }

    /// <summary>
    /// The same guarantee on a sequence that does not exist yet.
    /// <para>
    /// The test above warms the sequence on one connection before the parallel callers start, and
    /// that warm-up is what hid this: the create is a check followed by a create, so cold callers
    /// race, and the loser is told <c>there is already an object named 'seq_transaction_2'</c>. On
    /// the sale path that arrives as an exception inside the transaction and refuses the sale —
    /// meaning two tills ringing the first sale of a new shop at the same moment, which is the one
    /// morning it is guaranteed to be cold.
    /// </para>
    /// <para>
    /// <b>This test is a probabilistic detector, not a proof.</b> Against the unguarded create it
    /// failed six times in nine; the contended window is short and SQL Server's schema lock
    /// serialises most callers cleanly, so an interleaving where nobody loses is common. Doubling to
    /// twenty-four tills did not improve that and only made it slower. Against the guarded one it
    /// passed six times in six. It is kept because it does catch the defect and is stable when the
    /// code is right — the guarantee itself is carried by <see cref="SequenceGenerator"/> handling
    /// 2714 in the database, and this is what would notice if that were removed.
    /// </para>
    /// </summary>
    [RequiresIsolatedDatabaseFact]
    public async Task The_first_number_of_a_new_location_survives_two_tills_asking_at_once()
    {
        var second = Location.Create("Cold Store", "CLD", "CAD", "UTC", TimeOnly.MinValue).Value;
        _scope.Db.Locations.Add(second);
        await _scope.Db.SaveChangesAsync();

        // Deliberately not warmed: every one of these is a first use.
        //
        // Held at a barrier with their connections already open, because without one this test
        // passes against the defect. Opening a connection takes long enough that the callers arrive
        // in single file, each finding the sequence the previous one had already created — so the
        // window between the check and the create is never actually contended, and the test proves
        // only that a sequence can be created twelve times in a row.
        const int Tills = 12;
        var connectionString = _scope.Db.Database.GetConnectionString();
        var ready = new CountdownEvent(Tills);
        using var go = new ManualResetEventSlim(false);

        // A thread each rather than pooled work items: with twelve queued on a pool that grows on
        // demand, the later ones start after the earlier ones have finished and the barrier releases
        // into single file anyway.
        var tills = Enumerable.Range(0, Tills).Select(_ => Task.Factory.StartNew(
            async () =>
            {
                await using var db = _sqlServer.CreateContext(connectionString);
                await db.Database.OpenConnectionAsync();

                ready.Signal();
                go.Wait();

                return await new SequenceGenerator(db).NextAsync(SequenceKind.Transaction, second.Id);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap()).ToArray();

        ready.Wait();
        go.Set();

        var issued = await Task.WhenAll(tills);

        issued.Should().HaveCount(Tills, "no till may be refused because another created the sequence first");
        issued.Should().OnlyHaveUniqueItems();
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
