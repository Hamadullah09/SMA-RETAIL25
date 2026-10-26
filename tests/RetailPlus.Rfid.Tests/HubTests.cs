using System.Net;
using System.Text;
using RetailPlus.Core.Model;

namespace RetailPlus.Rfid.Tests;

public class HubTests
{
    private static RfidReader Reader(int id, params (int Port, int? Station)[] antennas) => new()
    {
        Id = id, Name = $"R{id}", Host = "sim", Port = 4000 + id, RssiThresholdDbm = -70, DedupSeconds = 60,
        Antennas = antennas.Select(a => new RfidAntenna { Port = a.Port, StationNumber = a.Station, IsEnabled = true }).ToList(),
    };

    private static async Task<List<RfidTagEvent>> Collect(RfidHub hub, int station, TimeSpan time)
    {
        var list = new List<RfidTagEvent>();
        using var cts = new CancellationTokenSource(time);
        try
        {
            await foreach (var e in hub.ListenAsync(station, cts.Token)) list.Add(e);
        }
        catch (OperationCanceledException)
        {
        }
        return list;
    }

    [Fact]
    public async Task Reads_go_only_to_the_station_of_the_antenna()
    {
        var readers = new List<RfidReader> { Reader(1, (1, 1), (2, 2)), Reader(2, (1, 2)) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await hub.StartAsync();
        ((SimulatedRfidDevice)hub.DeviceOf(1)!).Place(1, "AAAA0001");
        ((SimulatedRfidDevice)hub.DeviceOf(1)!).Place(2, "BBBB0002");
        ((SimulatedRfidDevice)hub.DeviceOf(2)!).Place(1, "CCCC0003");

        var station1 = Collect(hub, 1, TimeSpan.FromSeconds(1.5));
        var station2 = Collect(hub, 2, TimeSpan.FromSeconds(1.5));
        var one = await station1;
        var two = await station2;

        Assert.Equal(["AAAA0001"], one.Select(e => e.Epc).Distinct());
        Assert.Equal(["BBBB0002", "CCCC0003"], two.Select(e => e.Epc).Distinct().Order());
        Assert.All(one, e => Assert.Equal(1, e.StationNumber));
    }

    [Fact]
    public async Task Same_tag_is_reported_once_per_dedup_period_and_weak_tags_are_ignored()
    {
        var readers = new List<RfidReader> { Reader(1, (1, 5)) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await hub.StartAsync();
        var device = (SimulatedRfidDevice)hub.DeviceOf(1)!;
        device.Place(1, "AAAA0001", -40);
        device.Place(1, "FAAA0009", -85);   // below the -70 dBm threshold
        var events = await Collect(hub, 5, TimeSpan.FromSeconds(1));
        Assert.Equal(["AAAA0001"], events.Select(e => e.Epc));
    }

    [Fact]
    public async Task Antennas_are_not_read_while_nobody_listens()
    {
        var readers = new List<RfidReader> { Reader(1, (1, 7)) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await hub.StartAsync();
        var device = (SimulatedRfidDevice)hub.DeviceOf(1)!;
        await Task.Delay(400);
        Assert.Equal(0, device.Rounds);
        await Collect(hub, 7, TimeSpan.FromMilliseconds(500));
        Assert.True(device.Rounds > 0);
        var status = Assert.Single(await hub.GetStatusAsync());
        Assert.True(status.Connected);
    }

    [Fact]
    public async Task Reload_picks_up_new_assignments_without_reconnecting()
    {
        var readers = new List<RfidReader> { Reader(1, (1, null)) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await hub.StartAsync();
        var device = hub.DeviceOf(1)!;
        ((SimulatedRfidDevice)device).Place(1, "AAAA0001");
        Assert.Empty(await Collect(hub, 3, TimeSpan.FromMilliseconds(400)));

        readers[0] = Reader(1, (1, 3));
        await hub.ReloadAsync();
        Assert.Same(device, hub.DeviceOf(1));
        Assert.NotEmpty(await Collect(hub, 3, TimeSpan.FromMilliseconds(600)));
    }

    [Fact]
    public async Task Offline_reader_is_reported_and_does_not_stop_the_others()
    {
        var readers = new List<RfidReader> { Reader(1, (1, 1)), Reader(2, (1, 1)) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers), r =>
            new SimulatedRfidDevice(r) { Offline = r.Id == 2 });
        await hub.StartAsync();
        ((SimulatedRfidDevice)hub.DeviceOf(1)!).Place(1, "AAAA0001");
        var events = await Collect(hub, 1, TimeSpan.FromSeconds(1));
        Assert.NotEmpty(events);
        var status = await hub.GetStatusAsync();
        Assert.Equal("Offline", status.Single(s => s.ReaderId == 2).State);
        Assert.NotNull(status.Single(s => s.ReaderId == 2).LastError);
    }

    [Fact]
    public async Task Readers_connect_as_soon_as_the_hub_starts_and_reconnect_after_a_drop()
    {
        var readers = new List<RfidReader> { Reader(1, (1, 9)) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await hub.StartAsync();
        await Task.Delay(500);
        var status = Assert.Single(await hub.GetStatusAsync());
        Assert.True(status.Connected);
        Assert.Equal("Connected", status.State);
        Assert.Equal(0, status.Rounds);   // connected, but nothing is read while no one listens

        var device = (SimulatedRfidDevice)hub.DeviceOf(1)!;
        await device.DisconnectAsync();   // cable pulled
        await hub.ReloadAsync();          // wakes the reader loop
        await Task.Delay(500);
        Assert.True((await hub.GetStatusAsync()).Single().Connected);
    }

    [Fact]
    public async Task Reader_buzzer_follows_its_setting_from_the_first_connection()
    {
        var readers = new List<RfidReader> { Reader(1, (1, 4)) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await hub.StartAsync();
        await Task.Delay(400);
        var device = (SimulatedRfidDevice)hub.DeviceOf(1)!;
        Assert.Equal(RfidBeeperMode.Quiet, device.Beeper);   // the simulated reader starts out beeping at every tag

        readers[0] = Reader(1, (1, 4));
        readers[0].BeeperMode = RfidBeeperMode.AfterInventory;
        await hub.ReloadAsync();
        await Collect(hub, 4, TimeSpan.FromMilliseconds(500));
        Assert.Equal(RfidBeeperMode.AfterInventory, device.Beeper);
    }

    [Fact]
    public async Task Admin_commands_run_between_inventory_rounds()
    {
        var readers = new List<RfidReader> { Reader(1, (1, 1)) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await hub.StartAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var listening = Collect(hub, 1, TimeSpan.FromSeconds(1));
        var info = await hub.ReadInfoAsync(1, cts.Token);
        Assert.Equal("SIM", info.Firmware);
        Assert.Empty(await hub.ApplySettingsAsync(1, cts.Token));
        await listening;
    }

    [Fact]
    public async Task Many_readers_and_stations_are_handled_together()
    {
        // 63 readers x 4 antennas, every antenna its own station: each station gets exactly its own tag.
        var readers = Enumerable.Range(1, 63).Select(r => Reader(r, (1, r * 4 - 3), (2, r * 4 - 2), (3, r * 4 - 1), (4, r * 4))).ToList();
        foreach (var r in readers) r.DedupSeconds = 60;
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers), r => new SimulatedRfidDevice(r) { RoundTime = TimeSpan.FromMilliseconds(20) });
        await hub.StartAsync();
        foreach (var r in readers)
            for (int port = 1; port <= 4; port++)
                ((SimulatedRfidDevice)hub.DeviceOf(r.Id)!).Place(port, $"{r.Id:X4}{port:X4}");
        var listeners = Enumerable.Range(1, 252).Select(s => Collect(hub, s, TimeSpan.FromSeconds(2))).ToList();
        var results = await Task.WhenAll(listeners);
        for (int s = 1; s <= 252; s++)
        {
            int reader = (s + 3) / 4, port = (s - 1) % 4 + 1;
            Assert.Equal([$"{reader:X4}{port:X4}"], results[s - 1].Select(e => e.Epc).Distinct());
        }
    }
}

public class HubClientTests
{
    private sealed class SseHandler(string body) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            var content = request.RequestUri.AbsolutePath.EndsWith("/events", StringComparison.Ordinal)
                ? new StringContent(body, Encoding.UTF8, "text/event-stream")
                : new StringContent("[]", Encoding.UTF8, "application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    [Fact]
    public async Task Reads_tag_events_from_the_event_stream()
    {
        var body = ": station 4\n\n" +
                   "data: {\"epc\":\"E200AA\",\"stationNumber\":4,\"readerId\":1,\"readerName\":\"R1\",\"antenna\":2,\"rssiDbm\":-48,\"seenAt\":\"2026-10-26T10:00:00\"}\n\n" +
                   ": ping\n\n";
        var handler = new SseHandler(body);
        using var client = new RfidHubClient("http://hub:5280", "secret", handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        RfidTagEvent? first = null;
        await foreach (var e in client.ListenAsync(4, cts.Token))
        {
            first = e;
            break;
        }
        Assert.NotNull(first);
        Assert.Equal("E200AA", first.Epc);
        Assert.Equal(2, first.Antenna);
        Assert.Contains("/stations/4/events", handler.Paths);
        Assert.Empty(await client.GetStatusAsync(cts.Token));
    }
}
