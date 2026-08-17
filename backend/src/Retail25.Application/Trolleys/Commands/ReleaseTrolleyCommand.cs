using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Common;
using Retail25.Domain.Trolleys;

namespace Retail25.Application.Trolleys.Commands;

/// <summary>
/// Hands the trolley back without paying — the shopper changed their mind, or is starting over.
/// <para>
/// The session ends as <see cref="TrolleySessionState.Abandoned"/> rather than Released, because that
/// is what happened and the two mean different things to whoever reconciles the shop floor: a
/// released trolley was paid for, an abandoned one has goods in it that need putting back.
/// </para>
/// <para>
/// The cart is deliberately left alone. It is already an abandoned-cart row that the existing sweep
/// knows how to age out, and deleting it here would destroy the record of what was in the trolley at
/// exactly the moment somebody might need to go and find it.
/// </para>
/// </summary>
public sealed record ReleaseTrolleyCommand : IRequest<Result>;

public sealed class ReleaseTrolleyHandler : IRequestHandler<ReleaseTrolleyCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentShopper _shopper;
    private readonly ICartStore _store;
    private readonly IDateTime _clock;

    public ReleaseTrolleyHandler(
        IApplicationDbContext db,
        ICurrentShopper shopper,
        ICartStore store,
        IDateTime clock)
    {
        _db = db;
        _shopper = shopper;
        _store = store;
        _clock = clock;
    }

    public async Task<Result> Handle(ReleaseTrolleyCommand request, CancellationToken ct)
    {
        if (_shopper.ShopperId is not { } shopperId)
        {
            return Result.Failure(Services.TrolleyAllocator.NotSignedIn);
        }

        var session = await _db.TrolleySessions
            .FirstOrDefaultAsync(
                s => s.ShopperId == shopperId && s.State == TrolleySessionState.Shopping,
                ct);

        // Nothing to release is a success, not an error. A phone that retries after a timeout, or a
        // shopper who taps twice, has got what they asked for either way.
        if (session is null)
        {
            return Result.Success();
        }

        session.Abandon(_clock.Now);
        await _db.SaveChangesAsync(ct);

        // Free the counter.
        //
        // Ending the session is not enough on its own: the station keeps its open cart in the cart
        // store, and the claim path treats a station with an open cart as busy. Without this, the
        // first shopper to give up a counter locks it for everybody, permanently — the trolley is
        // released on paper and unusable in practice.
        var stationId = await _db.Trolleys
            .Where(t => t.Id == session.TrolleyId)
            .Select(t => (long?)t.StationId)
            .FirstOrDefaultAsync(ct);

        if (stationId is { } station)
        {
            await _store.RemoveAsync(session.CartId, station, ct);
        }

        return Result.Success();
    }
}
