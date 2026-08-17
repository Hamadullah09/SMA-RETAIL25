namespace Retail25.Application.Abstractions;

/// <summary>What a hub ticket stands for once redeemed.</summary>
/// <param name="CartId">
/// Pins the connection to a single cart, and is set only for the phone app.
/// <para>
/// A till's ticket leaves this null: a cashier legitimately watches whichever cart their station has
/// open, and the permission set is what bounds them. A shopper has no permissions at all, so without
/// a pin the hub would happily let them subscribe to <c>cart:{any id}</c> and watch a stranger's
/// shopping appear on their phone. Cart ids are sequential integers, so that is a two-minute attack.
/// </para>
/// </summary>
public sealed record HubTicket(
    long UserId,
    long? StaffId,
    long? StationId,
    long? LocationId,
    IReadOnlyList<string> Permissions,
    long? CartId = null);

/// <summary>
/// Single-use, 60-second tickets for opening a SignalR connection (doc 07 §Topology).
/// <para>
/// A WebSocket cannot carry the BFF's httpOnly cookie to another origin, and the obvious workaround —
/// handing the browser the access token for <c>accessTokenFactory</c> — would undo the entire point
/// of the BFF: the token would be reachable from JavaScript and would work against every API
/// endpoint.
/// </para>
/// <para>
/// A ticket is the narrow alternative. It authenticates one hub connection and nothing else, it is
/// consumed on redemption, and it expires in a minute — so the worst an XSS payload can steal is the
/// ability to open a socket it could already open through the page it is running in.
/// </para>
/// </summary>
public interface IHubTicketStore
{
    Task<string> IssueAsync(HubTicket ticket, TimeSpan lifetime, CancellationToken ct = default);

    /// <summary>Redeems a ticket. Returns null if it never existed, already expired, or was used.</summary>
    Task<HubTicket?> RedeemAsync(string ticket, CancellationToken ct = default);
}
