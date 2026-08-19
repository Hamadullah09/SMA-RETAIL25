using FluentAssertions;
using Retail25.Application.Terminals;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Terminals;
using Xunit;

namespace Retail25.Application.UnitTests.Rfid;

/// <summary>
/// What a machine is told to do.
/// <para>
/// Configuration flows down and observations flow up: the agent is told which station an antenna
/// stands for and never decides it. These pin that the whole picture arrives in one answer, and that
/// the answer is scoped to the machine asking.
/// </para>
/// </summary>
public sealed class DeviceConfigurationTests
{
    private static async Task<MastersTestHarness> ShopAsync()
    {
        var harness = await MastersTestHarness.CreateAsync();

        var pc = Device.Create(1, "PC-001", "Front office").Value;
        harness.Db.Devices.Add(pc);
        await harness.Db.SaveChangesAsync();

        // Two readers on one machine — the case the per-station profile could never describe.
        foreach (var key in new[] { "RFID-001", "RFID-002" })
        {
            var reader = RfidReader.Create(1, key, $"SN-{key}").Value;
            reader.MoveTo("192.168.0.50", 4001);
            reader.DeviceId = pc.Id;
            harness.Db.RfidReaders.Add(reader);
        }

        await harness.Db.SaveChangesAsync();

        var stationNumber = 1;

        foreach (var reader in harness.Db.RfidReaders.OrderBy(r => r.ReaderKey).ToList())
        {
            for (var antenna = 1; antenna <= 4; antenna++)
            {
                var station = Station.Create(1, $"{stationNumber:000}", $"Checkout {stationNumber}").Value;
                harness.Db.Stations.Add(station);
                await harness.Db.SaveChangesAsync();

                harness.Db.ReaderAntennaAssignments.Add(
                    ReaderAntennaAssignment.Create(reader.Id, antenna, station.Id).Value);

                stationNumber++;
            }
        }

        await harness.Db.SaveChangesAsync();

        return harness;
    }

    /// <summary>One machine, two readers, eight stations — in a single answer.</summary>
    [Fact]
    public async Task A_machine_is_told_about_every_reader_it_drives()
    {
        using var harness = await ShopAsync();

        var result = await new DeviceConfigurationHandler(harness.Db)
            .Handle(new GetDeviceConfigurationQuery(1, "PC-001"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Readers.Should().HaveCount(2);
        result.Value.Readers.Should().OnlyContain(r => r.Antennas.Count == 4);
        result.Value.Readers.SelectMany(r => r.Antennas).Select(a => a.StationId)
            .Should().OnlyHaveUniqueItems("eight antennas stand for eight different stations");
    }

    /// <summary>Lower case, spaces, whatever an installer typed — the key is normalised.</summary>
    [Fact]
    public async Task The_device_key_is_matched_however_it_was_typed()
    {
        using var harness = await ShopAsync();

        var result = await new DeviceConfigurationHandler(harness.Db)
            .Handle(new GetDeviceConfigurationQuery(1, " pc-001 "), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.DeviceKey.Should().Be("PC-001");
    }

    /// <summary>
    /// A machine nobody has registered is refused by name rather than handed an empty configuration,
    /// which would look identical to a machine with nothing assigned to it.
    /// </summary>
    [Fact]
    public async Task An_unknown_machine_is_refused_rather_than_given_nothing()
    {
        using var harness = await ShopAsync();

        var result = await new DeviceConfigurationHandler(harness.Db)
            .Handle(new GetDeviceConfigurationQuery(1, "PC-999"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("device.not_found");
    }

    /// <summary>
    /// The revision changes when an assignment moves, which is what lets an agent apply a
    /// reassignment on its next poll instead of at its next restart.
    /// </summary>
    [Fact]
    public async Task Moving_an_antenna_changes_the_revision()
    {
        using var harness = await ShopAsync();
        var handler = new DeviceConfigurationHandler(harness.Db);

        var before = (await handler.Handle(new GetDeviceConfigurationQuery(1, "PC-001"), default)).Value.Revision;

        var moved = Station.Create(1, "099", "Moved").Value;
        harness.Db.Stations.Add(moved);
        await harness.Db.SaveChangesAsync();

        var assignment = harness.Db.ReaderAntennaAssignments.OrderBy(a => a.Id).First();
        assignment.ReassignTo(moved.Id);
        await harness.Db.SaveChangesAsync();

        var after = (await handler.Handle(new GetDeviceConfigurationQuery(1, "PC-001"), default)).Value.Revision;

        after.Should().NotBe(before);
    }

    /// <summary>A machine is told about its own readers and no others.</summary>
    [Fact]
    public async Task One_machine_is_never_told_about_another_machines_readers()
    {
        using var harness = await ShopAsync();

        var other = Device.Create(1, "PC-002").Value;
        harness.Db.Devices.Add(other);
        await harness.Db.SaveChangesAsync();

        var theirs = RfidReader.Create(1, "RFID-009", "SN-009").Value;
        theirs.DeviceId = other.Id;
        harness.Db.RfidReaders.Add(theirs);
        await harness.Db.SaveChangesAsync();

        var result = await new DeviceConfigurationHandler(harness.Db)
            .Handle(new GetDeviceConfigurationQuery(1, "PC-001"), default);

        result.Value.Readers.Should().NotContain(r => r.ReaderKey == "RFID-009");
    }
}
