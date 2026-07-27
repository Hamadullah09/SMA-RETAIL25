using Retail25.Domain.Sales;

namespace Retail25.Application.Abstractions;

/// <summary>
/// Server-authoritative cart storage backed by Redis with Postgres write-behind.
/// The cart lives on the server because RFID reads arrive from a daemon, not a browser.
/// </summary>
public interface ICartStore
{
    Task<Cart?> GetAsync(Guid cartId, CancellationToken ct = default);

    Task<Cart?> GetByStationAsync(Guid stationId, CancellationToken ct = default);

    Task SetAsync(Cart cart, CancellationToken ct = default);

    Task RemoveAsync(Guid cartId, CancellationToken ct = default);

    Task<IReadOnlyList<CartLine>> GetLinesAsync(Guid cartId, CancellationToken ct = default);

    Task SetLinesAsync(Guid cartId, IReadOnlyList<CartLine> lines, CancellationToken ct = default);

    /// <summary>Increment the cart revision for optimistic concurrency.</summary>
    Task<int> IncrementRevisionAsync(Guid cartId, CancellationToken ct = default);
}
