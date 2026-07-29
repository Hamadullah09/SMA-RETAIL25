using Retail25.Domain.Common;

namespace Retail25.Domain.Terminals;

/// <summary>How aggressively the reader should be running right now (doc 06 §5).</summary>
public enum ReaderMode
{
    Off = 0,
    OnDemand = 1,
    Continuous = 2,
}

/// <summary>
/// A physical POS workstation, identified by the three-digit code staff already know from the
/// legacy system (guide p.77–78). Station rows carry the per-till overrides of the store-wide
/// <c>PosPolicy</c>, so one touchscreen till can run fast-scan while the service desk does not.
/// </summary>
public sealed class Station : AggregateRoot, IAuditable
{
    public static readonly Error CodeInvalid = new("station.code_invalid", "A station code must be 1–3 digits.");

    public Station()
    {
    }

    public string StationCode { get; set; } = string.Empty;

    public Guid LocationId { get; set; }

    public string? Name { get; set; }

    // --- Per-station overrides of PosPolicy ----------------------------------------------------

    /// <summary>Null defers to the store policy. Only an explicit value overrides it.</summary>
    public bool? FastScanMode { get; set; }

    public bool? AutoSaveSales { get; set; }

    public bool? ConfirmBeforeSaving { get; set; }

    /// <summary>Type 2 embedded-price barcodes are only recognised where a scale actually feeds this till (guide p.98).</summary>
    public bool? ScanRandomWeightBarcodes { get; set; }

    public Guid? DefaultTenderTypeId { get; set; }

    // --- Peripherals ---------------------------------------------------------------------------

    public Guid? PrinterProfileId { get; set; }

    public Guid? ReaderProfileId { get; set; }

    public Guid? ScaleProfileId { get; set; }

    public Guid? PoleDisplayProfileId { get; set; }

    public ReaderMode ReaderMode { get; set; } = ReaderMode.OnDemand;

    // --- Agent -----------------------------------------------------------------------------------

    /// <summary>Version of Retail25.TerminalAgent last reported by this machine.</summary>
    public string? AgentVersion { get; set; }

    public DateTimeOffset? LastHeartbeat { get; set; }

    /// <summary>
    /// Hash of the pairing token the agent presents. The token itself is minted once at registration
    /// and never stored, so a database read cannot impersonate a till.
    /// </summary>
    public string? AgentTokenHash { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    /// <summary>An agent is considered present if it checked in within the last 15 seconds (doc 06 §6).</summary>
    public bool IsAgentOnline(DateTimeOffset now) => LastHeartbeat is { } beat && now - beat < TimeSpan.FromSeconds(15);

    public static Result<Station> Create(Guid locationId, string stationCode, string? name = null)
    {
        var trimmed = stationCode?.Trim() ?? string.Empty;

        if (trimmed.Length is 0 or > 3 || !trimmed.All(char.IsDigit))
        {
            return Result.Failure<Station>(CodeInvalid.With("value", stationCode));
        }

        return Result.Success(new Station
        {
            LocationId = locationId,
            StationCode = trimmed.PadLeft(3, '0'),
            Name = name?.Trim(),
        });
    }

    public void Heartbeat(string? agentVersion, DateTimeOffset now)
    {
        AgentVersion = agentVersion;
        LastHeartbeat = now;
    }

    public void SetReaderMode(ReaderMode mode) => ReaderMode = mode;

    public void AssignPeripherals(Guid? printerProfileId, Guid? readerProfileId, Guid? scaleProfileId, Guid? poleDisplayProfileId)
    {
        PrinterProfileId = printerProfileId;
        ReaderProfileId = readerProfileId;
        ScaleProfileId = scaleProfileId;
        PoleDisplayProfileId = poleDisplayProfileId;
    }
}
