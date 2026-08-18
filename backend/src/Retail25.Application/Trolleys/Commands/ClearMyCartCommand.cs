using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Services;
using Retail25.Application.Trolleys.Dtos;
using Retail25.Application.Trolleys.Services;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Trolleys;

namespace Retail25.Application.Trolleys.Commands;

/// <summary>
/// The shopper empties their own basket in one action.
/// <para>
/// Changed their mind about the whole trip, or swept a neighbouring shelf into the bill by holding
/// the trigger too long. Removing thirty lines one at a time is thirty confirmations and thirty
/// round trips, and a customer who cannot undo a bad sweep quickly will hand the handheld to staff
/// instead — which is the queue this feature exists to remove.
/// </para>
/// <para>
/// Every unit goes back to stock and every tag claim is released, exactly as removing the lines
/// singly would, because a tag left claimed is an item nobody can sell at any counter. It is one
/// mutation rather than N, so the counter's screen sees one update instead of watching the bill
/// count down.
/// </para>
/// <para>
/// No cart id in the request. The cart is resolved from the caller's own live session, so there is
/// nothing to point at anybody else's basket.
/// </para>
/// </summary>
public sealed record ClearMyCartCommand : IRequest<Result<ShopperCartDto>>;

public sealed class ClearMyCartHandler : IRequestHandler<ClearMyCartCommand, Result<ShopperCartDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentShopper _shopper;
    private readonly CartWorkflow _workflow;
    private readonly ITagDebouncer _debouncer;
    private readonly IDateTime _clock;

    public ClearMyCartHandler(
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

    public async Task<Result<ShopperCartDto>> Handle(ClearMyCartCommand request, CancellationToken ct)
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

        var mutated = await _workflow.MutateAsync(session.CartId, async (snapshot, _, token) =>
        {
            // Copied before iterating: the loop empties the collection it would otherwise be walking.
            var lines = snapshot.Lines.ToList();

            foreach (var line in lines)
            {
                if (line.SerializedUnitId is { } unitId)
                {
                    var unit = await _db.SerializedUnits.FirstOrDefaultAsync(u => u.Id == unitId, token);

                    if (unit is { State: SerializedUnitState.InCart })
                    {
                        unit.ReleaseFromCart();
                    }
                }

                if (!string.IsNullOrWhiteSpace(line.Epc))
                {
                    await _debouncer.ReleaseAsync(line.Epc, snapshot.Cart.StationId, token);
                }
            }

            // One save for every unit rather than one per line: emptying a full basket is a single
            // decision by the shopper and should be a single write.
            await _db.SaveChangesAsync(token);

            snapshot.Lines.Clear();

            // Adjustments belong to the lines that justified them — a discount on an empty bill is a
            // discount on nothing, and leaving them would show a negative total.
            snapshot.Adjustments.Clear();

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
