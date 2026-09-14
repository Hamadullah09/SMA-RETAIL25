using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Application.Rfid;
using Retail25.Domain.Common;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Terminals;

public sealed record DeviceRow(
    long Id,
    string DeviceKey,
    string? Name,
    string? Hostname,
    string? LocalIpAddresses,
    string? AgentVersion,
    bool IsOnline,
    DateTimeOffset? LastHeartbeat,
    int ReaderCount);

public sealed record AntennaRow(int AntennaNumber, long? StationId, string? StationCode, bool Enabled);

/// <summary>
/// Where one reader stands between "something answered on the network" and "tags are arriving".
/// <para>
/// Five states rather than a connected flag because they need five different responses, and a screen
/// that showed two would send somebody to the wrong place. Each is derived from facts the system
/// already records — an agent's heartbeat, a reader's last sighting, the enabled flag — so nothing
/// here is a new column that could disagree with the old ones.
/// </para>
/// </summary>
public enum ReaderState
{
    /// <summary>
    /// Found by a sweep, but no machine has claimed it. It is on the network and reads nothing:
    /// somebody has to assign its antennas before it means anything.
    /// </summary>
    Discovered = 0,

    /// <summary>An agent is holding it and has said so recently. Tags will arrive.</summary>
    Connected = 1,

    /// <summary>
    /// The machine that drives it has stopped checking in, so nothing can be said about the reader
    /// itself. The fault is upstream of the reader and walking to the reader would waste the trip.
    /// </summary>
    Offline = 2,

    /// <summary>
    /// The machine is alive and reports that it cannot hold the reader. This is the state that means
    /// go and look at the hardware: power, cable, switch port, or another client holding the socket.
    /// </summary>
    Error = 3,

    /// <summary>Switched off by an administrator. Not a fault, and deliberately not counted as one.</summary>
    Disabled = 4,
}

public sealed record ReaderRow(
    long Id,
    string ReaderKey,
    string? SerialNumber,
    string? Model,
    string Host,
    int Port,
    string Protocol,
    int AntennaCount,
    long? DeviceId,
    string? DeviceKey,
    bool IsEnabled,
    DateTimeOffset? LastSeen,
    IReadOnlyList<AntennaRow> Antennas,
    ReaderState State,
    bool DeviceOnline);

/// <summary>
/// How many readers are in each state, counted once on the server.
/// <para>
/// Counted here rather than in the browser so that every screen asking the question gets the same
/// answer from the same rule. The count a shop actually asks for is <c>Connected</c> out of
/// <c>Total</c>; the rest exist so the number that is missing can be explained without opening a
/// second screen.
/// </para>
/// </summary>
public sealed record ReaderStateSummary(
    int Total,
    int Connected,
    int Offline,
    int Error,
    int Discovered,
    int Disabled);

/// <summary>The whole topology of a shop, for the screen that administers it.</summary>
public sealed record RfidTopologyDto(
    IReadOnlyList<DeviceRow> Devices,
    IReadOnlyList<ReaderRow> Readers,
    ReaderStateSummary Summary);

[RequiresPermission(PermissionKeys.Settings.Read)]
public sealed record GetRfidTopologyQuery(long LocationId) : IRequest<Result<RfidTopologyDto>>;

/// <summary>Registers a reader, or updates the one with this key.</summary>
[RequiresPermission(PermissionKeys.Settings.Hardware)]
public sealed record SaveReaderCommand(
    long LocationId,
    string ReaderKey,
    string? SerialNumber,
    string? Model,
    string Host,
    int Port,
    string Protocol,
    int AntennaCount,
    long? DeviceId) : IRequest<Result<long>>;

/// <summary>
/// Points one antenna at one station, or clears it.
/// <para>
/// A null station removes the assignment, which is how an antenna is taken out of use entirely.
/// Disabling it instead keeps the mapping and stops the reads, which is the right move for an
/// antenna that is being worked on rather than removed.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Settings.Hardware)]
public sealed record AssignAntennaCommand(
    long ReaderId,
    int AntennaNumber,
    long? StationId,
    bool Enabled = true) : IRequest<Result>;

public sealed class RfidTopologyAdminHandlers
    : IRequestHandler<GetRfidTopologyQuery, Result<RfidTopologyDto>>,
      IRequestHandler<SaveReaderCommand, Result<long>>,
      IRequestHandler<AssignAntennaCommand, Result>
{
    public static readonly Error ReaderNotFound = new("reader.not_found", "No such reader.");
    public static readonly Error StationNotFound = new("station.not_found", "No such station.");

    public static readonly Error AntennaOutOfRange =
        new("reader.antenna_out_of_range", "That antenna number does not exist on this reader.");

    public static readonly Error StationAlreadyServed = new(
        "station.already_served",
        "Another antenna already feeds that station. Two antennas may share a station only if you mean them to.");

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;
    private readonly IRfidNotifier _notifier;
    private readonly IReaderConnectionStatus _serverReaders;

    public RfidTopologyAdminHandlers(
        IApplicationDbContext db,
        IDateTime clock,
        IRfidNotifier notifier,
        IReaderConnectionStatus serverReaders)
    {
        _db = db;
        _clock = clock;
        _notifier = notifier;
        _serverReaders = serverReaders;
    }

    public async Task<Result<RfidTopologyDto>> Handle(GetRfidTopologyQuery request, CancellationToken ct)
    {
        var devices = await _db.Devices.AsNoTracking()
            .Where(d => d.LocationId == request.LocationId)
            .OrderBy(d => d.DeviceKey)
            .ToListAsync(ct);

        var readers = await _db.RfidReaders.AsNoTracking()
            .Where(r => r.LocationId == request.LocationId)
            .OrderBy(r => r.ReaderKey)
            .ToListAsync(ct);

        var readerIds = readers.Select(r => r.Id).ToList();

        var assignments = await _db.ReaderAntennaAssignments.AsNoTracking()
            .Where(a => readerIds.Contains(a.ReaderId))
            .Join(
                _db.Stations.AsNoTracking(),
                a => a.StationId,
                s => s.Id,
                (a, s) => new { a.ReaderId, a.AntennaNumber, a.StationId, s.StationCode, a.IsEnabled })
            .ToListAsync(ct);

        var deviceKeys = devices.ToDictionary(d => d.Id, d => d.DeviceKey);
        var now = _clock.Now;

        // Taken once, before the rows are built. It is a live in-memory view of this process's own
        // sessions, and sampling it per reader would let the list describe two different moments.
        var serverHeld = _serverReaders.Current;

        var deviceRows = devices
            .Select(d => new DeviceRow(
                d.Id,
                d.DeviceKey,
                d.Name,
                d.Hostname,
                d.LocalIpAddresses,
                d.AgentVersion,
                d.IsOnline(now, DeviceRegistryHandlers.OfflineAfter),
                d.LastHeartbeat,
                readers.Count(r => r.DeviceId == d.Id)))
            .ToList();

        var readerRows = readers
            .Select(r =>
            {
                var mapped = assignments.Where(a => a.ReaderId == r.Id).ToList();

                // Every port the reader has, not only the assigned ones. An antenna with no station
                // is the thing an administrator is looking for on this screen, and a list that only
                // showed the configured ones would hide exactly that.
                var antennas = Enumerable.Range(1, Math.Max(1, r.AntennaCount))
                    .Select(number =>
                    {
                        var found = mapped.FirstOrDefault(a => a.AntennaNumber == number);

                        return found is null
                            ? new AntennaRow(number, null, null, false)
                            : new AntennaRow(number, found.StationId, found.StationCode, found.IsEnabled);
                    })
                    .ToList();

                var driver = r.DeviceId is { } deviceId
                    ? devices.FirstOrDefault(d => d.Id == deviceId)
                    : null;

                var driverOnline = driver?.IsOnline(now, DeviceRegistryHandlers.OfflineAfter) ?? false;
                var heldByServer = ServerSessionFor(serverHeld, r);

                return new ReaderRow(
                    r.Id,
                    r.ReaderKey,
                    r.SerialNumber,
                    r.Model,
                    r.Host,
                    r.Port,
                    r.Protocol.ToString(),
                    r.AntennaCount,
                    r.DeviceId,
                    r.DeviceId is { } id && deviceKeys.TryGetValue(id, out var key) ? key : null,
                    r.IsEnabled,
                    r.LastSeen,
                    antennas,
                    StateOf(r, driver, driverOnline, heldByServer, mapped.Exists(a => a.IsEnabled)),
                    driverOnline);
            })
            .ToList();

        var summary = new ReaderStateSummary(
            readerRows.Count,
            readerRows.Count(r => r.State == ReaderState.Connected),
            readerRows.Count(r => r.State == ReaderState.Offline),
            readerRows.Count(r => r.State == ReaderState.Error),
            readerRows.Count(r => r.State == ReaderState.Discovered),
            readerRows.Count(r => r.State == ReaderState.Disabled));

        return Result.Success(new RfidTopologyDto(deviceRows, readerRows, summary));
    }

    /// <summary>
    /// The session this server is holding to the same box, if it is holding one.
    /// <para>
    /// Matched on address rather than on identity, and that is the seam between the two halves of the
    /// system rather than a shortcut. Server-held connections are driven from the reader
    /// <em>profile</em> table, which predates serial numbers and has no field for one; the topology
    /// table is keyed on the hardware's own identity. Host and port are the only fact both tables
    /// hold, so they are what joins them until the profile table carries a serial.
    /// </para>
    /// </summary>
    private static Retail25.Application.Rfid.ReaderConnectionState? ServerSessionFor(
        ReaderConnectionSnapshot serverHeld,
        RfidReader reader)
    {
        if (!serverHeld.ServerHosted)
        {
            return null;
        }

        var endpoint = $"{reader.Host}:{reader.Port}";

        return serverHeld.Readers.FirstOrDefault(s =>
            string.Equals(s.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The one rule that decides what a reader's light says.
    /// <para>
    /// Order matters and is the whole content of the method. Disabled outranks everything because a
    /// reader somebody switched off is not a fault and must not be counted as one. A connection this
    /// server is holding itself comes next, because it is first-hand knowledge: there is no heartbeat
    /// to interpret and no staleness window to guess at, the socket is either open in this process or
    /// it is not. Only then does the agent path apply, and there the question is which layer failed —
    /// a silent machine hides the reader's state entirely, so that is <c>Offline</c>; a machine that
    /// is talking and still not holding the reader has told us something specific, and that is
    /// <c>Error</c>.
    /// </para>
    /// <para>
    /// A reader nobody drives at all is <c>Discovered</c>: it answered a sweep, it is on the network,
    /// and it reads nothing until an administrator points its antennas at tills. That is a state to
    /// act on rather than a fault, and counting it as one would make a freshly installed reader look
    /// broken.
    /// </para>
    /// </summary>
    private static ReaderState StateOf(
        RfidReader reader,
        Device? driver,
        bool driverOnline,
        Retail25.Application.Rfid.ReaderConnectionState? heldByServer,
        bool hasAntennaInService)
    {
        if (!reader.IsEnabled)
        {
            return ReaderState.Disabled;
        }

        if (heldByServer is { } session)
        {
            return session.Connected ? ReaderState.Connected : ReaderState.Error;
        }

        // Nobody has pointed an antenna at a till yet, so nothing drives it and nothing should.
        //
        // This has to be tested before the agent questions below, and testing it after them was a
        // bug that reached a live shop: a reader that had just been discovered showed "Not answering
        // - check power, cable and switch port" about hardware that was answering perfectly, because
        // an unassigned reader is one an agent correctly declines to open a session to.
        //
        // It also made Discovered unreachable. That state keyed on the reader having no machine,
        // and discovery stamps the machine on the row the moment it registers one — so the state
        // meant for "found, waiting to be commissioned" could never be the answer for a reader that
        // had just been found.
        if (!hasAntennaInService || reader.DeviceId is null)
        {
            return ReaderState.Discovered;
        }

        if (!driverOnline)
        {
            return ReaderState.Offline;
        }

        // The same test the check-in itself uses to decide whether the state changed, called rather
        // than restated: two copies of this rule would eventually disagree, and the symptom would be
        // a screen that is pushed an update and then shows the state it already had.
        return DeviceRegistryHandlers.Held(reader.LastSeen, driver?.LastHeartbeat)
            ? ReaderState.Connected
            : ReaderState.Error;
    }

    public async Task<Result<long>> Handle(SaveReaderCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = request.ReaderKey.Trim().ToUpperInvariant();

        var reader = await _db.RfidReaders
            .FirstOrDefaultAsync(r => r.LocationId == request.LocationId && r.ReaderKey == key, ct);

        if (reader is null)
        {
            var created = RfidReader.Create(request.LocationId, key, request.SerialNumber, request.AntennaCount);

            if (created.IsFailure)
            {
                return Result.Failure<long>(created.Error);
            }

            reader = created.Value;
            _db.RfidReaders.Add(reader);
        }
        else
        {
            reader.SerialNumber = request.SerialNumber?.Trim();
        }

        reader.Model = request.Model?.Trim();
        reader.DeviceId = request.DeviceId;
        reader.AntennaCount = Math.Max(1, request.AntennaCount);
        reader.MoveTo(request.Host, request.Port);

        if (Enum.TryParse<ReaderTransportProtocol>(request.Protocol, ignoreCase: true, out var protocol))
        {
            reader.Protocol = protocol;
        }

        await _db.SaveChangesAsync(ct);

        await _notifier.TopologyChangedAsync(request.LocationId, "reader", ct);

        return Result.Success(reader.Id);
    }

    public async Task<Result> Handle(AssignAntennaCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reader = await _db.RfidReaders.FirstOrDefaultAsync(r => r.Id == request.ReaderId, ct);

        if (reader is null)
        {
            return Result.Failure(ReaderNotFound.With("readerId", request.ReaderId));
        }

        // Refused rather than accepted and never read. An assignment on antenna 5 of a four-port
        // reader looks configured on the screen and produces nothing at the till, which is the
        // hardest kind of fault to find.
        if (!reader.HasAntenna(request.AntennaNumber))
        {
            return Result.Failure(AntennaOutOfRange
                .With("antenna", request.AntennaNumber)
                .With("antennaCount", reader.AntennaCount));
        }

        var existing = await _db.ReaderAntennaAssignments
            .FirstOrDefaultAsync(a => a.ReaderId == request.ReaderId && a.AntennaNumber == request.AntennaNumber, ct);

        if (request.StationId is not { } stationId)
        {
            if (existing is not null)
            {
                _db.ReaderAntennaAssignments.Remove(existing);
                await _db.SaveChangesAsync(ct);
                await _notifier.TopologyChangedAsync(reader.LocationId, "assignment", ct);
            }

            return Result.Success();
        }

        var stationExists = await _db.Stations.AnyAsync(s => s.Id == stationId, ct);

        if (!stationExists)
        {
            return Result.Failure(StationNotFound.With("stationId", stationId));
        }

        if (existing is null)
        {
            var created = ReaderAntennaAssignment.Create(request.ReaderId, request.AntennaNumber, stationId);

            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            created.Value.SetEnabled(request.Enabled);
            _db.ReaderAntennaAssignments.Add(created.Value);
        }
        else
        {
            existing.ReassignTo(stationId);
            existing.SetEnabled(request.Enabled);
        }

        await _db.SaveChangesAsync(ct);

        // Every screen watching this shop, not only the one that made the change. Two people
        // commissioning an estate from two laptops is the ordinary case on an installation day, and
        // the second one silently overwriting the first is what this prevents them from doing blind.
        await _notifier.TopologyChangedAsync(reader.LocationId, "assignment", ct);

        return Result.Success();
    }
}
