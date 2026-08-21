using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Retail25.Contracts.Terminals;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests;

/// <summary>
/// The configuration payload as the live server actually sends it.
/// <para>
/// Copied verbatim from a running shop rather than written to match the contract, because the two
/// disagreeing is exactly the failure this exists to catch: a reader whose address arrives as
/// something the agent then ignores looks, from every screen, like a reader that is simply offline.
/// </para>
/// </summary>
public sealed class DeviceConfigurationWireTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly int[] ExpectedAntennas = [1, 2];
    private static readonly string[] ExpectedStations = ["001", "002"];

    private const string LivePayload = """
    {
      "deviceId": 1,
      "deviceKey": "DESKTOP-RE0V4T7",
      "locationId": 1,
      "readers": [
        {
          "readerId": 1,
          "readerKey": "DEFAULT",
          "serialNumber": null,
          "host": "192.168.0.178",
          "port": 4001,
          "protocol": "UhfSerial",
          "antennaCount": 2,
          "antennas": [
            { "antennaNumber": 1, "stationId": 1, "stationCode": "001", "enabled": true },
            { "antennaNumber": 2, "stationId": 2, "stationCode": "002", "enabled": true }
          ],
          "settings": null
        }
      ],
      "version": 1,
      "revision": "1:192.168.0.178:4001:1>1,2>2"
    }
    """;

    [Fact]
    public void The_readers_address_survives_the_wire()
    {
        var configuration = JsonSerializer.Deserialize<DeviceConfigurationContract>(LivePayload, Options);

        configuration.Should().NotBeNull();
        configuration!.Readers.Should().HaveCount(1);

        var reader = configuration.Readers[0];

        reader.ReaderId.Should().Be(1);
        reader.ReaderKey.Should().Be("DEFAULT");

        // The two that decide whether the agent can find the hardware at all.
        reader.Host.Should().Be("192.168.0.178");
        reader.Port.Should().Be(4001);

        reader.Protocol.Should().Be("UhfSerial");
        reader.AntennaCount.Should().Be(2);
        reader.Antennas.Should().HaveCount(2);
        reader.Antennas.Select(a => a.AntennaNumber).Should().BeEquivalentTo(ExpectedAntennas);
        reader.Antennas.Select(a => a.StationCode).Should().BeEquivalentTo(ExpectedStations);
        reader.Antennas.Should().OnlyContain(a => a.Enabled);
    }
}
