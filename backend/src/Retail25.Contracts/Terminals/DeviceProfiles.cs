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
/// The regulatory band a reader may transmit in. Values are the wire values of the UHF serial
/// protocol's <c>SetFrequencyRegion</c>, so this enum is the protocol rather than a mapping onto it.
/// </summary>
public enum RadioRegion
{
    /// <summary>865.1–867.9 MHz. Europe and most of the world outside the Americas.</summary>
    Fcc = 1,

    /// <summary>902.75–927.25 MHz. North America.</summary>
    Etsi = 2,

    /// <summary>920.125–924.875 MHz. Mainland China.</summary>
    Chn = 3,
}

/// <summary>Reader-to-tag data rate and encoding. Protocol wire values.</summary>
public enum RfLinkProfile
{
    Fm0_40kHz = 0xD0,

    /// <summary>The vendor's default, and ours: the one that works in a shop.</summary>
    Miller4_250kHz = 0xD1,

    Miller4_300kHz = 0xD2,
    Fm0_400kHz = 0xD3,
}

/// <summary>When the reader's own buzzer sounds. Protocol wire values.</summary>
public enum BeeperMode
{
    Quiet = 0,
    AfterInventory = 1,
    EveryTag = 2,
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
    long Id,
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
    bool ContinuousMode,

    // The reader's own hardware configuration, pushed to the device on every connect. Defaulted so
    // an older server talking to a newer agent still produces a usable profile rather than a
    // deserialisation failure — and so the defaults are the conservative ones: quiet, legal band,
    // the vendor's recommended link profile.
    string OutputPowerDbm = "30",
    RadioRegion Region = RadioRegion.Fcc,
    int FrequencyStartIndex = 0,
    int FrequencyEndIndex = 0,
    RfLinkProfile LinkProfile = RfLinkProfile.Miller4_250kHz,
    BeeperMode Beeper = BeeperMode.Quiet,
    int AntennaReturnLossThresholdDb = 0,
    bool ImpinjFastTid = false,
    bool DenseReaderMode = false,
    int DeviceAddress = 0xFF);

/// <summary>
/// Printer wiring. Every escape sequence is a decimal-ASCII string, because Epson cuts with
/// <c>27,105</c> and Star with <c>27,100,48</c> and a store replacing a printer should not need a build.
/// </summary>
public sealed record PrinterProfileContract(
    long Id,
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
    long Id,
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
    long Id,
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
    long StationId,
    string StationCode,
    ReaderMode ReaderMode,
    ReaderProfileContract? Reader,
    PrinterProfileContract? Printer,
    ScaleProfileContract? Scale,
    PoleDisplayProfileContract? PoleDisplay);
