using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Retail25.Api.Common;
using Retail25.Contracts.Terminals;
using Retail25.Application.Sales.Commands;
using Retail25.Application.Sales.Queries;

namespace Retail25.Api.Controllers;

/// <summary>Completed sales: the itemized log, one sale in full, void, reprint and packing slip.</summary>
[ApiController]
[Authorize]
[Route("api/v1/sales")]
[Produces("application/json")]
public sealed class SalesController : ControllerBase
{
    private readonly ISender _sender;

    public SalesController(ISender sender) => _sender = sender;

    /// <summary>The itemized sales log with filters (guide p.14–15).</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] long? stationId = null,
        [FromQuery] long? staffId = null,
        [FromQuery] long? customerId = null,
        [FromQuery] bool includeVoided = true,
        [FromQuery] bool includeTraining = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
        => Ok(await _sender.Send(new SalesLogQuery(
            locationId, from, to, stationId, staffId, customerId, includeVoided, includeTraining, skip, take)));

    /// <summary>The same rows as CSV — the modern "Open In MS-Excel" (guide p.101).</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] long? stationId = null,
        [FromQuery] long? staffId = null,
        [FromQuery] bool includeVoided = true,
        [FromQuery] bool includeTraining = false)
    {
        var csv = await _sender.Send(new ExportSalesLogQuery(
            new SalesLogQuery(locationId, from, to, stationId, staffId, null, includeVoided, includeTraining, 0, int.MaxValue)));

        return File(Encoding.UTF8.GetBytes(csv), "text/csv", "sales-log.csv");
    }

    [HttpGet("{transactionId:long}")]
    public async Task<IActionResult> Get(long transactionId)
        => (await _sender.Send(new GetSaleQuery(transactionId))).ToActionResult(this);

    /// <summary>
    /// Gives part of a sale back, as its own transaction.
    /// <para>
    /// The <c>Idempotency-Key</c> header is required for the reason it is required on completion: a
    /// retried refund must hand back the first one, not pay the customer twice.
    /// </para>
    /// </summary>
    [HttpPost("{transactionId:long}/refund")]
    public async Task<IActionResult> Refund(
        long transactionId,
        [FromBody] RefundRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ResultExtensions.Problem(
                new Domain.Common.Error("idempotency.key_required", "An Idempotency-Key header is required to refund a sale."),
                this);
        }

        return (await _sender.Send(new RefundSaleCommand(
            transactionId,
            request.Lines,
            request.Tenders,
            idempotencyKey,
            request.Reason))).ToActionResult(this);
    }

    /// <summary>
    /// Voids a sale by writing a reversal (guide p.14). Requires an idempotency key for the same
    /// reason completion does: a retried void must not reverse the sale twice.
    /// </summary>
    [HttpPost("{transactionId:long}/void")]
    public async Task<IActionResult> Void(
        long transactionId,
        [FromBody] VoidSaleRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ResultExtensions.Problem(
                new Domain.Common.Error("idempotency.key_required", "An Idempotency-Key header is required to void a sale."),
                this);
        }

        var result = await _sender.Send(new VoidSaleCommand(
            transactionId, idempotencyKey, request?.Reason, request?.ApprovedByStaffId));

        return result.ToActionResult(this);
    }

    /// <summary>Reprints the document exactly as it was first issued (guide p.12, p.56).</summary>
    [HttpPost("{transactionId:long}/reprint")]
    public async Task<IActionResult> Reprint(long transactionId, [FromBody] ReprintRequest? request)
        => (await _sender.Send(new ReprintTransactionCommand(
            transactionId,
            request?.Format ?? ReceiptFormat.Slip40,
            request?.Copies ?? 1,
            request?.StationId,
            request?.SendToPrinter ?? true))).ToActionResult(this);

    /// <summary>F7 at the till: the last sale rung here (guide p.12).</summary>
    [HttpPost("reprint-last")]
    public async Task<IActionResult> ReprintLast([FromBody] ReprintLastRequest request)
        => (await _sender.Send(new ReprintLastSaleCommand(
            request.StationId, request.Format, request.Copies))).ToActionResult(this);

    /// <summary>F8: quantities and descriptions, no money (guide p.12).</summary>
    [HttpPost("{transactionId:long}/packing-slip")]
    public async Task<IActionResult> PackingSlip(long transactionId, [FromBody] ReprintRequest? request)
        => (await _sender.Send(new ReprintTransactionCommand(
            transactionId,
            ReceiptFormat.PackingSlip,
            request?.Copies ?? 1,
            request?.StationId,
            request?.SendToPrinter ?? true))).ToActionResult(this);
}

public sealed record VoidSaleRequest(string? Reason = null, long? ApprovedByStaffId = null);

public sealed record RefundRequest(
    IReadOnlyList<RefundLineRequest> Lines,
    IReadOnlyList<RefundTenderRequest> Tenders,
    string? Reason = null);

public sealed record ReprintRequest(
    ReceiptFormat Format = ReceiptFormat.Slip40,
    int Copies = 1,
    long? StationId = null,
    bool SendToPrinter = true);

public sealed record ReprintLastRequest(long StationId, ReceiptFormat Format = ReceiptFormat.Slip40, int Copies = 1);
