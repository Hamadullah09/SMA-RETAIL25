using System.Collections.Concurrent;
using RetailPlus.Core.Model;

namespace RetailPlus.Rfid;

/// <summary>
/// A reader that exists only in memory: tags are "placed" on an antenna and read back each round. Used by the tests,
/// and for trying RFID without hardware: give a reader the IP address <c>sim</c> and list demo tags in its notes, one
/// <c>antenna:EPC</c> per line (for example <c>1:E2000017221101441890A1B2</c>).
/// </summary>
public sealed class SimulatedRfidDevice : IRfidDevice
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, int>> _field = new();
    private RfidReader _applied;

    public SimulatedRfidDevice(RfidReader settings)
    {
        _applied = settings;
        foreach (var line in (settings.Notes ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split([':', '='], 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var antenna) && antenna is >= 1 and <= 4 && Core.Rules.Epc.Normalize(parts[1]) is { } epc)
                Place(antenna, epc);
        }
    }

    public bool IsConnected { get; private set; }
    public int Rounds { get; private set; }
    public TimeSpan RoundTime { get; set; } = TimeSpan.FromMilliseconds(40);
    public bool Offline { get; set; }

    /// <summary>Puts a tag in front of an antenna (with a signal strength in dBm).</summary>
    public void Place(int antenna, string epc, int rssiDbm = -45) => _field.GetOrAdd(antenna, _ => new())[epc] = rssiDbm;

    public void Remove(int antenna, string epc)
    {
        if (_field.TryGetValue(antenna, out var tags)) tags.TryRemove(epc, out _);
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (Offline) throw new RfidException("Simulated reader is offline.");
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task<InventoryRoundResult> InventoryAsync(IReadOnlyList<int> antennas, int dwellRounds, Action<TagRead> onTag, CancellationToken cancellationToken)
    {
        if (!IsConnected) throw new RfidException("The reader is not connected.");
        await Task.Delay(RoundTime, cancellationToken);
        Rounds++;
        int n = 0;
        foreach (var antenna in antennas)
        {
            if (!_field.TryGetValue(antenna, out var tags)) continue;
            foreach (var (epc, rssi) in tags)
            {
                onTag(new TagRead(antenna, epc, "3000", rssi, 33, DateTime.Now));
                n++;
            }
        }
        return new InventoryRoundResult(n, [], null);
    }

    public Task<ReaderInfo> ReadInfoAsync(CancellationToken cancellationToken) => Task.FromResult(new ReaderInfo
    {
        Firmware = "SIM",
        TemperatureC = 31,
        PowerDbm = Enumerable.Range(1, 4).Select(p => _applied.Antennas.FirstOrDefault(a => a.Port == p)?.PowerDbm ?? 30).ToList(),
        Region = _applied.Region,
        StartFrequency = _applied.StartFrequency,
        EndFrequency = _applied.EndFrequency,
        LinkProfile = _applied.LinkProfile,
        DrmEnabled = _applied.DrmEnabled,
        AntennaDetectorDb = _applied.AntennaDetectorDb,
        FastTid = _applied.FastTid,
        Gpio1High = false,
        Gpio2High = false,
    });

    public Task<IReadOnlyList<string>> ApplySettingsAsync(RfidReader target, CancellationToken cancellationToken)
    {
        _applied = target;
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<int> MeasureReturnLossAsync(int antenna, int frequencyIndex, CancellationToken cancellationToken) =>
        Task.FromResult(_field.ContainsKey(antenna) ? 18 : 12);

    public Task WriteGpioAsync(int gpio, bool high, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PingAsync(CancellationToken cancellationToken) =>
        IsConnected && !Offline ? Task.CompletedTask : throw new RfidException("The reader is not connected.");
}
