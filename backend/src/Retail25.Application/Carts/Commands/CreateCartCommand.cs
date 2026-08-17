using MediatR;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Dtos;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Domain.Common;

namespace Retail25.Application.Carts.Commands;

/// <summary>
/// Opens the cart for a station, or hands back the one that is already open there.
/// <para>
/// The mechanics live in <see cref="CartOpener"/>, shared with the shopper app's trolley pairing.
/// What this command adds is the half that is specific to a member of staff ringing a sale: the
/// permission check, and resolving whose staff number the sale is booked against.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record CreateCartCommand(long StationId, long? StaffId = null) : IRequest<Result<CartDto>>;

public sealed class CreateCartHandler : IRequestHandler<CreateCartCommand, Result<CartDto>>
{
    private readonly CartOpener _opener;
    private readonly ICurrentUser _currentUser;

    public CreateCartHandler(CartOpener opener, ICurrentUser currentUser)
    {
        _opener = opener;
        _currentUser = currentUser;
    }

    public async Task<Result<CartDto>> Handle(CreateCartCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var staffId = request.StaffId ?? _currentUser.StaffId ?? 0L;

        return await _opener.OpenAsync(request.StationId, staffId, ct);
    }
}
