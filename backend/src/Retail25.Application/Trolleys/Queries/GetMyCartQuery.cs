using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Services;
using Retail25.Application.Trolleys.Commands;
using Retail25.Application.Trolleys.Dtos;
using Retail25.Domain.Common;
using Retail25.Domain.Trolleys;

namespace Retail25.Application.Trolleys.Queries;

/// <summary>
/// The shopper's own basket, priced as of now.
/// <para>
/// Takes no cart id, and that is the security design rather than a convenience. Cart ids are
/// sequential integers, so an endpoint that accepted one would be trivially enumerable by anyone with
/// a token — every basket in the shop, readable. Here the id is derived from the caller's live
/// session, so there is no parameter to tamper with.
/// </para>
/// <para>
/// The phone gets its updates pushed over the hub; this is what it calls on a cold start, on
/// reconnect, and whenever it notices it has missed a revision.
/// </para>
/// </summary>
public sealed record GetMyCartQuery : IRequest<Result<ShopperCartDto>>;

public sealed class GetMyCartHandler : IRequestHandler<GetMyCartQuery, Result<ShopperCartDto>>
{
    public static readonly Error NoLiveSession =
        new("trolley_session.none", "You are not connected to a counter.");

    private readonly IApplicationDbContext _db;
    private readonly ICurrentShopper _shopper;
    private readonly CartWorkflow _carts;
    private readonly IDateTime _clock;

    public GetMyCartHandler(
        IApplicationDbContext db,
        ICurrentShopper shopper,
        CartWorkflow carts,
        IDateTime clock)
    {
        _db = db;
        _shopper = shopper;
        _carts = carts;
        _clock = clock;
    }

    public async Task<Result<ShopperCartDto>> Handle(GetMyCartQuery request, CancellationToken ct)
    {
        if (_shopper.ShopperId is not { } shopperId)
        {
            return Result.Failure<ShopperCartDto>(Services.TrolleyAllocator.NotSignedIn);
        }

        var session = await _db.TrolleySessions
            .FirstOrDefaultAsync(
                s => s.ShopperId == shopperId && s.State == TrolleySessionState.Shopping,
                ct);

        if (session is null)
        {
            return Result.Failure<ShopperCartDto>(NoLiveSession);
        }

        var trolley = await _db.Trolleys
            .FirstOrDefaultAsync(t => t.Id == session.TrolleyId, ct);

        if (trolley is null)
        {
            return Result.Failure<ShopperCartDto>(Trolley.NotFound);
        }

        var quote = await _carts.QuoteAsync(session.CartId, ct);

        if (quote.IsFailure)
        {
            return Result.Failure<ShopperCartDto>(quote.Error);
        }

        // Opening the screen counts as being alive, so a shopper reading their basket in a queue is
        // not swept up as abandoned.
        session.Touch(_clock.Now);
        await _db.SaveChangesAsync(ct);

        return new ShopperCartDto(
            session.Id,
            trolley.Id,
            trolley.Code,
            session.State,
            quote.Value.Dto);
    }
}
