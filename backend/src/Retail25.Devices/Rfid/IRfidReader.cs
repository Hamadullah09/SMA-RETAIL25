using Retail25.Contracts.Terminals;

namespace Retail25.Devices.Rfid;

/// <summary>
/// A tag source (decision Q3).
/// <para>
/// The port exists so the whole flow â€” ingest, debounce, cart, rejection reasons, UI â€” is developable
/// and testable with no hardware in the room. That is not a convenience: RFID readers are expensive,
/// slow to configure and impossible to put in CI, and a design that only works with one attached
/// would leave the most complex path in the system permanently untested.
/// </para>
/// </summary>
public interface IRfidReader : IAsyncDisposable
{
    /// <summary>Human-readable, for the status strip and the logs.</summary>
    string Description { get; }

    bool IsConnected { get; }

    Task ConnectAsync(ReaderProfileContract profile, CancellationToken ct);

    /// <summary>Begins inventorying. Idempotent â€” calling it twice must not start two sessions.</summary>
    Task StartAsync(CancellationToken ct);

    Task StopAsync(CancellationToken ct);

    /// <summary>Streams reads until the token is cancelled or the reader faults.</summary>
    IAsyncEnumerable<TagRead> ReadsAsync(CancellationToken ct);

    /// <summary>
    /// What the device says about itself right now â€” firmware, temperature, the settings it is
    /// actually running, and whether each antenna port has something connected to it.
    /// <para>
    /// Asked of the hardware rather than read back from the profile, and that is the entire point.
    /// The profile says what the reader was told; this says what it did. When a shop reports poor
    /// range, the useful fact is that the reader is transmitting at 2 dBm â€” not that somebody typed
    /// 30 into a form once.
    /// </para>
    /// <para>
    /// Readers that cannot answer (the simulator, LLRP) return what they can and leave the rest null,
    /// rather than throwing: a diagnostics screen that fails entirely because one field is
    /// unavailable is worse than one that says "unknown".
    /// </para>
    /// </summary>
    Task<ReaderDiagnostics> ReadDiagnosticsAsync(CancellationToken ct);

    /// <summary>
    /// Pushes the profile's hardware settings into the device.
    /// <para>
    /// Called on every connect, not only on change. A reader that has been swapped, factory-reset or
    /// reconfigured by somebody with the vendor's demo open is otherwise silently running settings
    /// nobody chose, and the first anyone knows is a basket that will not read.
    /// </para>
    /// <para>
    /// Returns what could not be applied rather than throwing on the first refusal. A reader that
    /// rejects one setting should still get the other eight.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<string>> ApplySettingsAsync(ReaderProfileContract profile, CancellationToken ct);
}

/// <summary>
/// A snapshot of what a reader reports about itself. Every field is nullable because every reader
/// answers a different subset, and "we did not ask" and "it said zero" are different facts.
/// </summary>
public sealed record ReaderDiagnostics
{
    public string? FirmwareVersion { get; init; }

    public int? TemperatureCelsius { get; init; }

    /// <summary>Transmit power in dBm, per antenna port, as the device reports it.</summary>
    public IReadOnlyList<int>? OutputPowerDbm { get; init; }

    public string? Region { get; init; }

    public int? FrequencyStartIndex { get; init; }

    public int? FrequencyEndIndex { get; init; }

    public string? LinkProfile { get; init; }

    public int? WorkAntenna { get; init; }

    public int? AntennaReturnLossThresholdDb { get; init; }

    public bool? ImpinjFastTid { get; init; }

    /// <summary>Input pin states, lowest pin first.</summary>
    public IReadOnlyList<bool>? GpioInputs { get; init; }

    /// <summary>Measured return loss per antenna port in dB, where the reader will measure it.</summary>
    public IReadOnlyDictionary<int, int>? ReturnLossDb { get; init; }

    /// <summary>Anything that could not be read, in words, for the screen rather than the log.</summary>
    public IReadOnlyList<string> Unavailable { get; init; } = [];
}
