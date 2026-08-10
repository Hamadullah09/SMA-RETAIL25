using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Retail25.Application.Abstractions;
using Retail25.Application.Rfid;
using Retail25.Infrastructure.Identity;

namespace Retail25.Infrastructure.Realtime;

/// <summary>
/// The till's realtime channel (doc 05 §SignalR).
/// <para>
/// Groups are scoped so a station only ever receives its own cart: <c>station:{id}</c> for peripheral
/// and rejection messages, <c>cart:{id}</c> for cart state, <c>location:{id}</c> for things every till
/// in the shop should see, such as a cart being suspended.
/// </para>
/// </summary>
[Authorize(Policy = IdentityRegistration.HubAuthorizationPolicy)]
public sealed class PosHub : Hub
{
    private readonly ICartStore _cartStore;
    private readonly IReaderConnectionStatus _readerStatus;

    public PosHub(ICartStore cartStore, IReaderConnectionStatus readerStatus)
    {
        _cartStore = cartStore;
        _readerStatus = readerStatus;
    }

    // Ids are numbers on the wire since the integer re-key: a string parameter here makes SignalR
    // refuse the invocation outright ("Error binding arguments"), which the till shows as a server
    // that never comes online.
    public async Task JoinStation(long stationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, PosGroups.Station(stationId));

        // The reader's state, told to whoever just arrived.
        //
        // Status is pushed when it changes, and a change that happened before this till opened its
        // browser is a change it never hears about. That is the whole bug behind a status chip that
        // reads "offline" while the reader is plainly working: the announcement was made to an empty
        // room. Sending it on join makes the strip correct from the first paint.
        //
        // Only when this server holds the readers. Where tills run agents, the agent is the
        // authority and a second opinion from here would fight it.
        var status = _readerStatus.Current;

        if (status.ServerHosted)
        {
            var online = status.Readers.Any(r => r.StationId == stationId && r.Connected);
            await Clients.Caller.SendAsync("TagStreamStatus", new { readerOnline = online, readRate = 0 });
        }
    }

    public Task LeaveStation(long stationId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, PosGroups.Station(stationId));

    public Task JoinLocation(long locationId)
        => Groups.AddToGroupAsync(Context.ConnectionId, PosGroups.Location(locationId));

    public Task JoinCart(long cartId)
        => Groups.AddToGroupAsync(Context.ConnectionId, PosGroups.Cart(cartId));

    public Task LeaveCart(long cartId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, PosGroups.Cart(cartId));

    /// <summary>
    /// Called when a client notices a gap in the revision sequence. Answering with the server's
    /// revision rather than the whole cart keeps the round trip cheap; the client then fetches state
    /// over HTTP only if it is genuinely behind.
    /// </summary>
    public async Task RequestCartResync(long cartId, int knownRevision)
    {
        var snapshot = await _cartStore.GetAsync(cartId, Context.ConnectionAborted);

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

    public static string Station(long stationId) => Station(stationId.ToString(CultureInfo.InvariantCulture));

    public static string Location(string locationId) => $"location:{locationId}";

    public static string Location(long locationId) => Location(locationId.ToString(CultureInfo.InvariantCulture));

    public static string Cart(string cartId) => $"cart:{cartId}";

    public static string Cart(long cartId) => Cart(cartId.ToString(CultureInfo.InvariantCulture));

    public static string Grid(string entity, string filterHash) => $"grid:{entity}:{filterHash}";
}

/// <summary>
/// Browse-grid patching (doc 05 §SignalR). This is the direct answer to the legacy complaint that
/// browse windows go stale over a network (guide p.100–101): grids subscribe to their filter and
/// receive row-level patches instead of polling.
/// </summary>
[Authorize(Policy = IdentityRegistration.HubAuthorizationPolicy)]
public sealed class InventoryHub : Hub
{
    public Task SubscribeToGrid(string entity, string filterHash)
        => Groups.AddToGroupAsync(Context.ConnectionId, PosGroups.Grid(entity, filterHash));

    public Task UnsubscribeFromGrid(string entity, string filterHash)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, PosGroups.Grid(entity, filterHash));

    public Task JoinLocation(long locationId)
        => Groups.AddToGroupAsync(Context.ConnectionId, PosGroups.Location(locationId));
}
