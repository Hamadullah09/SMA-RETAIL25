using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Retail25.Application.Abstractions;

namespace Retail25.Infrastructure.Realtime;

/// <summary>
/// The till's realtime channel (doc 05 §SignalR).
/// <para>
/// Groups are scoped so a station only ever receives its own cart: <c>station:{id}</c> for peripheral
/// and rejection messages, <c>cart:{id}</c> for cart state, <c>location:{id}</c> for things every till
/// in the shop should see, such as a cart being suspended.
/// </para>
/// </summary>
[Authorize]
public sealed class PosHub : Hub
{
    private readonly ICartStore _cartStore;

    public PosHub(ICartStore cartStore) => _cartStore = cartStore;

    public Task JoinStation(string stationId)
        => Groups.AddToGroupAsync(Context.ConnectionId, PosGroups.Station(stationId));

    public Task LeaveStation(string stationId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, PosGroups.Station(stationId));

    public Task JoinLocation(string locationId)
        => Groups.AddToGroupAsync(Context.ConnectionId, PosGroups.Location(locationId));

    public Task JoinCart(string cartId)
        => Groups.AddToGroupAsync(Context.ConnectionId, PosGroups.Cart(cartId));

    public Task LeaveCart(string cartId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, PosGroups.Cart(cartId));

    /// <summary>
    /// Called when a client notices a gap in the revision sequence. Answering with the server's
    /// revision rather than the whole cart keeps the round trip cheap; the client then fetches state
    /// over HTTP only if it is genuinely behind.
    /// </summary>
    public async Task RequestCartResync(string cartId, int knownRevision)
    {
        if (!Guid.TryParse(cartId, out var id))
        {
            return;
        }

        var snapshot = await _cartStore.GetAsync(id, Context.ConnectionAborted);

        await Clients.Caller.SendAsync(
            "CartResyncRequired",
            new { cartId, knownRevision, serverRevision = snapshot?.Cart.Revision ?? 0, exists = snapshot is not null },
            Context.ConnectionAborted);
    }

    public Task Heartbeat() => Clients.Caller.SendAsync("HeartbeatAck", DateTimeOffset.UtcNow);
}

/// <summary>Group naming, in one place so the hub and the notifier cannot disagree.</summary>
public static class PosGroups
{
    public static string Station(string stationId) => $"station:{stationId}";

    public static string Station(Guid stationId) => Station(stationId.ToString());

    public static string Location(string locationId) => $"location:{locationId}";

    public static string Location(Guid locationId) => Location(locationId.ToString());

    public static string Cart(string cartId) => $"cart:{cartId}";

    public static string Cart(Guid cartId) => Cart(cartId.ToString());

    public static string Grid(string entity, string filterHash) => $"grid:{entity}:{filterHash}";
}

/// <summary>
/// Browse-grid patching (doc 05 §SignalR). This is the direct answer to the legacy complaint that
/// browse windows go stale over a network (guide p.100–101): grids subscribe to their filter and
/// receive row-level patches instead of polling.
/// </summary>
[Authorize]
public sealed class InventoryHub : Hub
{
    public Task SubscribeToGrid(string entity, string filterHash)
        => Groups.AddToGroupAsync(Context.ConnectionId, PosGroups.Grid(entity, filterHash));

    public Task UnsubscribeFromGrid(string entity, string filterHash)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, PosGroups.Grid(entity, filterHash));

    public Task JoinLocation(string locationId)
        => Groups.AddToGroupAsync(Context.ConnectionId, PosGroups.Location(locationId));
}
