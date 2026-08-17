using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Carts.Services;
using Retail25.Application.Rfid.Commands;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Rfid.Services;

/// <summary>
/// Turns a batch of tag reads into cart lines — the mechanics behind <c>AddRfidBatchCommand</c>,
/// extracted so a second caller could exist.
/// <para>
/// That second caller is the shopper's handheld. The staff command carries
/// <c>[RequiresPermission(Pos.Sell)]</c> and a nested <c>Send</c> re-enters the authorization
/// pipeline, so a customer token — which resolves to the empty permission set by design — could never
/// reach it. The shopper command authorises by owning a live trolley session instead, and then needs
/// exactly these mechanics: same profile filter, same cross-till arbitration, same EPC state machine,
/// same rejection reasons. Two implementations of "a tag becomes a line" would drift, and the one
/// that drifted would be the one selling things twice.
/// </para>
/// </summary>
public sealed class RfidCheckout
{
    public static readonly Error ClaimedElsewhere = new("epc.claimed_by_other_station", "Another till is holding that tag.");
    public static readonly Error FilteredOut = new("epc.filtered", "The tag did not meet this reader's signal or zone thresholds.");
    public static readonly Error AlreadyOnCart = new("epc.already_on_cart", "That tag is already on this sale.");

    private readonly ICartStore _store;
    private readonly IApplicationDbContext _db;
    private readonly PosContextLoader _contextLoader;
    private readonly CartPricingService _pricing;
    private readonly IdentifierResolver _resolver;
    private readonly CartLineFactory _lineFactory;
    private readonly ITagDebouncer _debouncer;
    private readonly IPosNotifier _notifier;
    private readonly IDateTime _clock;

    public RfidCheckout(
        ICartStore store,
        IApplicationDbContext db,
        PosContextLoader contextLoader,
        CartPricingService pricing,
        IdentifierResolver resolver,
        CartLineFactory lineFactory,
        ITagDebouncer debouncer,
        IPosNotifier notifier,
        IDateTime clock)
    {
        _store = store;
        _db = db;
        _contextLoader = contextLoader;
        _pricing = pricing;
        _resolver = resolver;
        _lineFactory = lineFactory;
        _debouncer = debouncer;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task<Result<RfidBatchResult>> AddBatchAsync(
        long cartId,
        IReadOnlyList<TagRead> reads,
        CancellationToken ct)
    {
        var snapshot = await _store.GetAsync(cartId, ct);
        if (snapshot is null || !snapshot.Cart.IsActive)
        {
            return Result.Failure<RfidBatchResult>(Cart.NotActive.With("cartId", cartId));
        }

        var contextResult = await _contextLoader.LoadAsync(snapshot.Cart.StationId, ct);
        if (contextResult.IsFailure)
        {
            return Result.Failure<RfidBatchResult>(contextResult.Error);
        }

        var context = contextResult.Value;
        var profile = await LoadReaderProfileAsync(context, ct);
        var stationId = snapshot.Cart.StationId;

        var rejected = new List<RejectedTag>();
        // Sequences, not ids: a cached cart's lines have no database id to collect.
        var acceptedSequences = new List<int>();

        // Deduplicate inside the batch itself: a single antenna sweep can report the same tag from
        // two angles, and the agent's coalescing window does not span batches.
        var tags = reads
            .GroupBy(t => t.Epc.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(t => t.ReadCount).First())
            .ToList();

        foreach (var tag in tags)
        {
            var epc = tag.Epc.Trim().ToUpperInvariant();

            if (!profile.Accepts(tag.Antenna, tag.Rssi, tag.ReadCount))
            {
                rejected.Add(new RejectedTag(epc, FilteredOut.Code, FilteredOut.Message));
                continue;
            }

            if (snapshot.Lines.Any(l => string.Equals(l.Epc, epc, StringComparison.Ordinal)))
            {
                rejected.Add(new RejectedTag(epc, AlreadyOnCart.Code, AlreadyOnCart.Message));
                continue;
            }

            var claimed = await _debouncer.TryClaimAsync(epc, stationId, TimeSpan.FromMilliseconds(profile.DebounceMs), ct);
            if (!claimed)
            {
                rejected.Add(new RejectedTag(epc, ClaimedElsewhere.Code, ClaimedElsewhere.Message));
                continue;
            }

            var resolved = await _resolver.ResolveEpcAsync(epc, snapshot.Cart.LocationId, ct);
            if (resolved.IsFailure)
            {
                await _debouncer.ReleaseAsync(epc, stationId, ct);
                rejected.Add(new RejectedTag(epc, resolved.Error.Code, resolved.Error.Message));
                continue;
            }

            var added = await _lineFactory.AddAsync(
                snapshot,
                context,
                resolved.Value,
                new CartLineRequest(1m, null, null, null, null, null),
                ct);

            if (added.IsFailure)
            {
                await _debouncer.ReleaseAsync(epc, stationId, ct);
                rejected.Add(new RejectedTag(epc, added.Error.Code, added.Error.Message));
                continue;
            }

            acceptedSequences.Add(snapshot.Lines[^1].Sequence);
            resolved.Value.Unit?.UpdateLastSeen(tag.LastSeen);
        }

        foreach (var rejection in rejected)
        {
            await _notifier.CartLineRejectedAsync(stationId, rejection.Epc, rejection.Reason, rejection.Message, ct);
        }

        if (acceptedSequences.Count == 0)
        {
            return Result.Success(new RfidBatchResult(null, [], rejected, tags.Count));
        }

        await _db.SaveChangesAsync(ct);

        snapshot.Cart.Touch(_clock.Now, context.Policy.AbandonedCartTimeoutMinutes);
        var quote = await _pricing.QuoteAsync(snapshot, context, ct);
        await _store.SaveAsync(snapshot, ct);

        var accepted = quote.Dto.Lines.Where(l => acceptedSequences.Contains(l.Sequence)).ToList();

        // The fast path: send only the new lines plus totals, not the whole cart. At 300 tags that
        // difference is the gap between the 300 ms budget and a visibly stuttering list.
        await _notifier.CartLinesAddedAsync(
            snapshot.Cart.LocationId,
            snapshot.Cart.Id,
            accepted.Cast<object>().ToArray(),
            snapshot.Cart.Revision,
            ct);

        await _notifier.TotalsChangedAsync(
            snapshot.Cart.LocationId,
            snapshot.Cart.Id,
            quote.Dto.Totals,
            snapshot.Cart.Revision,
            ct);

        return Result.Success(new RfidBatchResult(quote.Dto, accepted, rejected, tags.Count));
    }

    /// <summary>
    /// The station's own reader profile, falling back to the location's, then to a default. A missing
    /// profile must not stop a till selling — it just means nothing is filtered out at this layer.
    /// </summary>
    private async Task<ReaderProfile> LoadReaderProfileAsync(PosContext context, CancellationToken ct)
    {
        if (context.Station.ReaderProfileId is { } profileId)
        {
            var assigned = await _db.ReaderProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == profileId, ct);
            if (assigned is not null)
            {
                return assigned;
            }
        }

        var stationProfile = await _db.ReaderProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.StationId == context.Station.Id && p.IsActive, ct);

        if (stationProfile is not null)
        {
            return stationProfile;
        }

        return await _db.ReaderProfiles.AsNoTracking()
                   .FirstOrDefaultAsync(p => p.LocationId == context.Location.Id && p.StationId == null && p.IsActive, ct)
               ?? ReaderProfile.CreateDefault(context.Location.Id);
    }
}
