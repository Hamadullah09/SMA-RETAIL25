using Retail25.Domain.Common;

namespace Retail25.Domain.Terminals;

/// <summary>
/// A physical POS workstation (guide p.77–78). Identified by a 3-digit code (001–999).
/// Each station has its own settings and connects to one or more peripherals via the agent.
/// </summary>
public sealed class Station : AggregateRoot, IAuditable
{
    private Station()
    {
    }

    public string StationCode { get; set; } = string.Empty;

    public Guid LocationId { get; set; }

    public bool FastScanMode { get; set; }

    public bool AutoSaveSales { get; set; } = true;

    public bool ConfirmBeforeSaving { get; set; }

    public bool ScanRandomWeightBarcodes { get; set; }

    public Guid? DefaultTenderTypeId { get; set; }

    /// <summary>Version of the Terminal Agent running on this machine.</summary>
    public string? AgentVersion { get; set; }

    public DateTimeOffset? LastHeartbeat { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Result<Station> Create(Guid locationId, string stationCode)
    {
        if (string.IsNullOrWhiteSpace(stationCode) || stationCode.Trim().Length > 3
            || !stationCode.Trim().All(char.IsDigit))
            return Result.Failure<Station>(new Error("station.code_invalid", "A station code must be 1–3 digits."));

        return Result.Success(new Station
        {
            LocationId = locationId,
            StationCode = stationCode.Trim().PadLeft(3, '0'),
        });
    }
}
