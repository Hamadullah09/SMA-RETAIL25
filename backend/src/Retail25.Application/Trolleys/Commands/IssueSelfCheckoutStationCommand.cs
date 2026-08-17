using MediatR;
using Retail25.Application.Abstractions;
using Retail25.Application.Trolleys.Dtos;
using Retail25.Application.Trolleys.Services;
using Retail25.Domain.Common;

namespace Retail25.Application.Trolleys.Commands;

/// <summary>
/// Gives a signed-in shopper a self-checkout station, without them having to know one exists.
/// <para>
/// This is what the phone calls the moment sign-in succeeds. The customer never types a number: the
/// 300 block is reserved for self-checkout, and the app hands out the lowest free counter in it —
/// creating the next one up when every existing counter is busy, bounded by the configured range so
/// it can never reach a staffed till.
/// </para>
/// <para>
/// Idempotent by design rather than by accident. Calling it again while a trip is live returns that
/// same trip, so a restarted app, a reinstalled app or a second handset all land back on the basket
/// the customer is standing next to instead of stranding it and taking a fresh counter.
/// </para>
/// <para>
/// No <c>[RequiresPermission]</c>, for the reason given on <see cref="ClaimTrolleyCommand"/>: a
/// shopper token carries no permissions at all, so an attribute here would refuse everybody.
/// </para>
/// </summary>
public sealed record IssueSelfCheckoutStationCommand(long? LocationId = null)
    : IRequest<Result<ShopperCartDto>>;

public sealed class IssueSelfCheckoutStationHandler
    : IRequestHandler<IssueSelfCheckoutStationCommand, Result<ShopperCartDto>>
{
    private readonly ICurrentShopper _shopper;
    private readonly TrolleyAllocator _allocator;

    public IssueSelfCheckoutStationHandler(ICurrentShopper shopper, TrolleyAllocator allocator)
    {
        _shopper = shopper;
        _allocator = allocator;
    }

    public async Task<Result<ShopperCartDto>> Handle(
        IssueSelfCheckoutStationCommand request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_shopper.ShopperId is not { } shopperId)
        {
            return Result.Failure<ShopperCartDto>(TrolleyAllocator.NotSignedIn);
        }

        return await _allocator.IssueNextFreeAsync(shopperId, request.LocationId, ct);
    }
}
