using MediatR;
using Microsoft.Extensions.Logging;
using Retail25.Application.Common;
using Retail25.Application.Rfid.Services;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Common;

namespace Retail25.Application.Rfid.Commands;

/// <summary>What one reader's batch did, once its antennas were resolved to stations.</summary>
public sealed record ReaderBatchResult(
    int StationsReached,
    int TagsRouted,
    IReadOnlyList<UnroutedAntenna> Unrouted);

/// <summary>
/// A batch from one reader, routed to whichever stations its antennas stand for.
/// <para>
/// The agent used to post tags already labelled with a station, which meant the reader-to-station
/// decision was made on the till before the reads left it — and a reader could therefore only ever
/// be one station. This command takes the other half of the pair: the agent says which reader saw
/// what, and the server decides where that belongs.
/// </para>
/// <para>
/// <see cref="IngestTagReadsCommand"/> is unchanged and still serves agents posting by station. This
/// is deliberate: an estate is upgraded one till at a time, and a migration that requires every
/// agent to be reinstalled on the same evening is one nobody performs.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record IngestReaderTagsCommand(long ReaderId, IReadOnlyList<TagRead> Tags)
    : IRequest<Result<ReaderBatchResult>>;

public sealed class IngestReaderTagsHandler : IRequestHandler<IngestReaderTagsCommand, Result<ReaderBatchResult>>
{
    private readonly TagObservationRouter _router;
    private readonly ISender _sender;
    private readonly ILogger<IngestReaderTagsHandler> _logger;

    public IngestReaderTagsHandler(
        TagObservationRouter router,
        ISender sender,
        ILogger<IngestReaderTagsHandler> logger)
    {
        _router = router;
        _sender = sender;
        _logger = logger;
    }

    public async Task<Result<ReaderBatchResult>> Handle(IngestReaderTagsCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var routed = await _router.RouteAsync(request.ReaderId, request.Tags, ct);

        var tagsRouted = 0;

        foreach (var (stationId, tags) in routed.ByStation)
        {
            // Straight into the existing per-station path, which already does the work that must not
            // be duplicated: debounce, the live feed, session gating, the cart. Re-implementing any
            // of that here would give one reader's reads different rules from another's.
            var result = await _sender.Send(new IngestTagReadsCommand(stationId, tags), ct);

            if (result.IsFailure)
            {
                // One station's failure must not cost the others their reads. A till with no sale
                // open is the ordinary case and already answers this way.
                _logger.LogDebug(
                    "Station {StationId} refused a routed batch from reader {ReaderId}: {Code}",
                    stationId,
                    request.ReaderId,
                    result.Error.Code);

                continue;
            }

            tagsRouted += tags.Count;
        }

        // Reported, never swallowed. An antenna with no station is the single most common
        // commissioning mistake, and the symptom without this is a till that simply never reacts —
        // indistinguishable from a dead antenna, a bad cable or a reader in the wrong mode.
        foreach (var orphan in routed.Unrouted)
        {
            _logger.LogWarning(
                "Reader {ReaderId} antenna {Antenna} saw {Count} tags but {Reason}",
                orphan.ReaderId,
                orphan.AntennaNumber,
                orphan.TagCount,
                orphan.Reason == UnroutedAntenna.Disabled
                    ? "its station assignment is disabled"
                    : "no station is assigned to it");
        }

        return Result.Success(new ReaderBatchResult(
            routed.ByStation.Count,
            tagsRouted,
            routed.Unrouted));
    }
}
