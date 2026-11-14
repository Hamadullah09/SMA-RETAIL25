using RetailPlus.Core.Model;

namespace RetailPlus.Rfid;

/// <summary>A tag seen by an antenna during an inventory round.</summary>
/// <param name="Antenna">Antenna port, 1–4.</param>
/// <param name="Epc">EPC as upper-case hex.</param>
public sealed record TagRead(int Antenna, string Epc, string Pc, int RssiDbm, int FrequencyIndex, DateTime SeenAt);

/// <summary>Outcome of one inventory round.</summary>
public sealed record InventoryRoundResult(int TagsRead, IReadOnlyList<int> MissingAntennas, string? Error);

/// <summary>Settings and state read back from a reader (the "Read" buttons of the vendor demo).</summary>
public sealed record ReaderInfo
{
    public string? Firmware { get; init; }
    public int? TemperatureC { get; init; }
    public IReadOnlyList<int>? PowerDbm { get; init; }
    public RfidFrequencyRegion? Region { get; init; }
    public int? StartFrequency { get; init; }
    public int? EndFrequency { get; init; }
    public int? CustomStartKHz { get; init; }
    public int? CustomSpacing { get; init; }
    public int? CustomChannels { get; init; }
    public RfidLinkProfile? LinkProfile { get; init; }
    public bool? DrmEnabled { get; init; }
    public int? AntennaDetectorDb { get; init; }
    public bool? FastTid { get; init; }
    public bool? Gpio1High { get; init; }
    public bool? Gpio2High { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>A command the reader refused (its error code is in the message).</summary>
public sealed class RfidException : Exception
{
    public RfidException() { }
    public RfidException(string message) : base(message) { }
    public RfidException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>One physical reader, real or simulated.</summary>
public interface IRfidDevice : IAsyncDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync();

    /// <summary>
    /// One inventory round over the given antenna ports (1–4). Tags are passed to <paramref name="onTag"/> as they
    /// arrive, before the round finishes.
    /// </summary>
    Task<InventoryRoundResult> InventoryAsync(IReadOnlyList<int> antennas, int dwellRounds, Action<TagRead> onTag, CancellationToken cancellationToken);

    Task<ReaderInfo> ReadInfoAsync(CancellationToken cancellationToken);

    /// <summary>Writes the radio settings to the reader; returns the settings it refused (empty when all were taken).</summary>
    Task<IReadOnlyList<string>> ApplySettingsAsync(RfidReader target, CancellationToken cancellationToken);

    Task ResetAsync(CancellationToken cancellationToken);

    /// <summary>Return loss of an antenna port in dB at a frequency (table index); higher is a better match.</summary>
    Task<int> MeasureReturnLossAsync(int antenna, int frequencyIndex, CancellationToken cancellationToken);

    /// <summary>Sets output GPIO 3 or 4 (e.g. a light or gate lock).</summary>
    Task WriteGpioAsync(int gpio, bool high, CancellationToken cancellationToken);

    /// <summary>A cheap command that proves the link still works (keep-alive while no one is reading).</summary>
    Task PingAsync(CancellationToken cancellationToken);

    /// <summary>Sets the reader's own buzzer (normally quiet: the POS beeps itself for each new tag).</summary>
    Task SetBeeperAsync(RfidBeeperMode mode, CancellationToken cancellationToken);
}

/// <summary>
/// A tag read routed to the POS station its antenna is assigned to, with what the antenna is for (a hub from before
/// antenna purposes sends none: point of sale).
/// </summary>
public sealed record RfidTagEvent(string Epc, int StationNumber, Guid ReaderId, string ReaderName, int Antenna, int RssiDbm, DateTime SeenAt,
    RfidAntennaPurpose Purpose = RfidAntennaPurpose.PointOfSale);

/// <summary>Which antennas of a station a listener hears, by what they are for.</summary>
public static class RfidListen
{
    /// <summary>The station's sale: point-of-sale antennas only.</summary>
    public static IReadOnlySet<RfidAntennaPurpose> Sale { get; } = new HashSet<RfidAntennaPurpose> { RfidAntennaPurpose.PointOfSale };

    /// <summary>The exit gate's alarm: gate antennas only.</summary>
    public static IReadOnlySet<RfidAntennaPurpose> Gate { get; } = new HashSet<RfidAntennaPurpose> { RfidAntennaPurpose.Gate };

    /// <summary>Registering tags and counting stock: every antenna of the station except the exit gate's.</summary>
    public static IReadOnlySet<RfidAntennaPurpose> Counter { get; } = new HashSet<RfidAntennaPurpose>
        { RfidAntennaPurpose.PointOfSale, RfidAntennaPurpose.TagRegistration, RfidAntennaPurpose.Shelf };

    /// <summary>True when a listener for <paramref name="purposes"/> (null: every antenna) hears an antenna for <paramref name="purpose"/>.</summary>
    public static bool Hears(IReadOnlySet<RfidAntennaPurpose>? purposes, RfidAntennaPurpose purpose) => purposes is null || purposes.Contains(purpose);

    /// <summary>The query string of the hub's event stream for these purposes ("" for every antenna).</summary>
    public static string Query(IReadOnlySet<RfidAntennaPurpose>? purposes) =>
        purposes is null ? "" : "?purposes=" + string.Join(",", purposes.Order());

    /// <summary>The purposes named in the hub's query string; null (every antenna) when none are named or one is not known.</summary>
    public static IReadOnlySet<RfidAntennaPurpose>? Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var set = new HashSet<RfidAntennaPurpose>();
        foreach (var name in query.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<RfidAntennaPurpose>(name, ignoreCase: true, out var purpose) || !Enum.IsDefined(purpose)) return null;
            set.Add(purpose);
        }
        return set.Count == 0 ? null : set;
    }
}

/// <summary>The live state of a reader as the hub sees it.</summary>
public sealed record ReaderStatus(
    Guid ReaderId,
    string Name,
    string Endpoint,
    bool Enabled,
    bool Connected,
    string State,
    string? LastError,
    DateTime? LastTagAt,
    long TagsRead,
    long Rounds,
    int ActiveAntennas,
    string? Firmware);

/// <summary>
/// What the program needs from the RFID side: tag reads for a station, reader status, and the administrator's
/// commands. Implemented by the in-process <see cref="RfidHub"/> and by <see cref="RfidHubClient"/> for a hub
/// running on another computer.
/// </summary>
public interface IRfidControl
{
    /// <summary>Tag reads from every antenna assigned to a station, until cancelled.</summary>
    IAsyncEnumerable<RfidTagEvent> ListenAsync(int stationNumber, CancellationToken cancellationToken);
    /// <summary>
    /// Tag reads from the station's antennas for these purposes (see <see cref="RfidListen"/>; null: every antenna), until
    /// cancelled. Antennas nobody hears are not read.
    /// </summary>
    IAsyncEnumerable<RfidTagEvent> ListenAsync(int stationNumber, IReadOnlySet<RfidAntennaPurpose>? purposes, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReaderStatus>> GetStatusAsync(CancellationToken cancellationToken = default);
    /// <summary>Re-reads readers, antennas and station assignments from the database.</summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);
    Task<ReaderInfo> ReadInfoAsync(Guid readerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ApplySettingsAsync(Guid readerId, CancellationToken cancellationToken = default);
    Task ResetAsync(Guid readerId, CancellationToken cancellationToken = default);
    Task<int> MeasureReturnLossAsync(Guid readerId, int antenna, int frequencyIndex, CancellationToken cancellationToken = default);
    Task WriteGpioAsync(Guid readerId, int gpio, bool high, CancellationToken cancellationToken = default);
}
