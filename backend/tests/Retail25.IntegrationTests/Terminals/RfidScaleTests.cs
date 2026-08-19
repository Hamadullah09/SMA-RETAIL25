using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Rfid.Services;
using Retail25.Application.Terminals;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Terminals;
using Retail25.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace Retail25.IntegrationTests.Terminals;

/// <summary>
/// The estate at full size: 63 readers, 4 antennas each, 252 stations.
/// <para>
/// The claim the whole refactor rests on is that scale arrives as rows rather than as code — that
/// 4, 28 and 252 stations run the same routing with a bigger table. This is where that claim is
/// either true or found out, against a real SQL Server rather than an in-memory provider, because
/// what would break at this size is a query plan and not a data structure.
/// </para>
/// <para>
/// The thresholds are deliberately loose. They exist to catch a change that turns a keyed lookup
/// into a scan of every assignment in the shop — an order-of-magnitude regression — not to police
/// milliseconds on whatever machine happens to run the suite.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class RfidScaleTests
{
    private const int Readers = 63;
    private const int AntennasPerReader = 4;
    private const int Stations = Readers * AntennasPerReader;

    private readonly CommerceApiFixture _api;
    private readonly ITestOutputHelper _output;

    public RfidScaleTests(CommerceApiFixture api, ITestOutputHelper output)
    {
        _api = api;
        _output = output;
    }

    private sealed record Estate(long DeviceId, IReadOnlyList<long> ReaderIds, IReadOnlyList<long> StationIds);

    /// <summary>
    /// Builds the estate once per test, under a location of its own.
    /// <para>
    /// Its own location because the fixture's database is shared: a test that counted every station
    /// in the shop would be measuring whatever else the suite had created, and would pass or fail
    /// depending on the order tests ran in.
    /// </para>
    /// </summary>
    private static async Task<Estate> BuildAsync(ApplicationDbContext db, long locationId)
    {
        var device = Device.Create(locationId, $"PC-SCALE-{locationId}").Value;
        device.LastHeartbeat = DateTimeOffset.UtcNow;
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var readers = new List<RfidReader>(Readers);

        for (var r = 1; r <= Readers; r++)
        {
            var reader = RfidReader.Create(locationId, $"RFID-{r:000}", $"SN-{locationId}-{r:000}").Value;
            reader.DeviceId = device.Id;
            reader.LastSeen = DateTimeOffset.UtcNow;
            reader.MoveTo("192.168.0.50", 4001);
            readers.Add(reader);
        }

        db.RfidReaders.AddRange(readers);
        await db.SaveChangesAsync();

        var stations = new List<Station>(Stations);

        for (var s = 1; s <= Stations; s++)
        {
            // Three digits is the legacy station code width, so past 999 this would collide. 252 is
            // the size this estate is specified at; a chain outgrowing the code width is a different
            // conversation and would be a schema change rather than a routing one.
            stations.Add(Station.Create(locationId, $"{s:000}", $"Checkout {s}").Value);
        }

        db.Stations.AddRange(stations);
        await db.SaveChangesAsync();

        var assignments = new List<ReaderAntennaAssignment>(Stations);
        var index = 0;

        foreach (var reader in readers)
        {
            for (var antenna = 1; antenna <= AntennasPerReader; antenna++)
            {
                assignments.Add(ReaderAntennaAssignment.Create(reader.Id, antenna, stations[index++].Id).Value);
            }
        }

        db.ReaderAntennaAssignments.AddRange(assignments);
        await db.SaveChangesAsync();

        return new Estate(
            device.Id,
            readers.Select(r => r.Id).ToList(),
            stations.Select(s => s.Id).ToList());
    }

    private static long NextLocationId() => Random.Shared.NextInt64(900_000, 999_999);

    private static TagRead Read(int antenna, int index)
        => new(
            $"E28011700000020A7A6B{index:X4}",
            antenna,
            -50,
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    /// <summary>
    /// 252 assignments exist, and one reader's batch still resolves to its own four stations.
    /// <para>
    /// The failure this guards against is routing that works on a small table by accident — matching
    /// on antenna number alone, say, which is correct with one reader and sends 63 readers' antenna 1
    /// to the same till.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_hundred_and_fifty_two_assignments_still_route_one_reader_to_its_own_four_stations()
    {
        using var scope = _api.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var locationId = NextLocationId();
        var estate = await BuildAsync(db, locationId);

        var router = new TagObservationRouter(db);

        // A reader from the middle of the estate rather than the first, so an off-by-one in the
        // assignment loop is visible.
        var readerId = estate.ReaderIds[31];

        var stopwatch = Stopwatch.StartNew();

        var routed = await router.RouteAsync(
            readerId,
            [Read(1, 1), Read(2, 2), Read(3, 3), Read(4, 4)],
            default);

        stopwatch.Stop();

        _output.WriteLine($"Routing one batch against {Stations} assignments took {stopwatch.ElapsedMilliseconds} ms");

        routed.ByStation.Should().HaveCount(4, "one reader's four antennas are four separate tills");
        routed.Unrouted.Should().BeEmpty();

        // The four stations are this reader's own, not any other reader's.
        var expected = estate.StationIds.Skip(31 * AntennasPerReader).Take(AntennasPerReader);
        routed.ByStation.Keys.Should().BeEquivalentTo(expected);

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500, "routing reads one reader's rows, not the estate");
    }

    /// <summary>
    /// Every reader in the estate routes to a distinct set of stations.
    /// <para>
    /// The strong form of the previous test: 63 readers, 252 antennas, and no two antennas anywhere
    /// resolving to the same till.
    /// </para>
    /// </summary>
    [Fact]
    public async Task No_two_antennas_in_the_estate_resolve_to_the_same_station()
    {
        using var scope = _api.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var locationId = NextLocationId();
        var estate = await BuildAsync(db, locationId);

        var router = new TagObservationRouter(db);
        var seen = new List<long>(Stations);

        var stopwatch = Stopwatch.StartNew();

        foreach (var readerId in estate.ReaderIds)
        {
            var routed = await router.RouteAsync(
                readerId,
                Enumerable.Range(1, AntennasPerReader).Select(a => Read(a, a)).ToList(),
                default);

            seen.AddRange(routed.ByStation.Keys);
        }

        stopwatch.Stop();

        _output.WriteLine($"Routing all {Readers} readers took {stopwatch.ElapsedMilliseconds} ms");

        seen.Should().HaveCount(Stations);
        seen.Should().OnlyHaveUniqueItems("an antenna feeds exactly one till");
    }

    /// <summary>
    /// The dashboard reads the whole estate in one answer, not one query per antenna.
    /// <para>
    /// This is the query most likely to become a fan-out by accident, because it joins three tables
    /// and is the one a person refreshes every few seconds.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_dashboard_reports_all_two_hundred_and_fifty_two_antennas()
    {
        using var scope = _api.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<Retail25.Application.Abstractions.IDateTime>();

        var locationId = NextLocationId();
        await BuildAsync(db, locationId);

        var stopwatch = Stopwatch.StartNew();

        var dashboard = await new RfidDashboardHandler(db, clock)
            .Handle(new GetRfidDashboardQuery(locationId), default);

        stopwatch.Stop();

        _output.WriteLine($"Dashboard over {Stations} antennas took {stopwatch.ElapsedMilliseconds} ms");

        dashboard.IsSuccess.Should().BeTrue();
        dashboard.Value.Summary.Total.Should().Be(Stations);
        dashboard.Value.Summary.Operational.Should().Be(Stations, "the estate was built healthy");

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3_000);
    }

    /// <summary>
    /// One machine driving 63 readers gets its whole configuration in one round trip.
    /// <para>
    /// The alternative — a call per station — is what makes 252 stations an estate polling itself to
    /// a standstill, so the shape of this answer is the scaling property rather than an optimisation.
    /// </para>
    /// </summary>
    [Fact]
    public async Task One_machine_receives_all_sixty_three_readers_in_a_single_answer()
    {
        using var scope = _api.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var locationId = NextLocationId();
        await BuildAsync(db, locationId);

        var stopwatch = Stopwatch.StartNew();

        var configuration = await new DeviceConfigurationHandler(db)
            .Handle(new GetDeviceConfigurationQuery(locationId, $"PC-SCALE-{locationId}"), default);

        stopwatch.Stop();

        _output.WriteLine($"Configuration for {Readers} readers took {stopwatch.ElapsedMilliseconds} ms");

        configuration.IsSuccess.Should().BeTrue();
        configuration.Value.Readers.Should().HaveCount(Readers);
        configuration.Value.Readers.Sum(r => r.Antennas.Count).Should().Be(Stations);

        configuration.Value.Revision.Should().NotBeNullOrEmpty(
            "the agent compares one string to decide whether anything moved");

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3_000);
    }
}
