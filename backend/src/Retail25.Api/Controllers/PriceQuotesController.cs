using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Retail25.Api.Common;
using Retail25.Application.Orders;
using Retail25.Domain.Orders;

namespace Retail25.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/price-quotes")]
[Produces("application/json")]
public sealed class PriceQuotesController : ControllerBase
{
    private readonly ISender _sender;

    public PriceQuotesController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] long? customerId,
        [FromQuery] PriceQuoteStatus? status,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new BrowsePriceQuotesQuery(locationId, customerId, status, cursor, pageSize), ct));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
        => (await _sender.Send(new GetPriceQuoteQuery(id), ct)).ToActionResult(this);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePriceQuoteCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("{id:long}/convert")]
    public async Task<IActionResult> Convert(long id, CancellationToken ct)
        => (await _sender.Send(new ConvertPriceQuoteCommand(id), ct)).ToActionResult(this);

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
        => (await _sender.Send(new CancelPriceQuoteCommand(id), ct)).ToActionResult(this);
}
