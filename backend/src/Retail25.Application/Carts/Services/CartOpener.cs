using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Dtos;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Services;

/// <summary>
/// Opens the cart for a station, or hands back the one already open there.
/// <para>
/// Extracted from <c>CreateCartHandler</c> because there are now two ways a sale begins — a cashier
/// pressing New Sale, and a shopper claiming a trolley with their phone — and they must not be two
/// implementations. The cashier's route is permission-gated and the shopper's is gated by owning a
/// trolley session, but what "open a cart" means has to be one thing, or the two drift and only one
/// of them keeps the guarantees below.
/// </para>
/// <para>
/// Returning the existing cart rather than erroring is deliberate: a browser refresh, a second tab,
/// an agent reconnect or a phone that lost signal in an aisle must all land on the same sale. A
/// station has exactly one cart at a time.
/// </para>
/// </summary>
public sealed class CartOpener
{
    private readonly ICartStore _store;
    private readonly PosContextLoader _contextLoader;
    private readonly CartPricingService _pricing;
    private readonly IDateTime _clock;
    private readonly IApplicationDbContext _db;

    public CartOpener(
        ICartStore store,
        PosContextLoader contextLoader,
        CartPricingService pricing,
        IDateTime clock,
        IApplicationDbContext db)
    {
        _store = store;
        _contextLoader = contextLoader;
        _pricing = pricing;
        _clock = clock;
        _db = db;
    }

    /// <param name="staffId">
    /// Zero where no member of staff is behind the sale, which is every trolley cart. The column
    /// already tolerated zero — the cashier path passes it whenever the token carries no staff
    /// profile — so a shopper's cart is not a new shape of row, just one nobody signed for.
    /// </param>
    public async Task<Result<CartDto>> OpenAsync(long stationId, long staffId, CancellationToken ct)
    {
        var contextResult = await _contextLoader.LoadAsync(stationId, ct);
        if (contextResult.IsFailure)
        {
            return Result.Failure<CartDto>(contextResult.Error);
        }

        var context = contextResult.Value;

        var existing = await _store.GetByStationAsync(stationId, ct);

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
            await _store.RemoveAsync(existing.Cart.Id, stationId, ct);
        }

        var cart = Cart.Open(
            stationId,
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
        // updates it. The cost is one insert per sale opened.
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
