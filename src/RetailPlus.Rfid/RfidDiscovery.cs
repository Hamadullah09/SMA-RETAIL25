using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using RetailPlus.Core.Model;
using RetailPlus.Rfid.D2184;

namespace RetailPlus.Rfid;

/// <summary>A reader that answered on the network, with its current settings and the antenna ports that have an antenna.</summary>
public sealed record DiscoveredReader(string Host, int Port, ReaderInfo Info, IReadOnlyList<int> ConnectedAntennas);

/// <summary>
/// Finds D2184 readers on this computer's networks: the factory address first, then every address of each local
/// IPv4 network (the /24 around each of this PC's addresses), 64 at a time with a short timeout. A port that opens is
/// confirmed with the reader's own firmware command, so other devices on port 4001 are not mistaken for readers.
/// </summary>
public static class RfidDiscovery
{
    public const string FactoryHost = "192.168.0.178";
    public const int DefaultPort = 4001;

    /// <summary>The factory address, then (optionally) every neighbour address on this PC's local networks.</summary>
    public static IReadOnlyList<string> CandidateHosts(bool scanLocalNetworks)
    {
        var hosts = new List<string> { FactoryHost };
        if (!scanLocalNetworks) return hosts;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork || address.PrefixLength < 16) continue;
                var b = address.Address.GetAddressBytes();
                if (b[0] == 127 || (b[0] == 169 && b[1] == 254)) continue;
                for (int i = 1; i < 255; i++)
                    if (i != b[3]) hosts.Add($"{b[0]}.{b[1]}.{b[2]}.{i}");
            }
        }
        return hosts.Distinct().ToList();
    }

    public static async Task<IReadOnlyList<DiscoveredReader>> FindAsync(IEnumerable<string> hosts, int port = DefaultPort,
        TimeSpan? connectTimeout = null, CancellationToken cancellationToken = default)
    {
        var timeout = connectTimeout ?? TimeSpan.FromMilliseconds(400);
        var open = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(hosts.Distinct(), new ParallelOptions { MaxDegreeOfParallelism = 64, CancellationToken = cancellationToken },
            async (host, token) =>
            {
                using var tcp = new TcpClient();
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
                limit.CancelAfter(timeout);
                try
                {
                    await tcp.ConnectAsync(host, port, limit.Token);
                    open.Add(host);
                }
                catch (Exception ex) when (ex is SocketException or OperationCanceledException)
                {
                }
            });

        var found = new List<DiscoveredReader>();
        foreach (var host in open.OrderBy(h => IPAddress.TryParse(h, out var ip) ? BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray()) : 0))
        {
            try
            {
                await using var device = new D2184Device(new RfidReader { Name = host, Host = host, Port = port, Address = 0xFF });
                await device.ConnectAsync(cancellationToken);
                var info = await device.ReadInfoAsync(cancellationToken);
                if (info.Firmware is null) continue;
                int probeFrequency = info.StartFrequency is int s && info.EndFrequency is int e ? (s + e) / 2 : 33;
                var antennas = new List<int>();
                for (int antenna = 1; antenna <= 4; antenna++)
                {
                    try
                    {
                        if (await device.MeasureReturnLossAsync(antenna, probeFrequency, cancellationToken) >= 5) antennas.Add(antenna);
                    }
                    catch (RfidException)
                    {
                    }
                }
                found.Add(new DiscoveredReader(host, port, info, antennas));
            }
            catch (Exception ex) when (ex is RfidException or IOException or SocketException)
            {
            }
        }
        return found;
    }

    /// <summary>
    /// A reader record that matches what the reader is set to now (so sending settings later changes nothing by
    /// surprise). Ports with an antenna are switched on and, when <paramref name="stationNumber"/> is given, assigned
    /// to that station.
    /// </summary>
    public static RfidReader ToReader(DiscoveredReader found, string name, int? stationNumber)
    {
        var info = found.Info;
        var reader = new RfidReader
        {
            Name = name, Host = found.Host, Port = found.Port, Location = "Found on the network", FirmwareVersion = info.Firmware,
            RssiThresholdDbm = -85, LinkProfile = info.LinkProfile ?? RfidLinkProfile.Profile1, DrmEnabled = info.DrmEnabled ?? false,
            AntennaDetectorDb = info.AntennaDetectorDb ?? 0, FastTid = info.FastTid ?? false,
        };
        if (info.Region is { } region)
        {
            reader.Region = region;
            if (region == RfidFrequencyRegion.Custom)
            {
                reader.CustomStartKHz = info.CustomStartKHz ?? reader.CustomStartKHz;
                reader.CustomSpacing = info.CustomSpacing ?? reader.CustomSpacing;
                reader.CustomChannels = info.CustomChannels ?? reader.CustomChannels;
            }
            else
            {
                reader.StartFrequency = info.StartFrequency ?? reader.StartFrequency;
                reader.EndFrequency = info.EndFrequency ?? reader.EndFrequency;
            }
        }
        var connected = found.ConnectedAntennas.Count > 0 ? found.ConnectedAntennas : [1];
        reader.Antennas = Enumerable.Range(1, 4).Select(port => new RfidAntenna
        {
            Port = port,
            Name = $"Antenna {port}",
            IsEnabled = connected.Contains(port),
            PowerDbm = info.PowerDbm is { Count: 4 } power ? power[port - 1] : 30,
            Purpose = RfidAntennaPurpose.PointOfSale,
            StationNumber = connected.Contains(port) ? stationNumber : null,
        }).ToList();
        return reader;
    }
}
