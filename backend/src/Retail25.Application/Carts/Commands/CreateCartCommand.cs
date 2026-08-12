using MediatR;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Dtos;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Commands;

/// <summary>
/// Opens the cart for a station, or hands back the one that is already open there.
/// <para>
/// Returning the existing cart rather than erroring is deliberate: a browser refresh, a second tab
/// or an agent reconnect must all land on the same sale. A station has exactly one cart at a time.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record CreateCartCommand(long StationId, long? StaffId = null) : IRequest<Result<CartDto>>;

public sealed class CreateCartHandler : IRequestHandler<CreateCartCommand, Result<CartDto>>
{
    private readonly ICartStore _store;
    private readonly PosContextLoader _contextLoader;
    private readonly CartPricingService _pricing;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;
    private readonly ISequenceGenerator _sequences;

    public CreateCartHandler(
        ICartStore store,
        PosContextLoader contextLoader,
        CartPricingService pricing,
        ICurrentUser currentUser,
        IDateTime clock,
        ISequenceGenerator sequences)
    {
        _store = store;
        _contextLoader = contextLoader;
        _pricing = pricing;
        _currentUser = currentUser;
        _clock = clock;
        _sequences = sequences;
    }

    public async Task<Result<CartDto>> Handle(CreateCartCommand request, CancellationToken ct)
    {
        var contextResult = await _contextLoader.LoadAsync(request.StationId, ct);
        if (contextResult.IsFailure)
        {
            return Result.Failure<CartDto>(contextResult.Error);
        }

        var context = contextResult.Value;
        var staffId = request.StaffId ?? _currentUser.StaffId ?? 0L;

        var existing = await _store.GetByStationAsync(request.StationId, ct);

        // Resumed only if it can actually be addressed.
        //
        // A cart whose id is 0 is one nothing can act on: every route is /carts/{cartId}/…, so the
        // till can read that basket and never add to it, empty it or tender it. Carts opened before
        // ids were assigned are exactly that, and because this method hands back any active cart for
        // the station, one of them would be returned for ever — a station permanently unable to sell
        // even though new carts are fine. Discarding it is the only exit, and it costs nothing that
        // was reachable anyway.
        if (existing is { Cart.IsActive: true } && existing.Cart.Id != 0)
        {
            var current = await _pricing.QuoteAsync(existing, context, ct);
            return Result.Success(current.Dto);
        }

        if (existing is { Cart.Id: 0 })
        {
            await _store.RemoveAsync(0, request.StationId, ct);
        }

        var snapshot = new CartSnapshot(Cart.Open(
            await _sequences.NextCartIdAsync(ct),
            request.StationId,
            context.Location.Id,
            staffId,
            _clock.Now,
            context.Policy.AbandonedCartTimeoutMinutes));

        var quote = await _pricing.QuoteAsync(snapshot, context, ct);
        await _store.SaveAsync(snapshot, ct);

        return Result.Success(quote.Dto);
    }
}
