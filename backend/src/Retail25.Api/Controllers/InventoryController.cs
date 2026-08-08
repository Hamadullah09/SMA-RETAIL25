using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Inventory;

namespace Retail25.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/inventory")]
[Produces("application/json")]
public sealed class InventoryController : ControllerBase
{
    private readonly ISender _sender;

    public InventoryController(ISender sender) => _sender = sender;

    [HttpGet("stock-levels")]
    public async Task<IActionResult> StockLevels(
        [FromQuery] long locationId,
        [FromQuery] string? search,
        [FromQuery] bool belowReorderOnly = false,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new BrowseStockLevelsQuery(locationId, search, belowReorderOnly, cursor, pageSize), ct));

    [HttpPost("receive")]
    public async Task<IActionResult> Receive([FromBody] ReceiveStockCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("adjust")]
    public async Task<IActionResult> Adjust([FromBody] AdjustStockCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("case-break")]
    public async Task<IActionResult> BreakCase([FromBody] BreakCaseCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);
}
