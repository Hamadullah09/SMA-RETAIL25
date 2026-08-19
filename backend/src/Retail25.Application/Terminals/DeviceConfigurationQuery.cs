using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Common;

namespace Retail25.Application.Terminals;

/// <summary>
/// What one machine should be doing: its readers, and what each antenna stands for.
/// <para>
/// The per-station profile could only ever describe one till and therefore one reader. A machine
/// driving three readers across twelve stations has no single station to ask about, so it asks about
/// itself and is told the whole picture in one round trip — rather than twelve, which at 252
/// stations would be an estate polling itself to a standstill.
/// </para>
/// <para>
/// A station may only learn about its own antennas: the query is keyed on the device, and the joins
/// walk outward from it. There is no shape of request that returns another machine's readers, which
/// is the authorisation property stated as a query rather than enforced by a check somebody can
/// forget.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Terminals.Read)]
public sealed record GetDeviceConfigurationQuery(long LocationId, string DeviceKey)
    : IRequest<Result<DeviceConfigurationContract>>;

public sealed class DeviceConfigurationHandler
    : IRequestHandler<GetDeviceConfigurationQuery, Result<DeviceConfigurationContract>>
{
    public static readonly Error DeviceNotFound = new("device.not_found", "No such device.");

    private readonly IApplicationDbContext _db;

    public DeviceConfigurationHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<DeviceConfigurationContract>> Handle(
        GetDeviceConfigurationQuery request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = (request.DeviceKey ?? string.Empty).Trim().ToUpperInvariant();

        var device = await _db.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.LocationId == request.LocationId && d.DeviceKey == key, ct);

        if (device is null)
        {
            return Result.Failure<DeviceConfigurationContract>(DeviceNotFound.With("deviceKey", key));
        }

        var readers = await _db.RfidReaders
            .AsNoTracking()
            .Where(r => r.DeviceId == device.Id && r.IsEnabled)
            .OrderBy(r => r.ReaderKey)
            .ToListAsync(ct);

        var readerIds = readers.Select(r => r.Id).ToList();

        // Assignments joined to stations for the code, which is what a person reads in a log line.
        // One query for the whole device rather than one per reader: an estate of 63 readers should
        // cost a poll, not sixty-three.
        var assignments = await _db.ReaderAntennaAssignments
            .AsNoTracking()
            .Where(a => readerIds.Contains(a.ReaderId))
            .Join(
                _db.Stations.AsNoTracking(),
                a => a.StationId,
                s => s.Id,
                (a, s) => new { a.ReaderId, a.AntennaNumber, a.StationId, s.StationCode, a.IsEnabled })
            .ToListAsync(ct);

        var byReader = assignments
            .GroupBy(a => a.ReaderId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AntennaAssignmentContract>)g
                    .OrderBy(a => a.AntennaNumber)
                    .Select(a => new AntennaAssignmentContract(
                        a.AntennaNumber,
                        a.StationId,
                        a.StationCode,
                        a.IsEnabled))
                    .ToList());

        var managed = readers
            .Select(r => new ManagedReaderContract(
                r.Id,
                r.ReaderKey,
                r.SerialNumber,
                r.Host,
                r.Port,
                r.Protocol.ToString(),
                r.AntennaCount,
                byReader.TryGetValue(r.Id, out var antennas) ? antennas : []))
            .ToList();

        return Result.Success(new DeviceConfigurationContract(
            device.Id,
            device.DeviceKey,
            device.LocationId,
            managed));
    }
}
