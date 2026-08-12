using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Retail25.Api.Common;
using Retail25.Application.Inventory;
using Retail25.Domain.Inventory;

namespace Retail25.Api.Controllers;

/// <summary>Stock transfers between locations (guide p.20–21).</summary>
[ApiController]
[Authorize]
[Route("api/v1/transfers")]
[Produces("application/json")]
public sealed class TransfersController : ControllerBase
{
    private readonly ISender _sender;

    public TransfersController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] TransferStatus? status = null,
        [FromQuery] bool includeInbound = true,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new BrowseTransfersQuery(locationId, status, includeInbound, skip, take), ct));

    /// <summary>Where stock can be sent, for the picker.</summary>
    [HttpGet("destinations")]
    public async Task<IActionResult> Destinations([FromQuery][BindRequired] long locationId, CancellationToken ct)
        => Ok(await _sender.Send(new ListTransferDestinationsQuery(locationId), ct));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
        => (await _sender.Send(new GetTransferQuery(id), ct)).ToActionResult(this);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransferCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("{id:long}/lines")]
    public async Task<IActionResult> UpsertLine(long id, [FromBody] TransferLineRequest request, CancellationToken ct)
        => (await _sender.Send(new UpsertTransferLineCommand(id, request.ProductId, request.Quantity), ct))
            .ToActionResult(this);

    [HttpDelete("{id:long}/lines/{lineId:long}")]
    public async Task<IActionResult> RemoveLine(long id, long lineId, CancellationToken ct)
        => (await _sender.Send(new RemoveTransferLineCommand(id, lineId), ct)).ToActionResult(this);

    [HttpPost("{id:long}/ship")]
    public async Task<IActionResult> Ship(long id, CancellationToken ct)
        => (await _sender.Send(new ShipTransferCommand(id), ct)).ToActionResult(this);

    /// <summary>An empty body receives everything still outstanding.</summary>
    [HttpPost("{id:long}/receive")]
    public async Task<IActionResult> Receive(long id, [FromBody] ReceiveTransferRequest? request, CancellationToken ct)
        => (await _sender.Send(new ReceiveTransferCommand(id, request?.Lines), ct)).ToActionResult(this);

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
        => (await _sender.Send(new CancelTransferCommand(id), ct)).ToActionResult(this);
}

public sealed record TransferLineRequest(long ProductId, decimal Quantity);

public sealed record ReceiveTransferRequest(IReadOnlyList<ReceiveTransferLine>? Lines);
