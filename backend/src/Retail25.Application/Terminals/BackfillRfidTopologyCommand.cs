using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Terminals;

public sealed record BackfillTopologyResult(
    int ReadersCreated,
    int AssignmentsCreated,
    int ProfilesWithoutStation,
    IReadOnlyList<string> Skipped);

/// <summary>
/// Turns the existing one-reader-one-station configuration into the new model, without changing what
/// any till currently does.
/// <para>
/// Every <c>ReaderProfile</c> that names a station becomes an <see cref="RfidReader"/> plus a single
/// assignment on antenna 1 pointing at that same station. Behaviour is identical afterwards — reads
/// from that reader still reach that station — but they now arrive by the new route, so the estate
/// can be re-pointed antenna by antenna afterwards rather than all at once.
/// </para>
/// <para>
/// Idempotent, and safe to run against a live shop: a profile that already has a reader is left
/// alone, so a second run creates nothing. That matters because this is the sort of thing an
/// administrator presses twice when the page looks slow.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Settings.Hardware)]
public sealed record BackfillRfidTopologyCommand(long LocationId, bool DryRun = false)
    : IRequest<Result<BackfillTopologyResult>>;

public sealed class BackfillRfidTopologyHandler
    : IRequestHandler<BackfillRfidTopologyCommand, Result<BackfillTopologyResult>>
{
    private readonly IApplicationDbContext _db;

    public BackfillRfidTopologyHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<BackfillTopologyResult>> Handle(
        BackfillRfidTopologyCommand request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profiles = await _db.ReaderProfiles
            .AsNoTracking()
            .Where(p => p.LocationId == request.LocationId)
            .ToListAsync(ct);

        var existingKeys = await _db.RfidReaders
            .Where(r => r.LocationId == request.LocationId)
            .Select(r => r.ReaderKey)
            .ToListAsync(ct);

        var known = new HashSet<string>(existingKeys, StringComparer.OrdinalIgnoreCase);

        var readersCreated = 0;
        var assignmentsCreated = 0;
        var withoutStation = 0;
        var skipped = new List<string>();

        foreach (var profile in profiles)
        {
            // A profile bound to no station has nowhere to send reads and is not evidence of a
            // station that should exist. Counted so the number is visible, not invented into one.
            if (profile.StationId is not { } stationId)
            {
                withoutStation++;
                continue;
            }

            // Derived from the profile's own name so an administrator recognises the row afterwards.
            // Uppercased and space-free because it is an identity, and "Front Door" and "FRONT DOOR"
            // being two readers is the kind of duplicate nobody spots until reads go missing.
            var key = Key(profile.Name, profile.Id);

            if (known.Contains(key))
            {
                skipped.Add(key);
                continue;
            }

            if (request.DryRun)
            {
                known.Add(key);
                readersCreated++;
                assignmentsCreated++;
                continue;
            }

            var reader = RfidReader.Create(request.LocationId, key);

            if (reader.IsFailure)
            {
                skipped.Add(key);
                continue;
            }

            reader.Value.MoveTo(profile.Host, profile.Port);
            reader.Value.Protocol = Map(profile.Protocol);

            _db.RfidReaders.Add(reader.Value);
            await _db.SaveChangesAsync(ct);

            known.Add(key);
            readersCreated++;

            // Antenna 1, because that is what the old model meant: one reader, one station, and
            // whichever antenna saw the tag was irrelevant. Antennas 2-4 are left unassigned rather
            // than guessed at — an unassigned antenna is reported loudly, and a wrong one is silent.
            var assignment = ReaderAntennaAssignment.Create(reader.Value.Id, 1, stationId);

            if (assignment.IsFailure)
            {
                continue;
            }

            _db.ReaderAntennaAssignments.Add(assignment.Value);
            await _db.SaveChangesAsync(ct);

            assignmentsCreated++;
        }

        return Result.Success(new BackfillTopologyResult(
            readersCreated,
            assignmentsCreated,
            withoutStation,
            skipped));
    }

    private static string Key(string? name, long profileId)
    {
        var basis = (name ?? string.Empty).Trim().ToUpperInvariant().Replace(' ', '-');

        return basis.Length == 0 ? $"RFID-{profileId:000}" : basis;
    }

    private static ReaderTransportProtocol Map(ReaderProtocol protocol) => protocol switch
    {
        ReaderProtocol.Llrp => ReaderTransportProtocol.Llrp,
        ReaderProtocol.UhfSerial => ReaderTransportProtocol.UhfSerial,
        ReaderProtocol.Http => ReaderTransportProtocol.Http,
        ReaderProtocol.Mqtt => ReaderTransportProtocol.Mqtt,
        _ => ReaderTransportProtocol.Simulator,
    };
}
