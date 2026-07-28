using System.Collections.Concurrent;
using Retail25.Application.Abstractions;
using Retail25.Domain.Sales;

namespace Retail25.Infrastructure.Carts;

/// <summary>
/// Cart storage held in the process.
/// <para>
/// This exists so the system can be started and used with nothing but a database — press F5 and
/// sell something, without Redis running. It is registered only when no Redis connection is
/// configured, and it is deliberately unsuitable for more than one API instance: carts would not be
/// shared, which is precisely the multi-station behaviour the product exists to provide.
/// </para>
/// </summary>
public sealed class InMemoryCartStore : ICartStore
{
    private readonly ConcurrentDictionary<Guid, Cart> _carts = new();
    private readonly ConcurrentDictionary<Guid, List<CartLine>> _lines = new();

    public Task<Cart?> GetAsync(Guid cartId, CancellationToken ct = default)
        => Task.FromResult(_carts.TryGetValue(cartId, out var cart) ? cart : null);

    public Task<Cart?> GetByStationAsync(Guid stationId, CancellationToken ct = default)
    {
        var cart = _carts.Values.FirstOrDefault(c => c.StationId == stationId && c.Status == CartStatus.Active);
        return Task.FromResult(cart);
    }

    public Task SetAsync(Cart cart, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cart);
        _carts[cart.Id] = cart;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid cartId, CancellationToken ct = default)
    {
        _carts.TryRemove(cartId, out _);
        _lines.TryRemove(cartId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CartLine>> GetLinesAsync(Guid cartId, CancellationToken ct = default)
    {
        var lines = _lines.TryGetValue(cartId, out var stored)
            ? stored.OrderBy(l => l.Sequence).ToList()
            : [];

        return Task.FromResult<IReadOnlyList<CartLine>>(lines);
    }

    public Task SetLinesAsync(Guid cartId, IReadOnlyList<CartLine> lines, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _lines[cartId] = [.. lines];
        return Task.CompletedTask;
    }

    public Task<int> IncrementRevisionAsync(Guid cartId, CancellationToken ct = default)
    {
        if (!_carts.TryGetValue(cartId, out var cart))
        {
            return Task.FromResult(0);
        }

        cart.Revision++;
        return Task.FromResult(cart.Revision);
    }
}
