using System.Net;
using System.Net.Sockets;
using RetailPlus.Core.Model;
using RetailPlus.Rfid.D2184;

namespace RetailPlus.Rfid.Tests;

/// <summary>
/// A pretend D2184 on a loopback TCP port: it answers the protocol the way the reader does (including streaming tag
/// frames before the end of an inventory), so the driver is tested over a real socket.
/// </summary>
internal sealed class FakeReader : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    public List<byte> Commands { get; } = [];
    public byte[] Power { get; } = [30, 30, 30, 30];
    public int WorkAntenna { get; private set; }
    public Dictionary<int, string[]> TagsOnAntenna { get; } = [];
    public HashSet<int> MissingAntennas { get; } = [];

    public FakeReader()
    {
        _listener.Start();
        _loop = Task.Run(ServeAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private async Task ServeAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                var stream = client.GetStream();
                var decoder = new FrameDecoder();
                var buffer = new byte[1024];
                while (!_cts.IsCancellationRequested)
                {
                    int n = await stream.ReadAsync(buffer, _cts.Token);
                    if (n == 0) break;
                    foreach (var f in decoder.Feed(buffer.AsSpan(0, n)))
                    {
                        lock (Commands) Commands.Add(f.Command);
                        foreach (var reply in Answer(f)) await stream.WriteAsync(reply, _cts.Token);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private IEnumerable<byte[]> Answer(ReaderFrame f)
    {
        byte Ok = D2184Error.Success;
        byte[] Reply(params byte[] data) => ReaderFrame.Encode(f.Address, f.Command, data);
        switch (f.Command)
        {
            case D2184Command.GetFirmwareVersion: yield return Reply(3, 1); break;
            case D2184Command.GetReaderTemperature: yield return Reply(1, 37); break;
            case D2184Command.GetOutputPower: yield return Reply(Power); break;
            case D2184Command.GetFrequencyRegion: yield return Reply(1, 7, 59); break;
            case D2184Command.GetLinkProfile: yield return Reply(0xD1); break;
            case D2184Command.GetDrmMode: yield return Reply(0); break;
            case D2184Command.GetAntennaDetector: yield return Reply(0); break;
            case D2184Command.GetFastTid: yield return Reply(0); break;
            case D2184Command.ReadGpio: yield return Reply(1, 0); break;
            case D2184Command.SetOutputPower:
                f.Data.CopyTo(Power, 0);
                yield return Reply(Ok);
                break;
            case D2184Command.SetWorkAntenna:
                WorkAntenna = f.Data[0] + 1;
                yield return Reply(Ok);
                break;
            case D2184Command.SetFrequencyRegion:
            case D2184Command.SetBeeperMode:
            case D2184Command.SetDrmMode:
            case D2184Command.SetAntennaDetector:
            case D2184Command.SetAndSaveFastTid:
            case D2184Command.WriteGpio:
                yield return Reply(Ok);
                break;
            case D2184Command.GetReturnLoss: yield return Reply(17); break;
            case D2184Command.RealTimeInventory:
                foreach (var t in Tags(WorkAntenna)) yield return t;
                yield return Reply((byte)(WorkAntenna - 1), 0, 10, 0, 0, 0, 2);
                break;
            case D2184Command.FastSwitchInventory:
                for (int i = 0; i < 4; i++)
                {
                    int ant = f.Data[i * 2];
                    if (ant > 3) continue;
                    if (MissingAntennas.Contains(ant + 1)) yield return Reply((byte)ant, D2184Error.AntennaMissing);
                    else foreach (var t in Tags(ant + 1)) yield return t;
                }
                yield return Reply(0, 0, 2, 0, 0, 0, 40);
                break;
            default: yield return Reply(0x11); break;
        }

        IEnumerable<byte[]> Tags(int antenna)
        {
            if (!TagsOnAntenna.TryGetValue(antenna, out var epcs)) yield break;
            foreach (var epc in epcs)
            {
                var bytes = Convert.FromHexString(epc);
                byte pc0 = (byte)((bytes.Length / 2) << 3);
                yield return Reply([(byte)((33 << 2) | (antenna - 1)), pc0, 0x00, .. bytes, 0x5A]);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try { await _loop; } catch (Exception) { }
        _cts.Dispose();
    }
}

public class DriverTests
{
    private static RfidReader Settings(int port) => new() { Name = "Test", Host = "127.0.0.1", Port = port, Address = 0xFF };

    [Fact]
    public async Task Reads_info_from_the_reader()
    {
        await using var fake = new FakeReader();
        await using var device = new D2184Device(Settings(fake.Port));
        await device.ConnectAsync(CancellationToken.None);
        var info = await device.ReadInfoAsync(CancellationToken.None);
        Assert.Equal("3.01", info.Firmware);
        Assert.Equal(37, info.TemperatureC);
        Assert.Equal([30, 30, 30, 30], info.PowerDbm!);
        Assert.Equal(RfidFrequencyRegion.Fcc, info.Region);
        Assert.Equal(RfidLinkProfile.Profile1, info.LinkProfile);
        Assert.True(info.Gpio1High);
        Assert.Empty(info.Problems);
    }

    [Fact]
    public async Task Applies_per_antenna_power_and_settings()
    {
        await using var fake = new FakeReader();
        var settings = Settings(fake.Port);
        settings.Antennas = [new() { Port = 1, PowerDbm = 26 }, new() { Port = 2, PowerDbm = 20 }, new() { Port = 3, PowerDbm = 33 }, new() { Port = 4, PowerDbm = 10 }];
        await using var device = new D2184Device(settings);
        await device.ConnectAsync(CancellationToken.None);
        var problems = await device.ApplySettingsAsync(settings, CancellationToken.None);
        Assert.Empty(problems);
        Assert.Equal(new byte[] { 26, 20, 33, 10 }, fake.Power);
        Assert.Contains(D2184Command.SetFrequencyRegion, fake.Commands);
        Assert.DoesNotContain(D2184Command.SetLinkProfile, fake.Commands);   // already profile 1: no reset
    }

    [Fact]
    public async Task One_antenna_uses_real_time_inventory()
    {
        await using var fake = new FakeReader();
        fake.TagsOnAntenna[3] = ["E2000017221101441890A1B2", "300833B2DDD9014000000001"];
        await using var device = new D2184Device(Settings(fake.Port));
        await device.ConnectAsync(CancellationToken.None);
        var tags = new List<TagRead>();
        var result = await device.InventoryAsync([3], 1, tags.Add, CancellationToken.None);
        Assert.Equal(2, result.TagsRead);
        Assert.All(tags, t => Assert.Equal(3, t.Antenna));
        Assert.Equal(3, fake.WorkAntenna);
        Assert.Contains(D2184Command.RealTimeInventory, fake.Commands);
    }

    [Fact]
    public async Task Several_antennas_use_fast_switch_and_report_missing_ones()
    {
        await using var fake = new FakeReader();
        fake.TagsOnAntenna[1] = ["E2000017221101441890A1B2"];
        fake.TagsOnAntenna[2] = ["E2000017221101441890FFFF"];
        fake.MissingAntennas.Add(4);
        await using var device = new D2184Device(Settings(fake.Port));
        await device.ConnectAsync(CancellationToken.None);
        var tags = new List<TagRead>();
        var result = await device.InventoryAsync([1, 2, 4], 1, tags.Add, CancellationToken.None);
        Assert.Equal([1, 2], tags.Select(t => t.Antenna).Order());
        Assert.Equal([4], result.MissingAntennas);
        Assert.Contains(D2184Command.FastSwitchInventory, fake.Commands);
    }

    [Fact]
    public async Task Measures_return_loss_and_writes_gpio()
    {
        await using var fake = new FakeReader();
        await using var device = new D2184Device(Settings(fake.Port));
        await device.ConnectAsync(CancellationToken.None);
        Assert.Equal(17, await device.MeasureReturnLossAsync(2, 33, CancellationToken.None));
        Assert.Equal(2, fake.WorkAntenna);
        await device.WriteGpioAsync(3, true, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => device.WriteGpioAsync(1, true, CancellationToken.None));
    }

    [Fact]
    public async Task Discovery_finds_a_reader_its_settings_and_antennas()
    {
        await using var fake = new FakeReader();
        var found = Assert.Single(await RfidDiscovery.FindAsync(["127.0.0.1", "127.0.0.2"], fake.Port, TimeSpan.FromMilliseconds(300)));
        Assert.Equal("127.0.0.1", found.Host);
        Assert.Equal("3.01", found.Info.Firmware);
        Assert.Equal([1, 2, 3, 4], found.ConnectedAntennas);
        var record = RfidDiscovery.ToReader(found, "Reader 1", 7);
        Assert.All(record.Antennas, a => Assert.Equal(7, a.StationNumber));
        Assert.Equal(RfidFrequencyRegion.Fcc, record.Region);
        Assert.Equal(fake.Port, record.Port);
    }

    [Fact]
    public void Candidate_hosts_start_with_the_factory_address() =>
        Assert.Equal(RfidDiscovery.FactoryHost, RfidDiscovery.CandidateHosts(true)[0]);

    [Fact]
    public async Task Unreachable_reader_fails_fast_with_a_clear_message()
    {
        D2184Connection.ConnectTimeout = TimeSpan.FromMilliseconds(500);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();   // nothing listens there now
        await using var device = new D2184Device(Settings(port));
        await Assert.ThrowsAnyAsync<Exception>(() => device.ConnectAsync(CancellationToken.None));
        Assert.False(device.IsConnected);
        D2184Connection.ConnectTimeout = TimeSpan.FromSeconds(3);
    }
}
