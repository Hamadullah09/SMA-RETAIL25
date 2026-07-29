using Retail25.Domain.Sales;

namespace Retail25.Application.Abstractions;

/// <summary>
/// The whole cart in one object: header, lines, sale-level adjustments and the tax override.
/// Handlers mutate this and hand it back to <see cref="ICartStore.SaveAsync"/> as a unit, so a cart
/// is never observed half-written.
/// </summary>
public sealed class CartSnapshot
{
    public CartSnapshot(Cart cart) => Cart = cart;

    public Cart Cart { get; set; }

    public List<CartLine> Lines { get; init; } = [];

    public List<CartAdjustment> Adjustments { get; init; } = [];

    public CartTaxOverride? TaxOverride { get; set; }

    public IReadOnlyList<CartLine> OrderedLines => Lines.OrderBy(l => l.Sequence).ToList();
}

/// <summary>
/// Storage for the server-authoritative cart: Redis for anything active, with a Postgres
/// write-behind when a cart is suspended so it survives a restart and can be recalled at another
/// till (doc 06 §2).
/// </summary>
public interface ICartStore
{
    Task<CartSnapshot?> GetAsync(Guid cartId, CancellationToken ct = default);

    /// <summary>The one active cart at a station, if there is one. Keyed by <c>station:{id}:cart</c>.</summary>
    Task<CartSnapshot?> GetByStationAsync(Guid stationId, CancellationToken ct = default);

    Task SaveAsync(CartSnapshot snapshot, CancellationToken ct = default);

    /// <summary>Drops the cart and releases the station key. Used on complete, void and expiry.</summary>
    Task RemoveAsync(Guid cartId, Guid stationId, CancellationToken ct = default);
}
