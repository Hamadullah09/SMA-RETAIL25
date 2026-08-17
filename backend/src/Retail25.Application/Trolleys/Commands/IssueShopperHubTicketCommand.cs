using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Common;
using Retail25.Domain.Trolleys;

namespace Retail25.Application.Trolleys.Commands;

/// <summary>What the phone needs to open its live connection.</summary>
/// <param name="Ticket">Single-use, 60-second value passed as the hub's <c>access_token</c>.</param>
/// <param name="CartId">The cart to subscribe to. The ticket permits this one and no other.</param>
public sealed record ShopperHubTicketDto(string Ticket, int ExpiresInSeconds, long CartId);

/// <summary>
/// Mints the ticket that authenticates the shopper's SignalR connection.
/// <para>
/// A WebSocket handshake cannot carry an <c>Authorization</c> header, which is why the hub reads a
/// value from the query string instead. Putting the real bearer token there would be a bad trade: it
/// would end up in server access logs and proxy logs, and it works against every shopper endpoint.
/// A ticket is worth exactly one hub connection, is consumed on redemption, and dies in a minute.
/// </para>
/// </summary>
public sealed record IssueShopperHubTicketCommand : IRequest<Result<ShopperHubTicketDto>>;

public sealed class IssueShopperHubTicketHandler
    : IRequestHandler<IssueShopperHubTicketCommand, Result<ShopperHubTicketDto>>
{
    /// <summary>
    /// A minute — long enough for a handshake on shop wi-fi, short enough that a captured ticket is
    /// almost always already dead.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentShopper _shopper;
    private readonly IHubTicketStore _tickets;

    public IssueShopperHubTicketHandler(
        IApplicationDbContext db,
        ICurrentShopper shopper,
        IHubTicketStore tickets)
    {
        _db = db;
        _shopper = shopper;
        _tickets = tickets;
    }

    public async Task<Result<ShopperHubTicketDto>> Handle(
        IssueShopperHubTicketCommand request,
        CancellationToken ct)
    {
        if (_shopper.ShopperId is not { } shopperId)
        {
            return Result.Failure<ShopperHubTicketDto>(Services.TrolleyAllocator.NotSignedIn);
        }

        var session = await _db.TrolleySessions
            .FirstOrDefaultAsync(
                s => s.ShopperId == shopperId && s.State == TrolleySessionState.Shopping,
                ct);

        if (session is null)
        {
            return Result.Failure<ShopperHubTicketDto>(Queries.GetMyCartHandler.NoLiveSession);
        }

        var ticket = new HubTicket(
            // A shopper id, not an ApplicationUser id. The two never mix, because this principal is
            // built only for a hub connection and carries no permissions — the empty list below is
            // the point. It can join one cart group and call nothing else.
            UserId: shopperId,
            StaffId: null,
            StationId: null,
            LocationId: session.LocationId,
            Permissions: [],
            CartId: session.CartId);

        var value = await _tickets.IssueAsync(ticket, Lifetime, ct);

        return Result.Success(new ShopperHubTicketDto(
            value,
            (int)Lifetime.TotalSeconds,
            session.CartId));
    }
}
