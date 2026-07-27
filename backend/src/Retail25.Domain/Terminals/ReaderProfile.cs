using Retail25.Domain.Common;

namespace Retail25.Domain.Terminals;

public enum ReaderProtocol
{
    Llrp = 0,
    Http = 1,
    Mqtt = 2,
    Simulator = 3,
}

/// <summary>
/// RFID reader configuration (doc 06). Antenna zones, RSSI thresholds and debounce windows
/// are all database rows, not code.
/// </summary>
public sealed class ReaderProfile : Entity, IAuditable
{
    private ReaderProfile()
    {
    }

    public Guid LocationId { get; set; }

    public string Name { get; set; } = "Default";

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 5084;

    public ReaderProtocol Protocol { get; set; } = ReaderProtocol.Simulator;

    /// <summary>JSON mapping of antenna numbers to zone names (Checkout, Exit, Receiving, Shelf).</summary>
    public string? AntennaZonesJson { get; set; }

    /// <summary>Minimum RSSI in dBm for a tag to be accepted (doc 06 §2).</summary>
    public int RssiThresholdDbm { get; set; } = -70;

    /// <summary>Cross-station debounce window in milliseconds (doc 06 §2).</summary>
    public int DebounceMs { get; set; } = 3000;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
