using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Retail25.Api.Common;
using Retail25.Application.Receivables;

namespace Retail25.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/receivables")]
[Produces("application/json")]
public sealed class ReceivablesController : ControllerBase
{
    private readonly ISender _sender;

    public ReceivablesController(ISender sender) => _sender = sender;

    [HttpGet("accounts")]
    public async Task<IActionResult> BrowseAccounts(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] string? search,
        [FromQuery] bool withBalanceOnly = false,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new BrowseCustomerAccountsQuery(locationId, search, withBalanceOnly, cursor, pageSize), ct));

    [HttpGet("customers/{customerId:long}/statement")]
    public async Task<IActionResult> Statement(long customerId, CancellationToken ct)
        => (await _sender.Send(new GetCustomerStatementQuery(customerId), ct)).ToActionResult(this);

    [HttpGet("aging")]
    public async Task<IActionResult> Aging([FromQuery][BindRequired] long locationId, CancellationToken ct)
        => Ok(await _sender.Send(new GetReceivablesAgingQuery(locationId), ct));

    [HttpPost("payments")]
    public async Task<IActionResult> TakePayment([FromBody] TakeInvoicePaymentCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("invoices/{invoiceId:long}/void")]
    public async Task<IActionResult> VoidInvoice(long invoiceId, [FromBody] VoidInvoiceRequest? request, CancellationToken ct)
        => (await _sender.Send(new VoidInvoiceCommand(invoiceId, request?.Reason), ct)).ToActionResult(this);

    [HttpPost("invoices/{invoiceId:long}/refund")]
    public async Task<IActionResult> RefundInvoice(long invoiceId, [FromBody] RefundInvoiceRequest request, CancellationToken ct)
        => (await _sender.Send(new RefundInvoiceCommand(invoiceId, request.Amount, request.Reason), ct)).ToActionResult(this);

    [HttpPost("late-charges/accrue")]
    public async Task<IActionResult> AccrueLateCharges([FromQuery] long? locationId, CancellationToken ct)
        => (await _sender.Send(new AccrueLateChargesCommand(locationId), ct)).ToActionResult(this);
}

public sealed record VoidInvoiceRequest(string? Reason);

public sealed record RefundInvoiceRequest(decimal Amount, string? Reason);
