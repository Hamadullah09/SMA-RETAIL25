namespace Retail25.Contracts.Terminals;

/// <summary>How hard the reader should be working right now (doc 06 §5).</summary>
public enum ReaderMode
{
    Off = 0,
    OnDemand = 1,
    Continuous = 2,
}

public enum ReaderProtocol
{
    Llrp = 0,
    Http = 1,
    Mqtt = 2,
    Simulator = 3,

    /// <summary>
    /// The R2000-family "UHF RFID Reader Serial Interface Protocol" (v3.1) spoken by devices such as
    /// the D2184B over TCP — either the reader's own network interface, or a serial-to-Ethernet bridge
    /// (e.g. an IPort module) in front of a unit wired via RS-232.
    /// </summary>
    UhfSerial = 4,
}

/// <summary>
/// The reader's wiring and its scepticism settings.
/// <para>
/// Endpoint, antenna zoning, RSSI floor, read-count floor and both debounce windows are all values
/// the server sends down, never constants in the agent. The right numbers differ per store and per
/// antenna mount, and they are discovered by trial on site — an agent that had them compiled in
/// would need a new build every time a shelf moved.
/// </para>
/// </summary>
public sealed record ReaderProfileContract(
    Guid Id,
    string Name,
    string Host,
    int Port,
    ReaderProtocol Protocol,
    string AntennaZones,
    int RssiThresholdDbm,
    int MinimumReadCount,
    int DebounceMs,
    int CoalesceMs,
    int FlushIntervalMs,
    int MaxBatchSize,
    bool AutoAcceptBatches,
    bool ContinuousMode);

/// <summary>
/// Printer wiring. Every escape sequence is a decimal-ASCII string, because Epson cuts with
/// <c>27,105</c> and Star with <c>27,100,48</c> and a store replacing a printer should not need a build.
/// </summary>
public sealed record PrinterProfileContract(
    Guid Id,
    string Name,
    string? Port,
    string? SetupCommand,
    string? CutterCommand,
    string? RedCommand,
    string? BlackCommand,
    int DefaultCopies,
    bool PageEject,
    bool ExtraCopyOnCard,
    bool InitializeSerial,
    string Output,
    int Columns,
    string DrawerTrigger,
    int DrawerRepeat,
    bool OpenDrawerOnPrint);

public sealed record ScaleProfileContract(
    Guid Id,
    string Name,
    string Port,
    int BaudRate,
    int DataBits,
    string Parity,
    string StopBits,
    string GetWeightCommand,
    string ZeroCommand,
    string Unit,
    int TimeoutMs);

public sealed record PoleDisplayProfileContract(
    Guid Id,
    string Name,
    string Port,
    int BaudRate,
    int Line1Width,
    int Line2Width,
    string IdleLine1,
    string IdleLine2,
    string ClearCommand,
    string Line1Command,
    string Line2Command);

/// <summary>
/// Everything the agent needs to drive this till's hardware. Pulled on connect and pushed again
/// whenever an administrator changes it, so a peripheral swap is a settings edit (doc 06 §7).
/// </summary>
public sealed record TerminalProfileContract(
    Guid StationId,
    string StationCode,
    ReaderMode ReaderMode,
    ReaderProfileContract? Reader,
    PrinterProfileContract? Printer,
    ScaleProfileContract? Scale,
    PoleDisplayProfileContract? PoleDisplay);
