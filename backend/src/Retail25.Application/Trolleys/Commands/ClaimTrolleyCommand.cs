using MediatR;
using Retail25.Application.Abstractions;
using Retail25.Application.Trolleys.Dtos;
using Retail25.Application.Trolleys.Services;
using Retail25.Domain.Common;

namespace Retail25.Application.Trolleys.Commands;

/// <summary>
/// Connects the phone to the self-checkout station whose code the shopper gave, and opens the basket.
/// <para>
/// The ordinary path no longer comes through here — signing in issues a station on its own, see
/// <c>IssueSelfCheckoutStationCommand</c>. This remains for the case where the shopper must land on a
/// <em>particular</em> counter: the RFID reader is bolted to one, so a customer standing at 307 has
/// to be on 307 and nowhere else.
/// </para>
/// <para>
/// Carries no <c>[RequiresPermission]</c> and must never carry one. A shopper token resolves to the
/// empty permission set, so an attribute here would make the feature refuse everybody. Authorisation
/// is <see cref="ICurrentShopper"/> being non-null for the claim, and from then on the trolley
/// session row is the only thing that says which cart is yours.
/// </para>
/// </summary>
/// <param name="LocationId">
/// Optional. Codes are unique per shop, not globally, so a chain with a trolley 482 in two branches
/// needs to know which. Where the app knows the store — scanned at the door, or the only store there
/// is — it says so, and an ambiguous code is reported rather than guessed.
/// </param>
public sealed record ClaimTrolleyCommand(string? Code, long? LocationId = null)
    : IRequest<Result<ShopperCartDto>>;

public sealed class ClaimTrolleyHandler : IRequestHandler<ClaimTrolleyCommand, Result<ShopperCartDto>>
{
    private readonly ICurrentShopper _shopper;
    private readonly TrolleyAllocator _allocator;

    public ClaimTrolleyHandler(ICurrentShopper shopper, TrolleyAllocator allocator)
    {
        _shopper = shopper;
        _allocator = allocator;
    }

    public async Task<Result<ShopperCartDto>> Handle(ClaimTrolleyCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_shopper.ShopperId is not { } shopperId)
        {
            return Result.Failure<ShopperCartDto>(TrolleyAllocator.NotSignedIn);
        }

        return await _allocator.ClaimAsync(shopperId, request.Code, request.LocationId, ct);
    }
}
