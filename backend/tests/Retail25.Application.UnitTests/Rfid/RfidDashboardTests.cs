using FluentAssertions;
using Retail25.Application.Abstractions;
using Retail25.Application.Terminals;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Terminals;
using Xunit;

namespace Retail25.Application.UnitTests.Rfid;

/// <summary>
/// Where to walk when a till stops reading.
/// <para>
/// One "Connected" light cannot answer that. The agent being reachable says nothing about the
/// reader, and the reader answering says nothing about whether anybody assigned its antenna to a
/// till. These pin that the four layers are reported separately and in the order somebody would
/// check them — a wrong answer here sends an engineer to the wrong end of the shop.
/// </para>
/// </summary>
public sealed class RfidDashboardTests
{
    private sealed class FixedClock : IDateTime
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        public DateOnly Today() => DateOnly.FromDateTime(Now.UtcDateTime);
    }

    private sealed record Shop(MastersTestHarness Harness, FixedClock Clock, RfidReader Reader, Device Device, long StationId);

    private static async Task<Shop> ShopAsync(bool assignAntennaOne = true)
    {
        var harness = await MastersTestHarness.CreateAsync();
        var clock = new FixedClock();

        var device = Device.Create(1, "PC-001").Value;
        device.LastHeartbeat = clock.Now;
        harness.Db.Devices.Add(device);

        var station = Station.Create(1, "001", "Checkout 1").Value;
        harness.Db.Stations.Add(station);
        await harness.Db.SaveChangesAsync();

        var reader = RfidReader.Create(1, "RFID-001", "SN-1").Value;
        reader.DeviceId = device.Id;
        reader.LastSeen = clock.Now;
        harness.Db.RfidReaders.Add(reader);
        await harness.Db.SaveChangesAsync();

        if (assignAntennaOne)
        {
            harness.Db.ReaderAntennaAssignments.Add(
                ReaderAntennaAssignment.Create(reader.Id, 1, station.Id).Value);

            await harness.Db.SaveChangesAsync();
        }

        return new Shop(harness, clock, reader, device, station.Id);
    }

    private static async Task<RfidDashboardDto> ReadAsync(Shop shop)
        => (await new RfidDashboardHandler(shop.Harness.Db, shop.Clock)
            .Handle(new GetRfidDashboardQuery(1), default)).Value;

    [Fact]
    public async Task A_healthy_antenna_is_operational()
    {
        var shop = await ShopAsync();
        using var _ = shop.Harness;

        var dashboard = await ReadAsync(shop);

        var antennaOne = dashboard.Stations.Single(s => s.AntennaNumber == 1);

        antennaOne.Health.Should().Be(StationHealth.Operational);
        antennaOne.AgentOnline.Should().BeTrue();
        antennaOne.ReaderOnline.Should().BeTrue();
        antennaOne.StationCode.Should().Be("001");
    }

    /// <summary>
    /// Every antenna is a row, assigned or not. An unassigned one is invisible everywhere else: the
    /// reads happen, resolve to nothing, and no till ever reacts.
    /// </summary>
    [Fact]
    public async Task Every_antenna_appears_even_unassigned_ones()
    {
        var shop = await ShopAsync();
        using var _ = shop.Harness;

        var dashboard = await ReadAsync(shop);

        dashboard.Summary.Total.Should().Be(4, "the reader has four ports");
        dashboard.Summary.Operational.Should().Be(1);
        dashboard.Summary.Unassigned.Should().Be(3);
    }

    /// <summary>
    /// The machine going quiet is reported as the machine, not as the reader. Sending somebody to
    /// check an antenna when the PC is off is the wrong end of the shop.
    /// </summary>
    [Fact]
    public async Task A_silent_machine_reports_the_agent_rather_than_the_reader()
    {
        var shop = await ShopAsync();
        using var _ = shop.Harness;

        shop.Clock.Now = shop.Clock.Now.AddMinutes(5);

        var dashboard = await ReadAsync(shop);

        dashboard.Stations.Single(s => s.AntennaNumber == 1).Health.Should().Be(StationHealth.AgentOffline);
    }

    /// <summary>The machine is there and the reader is not: a different walk, a different message.</summary>
    [Fact]
    public async Task A_live_machine_with_a_dead_reader_reports_the_reader()
    {
        var shop = await ShopAsync();
        using var _ = shop.Harness;

        // The agent keeps checking in; only the reader has stopped answering it.
        shop.Clock.Now = shop.Clock.Now.AddMinutes(5);
        shop.Device.LastHeartbeat = shop.Clock.Now;
        await shop.Harness.Db.SaveChangesAsync();

        var dashboard = await ReadAsync(shop);

        var row = dashboard.Stations.Single(s => s.AntennaNumber == 1);

        row.Health.Should().Be(StationHealth.ReaderOffline);
        row.AgentOnline.Should().BeTrue();
        row.ReaderOnline.Should().BeFalse();
    }

    /// <summary>
    /// Unassigned outranks everything below it. An antenna nobody configured is unassigned whether or
    /// not its machine is on, and reporting "agent offline" about it would send somebody to fix a PC
    /// that was never the problem.
    /// </summary>
    [Fact]
    public async Task Unassigned_outranks_an_offline_machine()
    {
        var shop = await ShopAsync(assignAntennaOne: false);
        using var _ = shop.Harness;

        shop.Clock.Now = shop.Clock.Now.AddMinutes(5);

        var dashboard = await ReadAsync(shop);

        dashboard.Stations.Should().OnlyContain(s => s.Health == StationHealth.Unassigned);
        dashboard.Summary.AgentOffline.Should().Be(0);
    }

    /// <summary>Switched off deliberately is not a fault, and is counted apart from one.</summary>
    [Fact]
    public async Task A_disabled_assignment_is_reported_as_disabled()
    {
        var shop = await ShopAsync();
        using var _ = shop.Harness;

        var assignment = shop.Harness.Db.ReaderAntennaAssignments.First();
        assignment.SetEnabled(false);
        await shop.Harness.Db.SaveChangesAsync();

        var dashboard = await ReadAsync(shop);

        dashboard.Stations.Single(s => s.AntennaNumber == 1).Health.Should().Be(StationHealth.Disabled);
        dashboard.Summary.Disabled.Should().Be(1);
    }

    /// <summary>A reader no machine has claimed cannot read, and says which fact is missing.</summary>
    [Fact]
    public async Task A_reader_with_no_machine_says_so()
    {
        var shop = await ShopAsync();
        using var _ = shop.Harness;

        shop.Reader.DeviceId = null;
        await shop.Harness.Db.SaveChangesAsync();

        var dashboard = await ReadAsync(shop);

        dashboard.Stations.Single(s => s.AntennaNumber == 1).Health.Should().Be(StationHealth.ReaderUnclaimed);
    }
}
