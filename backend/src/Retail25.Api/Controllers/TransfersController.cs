using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        [FromQuery] Guid locationId,
        [FromQuery] TransferStatus? status = null,
        [FromQuery] bool includeInbound = true,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new BrowseTransfersQuery(locationId, status, includeInbound, skip, take), ct));

    /// <summary>Where stock can be sent, for the picker.</summary>
    [HttpGet("destinations")]
    public async Task<IActionResult> Destinations([FromQuery] Guid locationId, CancellationToken ct)
        => Ok(await _sender.Send(new ListTransferDestinationsQuery(locationId), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => (await _sender.Send(new GetTransferQuery(id), ct)).ToActionResult(this);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransferCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("{id:guid}/lines")]
    public async Task<IActionResult> UpsertLine(Guid id, [FromBody] TransferLineRequest request, CancellationToken ct)
        => (await _sender.Send(new UpsertTransferLineCommand(id, request.ProductId, request.Quantity), ct))
            .ToActionResult(this);

    [HttpDelete("{id:guid}/lines/{lineId:guid}")]
    public async Task<IActionResult> RemoveLine(Guid id, Guid lineId, CancellationToken ct)
        => (await _sender.Send(new RemoveTransferLineCommand(id, lineId), ct)).ToActionResult(this);

    [HttpPost("{id:guid}/ship")]
    public async Task<IActionResult> Ship(Guid id, CancellationToken ct)
        => (await _sender.Send(new ShipTransferCommand(id), ct)).ToActionResult(this);

    /// <summary>An empty body receives everything still outstanding.</summary>
    [HttpPost("{id:guid}/receive")]
    public async Task<IActionResult> Receive(Guid id, [FromBody] ReceiveTransferRequest? request, CancellationToken ct)
        => (await _sender.Send(new ReceiveTransferCommand(id, request?.Lines), ct)).ToActionResult(this);

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => (await _sender.Send(new CancelTransferCommand(id), ct)).ToActionResult(this);
}

public sealed record TransferLineRequest(Guid ProductId, decimal Quantity);

public sealed record ReceiveTransferRequest(IReadOnlyList<ReceiveTransferLine>? Lines);
