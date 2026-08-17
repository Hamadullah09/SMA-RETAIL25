using System.Globalization;
using System.Security.Claims;
using OpenIddict.Abstractions;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Identity;

namespace Retail25.Api.Common;

/// <summary>
/// Authenticates a hub connection from a single-use ticket (doc 07 §Topology).
/// <para>
/// SignalR passes its token as the <c>access_token</c> query parameter, because a WebSocket handshake
/// cannot carry an Authorization header. This middleware redeems that value as a ticket and builds
/// the principal itself, so the browser never needs — and never receives — a real access token.
/// </para>
/// <para>
/// It runs before authentication and only for hub paths. Anything else is left alone, so the ticket
/// mechanism can never be used to reach an API endpoint.
/// </para>
/// </summary>
public sealed class HubTicketMiddleware
{
    private const string HubPathPrefix = "/hubs";

    private readonly RequestDelegate _next;
    private readonly ILogger<HubTicketMiddleware> _logger;

    public HubTicketMiddleware(RequestDelegate next, ILogger<HubTicketMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IHubTicketStore tickets)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tickets);

        if (!context.Request.Path.StartsWithSegments(HubPathPrefix))
        {
            await _next(context);
            return;
        }

        var value = context.Request.Query["access_token"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(value))
        {
            // Also the Authorization header, and this is not belt-and-braces — it is half the
            // handshake.
            //
            // Opening a connection is two requests. The WebSocket upgrade cannot carry headers, so
            // the token goes in the query string, which is what this middleware was written for. But
            // the negotiate POST that precedes it is an ordinary HTTP request, and every SignalR
            // client — .NET and JavaScript alike — sends the token as a bearer header there.
            //
            // Reading only the query meant negotiate arrived unauthenticated. The hub's [Authorize]
            // then challenged, the default challenge scheme is the Identity cookie, and the client
            // got a 302 to the sign-in page. What it reports is "Failed to complete negotiation:
            // Unexpected token '<'" — an HTML login page where JSON was expected — which reads like
            // a broken URL and is in fact an auth failure.
            var header = context.Request.Headers.Authorization.ToString();

            const string bearer = "Bearer ";

            if (header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
            {
                value = header[bearer.Length..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            await _next(context);
            return;
        }

        var ticket = await tickets.RedeemAsync(value, context.RequestAborted);

        if (ticket is null)
        {
            // An expired or already-used ticket is normal — a reconnect races its own expiry — so
            // this is not an error. The connection simply fails authorisation and the client asks
            // for a new one.
            _logger.LogDebug("A hub ticket could not be redeemed");
            await _next(context);
            return;
        }

        var identity = new ClaimsIdentity("HubTicket", OpenIddictConstants.Claims.Name, OpenIddictConstants.Claims.Role);

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, ticket.UserId.ToString(CultureInfo.InvariantCulture)));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, ticket.UserId.ToString(CultureInfo.InvariantCulture)));

        if (ticket.StaffId is { } staffId)
        {
            identity.AddClaim(new Claim(AuthConstants.StaffIdClaim, staffId.ToString(CultureInfo.InvariantCulture)));
        }

        if (ticket.StationId is { } stationId)
        {
            identity.AddClaim(new Claim(AuthConstants.StationIdClaim, stationId.ToString(CultureInfo.InvariantCulture)));
        }

        if (ticket.LocationId is { } locationId)
        {
            identity.AddClaim(new Claim(AuthConstants.LocationIdClaim, locationId.ToString(CultureInfo.InvariantCulture)));
        }

        // Only the phone app's tickets carry this. It is a ceiling, not a grant: the hub refuses any
        // cart group other than this one.
        if (ticket.CartId is { } cartId)
        {
            identity.AddClaim(new Claim(AuthConstants.CartIdClaim, cartId.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var permission in ticket.Permissions)
        {
            identity.AddClaim(new Claim(AuthConstants.PermissionClaim, permission));
        }

        context.User = new ClaimsPrincipal(identity);

        await _next(context);
    }
}
