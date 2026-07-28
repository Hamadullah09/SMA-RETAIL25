using MediatR;
using Microsoft.AspNetCore.Mvc;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Carts.Queries;

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

    /// <summary>
    /// Totals the cart as the server sees it. The client never computes a total of its own; this is
    /// the only figure the customer is asked to pay.
    /// </summary>
    [HttpGet("{cartId:guid}/quote")]
    public async Task<IActionResult> Quote(Guid cartId)
    {
        var result = await _sender.Send(new QuoteCartQuery(cartId));

        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Quote);
    }

    /// <summary>Edits a line already on the sale — the legacy item-detail window (guide p.6).</summary>
    [HttpPatch("{cartId:guid}/lines/{lineId:guid}")]
    public async Task<IActionResult> UpdateLine(Guid cartId, Guid lineId, [FromBody] UpdateLineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Respond(await _sender.Send(new UpdateCartLineCommand(
            cartId, lineId, request.Quantity, request.ManualPrice, request.ManualDiscountPct,
            request.PriceLevel, request.Tax1Override, request.Tax2Override, request.ReturnToStock)));
    }

    /// <summary>Removes a line from the sale (guide p.10).</summary>
    [HttpDelete("{cartId:guid}/lines/{lineId:guid}")]
    public async Task<IActionResult> RemoveLine(Guid cartId, Guid lineId)
        => Respond(await _sender.Send(new RemoveCartLineCommand(cartId, lineId)));

    /// <summary>Attaches a customer, or clears the one attached (guide p.9).</summary>
    [HttpPut("{cartId:guid}/customer")]
    public async Task<IActionResult> SetCustomer(Guid cartId, [FromBody] SetCustomerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Respond(await _sender.Send(new AttachCustomerToCartCommand(cartId, request.CustomerId)));
    }

    /// <summary>Applies a coupon, bottle return, subtotal discount or loyalty reward (guide p.7).</summary>
    [HttpPost("{cartId:guid}/adjustments")]
    public async Task<IActionResult> AddAdjustment(Guid cartId, [FromBody] AddAdjustmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Respond(await _sender.Send(new ApplyCartAdjustmentCommand(
            cartId, request.Type, request.Label, request.Amount, request.Percent, request.Serial)));
    }

    /// <summary>Removes a credit applied in error.</summary>
    [HttpDelete("{cartId:guid}/adjustments/{adjustmentId:guid}")]
    public async Task<IActionResult> RemoveAdjustment(Guid cartId, Guid adjustmentId)
        => Respond(await _sender.Send(new RemoveCartAdjustmentCommand(cartId, adjustmentId)));

    /// <summary>
    /// Suspends or applies a tax for the rest of this sale. Not retroactive: lines already rung up
    /// keep the tax they were rung up with (guide p.11).
    /// </summary>
    [HttpPost("{cartId:guid}/tax-override")]
    public async Task<IActionResult> OverrideTax(Guid cartId, [FromBody] TaxOverrideRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Respond(await _sender.Send(new OverrideCartTaxCommand(cartId, request.Tax1, request.Tax2)));
    }

    /// <summary>Puts the sale aside so the till is free for the next customer (guide p.11).</summary>
    [HttpPost("{cartId:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid cartId, [FromBody] SuspendRequest request)
        => Respond(await _sender.Send(new SuspendCartCommand(cartId, request?.HeldName)));

    /// <summary>Brings a suspended sale back to a till — not necessarily the one that held it.</summary>
    [HttpPost("{cartId:guid}/resume")]
    public async Task<IActionResult> Resume(Guid cartId, [FromBody] ResumeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Respond(await _sender.Send(new ResumeCartCommand(cartId, request.StationId)));
    }

    private IActionResult Respond(CartMutationResult result)
        => result.Success ? Ok(result.Quote) : BadRequest(new { error = result.Error });

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
public record UpdateLineRequest(decimal? Quantity, decimal? ManualPrice, decimal? ManualDiscountPct, int? PriceLevel, bool? Tax1Override, bool? Tax2Override, bool? ReturnToStock);
public record SetCustomerRequest(Guid? CustomerId);
public record AddAdjustmentRequest(Retail25.Domain.Sales.AdjustmentType Type, string Label, decimal Amount = 0m, decimal Percent = 0m, string? Serial = null);
public record TaxOverrideRequest(bool? Tax1, bool? Tax2);
public record SuspendRequest(string? HeldName);
public record ResumeRequest(Guid StationId);
public record AddLineRequest(string Identifier, decimal? Quantity, decimal? ManualPrice, decimal? ManualDiscount, int? PriceLevel, bool? Tax1Override, bool? Tax2Override);
public record CompleteSaleRequest(Guid StaffId, List<TenderInput> Tenders, bool PrintReceipt = true, int CopyCount = 1);
