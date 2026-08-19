using FluentAssertions;
using Retail25.Contracts.Terminals;
using Retail25.TerminalAgent.Rfid;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests;

/// <summary>
/// What this machine has been told to drive, and when that counts as having changed.
/// <para>
/// The store exists to answer one question cheaply: has anything moved? The server re-sends the same
/// configuration on every poll, and treating each answer as a change would tear down and rebuild
/// every reader session on the machine several times a minute — dropping tags in every gap.
/// </para>
/// </summary>
public sealed class DeviceConfigurationStoreTests
{
    private static DeviceConfigurationContract Configuration(long stationForAntennaOne = 11)
        => new(
            DeviceId: 1,
            DeviceKey: "PC-001",
            LocationId: 1,
            Readers:
            [
                new ManagedReaderContract(
                    ReaderId: 7,
                    ReaderKey: "RFID-001",
                    SerialNumber: "SN-1",
                    Host: "192.168.0.50",
                    Port: 4001,
                    Protocol: "UhfSerial",
                    AntennaCount: 4,
                    Antennas:
                    [
                        new AntennaAssignmentContract(1, stationForAntennaOne, "001", true),
                        new AntennaAssignmentContract(2, 12, "002", true),
                    ]),
            ]);

    [Fact]
    public void The_first_configuration_is_a_change()
    {
        var store = new DeviceConfigurationStore();
        var raised = 0;
        store.Changed += () => raised++;

        store.Set(Configuration());

        raised.Should().Be(1);
        store.Current.Should().NotBeNull();
    }

    /// <summary>
    /// The same configuration arriving again is not a change. This is the one that protects a
    /// trading shop: the poll repeats every few seconds and each false change costs every reader on
    /// the machine its connection.
    /// </summary>
    [Fact]
    public void The_same_configuration_arriving_again_is_not()
    {
        var store = new DeviceConfigurationStore();
        store.Set(Configuration());

        var raised = 0;
        store.Changed += () => raised++;

        store.Set(Configuration());
        store.Set(Configuration());

        raised.Should().Be(0, "the server re-sends the same content on every poll");
    }

    /// <summary>Moving one antenna to another station is a change, and must reach the agent.</summary>
    [Fact]
    public void Moving_an_antenna_to_another_station_is_a_change()
    {
        var store = new DeviceConfigurationStore();
        store.Set(Configuration());

        var raised = 0;
        store.Changed += () => raised++;

        store.Set(Configuration(stationForAntennaOne: 99));

        raised.Should().Be(1);
        store.Current!.Readers[0].Antennas[0].StationId.Should().Be(99);
    }

    /// <summary>
    /// Clearing is how a revoked registration is honoured: the machine stops driving readers it no
    /// longer owns and falls back to the per-station profile.
    /// </summary>
    [Fact]
    public void Clearing_a_configuration_is_a_change_and_leaves_nothing()
    {
        var store = new DeviceConfigurationStore();
        store.Set(Configuration());

        var raised = 0;
        store.Changed += () => raised++;

        store.Clear();

        raised.Should().Be(1);
        store.Current.Should().BeNull();
    }

    /// <summary>
    /// Clearing when there was nothing is not a change. An agent the server has never registered
    /// polls indefinitely, and each 404 must not restart its reader.
    /// </summary>
    [Fact]
    public void Clearing_when_there_was_nothing_is_not_a_change()
    {
        var store = new DeviceConfigurationStore();

        var raised = 0;
        store.Changed += () => raised++;

        store.Clear();
        store.Clear();

        raised.Should().Be(0);
    }
}
