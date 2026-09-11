using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Terminals;

/// <summary>One reader as the agent currently finds it.</summary>
public sealed record ReaderHealthReport(
    string ReaderKey,
    string? SerialNumber,
    bool Connected,
    string? Host = null,
    int? Port = null);

/// <summary>What the server knows about a machine after it has checked in.</summary>
public sealed record DeviceStatusDto(
    long DeviceId,
    string DeviceKey,
    bool IsOnline,
    int ReadersManaged,
    DateTimeOffset? LastHeartbeat);

/// <summary>
/// A machine checking in, and saying which readers it is driving.
/// <para>
/// Beside <see cref="ReportAgentStatusCommand"/>, not instead of it. That one reports a station's
/// peripherals — printer, scale, drawer — which are genuinely per-till, and it keeps doing so. This
/// reports the machine and its readers, which are not: one PC may drive three readers serving twelve
/// tills, and there is no station that owns that fact.
/// </para>
/// <para>
/// Liveness lives here from now on. Asking "is this station alive" through the station row meant a
/// station could look dead because a heartbeat was late, when the truth is a property of the machine
/// — so availability is derived downward, machine to reader to antenna to station, rather than
/// copied sideways into every station a device happens to serve.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Terminals.Operate)]
public sealed record ReportDeviceStatusCommand(
    long LocationId,
    string DeviceKey,
    string? Hostname,
    string? LocalIpAddresses,
    string? OperatingSystem,
    string? AgentVersion,
    IReadOnlyList<ReaderHealthReport> Readers) : IRequest<Result<DeviceStatusDto>>;

public sealed class DeviceRegistryHandlers : IRequestHandler<ReportDeviceStatusCommand, Result<DeviceStatusDto>>
{
    /// <summary>
    /// How stale a heartbeat makes a machine offline.
    /// <para>
    /// Three times the five-second interval: one missed beat is a slow network, three is a machine
    /// that has gone. Marking a till offline on a single late packet would make the dashboard flicker
    /// across an estate of 252 and teach everyone to ignore it.
    /// </para>
    /// </summary>
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(15);

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;
    private readonly IRfidNotifier _notifier;

    public DeviceRegistryHandlers(IApplicationDbContext db, IDateTime clock, IRfidNotifier notifier)
    {
        _db = db;
        _clock = clock;
        _notifier = notifier;
    }

    public async Task<Result<DeviceStatusDto>> Handle(ReportDeviceStatusCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = (request.DeviceKey ?? string.Empty).Trim().ToUpperInvariant();

        var device = await _db.Devices
            .FirstOrDefaultAsync(d => d.LocationId == request.LocationId && d.DeviceKey == key, ct);

        if (device is null)
        {
            // First contact registers the machine.
            //
            // Created rather than refused because the alternative is an administrator having to key a
            // row before an agent will speak, and an agent that cannot report until somebody has
            // noticed it is an agent nobody notices. What it may *do* is still gated: a device with
            // no antenna assignments routes no reads anywhere.
            var created = Device.Create(request.LocationId, key);

            if (created.IsFailure)
            {
                return Result.Failure<DeviceStatusDto>(created.Error);
            }

            device = created.Value;
            _db.Devices.Add(device);
        }

        // Kept before it is overwritten, because whether a reader counted as connected a moment ago
        // is measured against the heartbeat that was current then. Without it the comparison is
        // against the beat being written now, which always says the reader was fine.
        var previousHeartbeat = device.LastHeartbeat;

        device.Hostname = request.Hostname?.Trim();
        device.LocalIpAddresses = request.LocalIpAddresses?.Trim();
        device.OperatingSystem = request.OperatingSystem?.Trim();
        device.AgentVersion = request.AgentVersion?.Trim();
        device.LastHeartbeat = _clock.Now;

        await _db.SaveChangesAsync(ct);

        var managed = await ApplyReaderHealthAsync(request, device, previousHeartbeat, ct);

        return Result.Success(new DeviceStatusDto(
            device.Id,
            device.DeviceKey,
            device.IsOnline(_clock.Now, OfflineAfter),
            managed,
            device.LastHeartbeat));
    }

    /// <summary>
    /// Whether an address a machine reported is worth writing over the one an administrator set.
    /// <para>
    /// Loopback is not. It is the shipped placeholder in the agent's own fallback profile, and an
    /// agent that has not yet been told where its reader lives reports exactly that — so a check-in
    /// could overwrite a working address with one that means "this machine, and no other".
    /// </para>
    /// <para>
    /// It did. A till checked in reporting 127.0.0.1, the address was learned, the configuration then
    /// told the agent to dial 127.0.0.1, and the reader went dark — with the agent, the server and
    /// the health screen all agreeing on an address that was never right. Every layer was working;
    /// the number they agreed on was wrong, and each check-in wrote it again five seconds later, so
    /// correcting the row by hand lasted one beat.
    /// </para>
    /// <para>
    /// An administrator may still set loopback deliberately through the topology screen. This governs
    /// only what a machine may teach the server about itself, which is the path with no human on it.
    /// </para>
    /// </summary>
    private static bool IsWorthLearning(string host)
    {
        var trimmed = host.Trim();

        if (trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A serial lead — COM3 — is a real answer and is kept: it says the reader is plugged into
        // this machine, which is exactly the kind of thing only the machine can report.
        return !System.Net.IPAddress.TryParse(trimmed, out var address)
               || !System.Net.IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// Whether a sighting is recent enough, relative to the heartbeat it is compared against, to mean
    /// the agent was holding the reader at that moment.
    /// <para>
    /// Relative rather than absolute on purpose. The agent stamps a sighting and a heartbeat in the
    /// same check-in, so while it holds the reader the two are equal and the gap only grows once it
    /// checks in without it. Measuring the sighting against the wall clock instead would report a
    /// reader as connected for the whole staleness window after it had gone — which is exactly the
    /// minute somebody spends at a till that has stopped reading.
    /// </para>
    /// </summary>
    internal static bool Held(DateTimeOffset? lastSeen, DateTimeOffset? heartbeat)
        => lastSeen is { } seen && heartbeat is { } beat && beat - seen < OfflineAfter;

    /// <summary>
    /// Records where each reader is and whether the agent can currently reach it.
    /// <para>
    /// This is where a changed address stops mattering. The reader is found by its serial where the
    /// protocol reports one, and by its key otherwise; the host it answered on is then written to the
    /// row. A DHCP change updates a column instead of orphaning every station hanging off it.
    /// </para>
    /// </summary>
    private async Task<int> ApplyReaderHealthAsync(
        ReportDeviceStatusCommand request,
        Device device,
        DateTimeOffset? previousHeartbeat,
        CancellationToken ct)
    {
        if (request.Readers.Count == 0)
        {
            return 0;
        }

        var keys = request.Readers.Select(r => r.ReaderKey.Trim().ToUpperInvariant()).ToList();
        var serials = request.Readers
            .Where(r => !string.IsNullOrWhiteSpace(r.SerialNumber))
            .Select(r => r.SerialNumber!.Trim())
            .ToList();

        var known = await _db.RfidReaders
            .Where(r => r.LocationId == request.LocationId
                && (keys.Contains(r.ReaderKey) || (r.SerialNumber != null && serials.Contains(r.SerialNumber))))
            .ToListAsync(ct);

        var managed = 0;
        var changed = false;

        foreach (var report in request.Readers)
        {
            var reportedKey = report.ReaderKey.Trim().ToUpperInvariant();
            var reportedSerial = report.SerialNumber?.Trim();

            // Serial first: it is the hardware's own identity and outranks a key somebody typed.
            var reader = (reportedSerial is not null
                    ? known.FirstOrDefault(r => r.SerialNumber == reportedSerial)
                    : null)
                ?? known.FirstOrDefault(r => r.ReaderKey == reportedKey);

            if (reader is null)
            {
                // An unregistered reader is not created here. A reader that nobody has assigned
                // antennas to would route nothing anyway, and silently minting rows for whatever
                // answers on the network is how a neighbour's device ends up in the registry.
                continue;
            }

            reader.DeviceId = device.Id;

            if (reportedSerial is not null && reader.SerialNumber is null)
            {
                // Learned on first sight from a protocol that reports it. Never overwritten: two
                // different serials on one row means the hardware was swapped, and that is an
                // administrator's decision rather than a silent one.
                reader.SerialNumber = reportedSerial;
            }

            // Whether the topology screen was calling this reader connected before this check-in, by
            // the same rule that screen uses: the driving agent's last heartbeat carried a sighting
            // of it. Computed before LastSeen is written, so that the comparison below is between two
            // different moments rather than against itself.
            var wasConnected = Held(reader.LastSeen, previousHeartbeat);

            if (report.Connected)
            {
                reader.LastSeen = _clock.Now;

                if (report.Host is { } host && host.Length > 0 && IsWorthLearning(host))
                {
                    reader.MoveTo(host, report.Port ?? reader.Port);
                }
            }

            if (wasConnected != Held(reader.LastSeen, device.LastHeartbeat))
            {
                changed = true;
            }

            managed++;
        }

        await _db.SaveChangesAsync(ct);

        // One message for the check-in, however many readers flipped. A machine driving four readers
        // that all come back after a switch reboot is one event to a watcher, not four.
        if (changed)
        {
            await _notifier.TopologyChangedAsync(request.LocationId, "reader", ct);
        }

        return managed;
    }
}
