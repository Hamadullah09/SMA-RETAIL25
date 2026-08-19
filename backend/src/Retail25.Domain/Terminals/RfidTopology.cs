using Retail25.Domain.Common;

namespace Retail25.Domain.Terminals;

/// <summary>
/// The machine an agent runs on.
/// <para>
/// Separate from <see cref="Station"/> because they were conflated and that is the root of the
/// limitation being removed: a station used to <em>be</em> a PC, so a PC could only ever be one
/// station, and a reader hanging off it could only serve that one. A device is a computer; a station
/// is a place a customer stands. One device may drive several readers and therefore many stations.
/// </para>
/// <para>
/// Identity is <see cref="DeviceKey"/> — issued once at enrolment and never derived from anything
/// the network can change. Hostname and address are reported by the agent and are description, not
/// identity: a DHCP lease change must not make a machine into a different machine.
/// </para>
/// </summary>
public sealed class Device : AggregateRoot, IAuditable
{
    public static readonly Error KeyRequired = new("device.key_required", "A device needs an identity.");

    public Device()
    {
    }

    /// <summary>Stable identity, e.g. <c>PC-001</c>. Never an address.</summary>
    public string DeviceKey { get; private set; } = string.Empty;

    public string? Name { get; private set; }

    public long LocationId { get; private set; }

    // --- Reported by the agent, not identity ---------------------------------------------------

    public string? Hostname { get; set; }

    /// <summary>
    /// Whatever the agent currently answers on. Recorded so an administrator can find the machine,
    /// and for no other purpose — nothing resolves a device by it.
    /// </summary>
    public string? LocalIpAddresses { get; set; }

    public string? OperatingSystem { get; set; }

    public string? AgentVersion { get; set; }

    /// <summary>
    /// When the agent last spoke. The single source of "is this machine alive", so that liveness is
    /// not duplicated across the stations it happens to serve.
    /// </summary>
    public DateTimeOffset? LastHeartbeat { get; set; }

    public bool IsEnabled { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public bool IsOnline(DateTimeOffset now, TimeSpan within)
        => LastHeartbeat is { } beat && now - beat < within;

    public static Result<Device> Create(long locationId, string deviceKey, string? name = null)
    {
        var key = (deviceKey ?? string.Empty).Trim().ToUpperInvariant();

        if (key.Length == 0)
        {
            return Result.Failure<Device>(KeyRequired);
        }

        return Result.Success(new Device
        {
            LocationId = locationId,
            DeviceKey = key,
            Name = name?.Trim(),
        });
    }

    public void Rename(string? name) => Name = name?.Trim();

    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}

/// <summary>
/// A physical RFID reader, identified by what is printed on it rather than where it happens to be.
/// <para>
/// The reader used to be identified by host and port, which is an address rather than an identity:
/// a DHCP change turned it into a different reader as far as the system was concerned, and every
/// station assignment hanging off it broke. <see cref="SerialNumber"/> is the stable fact — it is
/// stamped on the hardware and survives being moved to another switch port, subnet or building.
/// </para>
/// <para>
/// <see cref="Host"/> and <see cref="Port"/> remain, demoted to what they always were: the current
/// way to reach it. They are expected to change and are updated in place when discovery finds the
/// same serial at a new address.
/// </para>
/// </summary>
public sealed class RfidReader : AggregateRoot, IAuditable
{
    public static readonly Error KeyRequired = new("reader.key_required", "A reader needs an identity.");

    public static readonly Error AntennaOutOfRange =
        new("reader.antenna_out_of_range", "That antenna number does not exist on this reader.");

    public RfidReader()
    {
    }

    /// <summary>Stable identity, e.g. <c>RFID-001</c>.</summary>
    public string ReaderKey { get; private set; } = string.Empty;

    /// <summary>
    /// The hardware's own identity, read from the device where the protocol exposes it.
    /// <para>
    /// Nullable because not every protocol reports one. Where it is absent the reader is identified
    /// by <see cref="ReaderKey"/> alone and the address is trusted more than anybody would like —
    /// that is a known weakness of those protocols, recorded here rather than hidden.
    /// </para>
    /// </summary>
    public string? SerialNumber { get; set; }

    public string? Model { get; set; }

    public long LocationId { get; private set; }

    /// <summary>Which machine's agent currently drives this reader. Null while unassigned.</summary>
    public long? DeviceId { get; set; }

    // --- Mutable network properties ------------------------------------------------------------

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public ReaderTransportProtocol Protocol { get; set; } = ReaderTransportProtocol.Simulator;

    /// <summary>
    /// How many antenna ports the unit has. Four on the common models, but stored rather than
    /// assumed: hardcoding four is exactly the kind of special case that makes a two-port desk
    /// reader and a sixteen-port portal both wrong.
    /// </summary>
    public int AntennaCount { get; set; } = 4;

    public DateTimeOffset? LastSeen { get; set; }

    public bool IsEnabled { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public bool HasAntenna(int antennaNumber) => antennaNumber >= 1 && antennaNumber <= AntennaCount;

    public static Result<RfidReader> Create(
        long locationId,
        string readerKey,
        string? serialNumber = null,
        int antennaCount = 4)
    {
        var key = (readerKey ?? string.Empty).Trim().ToUpperInvariant();

        if (key.Length == 0)
        {
            return Result.Failure<RfidReader>(KeyRequired);
        }

        if (antennaCount < 1)
        {
            return Result.Failure<RfidReader>(AntennaOutOfRange.With("antennaCount", antennaCount));
        }

        return Result.Success(new RfidReader
        {
            LocationId = locationId,
            ReaderKey = key,
            SerialNumber = serialNumber?.Trim(),
            AntennaCount = antennaCount,
        });
    }

    /// <summary>
    /// Follows the reader to a new address, keeping its identity.
    /// <para>
    /// The whole point of the serial number: this is an update, not a new reader, so every antenna
    /// assignment hanging off it survives a DHCP change untouched.
    /// </para>
    /// </summary>
    public void MoveTo(string host, int port)
    {
        Host = host.Trim();
        Port = port;
    }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}

/// <summary>How the agent talks to a reader. Mirrors the existing driver set.</summary>
public enum ReaderTransportProtocol
{
    Simulator = 0,
    Llrp = 1,
    UhfSerial = 2,
    Http = 3,
    Mqtt = 4,
}

/// <summary>
/// One antenna of one reader, standing for one station.
/// <para>
/// This is the row that replaces <c>Reader → Station</c> with <c>Reader + Antenna → Station</c>, and
/// it is the whole architectural change: four of these against one reader make four independent
/// stations out of one box, and 252 of them make 252 stations out of 63 boxes with no different
/// code running. Scale arrives as rows, not as branches.
/// </para>
/// <para>
/// Unique on (reader, antenna) — enforced in the database, not merely checked — so one physical
/// antenna can never quietly feed two tills. That is the failure this model exists to make
/// impossible, and a validation rule in a handler would not survive two administrators saving at
/// once.
/// </para>
/// </summary>
public sealed class ReaderAntennaAssignment : AggregateRoot, IAuditable
{
    public static readonly Error AntennaInvalid =
        new("antenna_assignment.antenna_invalid", "An antenna number must be 1 or more.");

    public ReaderAntennaAssignment()
    {
    }

    public long ReaderId { get; private set; }

    public int AntennaNumber { get; private set; }

    public long StationId { get; private set; }

    /// <summary>
    /// Disabled rather than deleted, so an antenna can be taken out of service for an afternoon
    /// without losing which station it belongs to.
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static Result<ReaderAntennaAssignment> Create(long readerId, int antennaNumber, long stationId)
    {
        if (antennaNumber < 1)
        {
            return Result.Failure<ReaderAntennaAssignment>(AntennaInvalid.With("antenna", antennaNumber));
        }

        return Result.Success(new ReaderAntennaAssignment
        {
            ReaderId = readerId,
            AntennaNumber = antennaNumber,
            StationId = stationId,
        });
    }

    public void ReassignTo(long stationId) => StationId = stationId;

    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}
