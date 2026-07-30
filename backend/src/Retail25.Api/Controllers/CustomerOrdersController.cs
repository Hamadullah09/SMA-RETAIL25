using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Orders;
using Retail25.Domain.Orders;

namespace Retail25.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/customer-orders")]
[Produces("application/json")]
public sealed class CustomerOrdersController : ControllerBase
{
    private readonly ISender _sender;

    public CustomerOrdersController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery] Guid locationId,
        [FromQuery] Guid? customerId,
        [FromQuery] CustomerOrderStatus? status,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new BrowseCustomerOrdersQuery(locationId, customerId, status, cursor, pageSize), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => (await _sender.Send(new GetCustomerOrderQuery(id), ct)).ToActionResult(this);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerOrderCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("{id:guid}/fill")]
    public async Task<IActionResult> Fill(Guid id, CancellationToken ct)
        => (await _sender.Send(new FillCustomerOrderCommand(id), ct)).ToActionResult(this);

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => (await _sender.Send(new CancelCustomerOrderCommand(id), ct)).ToActionResult(this);
}
