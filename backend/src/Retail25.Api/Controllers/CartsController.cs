using MediatR;
using Microsoft.AspNetCore.Mvc;
using Retail25.Application.Carts.Commands;

namespace Retail25.Api.Controllers;

[ApiController]
[Route("api/v1/carts")]
public class CartsController : ControllerBase
{
    private readonly ISender _sender;

    public CartsController(ISender sender) => _sender = sender;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCartRequest request)
    {
        var result = await _sender.Send(new CreateCartCommand(request.StationId, request.StaffId));
        return Ok(result);
    }

    [HttpPost("{cartId:guid}/lines")]
    public async Task<IActionResult> AddLine(Guid cartId, [FromBody] AddLineRequest request)
    {
        var result = await _sender.Send(new AddCartLineByIdentifierCommand(
            cartId, request.Identifier, request.Quantity, request.ManualPrice,
            request.ManualDiscount, request.PriceLevel, request.Tax1Override, request.Tax2Override));

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(result);
    }

    [HttpPost("{cartId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid cartId, [FromBody] CompleteSaleRequest request)
    {
        var result = await _sender.Send(new CompleteSaleCommand(
            cartId, request.StaffId, request.Tenders, request.PrintReceipt, request.CopyCount));

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(result);
    }
}

public record CreateCartRequest(Guid StationId, Guid StaffId);
public record AddLineRequest(string Identifier, decimal? Quantity, decimal? ManualPrice, decimal? ManualDiscount, int? PriceLevel, bool? Tax1Override, bool? Tax2Override);
public record CompleteSaleRequest(Guid StaffId, List<TenderInput> Tenders, bool PrintReceipt = true, int CopyCount = 1);
