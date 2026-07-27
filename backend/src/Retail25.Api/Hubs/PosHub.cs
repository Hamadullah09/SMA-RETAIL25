using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Retail25.Api.Hubs;

/// <summary>
/// POS real-time hub (doc 05 §SignalR). Groups: station, location, cart.
/// Server → client: CartUpdated, CartLinesAdded, TotalsChanged, TagStreamStatus.
/// Client → server: JoinStation, LeaveStation, Heartbeat, RequestCartResync.
/// </summary>
[Authorize]
public class PosHub : Hub
{
    public async Task JoinStation(string stationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"station:{stationId}");
    }

    public async Task LeaveStation(string stationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"station:{stationId}");
    }

    public async Task JoinLocation(string locationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"location:{locationId}");
    }

    public async Task JoinCart(string cartId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"cart:{cartId}");
    }

    public async Task RequestCartResync(string cartId, int knownRevision)
    {
        // Server sends full cart state to the requesting client.
        await Clients.Caller.SendAsync("CartResyncRequired", cartId, knownRevision);
    }

    public async Task Heartbeat()
    {
        await Clients.Caller.SendAsync("HeartbeatAck");
    }
}
