using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Inventory;
using Retail25.Domain.Inventory;

namespace Retail25.Api.Controllers;

/// <summary>Stock counts (guide p.22): count, review the variances, then post.</summary>
[ApiController]
[Authorize]
[Route("api/v1/stock-counts")]
[Produces("application/json")]
public sealed class StockCountsController : ControllerBase
{
    private readonly ISender _sender;

    public StockCountsController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery] long locationId,
        [FromQuery] StockCountStatus? status = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new BrowseStockCountsQuery(locationId, status, skip, take), ct));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(
        long id,
        [FromQuery] bool varianceOnly = false,
        [FromQuery] int take = 500,
        CancellationToken ct = default)
        => (await _sender.Send(new GetStockCountQuery(id, varianceOnly, take), ct)).ToActionResult(this);

    [HttpPost]
    public async Task<IActionResult> Start([FromBody] StartStockCountCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    /// <summary>Counted rows as JSON — what a handheld or the grid sends.</summary>
    [HttpPost("{id:long}/lines")]
    public async Task<IActionResult> ImportLines(long id, [FromBody] ImportLinesRequest request, CancellationToken ct)
        => (await _sender.Send(new ImportCountLinesCommand(id, request.Items), ct)).ToActionResult(this);

    /// <summary>
    /// The count sheet as a CSV. Taken as text rather than a multipart upload: a count file is a few
    /// hundred kilobytes of two columns, and the browser already has it as a string.
    /// </summary>
    [HttpPost("{id:long}/import")]
    public async Task<IActionResult> ImportCsv(long id, [FromBody] ImportCsvRequest request, CancellationToken ct)
        => (await _sender.Send(new ImportCountCsvCommand(id, request.Csv), ct)).ToActionResult(this);

    [HttpDelete("{id:long}/lines/{lineId:long}")]
    public async Task<IActionResult> RemoveLine(long id, long lineId, CancellationToken ct)
        => (await _sender.Send(new RemoveCountLineCommand(id, lineId), ct)).ToActionResult(this);

    [HttpPost("{id:long}/post")]
    public async Task<IActionResult> Post(long id, [FromBody] PostCountRequest? request, CancellationToken ct)
        => (await _sender.Send(new PostStockCountCommand(id, request?.Reason), ct)).ToActionResult(this);

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
        => (await _sender.Send(new CancelStockCountCommand(id), ct)).ToActionResult(this);

    [HttpGet("{id:long}/export")]
    [Produces("text/csv")]
    public async Task<IActionResult> Export(long id, [FromQuery] bool varianceOnly = true, CancellationToken ct = default)
    {
        var result = await _sender.Send(new ExportStockCountQuery(id, varianceOnly), ct);

        return result.IsSuccess
            ? File(System.Text.Encoding.UTF8.GetBytes(result.Value), "text/csv", $"stock-count-{id}.csv")
            : result.ToActionResult(this);
    }
}

public sealed record ImportLinesRequest(IReadOnlyList<CountedItem> Items);

public sealed record ImportCsvRequest(string Csv);

public sealed record PostCountRequest(string? Reason);
