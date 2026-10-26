using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RetailPlus.Core.Model;
using RetailPlus.Rfid.D2184;

namespace RetailPlus.Rfid;

/// <summary>
/// Owns the connection to every reader and turns their antennas into per-station tag feeds.
/// <list type="bullet">
/// <item>One background loop per reader (async I/O, so 63 readers cost 63 sockets, not 63 threads).</item>
/// <item>Every enabled reader is connected as soon as the hub starts and kept connected (a keep-alive every 30
/// seconds notices a pulled cable); an antenna is read only while the station it is assigned to has someone listening,
/// so idle counters do not transmit and nearby antennas interfere less.</item>
/// <item>Reads below the reader's RSSI threshold are dropped, and a tag is reported to a station at most once per
/// <see cref="RfidReader.DedupSeconds"/>.</item>
/// <item>Each listener has a bounded queue; a slow listener loses its oldest reads instead of slowing the readers.</item>
/// <item>Offline readers are retried with back-off up to 30 seconds without affecting the others.</item>
/// </list>
/// </summary>
public sealed class RfidHub(Func<CancellationToken, Task<IReadOnlyList<RfidReader>>> loadConfiguration,
    Func<RfidReader, IRfidDevice>? deviceFactory = null) : IRfidControl, IAsyncDisposable
{
    private readonly Func<RfidReader, IRfidDevice> _deviceFactory = deviceFactory ?? DefaultDevice;
    private readonly ConcurrentDictionary<int, Worker> _workers = new();
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, Channel<RfidTagEvent>>> _listeners = new();
    private readonly ConcurrentDictionary<(int Station, string Epc), DateTime> _lastSent = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private IReadOnlyList<RfidReader> _disabled = [];
    private CancellationTokenSource _stopping = new();

    /// <summary>Diagnostic messages (connection problems); the hub service writes them to its log.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Real D2184 readers, or a simulated one when the reader's host is "sim".</summary>
    public static IRfidDevice DefaultDevice(RfidReader reader) =>
        string.Equals(reader.Host, "sim", StringComparison.OrdinalIgnoreCase) ? new SimulatedRfidDevice(reader) : new D2184Device(reader);

    /// <summary>The device behind a reader (the tests use this to place simulated tags).</summary>
    public IRfidDevice? DeviceOf(int readerId) => _workers.TryGetValue(readerId, out var w) ? w.Device : null;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _stopping = new CancellationTokenSource();
        return ReloadAsync(cancellationToken);
    }

    public async Task StopAsync()
    {
        await _stopping.CancelAsync();
        await Task.WhenAll(_workers.Values.Select(w => w.StopAsync()));
        _workers.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stopping.Dispose();
        _reloadGate.Dispose();
    }

    // Configuration ----------------------------------------------------------------------------------------------

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            var readers = await loadConfiguration(cancellationToken);
            var enabled = readers.Where(r => r.IsEnabled).ToDictionary(r => r.Id);
            foreach (var (id, worker) in _workers)
            {
                if (enabled.ContainsKey(id)) continue;
                _workers.TryRemove(id, out _);
                await worker.StopAsync();
            }
            foreach (var reader in enabled.Values)
            {
                if (_workers.TryGetValue(reader.Id, out var existing))
                {
                    var old = existing.Settings;
                    if (old.Transport == reader.Transport && old.Endpoint == reader.Endpoint && old.Address == reader.Address)
                    {
                        existing.Settings = reader;   // assignments, thresholds: no reconnect needed
                        continue;
                    }
                    await existing.StopAsync();
                }
                _workers[reader.Id] = StartWorker(reader);
            }
            _disabled = readers.Where(r => !r.IsEnabled).ToList();
            WakeAll();
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private Worker StartWorker(RfidReader reader)
    {
        var worker = new Worker(reader, _deviceFactory(reader), CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token));
        worker.Loop = Task.Run(() => RunAsync(worker, worker.Cancellation.Token));
        return worker;
    }

    // Reading ----------------------------------------------------------------------------------------------------

    private bool HasListeners(int station) => _listeners.TryGetValue(station, out var set) && !set.IsEmpty;

    private List<int> ActiveAntennas(RfidReader reader) => reader.Antennas
        .Where(a => a.IsEnabled && a.StationNumber is int s && HasListeners(s))
        .Select(a => a.Port).Distinct().Order().ToList();

    private async Task RunAsync(Worker w, CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            var active = ActiveAntennas(w.Settings);
            w.ActiveAntennas = active.Count;
            if (active.Count == 0)
            {
                // Nobody is listening: stay connected (connect now, keep alive, reconnect) but do not transmit.
                try
                {
                    backoff = await KeepConnectedAsync(w, backoff, cancellationToken);
                    await w.Wake.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                continue;
            }
            await w.Busy.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!w.Device.IsConnected)
                {
                    w.State = "Connecting";
                    await ConnectAsync(w, cancellationToken);
                    backoff = TimeSpan.FromSeconds(1);
                }
                await FollowBeeperAsync(w, cancellationToken);
                w.State = "Reading";
                var result = await w.Device.InventoryAsync(active, w.Settings.DwellRounds, tag => OnTag(w, tag), cancellationToken);
                Interlocked.Increment(ref w.Rounds);
                w.LastActivity = DateTime.UtcNow;
                w.LastError = result.MissingAntennas.Count > 0
                    ? $"Antenna {string.Join(", ", result.MissingAntennas)} not connected"
                    : result.Error;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                w.Busy.Release();
                break;
            }
            catch (Exception ex)
            {
                w.State = "Offline";
                w.LastError = ex.Message;
                Log?.Invoke($"Reader {w.Settings.Name} ({w.Settings.Endpoint}): {ex.Message}");
                try { await w.Device.DisconnectAsync(); } catch (Exception) { }
                w.Busy.Release();
                try
                {
                    await Task.Delay(backoff, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
                continue;
            }
            w.Busy.Release();
        }
        try { await w.Device.DisconnectAsync(); } catch (Exception) { }
    }

    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

    /// <summary>Connects an idle reader (with back-off) or pings it when it has been quiet; returns the next back-off.</summary>
    private async Task<TimeSpan> KeepConnectedAsync(Worker w, TimeSpan backoff, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (w.Device.IsConnected && now - w.LastActivity < KeepAliveInterval)
        {
            w.State = "Connected";
            return backoff;
        }
        if (!w.Device.IsConnected && now < w.NextConnectAttempt) return backoff;
        await w.Busy.WaitAsync(cancellationToken);
        try
        {
            if (w.Device.IsConnected)
            {
                await w.Device.PingAsync(cancellationToken);
                w.LastActivity = DateTime.UtcNow;
                w.State = "Connected";
                return backoff;
            }
            w.State = "Connecting";
            await ConnectAsync(w, cancellationToken);
            w.State = "Connected";
            return TimeSpan.FromSeconds(1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            w.State = "Offline";
            w.LastError = ex.Message;
            Log?.Invoke($"Reader {w.Settings.Name} ({w.Settings.Endpoint}): {ex.Message}");
            try { await w.Device.DisconnectAsync(); } catch (Exception) { }
            w.NextConnectAttempt = DateTime.UtcNow + backoff;
            return TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
        finally
        {
            w.Busy.Release();
        }
    }

    private static async Task ConnectAsync(Worker w, CancellationToken cancellationToken)
    {
        await w.Device.ConnectAsync(cancellationToken);
        w.LastActivity = DateTime.UtcNow;
        w.LastError = null;
        w.Beeper = null;   // a reader that was switched off may have its own buzzer setting again
        await FollowBeeperAsync(w, cancellationToken);
        if (w.Firmware is null)
        {
            try
            {
                w.Firmware = (await w.Device.ReadInfoAsync(cancellationToken)).Firmware;
            }
            catch (RfidException)
            {
            }
        }
    }

    private void OnTag(Worker w, TagRead tag)
    {
        Interlocked.Increment(ref w.TagsRead);
        w.LastTagAt = tag.SeenAt;
        var reader = w.Settings;
        var antenna = reader.Antennas.FirstOrDefault(a => a.Port == tag.Antenna);
        if (antenna?.StationNumber is not int station || !antenna.IsEnabled) return;
        if (tag.RssiDbm < reader.RssiThresholdDbm) return;
        var key = (station, tag.Epc);
        var now = tag.SeenAt;
        if (_lastSent.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(Math.Max(0, reader.DedupSeconds))) return;
        _lastSent[key] = now;
        if (_lastSent.Count > 20_000) Prune(now);
        if (!_listeners.TryGetValue(station, out var set)) return;
        var e = new RfidTagEvent(tag.Epc, station, reader.Id, reader.Name, tag.Antenna, tag.RssiDbm, now);
        foreach (var channel in set.Values) channel.Writer.TryWrite(e);
    }

    private void Prune(DateTime now)
    {
        foreach (var (key, at) in _lastSent)
            if (now - at > TimeSpan.FromMinutes(2)) _lastSent.TryRemove(key, out _);
    }

    private void WakeAll()
    {
        foreach (var w in _workers.Values)
            if (w.Wake.CurrentCount == 0) w.Wake.Release();
    }

    public async IAsyncEnumerable<RfidTagEvent> ListenAsync(int stationNumber, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<RfidTagEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        var id = Guid.NewGuid();
        _listeners.GetOrAdd(stationNumber, _ => new())[id] = channel;
        WakeAll();
        try
        {
            await foreach (var e in channel.Reader.ReadAllAsync(cancellationToken))
                yield return e;
        }
        finally
        {
            if (_listeners.TryGetValue(stationNumber, out var set)) set.TryRemove(id, out _);
        }
    }

    // Status and administration ---------------------------------------------------------------------------------

    public Task<IReadOnlyList<ReaderStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var list = _workers.Values.Select(w => new ReaderStatus(w.Settings.Id, w.Settings.Name, w.Settings.Endpoint, true, w.Device.IsConnected,
                w.State, w.LastError, w.LastTagAt, Interlocked.Read(ref w.TagsRead), Interlocked.Read(ref w.Rounds), w.ActiveAntennas,
                w.Firmware ?? w.Settings.FirmwareVersion))
            .Concat(_disabled.Select(r => new ReaderStatus(r.Id, r.Name, r.Endpoint, false, false, "Disabled", null, null, 0, 0, 0, r.FirmwareVersion)))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<ReaderStatus>>(list);
    }

    /// <summary>
    /// Runs an administrator command on a reader between two inventory rounds. A reader that is disabled (or not
    /// loaded yet) gets a short-lived connection of its own.
    /// </summary>
    private async Task<T> OnReaderAsync<T>(int readerId, Func<IRfidDevice, RfidReader, Task<T>> operation, CancellationToken cancellationToken)
    {
        var fresh = (await loadConfiguration(cancellationToken)).FirstOrDefault(r => r.Id == readerId)
                    ?? throw new RfidException("This reader is no longer in the list.");
        if (!_workers.TryGetValue(readerId, out var w))
        {
            await using var temporary = _deviceFactory(fresh);
            await temporary.ConnectAsync(cancellationToken);
            return await operation(temporary, fresh);
        }
        await w.Busy.WaitAsync(cancellationToken);
        try
        {
            if (!w.Device.IsConnected) await ConnectAsync(w, cancellationToken);
            return await operation(w.Device, fresh);
        }
        finally
        {
            w.Busy.Release();
        }
    }

    public Task<ReaderInfo> ReadInfoAsync(int readerId, CancellationToken cancellationToken = default) =>
        OnReaderAsync(readerId, (d, _) => d.ReadInfoAsync(cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<string>> ApplySettingsAsync(int readerId, CancellationToken cancellationToken = default)
    {
        var problems = await OnReaderAsync(readerId, (d, r) => d.ApplySettingsAsync(r, cancellationToken), cancellationToken);
        await ReloadAsync(cancellationToken);
        return problems;
    }

    public Task ResetAsync(int readerId, CancellationToken cancellationToken = default) =>
        OnReaderAsync(readerId, async (d, _) => { await d.ResetAsync(cancellationToken); return 0; }, cancellationToken);

    public Task<int> MeasureReturnLossAsync(int readerId, int antenna, int frequencyIndex, CancellationToken cancellationToken = default) =>
        OnReaderAsync(readerId, (d, _) => d.MeasureReturnLossAsync(antenna, frequencyIndex, cancellationToken), cancellationToken);

    public Task WriteGpioAsync(int readerId, int gpio, bool high, CancellationToken cancellationToken = default) =>
        OnReaderAsync(readerId, async (d, _) => { await d.WriteGpioAsync(gpio, high, cancellationToken); return 0; }, cancellationToken);

    /// <summary>
    /// The reader's own buzzer follows its setting, sent on every connection and whenever the setting changes. Readers
    /// beep at every tag on the antenna each round out of the box; the setting is normally Quiet and the POS beeps once
    /// per new tag instead.
    /// </summary>
    private static async Task FollowBeeperAsync(Worker w, CancellationToken cancellationToken)
    {
        var mode = w.Settings.BeeperMode;
        if (w.Beeper == mode) return;
        try
        {
            await w.Device.SetBeeperAsync(mode, cancellationToken);
        }
        catch (RfidException)
        {
            // A reader that refuses keeps its buzzer as it is; it still reads tags.
        }
        w.Beeper = mode;
    }

    private sealed class Worker(RfidReader settings, IRfidDevice device, CancellationTokenSource cancellation)
    {
        /// <summary>The buzzer mode last sent to the reader since it connected.</summary>
        public RfidBeeperMode? Beeper;
        public volatile RfidReader Settings = settings;
        public IRfidDevice Device { get; } = device;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public SemaphoreSlim Wake { get; } = new(0, 1);
        /// <summary>Held during an inventory round or an administrator command, so the two never overlap.</summary>
        public SemaphoreSlim Busy { get; } = new(1, 1);
        public Task? Loop { get; set; }
        public volatile string State = "Starting";
        public volatile string? LastError;
        public volatile string? Firmware;
        public DateTime? LastTagAt { get; set; }
        public DateTime LastActivity { get; set; }
        public DateTime NextConnectAttempt { get; set; }
        public int ActiveAntennas { get; set; }
        public long TagsRead;
        public long Rounds;

        public async Task StopAsync()
        {
            await Cancellation.CancelAsync();
            if (Loop is not null)
            {
                try { await Loop; } catch (Exception) { }
            }
            await Device.DisposeAsync();
            Cancellation.Dispose();
        }
    }
}
