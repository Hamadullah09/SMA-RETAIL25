using Microsoft.AspNetCore.SignalR;
using Retail25.Api.Hubs;
using Retail25.Application.Abstractions;

namespace Retail25.Api.Realtime;

/// <summary>
/// Broadcasts application events to connected tills and browse grids.
/// <para>
/// This is the answer to the complaint the legacy guide itself records (p.100): on a shared
/// network, one user's edit left everyone else looking at stale data until they scrolled or
/// reopened the window. Here the server pushes, so a second station sees a stock change as it
/// happens.
/// </para>
/// <para>
/// It lives in the API project because that is where the hubs are. Application talks only to
/// <see cref="IPosNotifier"/> and never learns that SignalR exists.
/// </para>
/// </summary>
public sealed class SignalRPosNotifier : IPosNotifier
{
    private readonly IHubContext<PosHub> _pos;
    private readonly IHubContext<InventoryHub> _inventory;
    private readonly IHubContext<TerminalHub> _terminal;

    public SignalRPosNotifier(
        IHubContext<PosHub> pos,
        IHubContext<InventoryHub> inventory,
        IHubContext<TerminalHub> terminal)
    {
        _pos = pos;
        _inventory = inventory;
        _terminal = terminal;
    }

    public Task CartUpdatedAsync(Guid locationId, Guid cartId, object cartDto, int revision, CancellationToken ct = default)
        => _pos.Clients.Group(Cart(cartId)).SendAsync("CartUpdated", cartId, cartDto, revision, ct);

    public Task CartLinesAddedAsync(Guid locationId, Guid cartId, object[] lineDtos, CancellationToken ct = default)
        => _pos.Clients.Group(Cart(cartId)).SendAsync("CartLinesAdded", cartId, lineDtos, ct);

    /// <summary>
    /// A rejected tag goes to the whole station, not just the cart: the cashier needs to see why a
    /// tag was refused even when no sale is open.
    /// </summary>
    public Task CartLineRejectedAsync(Guid locationId, string epc, string reason, CancellationToken ct = default)
        => _pos.Clients.Group(Location(locationId)).SendAsync("CartLineRejected", epc, reason, ct);

    public Task TotalsChangedAsync(Guid locationId, Guid cartId, object totalsDto, CancellationToken ct = default)
        => _pos.Clients.Group(Cart(cartId)).SendAsync("TotalsChanged", cartId, totalsDto, ct);

    public Task StockLevelChangedAsync(Guid locationId, Guid productId, decimal newOnHand, CancellationToken ct = default)
        => _inventory.Clients.Group(Location(locationId)).SendAsync("StockLevelChanged", productId, newOnHand, ct);

    public Task ProductChangedAsync(Guid locationId, Guid productId, CancellationToken ct = default)
        => _inventory.Clients.Group(Location(locationId)).SendAsync("ProductChanged", productId, ct);

    public Task ProductDeletedAsync(Guid locationId, Guid productId, CancellationToken ct = default)
        => _inventory.Clients.Group(Location(locationId)).SendAsync("ProductDeleted", productId, ct);

    public Task TagStreamStatusAsync(Guid stationId, bool readerOnline, int readRate, CancellationToken ct = default)
        => _terminal.Clients.Group(Station(stationId)).SendAsync("TagStreamStatus", readerOnline, readRate, ct);

    public Task PeripheralStatusAsync(Guid stationId, object statusDto, CancellationToken ct = default)
        => _terminal.Clients.Group(Station(stationId)).SendAsync("PeripheralStatus", statusDto, ct);

    private static string Cart(Guid cartId) => $"cart:{cartId}";

    private static string Location(Guid locationId) => $"location:{locationId}";

    private static string Station(Guid stationId) => $"station:{stationId}";
}
