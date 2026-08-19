using FluentAssertions;
using Retail25.Application.Rfid.Services;
using Retail25.Application.UnitTests.Masters;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Terminals;
using Xunit;

namespace Retail25.Application.UnitTests.Rfid;

/// <summary>
/// Which station a tag read belongs to.
/// <para>
/// This is the architectural change under test. The old model bound a reader to one station, so a
/// four-antenna reader could only ever watch one till; routing on (reader, antenna) is what turns
/// one box into four independent stations, and the same rows scaled up turn 63 boxes into 252.
/// </para>
/// </summary>
public sealed class TagObservationRouterTests
{
    private static TagRead Read(int antenna, string epc = "E28011700000020A7A6B6AE1")
        => new(epc, antenna, -50, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed record Fixture(
        MastersTestHarness Harness,
        TagObservationRouter Router,
        long ReaderId,
        long[] Stations);

    private static async Task<Fixture> FourAntennaReaderAsync()
    {
        var harness = await MastersTestHarness.CreateAsync();

        var reader = RfidReader.Create(1, "RFID-001", "ABC123456").Value;
        harness.Db.RfidReaders.Add(reader);

        var stations = new List<Station>();

        for (var i = 1; i <= 4; i++)
        {
            var station = Station.Create(1, $"{i:000}", $"Checkout {i}").Value;
            stations.Add(station);
            harness.Db.Stations.Add(station);
        }

        await harness.Db.SaveChangesAsync();

        for (var i = 1; i <= 4; i++)
        {
            harness.Db.ReaderAntennaAssignments.Add(
                ReaderAntennaAssignment.Create(reader.Id, i, stations[i - 1].Id).Value);
        }

        await harness.Db.SaveChangesAsync();

        return new Fixture(
            harness,
            new TagObservationRouter(harness.Db),
            reader.Id,
            stations.Select(s => s.Id).ToArray());
    }

    /// <summary>One physical reader, four antennas, four different tills.</summary>
    [Fact]
    public async Task Four_antennas_on_one_reader_reach_four_different_stations()
    {
        var fixture = await FourAntennaReaderAsync();
        using var harness = fixture.Harness;

        var routed = await fixture.Router.RouteAsync(
            fixture.ReaderId,
            [
                Read(1),
                Read(2, "E28011700000020A7A6B6AE2"),
                Read(3, "E28011700000020A7A6B6AE3"),
                Read(4, "E28011700000020A7A6B6AE4"),
            ],
            default);

        routed.ByStation.Should().HaveCount(4);
        routed.ByStation.Keys.Should().BeEquivalentTo(fixture.Stations);
        routed.Unrouted.Should().BeEmpty();
    }

    /// <summary>
    /// The routing key is (reader, antenna), never the antenna alone.
    /// <para>
    /// Antenna 1 exists on every reader in the building. Routing on it by itself would send seven
    /// readers' first antennas to one till, which is the bug this test exists to prevent forever.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Antenna_one_on_a_second_reader_is_a_different_station()
    {
        var fixture = await FourAntennaReaderAsync();
        using var harness = fixture.Harness;

        var second = RfidReader.Create(1, "RFID-002", "XYZ999").Value;
        harness.Db.RfidReaders.Add(second);

        var otherStation = Station.Create(1, "005", "Checkout 5").Value;
        harness.Db.Stations.Add(otherStation);
        await harness.Db.SaveChangesAsync();

        harness.Db.ReaderAntennaAssignments.Add(
            ReaderAntennaAssignment.Create(second.Id, 1, otherStation.Id).Value);
        await harness.Db.SaveChangesAsync();

        var fromFirst = await fixture.Router.RouteAsync(fixture.ReaderId, [Read(1)], default);
        var fromSecond = await fixture.Router.RouteAsync(second.Id, [Read(1)], default);

        fromFirst.ByStation.Keys.Should().ContainSingle().Which.Should().Be(fixture.Stations[0]);
        fromSecond.ByStation.Keys.Should().ContainSingle().Which.Should().Be(otherStation.Id);
        otherStation.Id.Should().NotBe(fixture.Stations[0]);
    }

    /// <summary>
    /// An unassigned antenna is reported, never dropped. Without this the symptom is a till that
    /// simply never reacts, which looks exactly like a dead antenna or a bad cable.
    /// </summary>
    [Fact]
    public async Task An_antenna_with_no_station_is_reported_rather_than_discarded()
    {
        var fixture = await FourAntennaReaderAsync();
        using var harness = fixture.Harness;

        var routed = await fixture.Router.RouteAsync(
            fixture.ReaderId,
            [Read(7), Read(7, "E28011700000020A7A6B6AE9")],
            default);

        routed.ByStation.Should().BeEmpty();
        routed.Unrouted.Should().ContainSingle();
        routed.Unrouted[0].AntennaNumber.Should().Be(7);
        routed.Unrouted[0].TagCount.Should().Be(2);
        routed.Unrouted[0].Reason.Should().Be(UnroutedAntenna.NoAssignment);
    }

    /// <summary>
    /// Switched off for the afternoon and never configured are different situations, and an
    /// administrator looking at a dead till needs to be told which.
    /// </summary>
    [Fact]
    public async Task A_disabled_assignment_is_reported_distinctly_from_a_missing_one()
    {
        var fixture = await FourAntennaReaderAsync();
        using var harness = fixture.Harness;

        var assignment = harness.Db.ReaderAntennaAssignments.First(a => a.AntennaNumber == 2);
        assignment.SetEnabled(false);
        await harness.Db.SaveChangesAsync();

        var routed = await fixture.Router.RouteAsync(fixture.ReaderId, [Read(2)], default);

        routed.ByStation.Should().BeEmpty();
        routed.Unrouted.Should().ContainSingle()
            .Which.Reason.Should().Be(UnroutedAntenna.Disabled);
    }

    /// <summary>
    /// Reassignment is data, not code. Moving antenna 2 to another till must take effect without a
    /// release, which is what makes 252 stations administrable at all.
    /// </summary>
    [Fact]
    public async Task Reassigning_an_antenna_changes_where_its_reads_land()
    {
        var fixture = await FourAntennaReaderAsync();
        using var harness = fixture.Harness;

        var moved = Station.Create(1, "010", "Checkout 10").Value;
        harness.Db.Stations.Add(moved);
        await harness.Db.SaveChangesAsync();

        var assignment = harness.Db.ReaderAntennaAssignments.First(a => a.AntennaNumber == 2);
        assignment.ReassignTo(moved.Id);
        await harness.Db.SaveChangesAsync();

        var routed = await fixture.Router.RouteAsync(fixture.ReaderId, [Read(2)], default);

        routed.ByStation.Keys.Should().ContainSingle().Which.Should().Be(moved.Id);
        routed.ByStation.Keys.Should().NotContain(fixture.Stations[1]);
    }

    /// <summary>Two antennas watching one gate both feed it; reads accumulate rather than replace.</summary>
    [Fact]
    public async Task Two_antennas_pointing_at_one_station_both_feed_it()
    {
        var fixture = await FourAntennaReaderAsync();
        using var harness = fixture.Harness;

        var assignment = harness.Db.ReaderAntennaAssignments.First(a => a.AntennaNumber == 3);
        assignment.ReassignTo(fixture.Stations[0]);
        await harness.Db.SaveChangesAsync();

        var routed = await fixture.Router.RouteAsync(
            fixture.ReaderId,
            [Read(1), Read(3, "E28011700000020A7A6B6AE5")],
            default);

        routed.ByStation.Should().ContainKey(fixture.Stations[0]);
        routed.ByStation[fixture.Stations[0]].Should().HaveCount(2);
    }
}
