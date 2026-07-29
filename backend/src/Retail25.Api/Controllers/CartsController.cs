using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Carts.Queries;
using Retail25.Application.Receipts;
using Retail25.Application.Rfid.Commands;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Sales;

namespace Retail25.Api.Controllers;

/// <summary>
/// The POS surface (doc 05 §HTTP API). Every mutation returns the whole authoritative cart, because
/// a till that patched its own state from a partial response is a till that can end up disagreeing
/// with the server about what the customer owes.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/carts")]
[Produces("application/json")]
public sealed class CartsController : ControllerBase
{
    private readonly ISender _sender;

    public CartsController(ISender sender) => _sender = sender;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCartRequest request)
        => (await _sender.Send(new CreateCartCommand(request.StationId, request.StaffId))).ToActionResult(this);

    [HttpGet("{cartId:guid}")]
    public async Task<IActionResult> Get(Guid cartId)
        => (await _sender.Send(new GetCartQuery(cartId))).ToActionResult(this);

    /// <summary>Live totals without persisting — what the totals panel calls (doc 05).</summary>
    [HttpGet("{cartId:guid}/quote")]
    public async Task<IActionResult> Quote(Guid cartId)
        => (await _sender.Send(new QuoteCartQuery(cartId))).ToActionResult(this);

    [HttpGet("by-station/{stationId:guid}")]
    public async Task<IActionResult> ByStation(Guid stationId)
        => (await _sender.Send(new GetStationCartQuery(stationId))).ToActionResult(this);

    /// <summary>The universal add: EPC, stock code, UPC, weighed barcode, variant or serial.</summary>
    [HttpPost("{cartId:guid}/lines")]
    public async Task<IActionResult> AddLine(Guid cartId, [FromBody] AddLineRequest request)
        => (await _sender.Send(new AddCartLineByIdentifierCommand(
            cartId,
            request.Identifier,
            request.Quantity,
            request.ManualPrice,
            request.ManualDiscountPct,
            request.PriceLevel,
            request.Tax1Override,
            request.Tax2Override,
            request.LineType,
            request.ReturnToStock,
            request.Note))).ToActionResult(this);

    /// <summary>A bulk RFID read (doc 06 §2). Returns accepted lines and every rejection with a reason.</summary>
    [HttpPost("{cartId:guid}/lines/rfid-batch")]
    public async Task<IActionResult> AddRfidBatch(Guid cartId, [FromBody] RfidBatchRequest request)
        => (await _sender.Send(new AddRfidBatchCommand(cartId, request.Tags))).ToActionResult(this);

    /// <summary>
    /// Adds the matrix variant the cashier picked. Ringing the parent code answers
    /// <c>variant.selection_required</c>; this is where that answer comes back.
    /// </summary>
    [HttpPost("{cartId:guid}/lines/variant")]
    public async Task<IActionResult> AddVariantLine(Guid cartId, [FromBody] AddVariantLineRequest request)
        => (await _sender.Send(new AddCartLineByVariantCommand(
            cartId, request.VariantId, request.Quantity, request.LineType))).ToActionResult(this);

    /// <summary>Adds the specific serialized unit the cashier picked (guide p.42).</summary>
    [HttpPost("{cartId:guid}/lines/unit")]
    public async Task<IActionResult> AddUnitLine(Guid cartId, [FromBody] AddUnitLineRequest request)
        => (await _sender.Send(new AddCartLineByUnitCommand(cartId, request.UnitId, request.LineType))).ToActionResult(this);

    /// <summary>The item-detail window (guide p.6).</summary>
    [HttpPatch("{cartId:guid}/lines/{lineId:guid}")]
    public async Task<IActionResult> UpdateLine(Guid cartId, Guid lineId, [FromBody] UpdateLineRequest request)
        => (await _sender.Send(new UpdateCartLineCommand(
            cartId,
            lineId,
            request.Quantity,
            request.ManualPrice,
            request.ManualDiscountPct,
            request.PriceLevel,
            request.Tax1Override,
            request.Tax2Override,
            request.LineType,
            request.ReturnToStock,
            request.Note,
            request.Clear))).ToActionResult(this);

    [HttpDelete("{cartId:guid}/lines/{lineId:guid}")]
    public async Task<IActionResult> RemoveLine(Guid cartId, Guid lineId)
        => (await _sender.Send(new RemoveCartLineCommand(cartId, lineId))).ToActionResult(this);

    [HttpDelete("{cartId:guid}/lines")]
    public async Task<IActionResult> Clear(Guid cartId)
        => (await _sender.Send(new ClearCartCommand(cartId))).ToActionResult(this);

    /// <summary>The Credits menu (guide p.7).</summary>
    [HttpPost("{cartId:guid}/adjustments")]
    public async Task<IActionResult> AddAdjustment(Guid cartId, [FromBody] AdjustmentRequest request)
        => (await _sender.Send(new ApplyCartAdjustmentCommand(
            cartId, request.Type, request.Label, request.Amount, request.Percent, request.Serial))).ToActionResult(this);

    [HttpDelete("{cartId:guid}/adjustments/{adjustmentId:guid}")]
    public async Task<IActionResult> RemoveAdjustment(Guid cartId, Guid adjustmentId)
        => (await _sender.Send(new RemoveCartAdjustmentCommand(cartId, adjustmentId))).ToActionResult(this);

    /// <summary>Legacy F11-F2 unknown item (guide p.11).</summary>
    [HttpPost("{cartId:guid}/unknown-item")]
    public async Task<IActionResult> AddUnknownItem(Guid cartId, [FromBody] UnknownItemRequest request)
        => (await _sender.Send(new AddUnknownItemCommand(
            cartId,
            request.Description,
            request.UnitPrice,
            request.Quantity,
            request.Tax1Applies,
            request.Tax2Applies,
            request.CreateProduct,
            request.StockCode,
            request.DepartmentId))).ToActionResult(this);

    /// <summary>Per-sale tax suspension, non-retroactive by design (guide p.11).</summary>
    [HttpPut("{cartId:guid}/tax-override")]
    public async Task<IActionResult> SetTaxOverride(Guid cartId, [FromBody] TaxOverrideRequest request)
        => (await _sender.Send(new SetCartTaxOverrideCommand(cartId, request.Tax1, request.Tax2))).ToActionResult(this);

    [HttpPut("{cartId:guid}/customer")]
    public async Task<IActionResult> SetCustomer(Guid cartId, [FromBody] SetCustomerRequest request)
        => (await _sender.Send(new AssignCartCustomerCommand(cartId, request.CustomerId))).ToActionResult(this);

    [HttpPost("{cartId:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid cartId, [FromBody] SuspendRequest? request)
        => (await _sender.Send(new SuspendCartCommand(cartId, request?.Label))).ToActionResult(this);

    [HttpPost("{cartId:guid}/recall")]
    public async Task<IActionResult> Recall(Guid cartId, [FromBody] RecallRequest request)
        => (await _sender.Send(new RecallCartCommand(cartId, request.StationId))).ToActionResult(this);

    [HttpGet("suspended")]
    public async Task<IActionResult> ListSuspended([FromQuery] Guid locationId)
        => Ok(await _sender.Send(new ListSuspendedCartsQuery(locationId)));

    /// <summary>
    /// Completes the sale. The <c>Idempotency-Key</c> header is required: a cashier who presses Pay
    /// again after a timeout must not be able to take the money twice.
    /// </summary>
    [HttpPost("{cartId:guid}/complete")]
    public async Task<IActionResult> Complete(
        Guid cartId,
        [FromBody] CompleteSaleRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ResultExtensions.Problem(
                new Domain.Common.Error("idempotency.key_required", "An Idempotency-Key header is required to complete a sale."),
                this);
        }

        var result = await _sender.Send(new CompleteSaleCommand(
            cartId,
            request.Tenders,
            idempotencyKey,
            request.PrintReceipt,
            request.Copies,
            request.Format));

        return result.ToActionResult(this);
    }
}

public sealed record CreateCartRequest(Guid StationId, Guid? StaffId = null);

public sealed record AddLineRequest(
    string Identifier,
    decimal? Quantity = null,
    decimal? ManualPrice = null,
    decimal? ManualDiscountPct = null,
    int? PriceLevel = null,
    bool? Tax1Override = null,
    bool? Tax2Override = null,
    LineType LineType = LineType.Sale,
    bool ReturnToStock = true,
    string? Note = null);

public sealed record RfidBatchRequest(IReadOnlyList<TagRead> Tags);

public sealed record AddVariantLineRequest(Guid VariantId, decimal Quantity = 1m, LineType LineType = LineType.Sale);

public sealed record AddUnitLineRequest(Guid UnitId, LineType LineType = LineType.Sale);

public sealed record UpdateLineRequest(
    decimal? Quantity = null,
    decimal? ManualPrice = null,
    decimal? ManualDiscountPct = null,
    int? PriceLevel = null,
    bool? Tax1Override = null,
    bool? Tax2Override = null,
    LineType? LineType = null,
    bool? ReturnToStock = null,
    string? Note = null,
    IReadOnlyList<string>? Clear = null);

public sealed record AdjustmentRequest(
    AdjustmentType Type,
    string Label,
    decimal Amount = 0m,
    decimal Percent = 0m,
    string? Serial = null);

public sealed record UnknownItemRequest(
    string Description,
    decimal UnitPrice,
    decimal Quantity = 1m,
    bool Tax1Applies = true,
    bool Tax2Applies = true,
    bool CreateProduct = false,
    string? StockCode = null,
    Guid? DepartmentId = null);

public sealed record TaxOverrideRequest(bool? Tax1, bool? Tax2);

public sealed record SetCustomerRequest(Guid? CustomerId);

public sealed record SuspendRequest(string? Label);

public sealed record RecallRequest(Guid StationId);

public sealed record CompleteSaleRequest(
    IReadOnlyList<TenderRequest> Tenders,
    bool PrintReceipt = true,
    int Copies = 1,
    ReceiptFormat Format = ReceiptFormat.Slip40);
