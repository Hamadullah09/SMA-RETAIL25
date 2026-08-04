using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Inventory;

namespace Retail25.Api.Controllers;

/// <summary>Fiscal years and the year-end close (guide p.29).</summary>
[ApiController]
[Authorize]
[Route("api/v1/fiscal-years")]
[Produces("application/json")]
public sealed class FiscalYearsController : ControllerBase
{
    private readonly ISender _sender;

    public FiscalYearsController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] long locationId, CancellationToken ct)
        => Ok(await _sender.Send(new ListFiscalYearsQuery(locationId), ct));

    [HttpPost]
    public async Task<IActionResult> Open([FromBody] OpenFiscalYearCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    /// <summary>
    /// Closes the year. <c>dryRun=true</c> does every calculation and writes nothing, which is how
    /// this should be run the first time — the figures it reports are the ones the real close writes.
    /// </summary>
    [HttpPost("{id:long}/close")]
    public async Task<IActionResult> Close(long id, [FromQuery] bool dryRun = false, CancellationToken ct = default)
        => (await _sender.Send(new RunFiscalYearCloseCommand(id, dryRun), ct)).ToActionResult(this);

    [HttpPost("{id:long}/reopen")]
    public async Task<IActionResult> Reopen(long id, CancellationToken ct)
        => (await _sender.Send(new ReopenFiscalYearCommand(id), ct)).ToActionResult(this);

    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] long locationId,
        [FromQuery] int? year = null,
        [FromQuery] long? productId = null,
        [FromQuery] int take = 500,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new GetSalesHistoryQuery(locationId, year, productId, take), ct));

    [HttpGet("history/export")]
    [Produces("text/csv")]
    public async Task<IActionResult> HistoryExport(
        [FromQuery] long locationId,
        [FromQuery] int? year = null,
        [FromQuery] long? productId = null,
        CancellationToken ct = default)
    {
        var csv = await _sender.Send(
            new ExportSalesHistoryQuery(new GetSalesHistoryQuery(locationId, year, productId, int.MaxValue)), ct);

        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"sales-history-{year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "all"}.csv");
    }
}
