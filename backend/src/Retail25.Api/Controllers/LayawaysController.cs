using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Orders;
using Retail25.Domain.Orders;

namespace Retail25.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/layaways")]
[Produces("application/json")]
public sealed class LayawaysController : ControllerBase
{
    private readonly ISender _sender;

    public LayawaysController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery] Guid locationId,
        [FromQuery] Guid? customerId,
        [FromQuery] LayawayStatus? status,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new BrowseLayawaysQuery(locationId, customerId, status, cursor, pageSize), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => (await _sender.Send(new GetLayawayQuery(id), ct)).ToActionResult(this);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLayawayCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("{id:guid}/payments")]
    public async Task<IActionResult> TakePayment(Guid id, [FromBody] TakeLayawayPaymentRequest request, CancellationToken ct)
        => (await _sender.Send(new TakeLayawayPaymentCommand(id, request.Amount, request.TenderTypeId), ct)).ToActionResult(this);

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => (await _sender.Send(new CancelLayawayCommand(id), ct)).ToActionResult(this);
}

public sealed record TakeLayawayPaymentRequest(decimal Amount, Guid TenderTypeId);
