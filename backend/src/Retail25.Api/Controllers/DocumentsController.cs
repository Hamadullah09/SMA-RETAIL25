using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Retail25.Api.Common;
using Retail25.Application.Documents;

namespace Retail25.Api.Controllers;

/// <summary>
/// Printable documents: price tags, barcode labels, statement envelopes and the price list
/// (guide App. L). Everything here answers with a PDF the browser downloads.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly ISender _sender;

    public DocumentsController(ISender sender) => _sender = sender;

    /// <summary>A sheet of price tags for the chosen items.</summary>
    [HttpPost("labels/price-tags")]
    [Produces("application/pdf")]
    public async Task<IActionResult> PriceTags([FromBody] PrintLabelsRequest request)
    {
        var result = await _sender.Send(new PrintPriceTagsQuery(
            request.LocationId,
            request.Lines,
            request.Stock,
            BarcodeFirst: false,
            request.ShowBarcode,
            request.SkipLabels));

        return result.IsSuccess
            ? File(result.Value, "application/pdf", "price-tags.pdf")
            : result.ToActionResult(this);
    }

    /// <summary>The same stock, laid out barcode-first for a shelf edge or a bin.</summary>
    [HttpPost("labels/barcodes")]
    [Produces("application/pdf")]
    public async Task<IActionResult> BarcodeLabels([FromBody] PrintLabelsRequest request)
    {
        var result = await _sender.Send(new PrintPriceTagsQuery(
            request.LocationId,
            request.Lines,
            request.Stock,
            BarcodeFirst: true,
            request.ShowBarcode,
            request.SkipLabels));

        return result.IsSuccess
            ? File(result.Value, "application/pdf", "barcode-labels.pdf")
            : result.ToActionResult(this);
    }

    /// <summary>A single item's tag, for the one-off reprint from the item screen.</summary>
    [HttpGet("labels/price-tag/{productId:long}")]
    [Produces("application/pdf")]
    public async Task<IActionResult> SinglePriceTag(
        long productId,
        [FromQuery][BindRequired] long locationId,
        [FromQuery] LabelStock stock = LabelStock.Avery5160,
        [FromQuery] int copies = 1)
    {
        var result = await _sender.Send(new PrintPriceTagsQuery(
            locationId, [new LabelRequestLine(productId, copies)], stock));

        return result.IsSuccess
            ? File(result.Value, "application/pdf", "price-tag.pdf")
            : result.ToActionResult(this);
    }

    [HttpGet("envelopes/statement/{customerId:long}")]
    [Produces("application/pdf")]
    public async Task<IActionResult> StatementEnvelope(long customerId)
    {
        var result = await _sender.Send(new PrintStatementEnvelopeQuery(customerId));

        return result.IsSuccess
            ? File(result.Value, "application/pdf", "envelope.pdf")
            : result.ToActionResult(this);
    }

    [HttpGet("catalogue")]
    [Produces("application/pdf")]
    public async Task<IActionResult> Catalogue(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] long? departmentId = null,
        [FromQuery] long? categoryId = null,
        [FromQuery] string? search = null)
    {
        var result = await _sender.Send(new PrintCatalogueQuery(locationId, departmentId, categoryId, search));

        return result.IsSuccess
            ? File(result.Value, "application/pdf", "price-list.pdf")
            : result.ToActionResult(this);
    }

    /// <summary>The label stocks this deployment knows how to lay out, for the picker.</summary>
    [HttpGet("labels/stocks")]
    [Produces("application/json")]
    public IActionResult Stocks()
        => Ok(Enum.GetValues<LabelStock>().Select(stock => new
        {
            value = stock.ToString(),
            label = LabelStockNames.Describe(stock),
        }));
}

public sealed record PrintLabelsRequest(
    long LocationId,
    IReadOnlyList<LabelRequestLine> Lines,
    LabelStock Stock = LabelStock.Avery5160,
    bool ShowBarcode = true,
    int SkipLabels = 0);
