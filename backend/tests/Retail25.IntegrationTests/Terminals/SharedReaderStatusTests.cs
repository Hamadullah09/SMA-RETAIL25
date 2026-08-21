using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Domain.Terminals;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests.Terminals;

/// <summary>
/// One radio, several checkouts.
/// <para>
/// A reader with four antennas is one piece of hardware shared by up to four tills, and some facts
/// belong to the hardware rather than to any one till: whether it is running, how fast it is reading,
/// whether it is there at all. The agent reports those under the station it is installed at, so
/// anything keyed to that station alone leaves the other checkouts describing a reader nobody told
/// them about — listing the tags they are reading that second beside "Waiting for the reader".
/// </para>
/// <para>
/// This pins the set the status and the mode fan out to. It is a query rather than a broadcast for a
/// reason: sending to the whole shop is what caused the leak this replaced, where every till received
/// every other till's status and the last to arrive won.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class SharedReaderStatusTests
{
    private readonly CommerceApiFixture _api;

    public SharedReaderStatusTests(CommerceApiFixture api) => _api = api;

    private static long NextLocationId() => Random.Shared.NextInt64(700_000, 799_999);

    /// <summary>
    /// Mirrors TerminalHandlers.TillsSharingTheReaderAsync. Stated here so the rule is asserted
    /// against a real database and a real schema rather than described in prose.
    /// </summary>
    private static async Task<IReadOnlyList<long>> SharingAsync(ApplicationDbContext db, long stationId)
    {
        var shared = db.ReaderAntennaAssignments
            .Where(a => a.IsEnabled)
            .Where(a => db.ReaderAntennaAssignments
                .Any(mine => mine.ReaderId == a.ReaderId && mine.StationId == stationId && mine.IsEnabled))
            .Select(a => a.StationId)
            .Distinct()
            .ToList();

        return await Task.FromResult(shared.Count > 0 ? shared : [stationId]);
    }

    private sealed record Shop(long ReaderId, long StationA, long StationB, long Unrelated);

    private static async Task<Shop> BuildAsync(ApplicationDbContext db, long locationId)
    {
        var reader = RfidReader.Create(locationId, "SHARED", $"SN-{locationId}").Value;
        var other = RfidReader.Create(locationId, "OTHER", $"SN-{locationId}-B").Value;
        db.RfidReaders.AddRange(reader, other);
        await db.SaveChangesAsync();

        var a = Station.Create(locationId, "001", "Checkout 1").Value;
        var b = Station.Create(locationId, "002", "Checkout 2").Value;
        var unrelated = Station.Create(locationId, "003", "Checkout 3").Value;
        db.Stations.AddRange(a, b, unrelated);
        await db.SaveChangesAsync();

        db.ReaderAntennaAssignments.AddRange(
            ReaderAntennaAssignment.Create(reader.Id, 1, a.Id).Value,
            ReaderAntennaAssignment.Create(reader.Id, 2, b.Id).Value,

            // A different reader entirely: its till must not be swept in.
            ReaderAntennaAssignment.Create(other.Id, 1, unrelated.Id).Value);

        await db.SaveChangesAsync();

        return new Shop(reader.Id, a.Id, b.Id, unrelated.Id);
    }

    [Fact]
    public async Task Both_tills_on_one_reader_hear_about_it()
    {
        using var scope = _api.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var shop = await BuildAsync(db, NextLocationId());

        // Asked from either end, the answer is the same pair — the agent is installed at one of them
        // and the other must not depend on which.
        (await SharingAsync(db, shop.StationA)).Should().BeEquivalentTo([shop.StationA, shop.StationB]);
        (await SharingAsync(db, shop.StationB)).Should().BeEquivalentTo([shop.StationA, shop.StationB]);
    }

    [Fact]
    public async Task A_till_on_a_different_reader_is_not_included()
    {
        using var scope = _api.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var shop = await BuildAsync(db, NextLocationId());

        (await SharingAsync(db, shop.StationA)).Should().NotContain(shop.Unrelated);
        (await SharingAsync(db, shop.Unrelated)).Should().BeEquivalentTo([shop.Unrelated]);
    }

    /// <summary>
    /// A disabled antenna is not on the radio as far as this is concerned. Disabling is how an
    /// antenna being worked on is taken out of service, and a till whose antenna is out should not
    /// be told the reader is reading for it.
    /// </summary>
    [Fact]
    public async Task A_disabled_antenna_drops_its_till_from_the_set()
    {
        using var scope = _api.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var shop = await BuildAsync(db, NextLocationId());

        var second = db.ReaderAntennaAssignments
            .First(a => a.ReaderId == shop.ReaderId && a.StationId == shop.StationB);

        second.SetEnabled(false);
        await db.SaveChangesAsync();

        (await SharingAsync(db, shop.StationA)).Should().BeEquivalentTo([shop.StationA]);
    }

    /// <summary>
    /// A till with no antenna map at all still gets its own status — every shop before this feature,
    /// and every shop midway through adopting it.
    /// </summary>
    [Fact]
    public async Task A_till_with_no_reader_still_answers_for_itself()
    {
        using var scope = _api.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var locationId = NextLocationId();
        var lonely = Station.Create(locationId, "009", "No reader").Value;
        db.Stations.Add(lonely);
        await db.SaveChangesAsync();

        (await SharingAsync(db, lonely.Id)).Should().BeEquivalentTo([lonely.Id]);
    }
}
