using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Rfid.Services;

/// <summary>An antenna that saw tags but stands for no station.</summary>
public sealed record UnroutedAntenna(long ReaderId, int AntennaNumber, int TagCount, string Reason)
{
    public const string NoAssignment = "no_station_assigned";
    public const string Disabled = "assignment_disabled";
}

/// <summary>Tags grouped by the station their antenna stands for.</summary>
public sealed record RoutedTags(
    IReadOnlyDictionary<long, IReadOnlyList<TagRead>> ByStation,
    IReadOnlyList<UnroutedAntenna> Unrouted);

/// <summary>
/// Decides which station a tag read belongs to.
/// <para>
/// The reader driver's job ends at "reader X saw tag Y through antenna Z". Which till that is, is a
/// configuration question, and answering it in the driver is what tied one reader to one station:
/// the station was baked in before the read had left the device. This is the seam that separates the
/// two, and it is the whole architectural change in one class.
/// </para>
/// <para>
/// Routing on (reader, antenna) rather than antenna alone is not a detail. Antenna 1 exists on every
/// reader in the building; routing on it by itself would send seven readers' first antennas to the
/// same till. The reader is half the key.
/// </para>
/// <para>
/// A batch fans out. One four-antenna reader watching four checkouts produces one batch containing
/// reads for four different stations, so this returns a grouping rather than a single station — the
/// old signature could not express that and is why the change reaches this far up.
/// </para>
/// </summary>
public sealed class TagObservationRouter
{
    private readonly IApplicationDbContext _db;

    public TagObservationRouter(IApplicationDbContext db) => _db = db;

    public async Task<RoutedTags> RouteAsync(
        long readerId,
        IReadOnlyList<TagRead> tags,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tags);

        // Every assignment for this reader, including the disabled ones.
        //
        // Disabled is loaded deliberately rather than filtered in the query: an antenna switched off
        // for the afternoon and an antenna nobody ever configured are different situations, and an
        // administrator staring at a dead till needs to be told which. Filtering here would collapse
        // them into the same silent nothing.
        var assignments = await _db.ReaderAntennaAssignments
            .AsNoTracking()
            .Where(a => a.ReaderId == readerId)
            .ToDictionaryAsync(a => a.AntennaNumber, ct);

        var byStation = new Dictionary<long, List<TagRead>>();
        var unrouted = new List<UnroutedAntenna>();

        foreach (var group in tags.GroupBy(t => t.Antenna))
        {
            if (!assignments.TryGetValue(group.Key, out var assignment))
            {
                unrouted.Add(new UnroutedAntenna(
                    readerId,
                    group.Key,
                    group.Count(),
                    UnroutedAntenna.NoAssignment));

                continue;
            }

            if (!assignment.IsEnabled)
            {
                unrouted.Add(new UnroutedAntenna(
                    readerId,
                    group.Key,
                    group.Count(),
                    UnroutedAntenna.Disabled));

                continue;
            }

            // Two antennas may legitimately point at one station — a gate watched from both sides —
            // so reads accumulate rather than replace.
            if (!byStation.TryGetValue(assignment.StationId, out var forStation))
            {
                byStation[assignment.StationId] = forStation = [];
            }

            forStation.AddRange(group);
        }

        return new RoutedTags(
            byStation.ToDictionary(p => p.Key, p => (IReadOnlyList<TagRead>)p.Value),
            unrouted);
    }
}
