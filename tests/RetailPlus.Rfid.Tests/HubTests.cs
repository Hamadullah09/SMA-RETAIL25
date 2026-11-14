using System.Net;
using System.Text;
using RetailPlus.Core.Model;

namespace RetailPlus.Rfid.Tests;

public class HubTests
{
    private static Guid Id(int n) => new(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);

    private static RfidReader Reader(int id, params (int Port, int? Station)[] antennas) => new()
    {
        Id = Id(id), Name = $"R{id}", Host = "sim", Port = 4000 + id, RssiThresholdDbm = -70, DedupSeconds = 60,
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
        ((SimulatedRfidDevice)hub.DeviceOf(Id(1))!).Place(1, "AAAA0001");
        ((SimulatedRfidDevice)hub.DeviceOf(Id(1))!).Place(2, "BBBB0002");
        ((SimulatedRfidDevice)hub.DeviceOf(Id(2))!).Place(1, "CCCC0003");

        var station1 = Collect(hub, 1, TimeSpan.FromSeconds(1.5));
        var station2 = Collect(hub, 2, TimeSpan.FromSeconds(1.5));
        var one = await station1;
        var two = await station2;

        Assert.Equal(["AAAA0001"], one.Select(e => e.Epc).Distinct());
        Assert.Equal(["BBBB0002", "CCCC0003"], two.Select(e => e.Epc).Distinct().Order());
        Assert.All(one, e => Assert.Equal(1, e.StationNumber));
    }

    private static async Task<List<RfidTagEvent>> Collect(RfidHub hub, int station, IReadOnlySet<RfidAntennaPurpose>? purposes, TimeSpan time)
    {
        var list = new List<RfidTagEvent>();
        using var cts = new CancellationTokenSource(time);
        try
        {
            await foreach (var e in hub.ListenAsync(station, purposes, cts.Token)) list.Add(e);
        }
        catch (OperationCanceledException)
        {
        }
        return list;
    }

    /// <summary>A reader whose antenna 1 is the counter of station 1 and antenna 2 the exit gate of the same station.</summary>
    private static RfidReader CounterAndGate(int station) => new()
    {
        Id = Id(1), Name = "R1", Host = "sim", Port = 4001, RssiThresholdDbm = -70, DedupSeconds = 60,
        Antennas =
        [
            new RfidAntenna { Port = 1, StationNumber = station, IsEnabled = true, Purpose = RfidAntennaPurpose.PointOfSale },
            new RfidAntenna { Port = 2, StationNumber = station, IsEnabled = true, Purpose = RfidAntennaPurpose.Gate },
        ],
    };

    [Fact]
    public async Task Exit_gate_reads_go_to_the_gate_alarm_and_never_to_the_sale()
    {
        var readers = new List<RfidReader> { CounterAndGate(1) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await hub.StartAsync();
        var device = (SimulatedRfidDevice)hub.DeviceOf(Id(1))!;
        device.Place(1, "AAAA0001");
        device.Place(2, "6A7E0002");
        // The same tag on both: the sale and the gate each hear it once (separate dedup).
        device.Place(1, "BBBB0003");
        device.Place(2, "BBBB0003");

        var sale = Collect(hub, 1, RfidListen.Sale, TimeSpan.FromSeconds(1.5));
        var gate = Collect(hub, 1, RfidListen.Gate, TimeSpan.FromSeconds(1.5));
        var all = Collect(hub, 1, TimeSpan.FromSeconds(1.5));

        var atSale = await sale;
        var atGate = await gate;
        Assert.Equal(["AAAA0001", "BBBB0003"], atSale.Select(e => e.Epc).Order());
        Assert.All(atSale, e => Assert.Equal(RfidAntennaPurpose.PointOfSale, e.Purpose));
        Assert.Equal(["6A7E0002", "BBBB0003"], atGate.Select(e => e.Epc).Order());
        Assert.All(atGate, e => Assert.Equal((RfidAntennaPurpose.Gate, 2), (e.Purpose, e.Antenna)));
        // A listener without purposes (the RFID screen's monitor) hears both.
        Assert.Equal(["6A7E0002", "AAAA0001", "BBBB0003"], (await all).Select(e => e.Epc).Distinct().Order());
    }

    [Fact]
    public async Task A_gate_antenna_is_read_only_while_the_gate_listens()
    {
        var reader = CounterAndGate(8);
        reader.Antennas.RemoveAt(0);   // the gate antenna only
        var readers = new List<RfidReader> { reader };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await hub.StartAsync();
        var device = (SimulatedRfidDevice)hub.DeviceOf(Id(1))!;
        device.Place(2, "6A7E0002");

        Assert.Empty(await Collect(hub, 8, RfidListen.Sale, TimeSpan.FromMilliseconds(600)));
        Assert.Equal(0, device.Rounds);   // the POS screen listening does not switch the gate on
        Assert.Equal(["6A7E0002"], (await Collect(hub, 8, RfidListen.Gate, TimeSpan.FromMilliseconds(600))).Select(e => e.Epc));
        Assert.True(device.Rounds > 0);
    }

    [Fact]
    public void Purposes_travel_in_the_query_string()
    {
        Assert.Equal("", RfidListen.Query(null));
        Assert.Equal("?purposes=Gate", RfidListen.Query(RfidListen.Gate));
        Assert.Equal("?purposes=PointOfSale,TagRegistration,Shelf", RfidListen.Query(RfidListen.Counter));
        Assert.Equal([RfidAntennaPurpose.Gate], RfidListen.Parse("gate")!);
        Assert.Equal([RfidAntennaPurpose.PointOfSale, RfidAntennaPurpose.Shelf], RfidListen.Parse("PointOfSale, Shelf")!.Order());
        Assert.Null(RfidListen.Parse(null));
        Assert.Null(RfidListen.Parse("Gate,Door"));   // a purpose this hub does not know: every antenna, as before
        Assert.True(RfidListen.Hears(null, RfidAntennaPurpose.Gate));
        Assert.False(RfidListen.Hears(RfidListen.Sale, RfidAntennaPurpose.Gate));
        Assert.False(RfidListen.Hears(RfidListen.Counter, RfidAntennaPurpose.Gate));
    }

    [Fact]
    public async Task Same_tag_is_reported_once_per_dedup_period_and_weak_tags_are_ignored()
    {
        var readers = new List<RfidReader> { Reader(1, (1, 5)) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await hub.StartAsync();
        var device = (SimulatedRfidDevice)hub.DeviceOf(Id(1))!;
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
        var device = (SimulatedRfidDevice)hub.DeviceOf(Id(1))!;
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
        var device = hub.DeviceOf(Id(1))!;
        ((SimulatedRfidDevice)device).Place(1, "AAAA0001");
        Assert.Empty(await Collect(hub, 3, TimeSpan.FromMilliseconds(400)));

        readers[0] = Reader(1, (1, 3));
        await hub.ReloadAsync();
        Assert.Same(device, hub.DeviceOf(Id(1)));
        Assert.NotEmpty(await Collect(hub, 3, TimeSpan.FromMilliseconds(600)));
    }

    [Fact]
    public async Task Offline_reader_is_reported_and_does_not_stop_the_others()
    {
        var readers = new List<RfidReader> { Reader(1, (1, 1)), Reader(2, (1, 1)) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers), r =>
            new SimulatedRfidDevice(r) { Offline = r.Id == Id(2) });
        await hub.StartAsync();
        ((SimulatedRfidDevice)hub.DeviceOf(Id(1))!).Place(1, "AAAA0001");
        var events = await Collect(hub, 1, TimeSpan.FromSeconds(1));
        Assert.NotEmpty(events);
        var status = await hub.GetStatusAsync();
        Assert.Equal("Offline", status.Single(s => s.ReaderId == Id(2)).State);
        Assert.NotNull(status.Single(s => s.ReaderId == Id(2)).LastError);
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

        var device = (SimulatedRfidDevice)hub.DeviceOf(Id(1))!;
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
        var device = (SimulatedRfidDevice)hub.DeviceOf(Id(1))!;
        Assert.Equal(RfidBeeperMode.Quiet, device.Beeper);   // the simulated reader starts out beeping at every tag

        readers[0] = Reader(1, (1, 4));
        readers[0].BeeperMode = RfidBeeperMode.AfterInventory;
        await hub.ReloadAsync();
        await Collect(hub, 4, TimeSpan.FromMilliseconds(500));
        Assert.Equal(RfidBeeperMode.AfterInventory, device.Beeper);
    }

    [Fact]
    public async Task A_pulled_cable_is_noticed_while_idle_and_a_network_change_reconnects_at_once()
    {
        var readers = new List<RfidReader> { Reader(1, (1, 9)) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await hub.StartAsync();
        await Task.Delay(500);
        var device = (SimulatedRfidDevice)hub.DeviceOf(Id(1))!;
        Assert.True((await hub.GetStatusAsync()).Single().Connected);

        device.Offline = true;   // cable pulled while no POS screen is open: the keep-alive notices
        var until = DateTime.UtcNow.AddSeconds(9);
        while (DateTime.UtcNow < until && (await hub.GetStatusAsync()).Single().Connected) await Task.Delay(200);
        Assert.False((await hub.GetStatusAsync()).Single().Connected);
        await Task.Delay(2500);   // a few failed attempts, so the next one is seconds away

        device.Offline = false;   // plugged back in; Windows reports the network change
        hub.RetryOfflineNow();
        until = DateTime.UtcNow.AddSeconds(1.5);
        while (DateTime.UtcNow < until && !(await hub.GetStatusAsync()).Single().Connected) await Task.Delay(100);
        Assert.True((await hub.GetStatusAsync()).Single().Connected);
    }

    [Fact]
    public void Attempts_to_reach_an_offline_reader_are_at_most_five_seconds_apart()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), RfidHub.Longer(TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(5), RfidHub.Longer(TimeSpan.FromSeconds(4)));
        Assert.Equal(TimeSpan.FromSeconds(5), RfidHub.Longer(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Admin_commands_run_between_inventory_rounds()
    {
        var readers = new List<RfidReader> { Reader(1, (1, 1)) };
        await using var hub = new RfidHub(_ => Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await hub.StartAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var listening = Collect(hub, 1, TimeSpan.FromSeconds(1));
        var info = await hub.ReadInfoAsync(Id(1), cts.Token);
        Assert.Equal("SIM", info.Firmware);
        Assert.Empty(await hub.ApplySettingsAsync(Id(1), cts.Token));
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
                ((SimulatedRfidDevice)hub.DeviceOf(r.Id)!).Place(port, $"{r.Port - 4000:X4}{port:X4}");
        static string TagOf(int station) => $"{(station + 3) / 4:X4}{(station - 1) % 4 + 1:X4}";

        // All 252 stations listen until each has heard its own tag (on a busy machine that can take a while), then a
        // little longer, so a read sent to the wrong station would still show up.
        var heard = Enumerable.Range(0, 253).Select(_ => new List<string>()).ToArray();
        int waiting = 252;
        var everyoneHeard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        async Task ListenAsync(int station)
        {
            bool own = false;
            try
            {
                await foreach (var e in hub.ListenAsync(station, stop.Token))
                {
                    heard[station].Add(e.Epc);
                    if (own || e.Epc != TagOf(station)) continue;
                    own = true;
                    if (Interlocked.Decrement(ref waiting) == 0) everyoneHeard.TrySetResult();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
        var listeners = Enumerable.Range(1, 252).Select(ListenAsync).ToList();
        var deadline = TimeSpan.FromSeconds(60);
        bool allHeard = await Task.WhenAny(everyoneHeard.Task, Task.Delay(deadline)) == everyoneHeard.Task;
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await stop.CancelAsync();
        await Task.WhenAll(listeners);

        Assert.True(allHeard, $"{Volatile.Read(ref waiting)} of 252 stations had not heard their tag after {deadline.TotalSeconds} seconds.");
        for (int s = 1; s <= 252; s++)
            Assert.Equal([TagOf(s)], heard[s].Distinct());
    }

    [Fact]
    public async Task A_hub_that_cannot_read_its_list_at_start_keeps_trying()
    {
        // The database or server is not reachable when the hub starts; it starts anyway and reads the list once it can.
        int attempts = 0;
        var readers = new List<RfidReader> { Reader(1, (1, 2)) };
        await using var hub = new RfidHub(_ => ++attempts <= 2
            ? throw new HttpRequestException("The server cannot be reached.")
            : Task.FromResult<IReadOnlyList<RfidReader>>(readers));
        await Assert.ThrowsAsync<HttpRequestException>(() => hub.StartAsync());
        Assert.False(hub.Loaded);
        Assert.Empty(await hub.GetStatusAsync());

        var problems = new List<Exception>();
        using var stop = new CancellationTokenSource();
        var keepCurrent = hub.KeepCurrentAsync(TimeSpan.FromMilliseconds(100), problems.Add, stop.Token);
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until && !hub.Loaded) await Task.Delay(50);
        await stop.CancelAsync();
        await keepCurrent;

        Assert.True(hub.Loaded);
        Assert.Equal("R1", Assert.Single(await hub.GetStatusAsync()).Name);
        Assert.IsType<HttpRequestException>(Assert.Single(problems));
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
                   "data: {\"epc\":\"E200AA\",\"stationNumber\":4,\"readerId\":\"00000001-0000-0000-0000-000000000000\",\"readerName\":\"R1\",\"antenna\":2,\"rssiDbm\":-48,\"seenAt\":\"2026-10-26T10:00:00\"}\n\n" +
                   ": ping\n\n";
        var handler = new SseHandler(body);
        using var client = new RfidHubClient("http://hub:5280", "secret", handler);
        // Only a safety limit: the loop below stops at the first event. Generous, for a busy CI runner.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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
        Assert.Equal(RfidAntennaPurpose.PointOfSale, first.Purpose);   // a hub from before purposes sends none
        Assert.Empty(await client.GetStatusAsync(cts.Token));
    }

    [Fact]
    public async Task The_gate_asks_the_hub_for_gate_reads_and_drops_the_others()
    {
        var body = ": station 4\n\n" +
                   "data: {\"epc\":\"AAAA01\",\"stationNumber\":4,\"readerId\":\"00000001-0000-0000-0000-000000000000\",\"readerName\":\"R1\",\"antenna\":1,\"rssiDbm\":-48,\"seenAt\":\"2026-11-14T10:00:00\"}\n\n" +
                   "data: {\"epc\":\"6A7E02\",\"stationNumber\":4,\"readerId\":\"00000001-0000-0000-0000-000000000000\",\"readerName\":\"R1\",\"antenna\":2,\"rssiDbm\":-50,\"seenAt\":\"2026-11-14T10:00:01\",\"purpose\":2}\n\n";
        var handler = new SseHandler(body);
        using var client = new RfidHubClient("http://hub:5280", null, handler);
        // Only a safety limit: the loop below stops at the first event. Generous, for a busy CI runner.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        RfidTagEvent? first = null;
        await foreach (var e in client.ListenAsync(4, RfidListen.Gate, cts.Token))
        {
            first = e;
            break;
        }
        Assert.NotNull(first);
        Assert.Equal(("6A7E02", RfidAntennaPurpose.Gate), (first.Epc, first.Purpose));
        Assert.Contains("/stations/4/events?purposes=Gate", handler.Paths);
    }
}
