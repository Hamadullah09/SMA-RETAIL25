using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Retail25.Api.Common;
using Retail25.Application.Documents;

namespace Retail25.Api.Controllers;

/// <summary>
/// Printable documents: price tags, barcode labels, statement envelopes and the price list
/// (guide App. L). Everything here answers with a PDF the browser opens rather than saves.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly ISender _sender;

    public DocumentsController(ISender sender) => _sender = sender;

    /// <summary>
    /// A PDF the browser shows, rather than one it files away.
    /// <para>
    /// <c>File(bytes, type, fileName)</c> sets <c>Content-Disposition: attachment</c>, and an
    /// attachment is downloaded whatever the caller does with it — opening it in a tab, in an
    /// iframe, or following a link all end the same way, with a file in Downloads and no print
    /// dialog. Every document here was returned that way while the comment above this class said
    /// they opened in the viewer, so pressing Print produced a saved file and nothing else. Somebody
    /// wanting the file can still save it from the viewer; somebody wanting to print can now print.
    /// </para>
    /// <para>
    /// The name is kept, because <c>inline</c> still supplies it to "Save as" and a viewer tab
    /// titled <c>price-tags.pdf</c> is worth more than one titled with a GUID.
    /// </para>
    /// </summary>
    private IActionResult Pdf(byte[] content, string fileName)
    {
        Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";

        return File(content, "application/pdf");
    }

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
            ? Pdf(result.Value, "price-tags.pdf")
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
            ? Pdf(result.Value, "barcode-labels.pdf")
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
            ? Pdf(result.Value, "price-tag.pdf")
            : result.ToActionResult(this);
    }

    [HttpGet("envelopes/statement/{customerId:long}")]
    [Produces("application/pdf")]
    public async Task<IActionResult> StatementEnvelope(long customerId)
    {
        var result = await _sender.Send(new PrintStatementEnvelopeQuery(customerId));

        return result.IsSuccess
            ? Pdf(result.Value, "envelope.pdf")
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
            ? Pdf(result.Value, "price-list.pdf")
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
