using MediatR;
using Microsoft.EntityFrameworkCore;
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
    private readonly IApplicationDbContext _db;

    public CreateCartHandler(
        ICartStore store,
        PosContextLoader contextLoader,
        CartPricingService pricing,
        ICurrentUser currentUser,
        IDateTime clock,
        IApplicationDbContext db)
    {
        _store = store;
        _contextLoader = contextLoader;
        _pricing = pricing;
        _currentUser = currentUser;
        _clock = clock;
        _db = db;
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

        // Resumed only if the id it claims names a real row.
        //
        // The store is a cache; the carts table is what decides whether a cart exists. Asking only
        // whether the snapshot carries a non-zero id is asking the cache to vouch for itself, and it
        // will: a cart parked by a build that numbered carts from a sequence has an id nobody ever
        // inserted, so the till resumes it, fills it, and the write-behind is then asked to create a
        // cart under an id the IDENTITY column will not accept. That is a sale lost at the moment of
        // payment, and it repeats for ever, because this method hands back the same doomed cart every
        // time the station asks for one.
        //
        // One extra existence check per cart opened buys the guarantee that everything downstream
        // depends on: an addressable cart is one the database agrees exists.
        if (existing is { Cart.IsActive: true } && await IsRecordedAsync(existing.Cart.Id, ct))
        {
            var current = await _pricing.QuoteAsync(existing, context, ct);
            return Result.Success(current.Dto);
        }

        if (existing is not null)
        {
            await _store.RemoveAsync(existing.Cart.Id, request.StationId, ct);
        }

        var cart = Cart.Open(
            request.StationId,
            context.Location.Id,
            staffId,
            _clock.Now,
            context.Policy.AbandonedCartTimeoutMinutes);

        // Written to the carts table now, purely to be given an identity.
        //
        // The column is an IDENTITY and the row was only ever inserted at completion — so the id
        // arrived at the end of the sale, long after the till needed it to address the basket. That
        // is the whole of BUG-01: the cart the cashier was filling had no id, and every route is
        // /carts/{cartId}/…, so nothing could be added to it.
        //
        // Opening the row here rather than inventing a number elsewhere keeps one source of cart
        // ids, which is the database, and means completion finds the row already present and
        // updates it. The cost is one insert per sale opened. The alternative was a second id
        // authority that the table would reject on the way out — which it did, with
        // "cannot insert explicit value for identity column".
        _db.Carts.Add(cart);
        await _db.SaveChangesAsync(ct);

        var snapshot = new CartSnapshot(cart);

        var quote = await _pricing.QuoteAsync(snapshot, context, ct);
        await _store.SaveAsync(snapshot, ct);

        return Result.Success(quote.Dto);
    }

    /// <summary>Whether the <c>carts</c> table has a row under this id. Id 0 never does.</summary>
    private async Task<bool> IsRecordedAsync(long cartId, CancellationToken ct)
        => cartId != 0 && await _db.Carts.AnyAsync(c => c.Id == cartId, ct);
}
