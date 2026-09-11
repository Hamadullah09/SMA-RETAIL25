using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Common;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Terminals;

/// <summary>What a sweep did to the reader list, for the screen that asked for it.</summary>
public sealed record ReaderDiscoveryOutcome(
    int Found,
    int Added,
    int Moved,
    int Unchanged,
    IReadOnlyList<string> AddedKeys);

/// <summary>
/// Records what an agent found on its own network.
///
/// <para>
/// The till sweeps and this stores the result, because the two are on different networks and only
/// one of them can see the readers. A server hosted at <c>pos.sma-techno.net</c> scanning
/// <c>192.168.x.x</c> would be scanning its own data centre, not a shop — which is why discovery
/// belongs to the agent and registration belongs here.
/// </para>
/// <para>
/// Matching is by serial number first and address second, and that order is the whole point. A
/// reader that took a new DHCP lease overnight has a new address and the same serial: matching on
/// serial makes that an update, so every antenna assignment an administrator made against it is
/// still attached to the right physical box in the morning. Matching on address would have made it
/// a new reader with no assignments, and a shop that stopped reading until somebody noticed.
/// </para>
/// <para>
/// Readers that were not found are deliberately left alone. A sweep is evidence that something is
/// present, never that something is absent — a reader mid-reboot, on a busy switch, or simply held
/// open by this machine's own session answers nothing, and deleting its configuration because one
/// scan missed it would discard an administrator's work to no purpose. Absence is reported by the
/// connection state, which knows the difference.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Terminals.Register)]
public sealed record RecordReaderDiscoveryCommand(
    long LocationId,
    string DeviceKey,
    IReadOnlyList<DiscoveredReaderContract> Readers) : IRequest<Result<ReaderDiscoveryOutcome>>;

public sealed class RecordReaderDiscoveryHandler
    : IRequestHandler<RecordReaderDiscoveryCommand, Result<ReaderDiscoveryOutcome>>
{
    public static readonly Error DeviceNotFound =
        new("device.not_found", "No machine is registered with that key, so its findings cannot be recorded.");

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;
    private readonly IRfidNotifier _notifier;

    public RecordReaderDiscoveryHandler(IApplicationDbContext db, IDateTime clock, IRfidNotifier notifier)
    {
        _db = db;
        _clock = clock;
        _notifier = notifier;
    }

    public async Task<Result<ReaderDiscoveryOutcome>> Handle(
        RecordReaderDiscoveryCommand request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var deviceKey = request.DeviceKey.Trim().ToUpperInvariant();

        var device = await _db.Devices
            .FirstOrDefaultAsync(d => d.LocationId == request.LocationId && d.DeviceKey == deviceKey, ct);

        if (device is null)
        {
            return Result.Failure<ReaderDiscoveryOutcome>(DeviceNotFound.With("deviceKey", deviceKey));
        }

        var existing = await _db.RfidReaders
            .Where(r => r.LocationId == request.LocationId)
            .ToListAsync(ct);

        var now = _clock.Now;
        var added = new List<string>();
        var moved = 0;
        var unchanged = 0;

        foreach (var found in request.Readers)
        {
            var match = Match(existing, found);

            if (match is null)
            {
                var key = NextKey(existing, added.Count);
                var created = RfidReader.Create(request.LocationId, key, found.SerialNumber, found.AntennaCount);

                if (created.IsFailure)
                {
                    return Result.Failure<ReaderDiscoveryOutcome>(created.Error);
                }

                match = created.Value;
                match.DeviceId = device.Id;
                _db.RfidReaders.Add(match);
                existing.Add(match);
                added.Add(key);
            }
            else if (!string.Equals(match.Host, found.Host, StringComparison.OrdinalIgnoreCase)
                     || match.Port != found.Port)
            {
                moved++;
            }
            else
            {
                unchanged++;
            }

            // The address is refreshed on every sighting, including the unchanged ones: writing the
            // same value costs nothing and means a reader never has a stale address recorded because
            // a change happened to coincide with a scan that matched on serial.
            match.MoveTo(found.Host, found.Port);

            // A sighting is not a session, and only a reader nobody is driving takes its timestamp
            // from one.
            //
            // A sweep deliberately skips the readers that are in use, so finding one means nothing
            // currently holds it. Stamping LastSeen on a reader that an agent owns would say the
            // opposite — the connection state is read as "the driving agent had it at its last
            // check-in", and a scan writing that column would show a dead reader as connected for as
            // long as the window lasts.
            if (match.DeviceId is null)
            {
                match.LastSeen = now;
            }

            if (!string.IsNullOrWhiteSpace(found.SerialNumber))
            {
                match.SerialNumber = found.SerialNumber.Trim();
            }

            if (!string.IsNullOrWhiteSpace(found.FirmwareVersion))
            {
                match.Model = found.FirmwareVersion.Trim();
            }

            if (Enum.TryParse<ReaderTransportProtocol>(found.Protocol, ignoreCase: true, out var protocol))
            {
                match.Protocol = protocol;
            }

            // Only widened, and only on the reader's own word. A unit that reported eight ports has
            // eight; a unit that stayed silent got the family default, and letting that default
            // shrink a count an administrator had already corrected upwards would quietly orphan the
            // assignments on the ports it removed.
            if (found.AntennaCountReported && found.AntennaCount > 0)
            {
                match.AntennaCount = found.AntennaCount;
            }
            else if (found.AntennaCount > match.AntennaCount)
            {
                match.AntennaCount = found.AntennaCount;
            }
        }

        await _db.SaveChangesAsync(ct);

        // Only when the list actually changed. A sweep that finds the same readers in the same places
        // is the normal outcome every quarter of an hour on every till in the estate, and pushing
        // that would be a message per machine per sweep telling every watcher that nothing happened.
        if (added.Count > 0 || moved > 0)
        {
            await _notifier.TopologyChangedAsync(request.LocationId, "discovery", ct);
        }

        return Result.Success(new ReaderDiscoveryOutcome(
            Found: request.Readers.Count,
            Added: added.Count,
            Moved: moved,
            Unchanged: unchanged,
            AddedKeys: added));
    }

    /// <summary>
    /// The reader this sighting is of, or null if it is one we have not seen before.
    /// <para>
    /// Serial first: it is the hardware's own name for itself and survives every address change.
    /// Address second, and only for units that report no serial — weaker, and knowingly so, because
    /// the alternative for those readers is registering a duplicate every time DHCP moves them.
    /// </para>
    /// </summary>
    private static RfidReader? Match(List<RfidReader> existing, DiscoveredReaderContract found)
    {
        if (!string.IsNullOrWhiteSpace(found.SerialNumber))
        {
            var bySerial = existing.FirstOrDefault(r =>
                string.Equals(r.SerialNumber, found.SerialNumber, StringComparison.OrdinalIgnoreCase));

            if (bySerial is not null)
            {
                return bySerial;
            }
        }

        // Deliberately does not fall back to address when the sighting HAS a serial: a serial that
        // matches nothing is a genuinely new reader that happens to sit where an old one did, and
        // inheriting the old one's assignments would point them at different hardware.
        return string.IsNullOrWhiteSpace(found.SerialNumber)
            ? existing.FirstOrDefault(r =>
                string.IsNullOrWhiteSpace(r.SerialNumber)
                && string.Equals(r.Host, found.Host, StringComparison.OrdinalIgnoreCase)
                && r.Port == found.Port)
            : null;
    }

    /// <summary>
    /// The next free <c>RFID-nnn</c>, so a discovered reader arrives with a name a person can say
    /// out loud rather than a serial number nobody can read across a shop floor.
    /// </summary>
    private static string NextKey(List<RfidReader> existing, int alreadyAdded)
    {
        var taken = existing
            .Select(r => r.ReaderKey)
            .Where(key => key.StartsWith("RFID-", StringComparison.OrdinalIgnoreCase))
            .Select(key => int.TryParse(key.AsSpan(5), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"RFID-{taken + alreadyAdded + 1:D3}";
    }
}
