using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Purchasing;
using Retail25.Domain.Purchasing;

namespace Retail25.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/purchase-orders")]
[Produces("application/json")]
public sealed class PurchaseOrdersController : ControllerBase
{
    private readonly ISender _sender;

    public PurchaseOrdersController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery] long locationId,
        [FromQuery] long? supplierId,
        [FromQuery] PurchaseOrderStatus? status,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new BrowsePurchaseOrdersQuery(locationId, supplierId, status, cursor, pageSize), ct));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
        => (await _sender.Send(new GetPurchaseOrderQuery(id), ct)).ToActionResult(this);

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GeneratePurchaseOrderCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("{id:long}/lines")]
    public async Task<IActionResult> AddLine(long id, [FromBody] AddPurchaseOrderLineRequest request, CancellationToken ct)
        => (await _sender.Send(
            new AddPurchaseOrderLineCommand(id, request.ProductId, request.OrderQty, request.CostEach, request.CaseQty), ct))
            .ToActionResult(this);

    [HttpPut("lines/{lineId:long}")]
    public async Task<IActionResult> UpdateLine(long lineId, [FromBody] UpdatePurchaseOrderLineRequest request, CancellationToken ct)
        => (await _sender.Send(new UpdatePurchaseOrderLineCommand(lineId, request.OrderQty, request.CostEach), ct))
            .ToActionResult(this);

    [HttpDelete("lines/{lineId:long}")]
    public async Task<IActionResult> RemoveLine(long lineId, CancellationToken ct)
        => (await _sender.Send(new RemovePurchaseOrderLineCommand(lineId), ct)).ToActionResult(this);

    [HttpPost("{id:long}/post")]
    public async Task<IActionResult> Post(long id, CancellationToken ct)
        => (await _sender.Send(new PostPurchaseOrderCommand(id), ct)).ToActionResult(this);

    [HttpPost("{id:long}/receive")]
    public async Task<IActionResult> Receive(long id, [FromBody] ReceivePurchaseOrderRequest request, CancellationToken ct)
        => (await _sender.Send(new ReceivePurchaseOrderCommand(id, request.ReceivedOn, request.FreightTotal, request.Lines), ct))
            .ToActionResult(this);

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
        => (await _sender.Send(new CancelPurchaseOrderCommand(id), ct)).ToActionResult(this);
}

public sealed record AddPurchaseOrderLineRequest(long ProductId, decimal OrderQty, decimal CostEach, decimal CaseQty);

public sealed record UpdatePurchaseOrderLineRequest(decimal OrderQty, decimal CostEach);

public sealed record ReceivePurchaseOrderRequest(DateOnly ReceivedOn, decimal FreightTotal, IReadOnlyList<ReceivePurchaseOrderLine> Lines);
