using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Catalog;
using Retail25.Application.Rfid.Commands;

namespace Retail25.Api.Controllers;

/// <summary>
/// EPCs and serial numbers: what is in stock, and how tags get associated with items (doc 06 §1).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/serialized-units")]
[Produces("application/json")]
public sealed class SerializedUnitsController : ControllerBase
{
    private readonly ISender _sender;

    public SerializedUnitsController(ISender sender) => _sender = sender;

    /// <summary>The picker shown when a serialized item is rung by its parent code (guide p.42).</summary>
    [HttpGet("available")]
    public async Task<IActionResult> Available(
        [FromQuery] Guid productId,
        [FromQuery] Guid locationId,
        [FromQuery] int take = 50)
        => Ok(await _sender.Send(new ListAvailableUnitsQuery(productId, locationId, take)));

    /// <summary>
    /// Associates one unmapped tag — the supervisor's answer to an <c>epc.unknown</c> row in the
    /// live feed.
    /// </summary>
    [HttpPost("commission")]
    public async Task<IActionResult> Commission([FromBody] CommissionTagRequest request)
        => (await _sender.Send(new CommissionTagCommand(
            request.Epc,
            request.ProductId,
            request.LocationId,
            request.VariantId,
            request.SerialNumber))).ToActionResult(this);

    /// <summary>Moves a tag that is already mapped onto a different item.</summary>
    [HttpPost("reassign")]
    public async Task<IActionResult> Reassign([FromBody] ReassignTagRequest request)
        => (await _sender.Send(new ReassignTagCommand(
            request.Epc,
            request.ProductId,
            request.VariantId))).ToActionResult(this);

    /// <summary>Commissions a delivery's worth of tags at once, reporting each tag's outcome.</summary>
    [HttpPost("commission-batch")]
    public async Task<IActionResult> CommissionBatch([FromBody] CommissionBatchRequest request)
        => (await _sender.Send(new CommissionTagBatchCommand(
            request.ProductId,
            request.LocationId,
            request.Epcs,
            request.VariantId))).ToActionResult(this);
}

/// <summary>Matrix items: the dimension grid and the variants it generates (guide p.39–40).</summary>
[ApiController]
[Authorize]
[Route("api/v1/products/{productId:guid}/matrix")]
[Produces("application/json")]
public sealed class MatrixController : ControllerBase
{
    private readonly ISender _sender;

    public MatrixController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Get(Guid productId)
        => (await _sender.Send(new GetMatrixQuery(productId))).ToActionResult(this);

    /// <summary>Defines the dimensions and generates the cross product of their values.</summary>
    [HttpPut]
    public async Task<IActionResult> Define(Guid productId, [FromBody] DefineMatrixRequest request)
        => (await _sender.Send(new DefineMatrixCommand(productId, request.Dimensions))).ToActionResult(this);

    /// <summary>The variant picker at the till, optionally limited to what is actually on the shelf.</summary>
    [HttpGet("variants")]
    public async Task<IActionResult> Variants(
        Guid productId,
        [FromQuery] Guid locationId,
        [FromQuery] bool inStockOnly = false)
        => Ok(await _sender.Send(new ListVariantsQuery(productId, locationId, inStockOnly)));
}

public sealed record ReassignTagRequest(string Epc, Guid ProductId, Guid? VariantId = null);

public sealed record CommissionTagRequest(
    string Epc,
    Guid ProductId,
    Guid LocationId,
    Guid? VariantId = null,
    string? SerialNumber = null);

public sealed record CommissionBatchRequest(
    Guid ProductId,
    Guid LocationId,
    IReadOnlyList<string> Epcs,
    Guid? VariantId = null);

public sealed record DefineMatrixRequest(IReadOnlyList<MatrixDimensionDto> Dimensions);
