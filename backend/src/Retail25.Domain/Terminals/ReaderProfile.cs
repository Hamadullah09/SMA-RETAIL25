using System.Globalization;
using Retail25.Domain.Common;

namespace Retail25.Domain.Terminals;

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
/// What an antenna is pointed at. Only <see cref="Checkout"/> antennas may put items in a cart —
/// this single distinction is the first and cheapest defence against reading the shelf behind the
/// till (doc 06 §2).
/// </summary>
public enum AntennaZone
{
    Unassigned = 0,
    Checkout = 1,
    Exit = 2,
    Receiving = 3,
    Shelf = 4,
}

/// <summary>
/// How a reader is reached and how sceptical to be about what it reports (doc 06 §2).
/// <para>
/// Bulk RFID at a checkout desk reads things nobody intended to sell. Every control that keeps those
/// reads out of the cart — zoning, an RSSI floor, a read-count floor, the debounce window — is a
/// column here rather than a constant, because the right values differ per store and per antenna
/// mount and are discovered by trial on site.
/// </para>
/// </summary>
public sealed class ReaderProfile : Entity, IAuditable, IStationScopedProfile
{
    public ReaderProfile()
    {
    }

    public Guid LocationId { get; set; }

    /// <summary>Null means the profile is shared by the location; set means it belongs to one station.</summary>
    public Guid? StationId { get; set; }

    public string Name { get; set; } = "Default";

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 5084;

    public ReaderProtocol Protocol { get; set; } = ReaderProtocol.Simulator;

    /// <summary>
    /// Antenna-to-zone map, e.g. <c>1=Checkout;2=Checkout;3=Exit</c>. Kept as a string so an
    /// administrator can edit it in the settings UI without a schema migration.
    /// </summary>
    public string AntennaZones { get; set; } = "1=Checkout";

    /// <summary>Tags quieter than this are ignored — a tag on the next shelf reads weaker than one in the basket.</summary>
    public int RssiThresholdDbm { get; set; } = -70;

    /// <summary>How many times a tag must be seen inside the window before it is believed.</summary>
    public int MinimumReadCount { get; set; } = 2;

    /// <summary>Cross-station arbitration window held in Redis (doc 06 §2).</summary>
    public int DebounceMs { get; set; } = 3000;

    /// <summary>Agent-side coalescing window. Pure noise reduction; must not cost a round trip.</summary>
    public int CoalesceMs { get; set; } = 250;

    /// <summary>How often the agent ships a batch to the server.</summary>
    public int FlushIntervalMs { get; set; } = 200;

    public int MaxBatchSize { get; set; } = 50;

    /// <summary>
    /// When false the cashier confirms each batch before it is priced; when true tags land straight
    /// on the cart. Stores with a well-shielded read zone turn this on (doc 06 §2 control 5).
    /// </summary>
    public bool AutoAcceptBatches { get; set; }

    /// <summary>Read continuously, or only while the cashier holds the read open.</summary>
    public bool ContinuousMode { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    /// <summary>Parses the antenna map. An unlisted antenna is <see cref="AntennaZone.Unassigned"/> and feeds nothing.</summary>
    public AntennaZone ZoneFor(int antenna)
    {
        foreach (var pair in AntennaZones.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number != antenna)
            {
                continue;
            }

            return Enum.TryParse<AntennaZone>(parts[1], ignoreCase: true, out var zone) ? zone : AntennaZone.Unassigned;
        }

        return AntennaZone.Unassigned;
    }

    public bool IsCheckoutAntenna(int antenna) => ZoneFor(antenna) == AntennaZone.Checkout;

    /// <summary>Applies the local pre-filter: right zone, loud enough, seen often enough (doc 06 §2).</summary>
    /// <summary>
    /// Whether a read is close enough, confident enough and on the right antenna to reach a cart.
    /// <para>
    /// <paramref name="rssiDbm"/> may be <see cref="UnmeasuredRssi"/>, meaning the reader saw the tag
    /// but reported no signal strength — which R2000-family readers do in real-time inventory mode.
    /// The proximity test is then skipped rather than failed. Failing it would reject every read from
    /// such a reader, and the remaining two conditions still do real work: the antenna must be one
    /// pointed at the counter, and one stray read is still not enough.
    /// </para>
    /// <para>
    /// This is a deliberate, narrow relaxation. It widens what reaches a cart on readers that do not
    /// measure, so a shop relying on proximity to keep the next aisle out of the basket should use a
    /// reader mode that reports RSSI — or lean on antenna zoning, which is unaffected.
    /// </para>
    /// </summary>
    public bool Accepts(int antenna, int rssiDbm, int readCount)
        => IsCheckoutAntenna(antenna)
            && (rssiDbm == UnmeasuredRssi || rssiDbm >= RssiThresholdDbm)
            && readCount >= MinimumReadCount;

    /// <summary>
    /// Mirrors <c>TagRead.UnknownRssi</c>. Duplicated rather than referenced because the domain does
    /// not depend on the contracts assembly, and one shared constant is not worth inverting that.
    /// </summary>
    public const int UnmeasuredRssi = int.MinValue;

    public static ReaderProfile CreateDefault(Guid locationId, string name = "Default")
        => new() { LocationId = locationId, Name = name };
}
