namespace Retail25.Application.Abstractions;

/// <summary>What a hub ticket stands for once redeemed.</summary>
public sealed record HubTicket(
    long UserId,
    long? StaffId,
    long? StationId,
    long? LocationId,
    IReadOnlyList<string> Permissions);

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
