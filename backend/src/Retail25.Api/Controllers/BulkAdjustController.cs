using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Catalog.Commands;

namespace Retail25.Api.Controllers;

/// <summary>
/// Batch changes across a selection of items (guide p.45).
/// <para>
/// The preview is a separate call and not optional in the UI: this is the most destructive thing in
/// the back office, and there is no undo short of a restore.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/catalog/bulk")]
[Produces("application/json")]
public sealed class BulkAdjustController : ControllerBase
{
    private readonly ISender _sender;

    public BulkAdjustController(ISender sender) => _sender = sender;

    [HttpPost("price/preview")]
    public async Task<IActionResult> PreviewPrice([FromBody] PreviewBulkPriceChangeQuery query, CancellationToken ct)
        => (await _sender.Send(query, ct)).ToActionResult(this);

    [HttpPost("price")]
    public async Task<IActionResult> ApplyPrice([FromBody] ApplyBulkPriceChangeCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("tax")]
    public async Task<IActionResult> ApplyTax([FromBody] ApplyBulkTaxChangeCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);
}
