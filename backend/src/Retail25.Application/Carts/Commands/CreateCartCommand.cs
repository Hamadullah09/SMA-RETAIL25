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

    public CreateCartHandler(
        ICartStore store,
        PosContextLoader contextLoader,
        CartPricingService pricing,
        ICurrentUser currentUser,
        IDateTime clock)
    {
        _store = store;
        _contextLoader = contextLoader;
        _pricing = pricing;
        _currentUser = currentUser;
        _clock = clock;
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
        if (existing is { Cart.IsActive: true })
        {
            var current = await _pricing.QuoteAsync(existing, context, ct);
            return Result.Success(current.Dto);
        }

        var snapshot = new CartSnapshot(Cart.Open(
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
