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
/// The regulatory band a reader may transmit in.
/// <para>
/// Values are the wire values of the UHF serial protocol's <c>SetFrequencyRegion</c>, so the enum is
/// the protocol rather than a translation of it. Which one is legal is decided by where the shop is,
/// not by what reads best — a reader on FCC channels in Europe is unlicensed transmission.
/// </para>
/// </summary>
public enum RadioRegion
{
    /// <summary>North America, 902.00–927.00 MHz.</summary>
    Fcc = 1,

    /// <summary>Europe and most of the world outside the Americas, 865.1–867.9 MHz.</summary>
    Etsi = 2,

    /// <summary>Mainland China, 920.125–924.875 MHz.</summary>
    Chn = 3,

    // The numbering is the device's, read off the device, and it is the opposite way round from the
    // order these regions are usually listed in. A live D2184B reports region 1 while its own utility
    // displays "FCC 902.00–927.00" — so 1 is FCC, not ETSI.
    //
    // Worth stating plainly because getting it backwards is not a bug that shows up as a crash. A
    // shop that picks FCC gets configured for the European band and reads nothing; a shop that picks
    // ETSI transmits on North American channels, in Europe, without a licence. Neither reports an
    // error. This was found by asking the hardware rather than by reading the specification.
}

/// <summary>
/// The reader-to-tag data rate and encoding.
/// <para>
/// Values are the protocol's own. The trade is range against speed: FM0 at 400 kHz empties a full
/// basket fastest and is the least tolerant of a noisy room, while Miller-4 at 250 kHz is what the
/// vendor ships as the default because it is the one that works in a shop.
/// </para>
/// </summary>
public enum RfLinkProfile
{
    /// <summary>Tari 25 µs, FM0, 40 kHz. Slowest and most robust.</summary>
    Fm0_40kHz = 0xD0,

    /// <summary>Tari 25 µs, Miller 4, 250 kHz. The vendor's default, and ours.</summary>
    Miller4_250kHz = 0xD1,

    /// <summary>Tari 25 µs, Miller 4, 300 kHz.</summary>
    Miller4_300kHz = 0xD2,

    /// <summary>Tari 6.25 µs, FM0, 400 kHz. Fastest, shortest range.</summary>
    Fm0_400kHz = 0xD3,
}

/// <summary>When the reader's buzzer sounds. Wire values of <c>SetBeeperMode</c>.</summary>
public enum BeeperMode
{
    /// <summary>Silent. The default: the till gives its own feedback, and two beeps is one too many.</summary>
    Quiet = 0,

    /// <summary>One beep when a round of inventory finishes.</summary>
    AfterInventory = 1,

    /// <summary>A beep per tag. Useful when commissioning stock, unbearable at a checkout.</summary>
    EveryTag = 2,
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

    public long LocationId { get; set; }

    /// <summary>Null means the profile is shared by the location; set means it belongs to one station.</summary>
    public long? StationId { get; set; }

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

    // -----------------------------------------------------------------------------------------
    // Reader hardware settings
    //
    // These are the reader's own configuration — the things the vendor's Windows demo writes over
    // the serial protocol. They live here, on the profile, rather than being left in the device,
    // because a reader is a replaceable part: swap a failed unit for a spare and the till should
    // work the same afternoon without anyone remembering what the old one was set to. The agent
    // applies the whole set on every connect, so the database is the truth and the device is a
    // cache of it.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Transmit power in dBm per antenna port, comma-separated, lowest port first ("30,30,30,30").
    /// <para>
    /// Per port because they do different jobs: a checkout antenna wants only enough power to reach
    /// the counter, while a door antenna wants the range. One figure for all four is how a checkout
    /// starts reading the shelf behind it.
    /// </para>
    /// </summary>
    public string OutputPowerDbm { get; set; } = "30";

    /// <summary>Which regulatory band the radio may use. Getting this wrong is an offence, not a setting.</summary>
    public RadioRegion Region { get; set; } = RadioRegion.Fcc;

    /// <summary>
    /// The slice of the region's band actually used, as the protocol's own channel indices.
    /// <para>
    /// Kept as indices rather than megahertz because that is what the reader accepts, and converting
    /// in only one direction is how a value drifts every time it is read and written back.
    /// </para>
    /// <para>
    /// Defaulted from <see cref="RadioFrequencyPlan"/> for the default region rather than left at
    /// zero. Channel numbering is shared across regions and FCC's window starts at 7, so a profile
    /// created with the language default of 0 was outside its own region's band from the moment it
    /// existed — and the update command rejects exactly that. The effect was a reader nobody could
    /// configure: every save of the settings screen came back 400, including a save that changed
    /// only the address, because the invalid pair was posted back untouched with everything else.
    /// </para>
    /// </summary>
    public int FrequencyStartIndex { get; set; } = RadioFrequencyPlan.MinChannel(RadioRegion.Fcc);

    public int FrequencyEndIndex { get; set; } = RadioFrequencyPlan.MaxChannel(RadioRegion.Fcc);

    /// <summary>Data rate and encoding. See <see cref="RfLinkProfile"/>.</summary>
    public RfLinkProfile LinkProfile { get; set; } = RfLinkProfile.Miller4_250kHz;

    public BeeperMode Beeper { get; set; } = BeeperMode.Quiet;

    /// <summary>
    /// Return loss, in dB, above which the reader refuses to transmit on a port.
    /// <para>
    /// Zero disables the check. It is worth having on: transmitting into a disconnected or damaged
    /// antenna reflects the power back into the output stage, and the reader that has been doing it
    /// for a month is the one that fails on a Saturday.
    /// </para>
    /// </summary>
    public int AntennaReturnLossThresholdDb { get; set; }

    /// <summary>
    /// Impinj's non-standard fast-TID read. Only Monza tags support it, and on tags that do not it
    /// makes inventory slower rather than faster — so it is off unless a shop knows its stock.
    /// </summary>
    public bool ImpinjFastTid { get; set; }

    /// <summary>Dense-reader mode: worth having where several readers share a ceiling, costly where they do not.</summary>
    public bool DenseReaderMode { get; set; }

    /// <summary>
    /// The unit's RS-485 address, for the one deployment that daisy-chains readers on a bus.
    /// <para>
    /// 255 (0xFF) is the protocol's broadcast address and is the sensible default: every reader
    /// answers to it whatever it has actually been given, so a single-reader till never needs to know.
    /// </para>
    /// </summary>
    public int DeviceAddress { get; set; } = 0xFF;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    /// <summary>
    /// Parses the antenna map. An unlisted antenna is <see cref="AntennaZone.Unassigned"/> and feeds
    /// nothing.
    /// <para>
    /// Both <c>;</c> and <c>,</c> separate entries. Only the semicolon was accepted originally, and
    /// the failure that produced was as quiet as it gets: an operator typing
    /// <c>1=Checkout,2=Checkout</c> — which reads perfectly naturally — left every antenna
    /// Unassigned, so every tag was filtered out and the till showed a healthy reader that never put
    /// anything on a sale. Accepting the obvious alternative costs nothing; a separator neither
    /// character can be part of a zone name.
    /// </para>
    /// </summary>
    public AntennaZone ZoneFor(int antenna)
    {
        foreach (var pair in AntennaZones.Split(
            [';', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

    public static ReaderProfile CreateDefault(long locationId, string name = "Default")
        => new() { LocationId = locationId, Name = name };
}
