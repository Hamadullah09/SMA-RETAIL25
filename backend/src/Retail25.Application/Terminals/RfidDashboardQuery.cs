using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;

namespace Retail25.Application.Terminals;

/// <summary>
/// Why a station is not working, stated as the layer that failed.
/// <para>
/// One "Connected" light cannot answer the question an engineer actually has, which is where to
/// walk. The agent being reachable says nothing about the reader; the reader answering says nothing
/// about whether anybody assigned its antenna to a till. These are four independent facts and the
/// dashboard reports them as four.
/// </para>
/// </summary>
public enum StationHealth
{
    /// <summary>Machine online, reader answering, antenna assigned. Tags will land.</summary>
    Operational = 0,

    /// <summary>Nobody has pointed this antenna at a till, so its reads go nowhere.</summary>
    Unassigned = 1,

    /// <summary>Assigned but switched off, deliberately.</summary>
    Disabled = 2,

    /// <summary>The machine that drives this reader has not checked in.</summary>
    AgentOffline = 3,

    /// <summary>The machine is there; the reader is not answering it.</summary>
    ReaderOffline = 4,

    /// <summary>No machine has claimed this reader at all.</summary>
    ReaderUnclaimed = 5,
}

public sealed record StationHealthRow(
    long? StationId,
    string? StationCode,
    string ReaderKey,
    int AntennaNumber,
    string? DeviceKey,
    bool AgentOnline,
    bool ReaderOnline,
    StationHealth Health,
    DateTimeOffset? AgentLastSeen,
    DateTimeOffset? ReaderLastSeen);

public sealed record RfidDashboardSummary(
    int Total,
    int Operational,
    int Unassigned,
    int Disabled,
    int AgentOffline,
    int ReaderOffline,
    int ReaderUnclaimed);

public sealed record RfidDashboardDto(RfidDashboardSummary Summary, IReadOnlyList<StationHealthRow> Stations);

[RequiresPermission(PermissionKeys.Settings.Read)]
public sealed record GetRfidDashboardQuery(long LocationId) : IRequest<Result<RfidDashboardDto>>;

public sealed class RfidDashboardHandler : IRequestHandler<GetRfidDashboardQuery, Result<RfidDashboardDto>>
{
    /// <summary>
    /// How long since a reader last answered before it counts as gone.
    /// <para>
    /// Longer than the agent's own timeout, because a reader's liveness is reported by a heartbeat
    /// that is itself only sent every few seconds — treating them as equally fresh would show readers
    /// dropping out whenever a heartbeat and a poll interleaved badly.
    /// </para>
    /// </summary>
    private static readonly TimeSpan ReaderOfflineAfter = TimeSpan.FromSeconds(60);

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public RfidDashboardHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<RfidDashboardDto>> Handle(GetRfidDashboardQuery request, CancellationToken ct)
    {
        var now = _clock.Now;

        var readers = await _db.RfidReaders.AsNoTracking()
            .Where(r => r.LocationId == request.LocationId)
            .OrderBy(r => r.ReaderKey)
            .ToListAsync(ct);

        var deviceIds = readers.Where(r => r.DeviceId is not null).Select(r => r.DeviceId!.Value).Distinct().ToList();

        var devices = await _db.Devices.AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id))
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

        var rows = new List<StationHealthRow>();

        foreach (var reader in readers)
        {
            var device = devices.FirstOrDefault(d => d.Id == reader.DeviceId);

            var agentOnline = device?.IsOnline(now, DeviceRegistryHandlers.OfflineAfter) ?? false;
            var readerOnline = reader.LastSeen is { } seen && now - seen < ReaderOfflineAfter;

            // Every antenna the reader has, whether or not anyone assigned it. An unassigned antenna
            // is a row on this screen precisely because it is invisible everywhere else — the reads
            // happen, resolve to nothing, and no till ever reacts.
            for (var antenna = 1; antenna <= Math.Max(1, reader.AntennaCount); antenna++)
            {
                var assignment = assignments
                    .FirstOrDefault(a => a.ReaderId == reader.Id && a.AntennaNumber == antenna);

                // Ordered by what an engineer would check first, and by what makes the others moot.
                // An unassigned antenna is unassigned whether or not its machine is online, and
                // saying "agent offline" about an antenna nobody configured would send somebody to
                // the wrong place.
                var health = assignment is null ? StationHealth.Unassigned
                    : !assignment.IsEnabled ? StationHealth.Disabled
                    : reader.DeviceId is null ? StationHealth.ReaderUnclaimed
                    : !agentOnline ? StationHealth.AgentOffline
                    : !readerOnline ? StationHealth.ReaderOffline
                    : StationHealth.Operational;

                rows.Add(new StationHealthRow(
                    assignment?.StationId,
                    assignment?.StationCode,
                    reader.ReaderKey,
                    antenna,
                    device?.DeviceKey,
                    agentOnline,
                    readerOnline,
                    health,
                    device?.LastHeartbeat,
                    reader.LastSeen));
            }
        }

        var summary = new RfidDashboardSummary(
            rows.Count,
            rows.Count(r => r.Health == StationHealth.Operational),
            rows.Count(r => r.Health == StationHealth.Unassigned),
            rows.Count(r => r.Health == StationHealth.Disabled),
            rows.Count(r => r.Health == StationHealth.AgentOffline),
            rows.Count(r => r.Health == StationHealth.ReaderOffline),
            rows.Count(r => r.Health == StationHealth.ReaderUnclaimed));

        return Result.Success(new RfidDashboardDto(summary, rows));
    }
}
