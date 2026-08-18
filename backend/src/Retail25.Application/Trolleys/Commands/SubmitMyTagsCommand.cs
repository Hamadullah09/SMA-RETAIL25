using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Rfid.Commands;
using Retail25.Application.Rfid.Services;
using Retail25.Application.Trolleys.Services;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Common;
using Retail25.Domain.Trolleys;

namespace Retail25.Application.Trolleys.Commands;

/// <summary>
/// Tags the shopper's own handheld read, applied to the shopper's own basket.
/// <para>
/// This is the C72 flow: the customer connects to a counter, walks the shop scanning items with the
/// handheld's reader, and every accepted tag becomes a line on the same cart the counter and the back
/// office see — the submission runs the identical <see cref="RfidCheckout"/> mechanics as a fixed
/// reader, so debounce, cross-till arbitration, EPC state checks and pricing are one code path
/// however the tag arrived.
/// </para>
/// <para>
/// Takes EPCs, not <see cref="TagRead"/>s. Antenna numbers and RSSI are facts about fixed reader
/// hardware; a handheld pressed against the item has neither in any meaningful sense, and inventing
/// plausible values here would smuggle "trust me" past the reader profile. The synthesized read says
/// exactly what is true — unmeasured signal, seen once, now — and the profile's checkout-antenna
/// convention (antenna 1) is used so a default profile accepts it.
/// </para>
/// <para>
/// No cart id, no station id, no <c>[RequiresPermission]</c> — the live trolley session is the whole
/// authorisation, exactly as everywhere else in the shopper API.
/// </para>
/// </summary>
public sealed record SubmitMyTagsCommand(IReadOnlyList<string> Epcs) : IRequest<Result<RfidBatchResult>>;

public sealed class SubmitMyTagsHandler : IRequestHandler<SubmitMyTagsCommand, Result<RfidBatchResult>>
{
    public static readonly Error NoTags = new("shopper_tags.empty", "Nothing was scanned.");

    /// <summary>
    /// Bounded because the phone batches scans: a genuine sweep is tens of tags, and an unbounded
    /// list from a customer-held device is a way to make the server do arbitrary work.
    /// </summary>
    public static readonly Error TooMany = new("shopper_tags.too_many", "Too many tags in one batch. Scan in smaller sweeps.");

    private const int MaxBatch = 200;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentShopper _shopper;
    private readonly RfidCheckout _checkout;
    private readonly TrolleyAllocator _allocator;
    private readonly IDateTime _clock;

    public SubmitMyTagsHandler(
        IApplicationDbContext db,
        ICurrentShopper shopper,
        RfidCheckout checkout,
        TrolleyAllocator allocator,
        IDateTime clock)
    {
        _db = db;
        _shopper = shopper;
        _checkout = checkout;
        _allocator = allocator;
        _clock = clock;
    }

    public async Task<Result<RfidBatchResult>> Handle(SubmitMyTagsCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_shopper.ShopperId is not { } shopperId)
        {
            return Result.Failure<RfidBatchResult>(TrolleyAllocator.NotSignedIn);
        }

        var epcs = (request.Epcs ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();

        if (epcs.Count == 0)
        {
            return Result.Failure<RfidBatchResult>(NoTags);
        }

        if (epcs.Count > MaxBatch)
        {
            return Result.Failure<RfidBatchResult>(TooMany.With("count", epcs.Count).With("max", MaxBatch));
        }

        var session = await _db.TrolleySessions
            .FirstOrDefaultAsync(
                s => s.ShopperId == shopperId && s.State == TrolleySessionState.Shopping,
                ct);

        if (session is null)
        {
            return Result.Failure<RfidBatchResult>(Queries.GetMyCartHandler.NoLiveSession);
        }

        // The session's cart may have aged out of the cart store, or been lost when the API last
        // restarted. The session row outlives both, so a shopper halfway round the shop can be
        // holding a cart id nobody can serve — and every scan then fails with "this cart is no
        // longer active" until they leave the counter and reconnect, carrying a basket of items
        // they have already scanned. Re-pointing the session at the station's live cart costs one
        // lookup and removes that dead end entirely.
        var cart = await _allocator.EnsureLiveCartAsync(session, ct);

        if (cart.IsFailure)
        {
            return Result.Failure<RfidBatchResult>(cart.Error);
        }

        var now = _clock.Now;

        // Antenna 1 is the default profile's checkout zone; UnknownRssi is the contract's own value
        // for "the reader reported no signal strength", which every profile accepts by design.
        var reads = epcs
            .Select(e => new TagRead(e.Trim().ToUpperInvariant(), 1, TagRead.UnknownRssi, 1, now, now))
            .ToList();

        // attested: a person aimed the handheld at this tag and pulled the trigger. Without it the
        // profile pre-filter refuses every scan — its MinimumReadCount defaults to 2 and one
        // deliberate presentation is one read, so the customer is told their tag "did not meet the
        // reader's signal or zone thresholds" about a reader that is not involved. See the parameter
        // on AddBatchAsync for what is skipped and, more importantly, what is not.
        var result = await _checkout.AddBatchAsync(cart.Value, reads, ct, attested: true);

        if (result.IsSuccess)
        {
            session.Touch(now);
            await _db.SaveChangesAsync(ct);
        }

        return result;
    }
}
