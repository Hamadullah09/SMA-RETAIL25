using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Terminals;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Terminals;
using Xunit;

namespace Retail25.Application.UnitTests.Rfid;

/// <summary>
/// Moving a live shop onto the new model without changing what any till does.
/// <para>
/// The migration that matters is not the schema — that is additive and harmless — but this one:
/// turning each existing reader-to-station binding into an antenna-1 assignment, so reads keep
/// arriving where they always did while now travelling by the new route.
/// </para>
/// </summary>
public sealed class TopologyBackfillTests
{
    private static async Task<(MastersTestHarness Harness, long StationId)> ShopWithOneReaderAsync()
    {
        var harness = await MastersTestHarness.CreateAsync();

        var station = Station.Create(1, "001", "Front counter").Value;
        harness.Db.Stations.Add(station);
        await harness.Db.SaveChangesAsync();

        harness.Db.ReaderProfiles.Add(new ReaderProfile
        {
            LocationId = 1,
            Name = "Front Door",
            Host = "192.168.0.178",
            Port = 4001,
            Protocol = ReaderProtocol.UhfSerial,
            StationId = station.Id,
        });

        await harness.Db.SaveChangesAsync();

        return (harness, station.Id);
    }

    [Fact]
    public async Task An_existing_reader_becomes_a_reader_row_and_an_antenna_one_assignment()
    {
        var (harness, stationId) = await ShopWithOneReaderAsync();
        using var _ = harness;

        var result = await new BackfillRfidTopologyHandler(harness.Db)
            .Handle(new BackfillRfidTopologyCommand(1), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.ReadersCreated.Should().Be(1);
        result.Value.AssignmentsCreated.Should().Be(1);

        var reader = await harness.Db.RfidReaders.SingleAsync();
        reader.Host.Should().Be("192.168.0.178", "the address carries over as a mutable property");
        reader.Protocol.Should().Be(ReaderTransportProtocol.UhfSerial);

        var assignment = await harness.Db.ReaderAntennaAssignments.SingleAsync();
        assignment.AntennaNumber.Should().Be(1, "the old model meant one reader, one station");
        assignment.StationId.Should().Be(stationId, "reads must keep arriving where they always did");
    }

    /// <summary>
    /// Pressing it twice is what an administrator does when a page looks slow, so the second run has
    /// to be harmless.
    /// </summary>
    [Fact]
    public async Task Running_it_again_creates_nothing()
    {
        var (harness, _) = await ShopWithOneReaderAsync();
        using var _h = harness;

        var handler = new BackfillRfidTopologyHandler(harness.Db);

        await handler.Handle(new BackfillRfidTopologyCommand(1), default);
        var second = await handler.Handle(new BackfillRfidTopologyCommand(1), default);

        second.Value.ReadersCreated.Should().Be(0);
        second.Value.Skipped.Should().ContainSingle();

        (await harness.Db.RfidReaders.CountAsync()).Should().Be(1);
        (await harness.Db.ReaderAntennaAssignments.CountAsync()).Should().Be(1);
    }

    /// <summary>A dry run reports and writes nothing, which is how this gets looked at first.</summary>
    [Fact]
    public async Task A_dry_run_reports_without_writing()
    {
        var (harness, _) = await ShopWithOneReaderAsync();
        using var _h = harness;

        var result = await new BackfillRfidTopologyHandler(harness.Db)
            .Handle(new BackfillRfidTopologyCommand(1, DryRun: true), default);

        result.Value.ReadersCreated.Should().Be(1);
        (await harness.Db.RfidReaders.CountAsync()).Should().Be(0, "a dry run that wrote would not be one");
    }

    /// <summary>
    /// A profile bound to no station has nowhere to send reads. It is counted, never invented into a
    /// station that does not exist.
    /// </summary>
    [Fact]
    public async Task A_profile_with_no_station_is_counted_rather_than_guessed_at()
    {
        var harness = await MastersTestHarness.CreateAsync();
        using var _ = harness;

        harness.Db.ReaderProfiles.Add(new ReaderProfile
        {
            LocationId = 1,
            Name = "Unbound",
            Host = "10.0.0.9",
            Port = 5084,
            StationId = null,
        });

        await harness.Db.SaveChangesAsync();

        var result = await new BackfillRfidTopologyHandler(harness.Db)
            .Handle(new BackfillRfidTopologyCommand(1), default);

        result.Value.ProfilesWithoutStation.Should().Be(1);
        result.Value.ReadersCreated.Should().Be(0);
        (await harness.Db.ReaderAntennaAssignments.CountAsync()).Should().Be(0);
    }
}
