using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Identity;

namespace Retail25.Api.Controllers;

/// <summary>
/// Mints the single-use ticket a browser needs to open a hub connection (doc 07 §Topology).
/// <para>
/// Called by the BFF with the session's bearer token, never by the browser directly. What comes back
/// is scoped to one connection and expires in a minute, so the value that does reach JavaScript
/// cannot be used against any API endpoint.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/hub-tickets")]
[Produces("application/json")]
public sealed class HubTicketsController : ControllerBase
{
    /// <summary>
    /// A minute. Long enough for a handshake on a slow shop network, short enough that a captured
    /// ticket is almost always already dead.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private readonly IHubTicketStore _tickets;
    private readonly ICurrentUser _currentUser;

    public HubTicketsController(IHubTicketStore tickets, ICurrentUser currentUser)
    {
        _tickets = tickets;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Issue([FromBody] HubTicketRequest? request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        var ticket = new HubTicket(
            userId,
            _currentUser.StaffId,
            // The station comes from the request because one user may work more than one till, and
            // the claim only carries a default.
            request?.StationId ?? _currentUser.StationId,
            _currentUser.LocationId,
            _currentUser.Permissions.ToList());

        var value = await _tickets.IssueAsync(ticket, Lifetime, ct);

        return Ok(new
        {
            ticket = value,
            expiresInSeconds = (int)Lifetime.TotalSeconds,
        });
    }
}

public sealed record HubTicketRequest(Guid? StationId);
