using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Drawer;

namespace Retail25.Api.Controllers;

/// <summary>The legacy F10 drawer menu (guide p.10–11) as endpoints.</summary>
[ApiController]
[Authorize]
[Route("api/v1/drawer-sessions")]
[Produces("application/json")]
public sealed class DrawerSessionsController : ControllerBase
{
    private readonly ISender _sender;

    public DrawerSessionsController(ISender sender) => _sender = sender;

    [HttpGet("current")]
    public async Task<IActionResult> Current([FromQuery] Guid stationId)
        => (await _sender.Send(new GetDrawerTotalsQuery(stationId))).ToActionResult(this);

    [HttpGet("{sessionId:guid}")]
    public async Task<IActionResult> Get(Guid sessionId, [FromQuery] Guid stationId)
        => (await _sender.Send(new GetDrawerTotalsQuery(stationId, sessionId))).ToActionResult(this);

    [HttpPost]
    public async Task<IActionResult> Open([FromBody] OpenDrawerRequest request)
        => (await _sender.Send(new OpenDrawerSessionCommand(request.StationId, request.OpeningFloat))).ToActionResult(this);

    [HttpPost("pay-in")]
    public async Task<IActionResult> PayIn([FromBody] DrawerMovementRequest request)
        => (await _sender.Send(new PayInCommand(request.StationId, request.Amount, request.Reason))).ToActionResult(this);

    [HttpPost("pay-out")]
    public async Task<IActionResult> PayOut([FromBody] DrawerMovementRequest request)
        => (await _sender.Send(new PayOutCommand(request.StationId, request.Amount, request.Reason))).ToActionResult(this);

    /// <summary>No-sale pop. Recorded even though no money moves (guide p.11).</summary>
    [HttpPost("pop")]
    public async Task<IActionResult> Pop([FromBody] PopDrawerRequest request)
        => (await _sender.Send(new PopDrawerCommand(request.StationId, request.Reason))).ToActionResult(this);

    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] CloseDrawerRequest request)
        => (await _sender.Send(new CloseDrawerSessionCommand(request.StationId, request.CountedCash))).ToActionResult(this);
}

public sealed record OpenDrawerRequest(Guid StationId, decimal OpeningFloat);

public sealed record DrawerMovementRequest(Guid StationId, decimal Amount, string Reason);

public sealed record PopDrawerRequest(Guid StationId, string? Reason = null);

public sealed record CloseDrawerRequest(Guid StationId, decimal CountedCash);
