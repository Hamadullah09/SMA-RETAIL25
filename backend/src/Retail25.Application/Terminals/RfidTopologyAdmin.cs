using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
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
    IReadOnlyList<AntennaRow> Antennas);

/// <summary>The whole topology of a shop, for the screen that administers it.</summary>
public sealed record RfidTopologyDto(IReadOnlyList<DeviceRow> Devices, IReadOnlyList<ReaderRow> Readers);

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

    public RfidTopologyAdminHandlers(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
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
                    antennas);
            })
            .ToList();

        return Result.Success(new RfidTopologyDto(deviceRows, readerRows));
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

        return Result.Success();
    }
}
