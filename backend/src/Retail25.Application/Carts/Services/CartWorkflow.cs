using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Dtos;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Services;

/// <summary>
/// The read-mutate-price-save-broadcast cycle every cart command shares.
/// <para>
/// Centralising it is not just tidiness: a command that forgets to bump the revision, or saves
/// without re-pricing, produces a client that renders stale money. Having exactly one place where
/// that sequence lives is what keeps the invariant true across two dozen commands.
/// </para>
/// </summary>
public sealed class CartWorkflow
{
    private readonly ICartStore _store;
    private readonly PosContextLoader _contextLoader;
    private readonly CartPricingService _pricing;
    private readonly IPosNotifier _notifier;
    private readonly IDateTime _clock;

    public CartWorkflow(
        ICartStore store,
        PosContextLoader contextLoader,
        CartPricingService pricing,
        IPosNotifier notifier,
        IDateTime clock)
    {
        _store = store;
        _contextLoader = contextLoader;
        _pricing = pricing;
        _notifier = notifier;
        _clock = clock;
    }

    public IDateTime Clock => _clock;

    /// <summary>Loads and prices a cart without changing anything. Backs the live totals panel.</summary>
    public async Task<Result<CartQuote>> QuoteAsync(Guid cartId, CancellationToken ct)
    {
        var snapshot = await _store.GetAsync(cartId, ct);
        if (snapshot is null)
        {
            return Result.Failure<CartQuote>(Cart.NotActive.With("cartId", cartId));
        }

        var context = await _contextLoader.LoadAsync(snapshot.Cart.StationId, ct);
        if (context.IsFailure)
        {
            return Result.Failure<CartQuote>(context.Error);
        }

        var quote = await _pricing.QuoteAsync(snapshot, context.Value, ct);
        return Result.Success(quote);
    }

    /// <summary>
    /// Applies a mutation to an active cart, re-prices, persists and broadcasts the authoritative
    /// state. A failed mutation is not saved and not broadcast.
    /// </summary>
    public async Task<Result<CartDto>> MutateAsync(
        Guid cartId,
        Func<CartSnapshot, PosContext, CancellationToken, Task<Result>> mutate,
        CancellationToken ct,
        bool requireActive = true)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        var snapshot = await _store.GetAsync(cartId, ct);
        if (snapshot is null)
        {
            return Result.Failure<CartDto>(Cart.NotActive.With("cartId", cartId));
        }

        if (requireActive && !snapshot.Cart.IsActive)
        {
            return Result.Failure<CartDto>(Cart.NotActive.With("status", snapshot.Cart.Status.ToString()));
        }

        var contextResult = await _contextLoader.LoadAsync(snapshot.Cart.StationId, ct);
        if (contextResult.IsFailure)
        {
            return Result.Failure<CartDto>(contextResult.Error);
        }

        var context = contextResult.Value;

        var mutation = await mutate(snapshot, context, ct);
        if (mutation.IsFailure)
        {
            return Result.Failure<CartDto>(mutation.Error);
        }

        snapshot.Cart.Touch(_clock.Now, context.Policy.AbandonedCartTimeoutMinutes);

        var quote = await _pricing.QuoteAsync(snapshot, context, ct);
        await _store.SaveAsync(snapshot, ct);

        await _notifier.CartUpdatedAsync(snapshot.Cart.LocationId, snapshot.Cart.Id, quote.Dto, snapshot.Cart.Revision, ct);
        await _notifier.TotalsChangedAsync(snapshot.Cart.LocationId, snapshot.Cart.Id, quote.Dto.Totals, snapshot.Cart.Revision, ct);

        return Result.Success(quote.Dto);
    }

    /// <summary>Prices, persists and broadcasts a snapshot the caller has already mutated and validated.</summary>
    public async Task<CartDto> PublishAsync(CartSnapshot snapshot, PosContext context, CancellationToken ct)
    {
        var quote = await _pricing.QuoteAsync(snapshot, context, ct);
        await _store.SaveAsync(snapshot, ct);
        await _notifier.CartUpdatedAsync(snapshot.Cart.LocationId, snapshot.Cart.Id, quote.Dto, snapshot.Cart.Revision, ct);
        return quote.Dto;
    }
}
