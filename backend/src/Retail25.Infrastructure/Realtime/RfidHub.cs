using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Identity;

namespace Retail25.Infrastructure.Realtime;

/// <summary>
/// The read feed (<c>/hubs/rfid</c>).
/// <para>
/// One direction only: the server pushes, clients listen. Tags arrive from the terminal agent over
/// <see cref="TerminalHub"/>, go through ingestion — debounce, EPC resolution, session gating — and
/// what survives is broadcast here. Nothing a client sends on this hub can put a tag into the system,
/// which is the point of splitting it from the agent's channel: a browser session should not be able
/// to impersonate a reader.
/// </para>
/// <para>
/// Subscriptions are per station or per location, so a till sees its own antenna field and a stock
/// count sees the whole shop. A client that subscribes to neither receives nothing at all rather than
/// everything — the safe default when a subscription call is forgotten.
/// </para>
/// </summary>
[Authorize(Policy = IdentityRegistration.HubAuthorizationPolicy)]
public sealed class RfidHub : Hub
{
    // Ids are numbers on the wire since the integer re-key; a string parameter makes SignalR refuse
    // the invocation before the method body ever runs.

    /// <summary>Watch one till's reader.</summary>
    public Task SubscribeToStation(long stationId)
        => Groups.AddToGroupAsync(Context.ConnectionId, RfidGroups.Station(stationId));

    public Task UnsubscribeFromStation(long stationId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, RfidGroups.Station(stationId));

    /// <summary>Watch every reader in a store — what a stock count or a goods-in bench wants.</summary>
    public Task SubscribeToLocation(long locationId)
        => Groups.AddToGroupAsync(Context.ConnectionId, RfidGroups.Location(locationId));

    public Task UnsubscribeFromLocation(long locationId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, RfidGroups.Location(locationId));
}

/// <summary>Group names for the read feed. Prefixed so they cannot collide with <see cref="PosGroups"/>.</summary>
public static class RfidGroups
{
    public static string Station(long stationId) => $"rfid:station:{stationId}";

    public static string Location(long locationId) => $"rfid:location:{locationId}";
}

/// <summary>
/// Fans an observation out to both the station's watchers and the store's.
/// <para>
/// Sent to two groups rather than one because the audiences genuinely differ and a client should not
/// have to subscribe to every station in the shop to watch the shop. SignalR de-duplicates by
/// connection, so a client in both groups still receives one copy.
/// </para>
/// </summary>
public sealed class RfidNotifier : IRfidNotifier
{
    private readonly IHubContext<RfidHub> _hub;

    public RfidNotifier(IHubContext<RfidHub> hub) => _hub = hub;

    public Task TagsObservedAsync(
        long locationId,
        long stationId,
        IReadOnlyList<ObservedTag> tags,
        CancellationToken ct = default)
    {
        if (tags.Count == 0)
        {
            return Task.CompletedTask;
        }

        var payload = new { stationId, locationId, tags };

        // The station that the reads were routed to, and nowhere else.
        //
        // This also went to the whole location, which was harmless while one reader served one till
        // and stopped being harmless the moment an antenna map existed: every till in the shop
        // received every read, and the till screen applies what it receives. An item held at the
        // antenna serving till one would go onto till two's open sale — silently, correctly
        // according to every log, and wrongly according to the customer.
        //
        // It is also what the estate cannot afford. Two hundred and fifty-two tills each receiving
        // every read in the building is the fan-out the routing exists to avoid.
        return _hub.Clients
            .Group(RfidGroups.Station(stationId))
            .SendAsync("TagsObserved", payload, ct);
    }

    public Task ReaderStatusAsync(
        long locationId,
        long stationId,
        RfidReaderStatus status,
        CancellationToken ct = default)
        => _hub.Clients
            .Groups(RfidGroups.Station(stationId), RfidGroups.Location(locationId))
            .SendAsync("ReaderStatus", new { stationId, locationId, status }, ct);
}
