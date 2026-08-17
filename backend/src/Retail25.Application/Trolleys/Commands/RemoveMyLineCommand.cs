using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Carts.Services;
using Retail25.Application.Trolleys.Dtos;
using Retail25.Application.Trolleys.Services;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Trolleys;

namespace Retail25.Application.Trolleys.Commands;

/// <summary>
/// The shopper takes an item back out of their own basket.
/// <para>
/// Changed their mind in aisle four, put the item back on the shelf, removed it from the bill. The
/// same rules as the cashier's delete apply, because they are the rules that keep tags honest: the
/// serialized unit returns to stock and the tag claim is released, so putting the item back and
/// somebody else picking it up sells it cleanly at another counter — including this one.
/// </para>
/// <para>
/// Addressed by sequence within the shopper's own cart, which is resolved from their live session.
/// No cart id in the request, so there is nothing to point at anybody else's basket.
/// </para>
/// </summary>
public sealed record RemoveMyLineCommand(int Sequence) : IRequest<Result<ShopperCartDto>>;

public sealed class RemoveMyLineHandler : IRequestHandler<RemoveMyLineCommand, Result<ShopperCartDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentShopper _shopper;
    private readonly CartWorkflow _workflow;
    private readonly ITagDebouncer _debouncer;
    private readonly IDateTime _clock;

    public RemoveMyLineHandler(
        IApplicationDbContext db,
        ICurrentShopper shopper,
        CartWorkflow workflow,
        ITagDebouncer debouncer,
        IDateTime clock)
    {
        _db = db;
        _shopper = shopper;
        _workflow = workflow;
        _debouncer = debouncer;
        _clock = clock;
    }

    public async Task<Result<ShopperCartDto>> Handle(RemoveMyLineCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_shopper.ShopperId is not { } shopperId)
        {
            return Result.Failure<ShopperCartDto>(TrolleyAllocator.NotSignedIn);
        }

        var session = await _db.TrolleySessions
            .FirstOrDefaultAsync(
                s => s.ShopperId == shopperId && s.State == TrolleySessionState.Shopping,
                ct);

        if (session is null)
        {
            return Result.Failure<ShopperCartDto>(Queries.GetMyCartHandler.NoLiveSession);
        }

        var trolley = await _db.Trolleys.FirstOrDefaultAsync(t => t.Id == session.TrolleyId, ct);

        if (trolley is null)
        {
            return Result.Failure<ShopperCartDto>(Trolley.NotFound);
        }

        // The same mutate-and-broadcast the cashier's delete uses, so the counter's screen and the
        // back office see the removal the moment it happens — that is the "station gets my info"
        // half of this feature, and it costs nothing extra because every mutation broadcasts.
        var mutated = await _workflow.MutateAsync(session.CartId, async (snapshot, _, token) =>
        {
            var line = snapshot.Lines.FirstOrDefault(l => l.Sequence == request.Sequence);
            if (line is null)
            {
                return Result.Failure(UpdateCartLineHandler.LineNotFound.With("sequence", request.Sequence));
            }

            // A removed line hands its tag back: the unit returns to stock and the claim drops, so
            // the next reader to see this tag can sell it (doc 06 §1).
            if (line.SerializedUnitId is { } unitId)
            {
                var unit = await _db.SerializedUnits.FirstOrDefaultAsync(u => u.Id == unitId, token);
                if (unit is { State: SerializedUnitState.InCart })
                {
                    unit.ReleaseFromCart();
                    await _db.SaveChangesAsync(token);
                }
            }

            if (!string.IsNullOrWhiteSpace(line.Epc))
            {
                await _debouncer.ReleaseAsync(line.Epc, snapshot.Cart.StationId, token);
            }

            snapshot.Lines.Remove(line);
            return Result.Success();
        }, ct);

        if (mutated.IsFailure)
        {
            return Result.Failure<ShopperCartDto>(mutated.Error);
        }

        session.Touch(_clock.Now);
        await _db.SaveChangesAsync(ct);

        return new ShopperCartDto(
            session.Id,
            trolley.Id,
            trolley.Code,
            session.State,
            mutated.Value);
    }
}
