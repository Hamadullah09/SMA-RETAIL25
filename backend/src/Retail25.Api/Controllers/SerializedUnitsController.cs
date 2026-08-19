using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Retail25.Api.Common;
using Retail25.Application.Catalog;
using Retail25.Application.Rfid.Commands;
using Retail25.Domain.Common;

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
        [FromQuery] long productId,
        [FromQuery][BindRequired] long locationId,
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

    /// <summary>
    /// Withdraws every tag at a location from service, for a shop being re-tagged.
    /// <para>
    /// Keeps the units and their history — a sold tag is left exactly as it is, because a receipt
    /// points at it. <c>dryRun</c> reports the counts and writes nothing.
    /// </para>
    /// </summary>
    [HttpPost("retire")]
    public async Task<IActionResult> Retire([FromBody] RetireTagsRequest request, CancellationToken ct)
        => (await _sender.Send(new RetireTagsCommand(request.LocationId, request.DryRun), ct)).ToActionResult(this);

    /// <summary>
    /// A starter file with the headings filled in and two example rows.
    /// <para>
    /// Here because "what should the CSV look like" is the question that stops an import before it
    /// starts, and every answer that lives only in documentation is an answer somebody has to be
    /// told about first. A file they can open, overwrite and send back needs no telling.
    /// </para>
    /// <para>
    /// The two rows are deliberately different: one tagged, one not. A template showing only tagged
    /// items reads as though the EPC column is required, which is the misunderstanding this whole
    /// change exists to remove.
    /// </para>
    /// </summary>
    [HttpGet("import/template")]
    public IActionResult ImportTemplate()
    {
        const string template =
            "Stock Code,Item Name,Description,Department,Category,Supplier,Barcode,Cost,Price,Qty," +
            "Bin,Weight,Case Qty,Reorder Point,Reorder Qty,Base Stock,Tax1,Tax2,POS Message," +
            "Invoice Message,Notes,Image URL,EPC\n" +
            "SHIRT-01,Blue Shirt Medium,Cotton oxford shirt,Menswear,Shirts,Acme Textiles," +
            "5012345678900,900,1500,12,A3,0.4,12,3,24,6,Yes,Yes,,,,https://example.com/shirt.jpg,\n" +
            "JACKET-01,Olive Quilted Jacket XL,,Menswear,Outerwear,Acme Textiles," +
            "5012345678917,4200,7500,1,B1,1.2,6,1,6,2,Yes,No,Check the zip,Dry clean only,,," +
            "E28011606000020C1B3E1234\n";

        // Attachment, not inline: this one is meant to be saved, filled in and sent back. It is the
        // opposite case to the printable documents, which open in a viewer.
        return File(System.Text.Encoding.UTF8.GetBytes(template), "text/csv", "inventory-import-template.csv");
    }

    /// <summary>
    /// Loads a tag export — items and their tags in one file — into a location's catalogue.
    /// <para>
    /// <c>dryRun</c> reports what the file would do and writes nothing, which is the only safe way
    /// to look at a file somebody has been editing by hand before it touches a live catalogue.
    /// </para>
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(MaximumImportBytes)]
    public async Task<IActionResult> Import(
        [FromForm] long locationId,
        IFormFile file,
        CancellationToken ct,
        [FromForm] bool dryRun = false,
        [FromForm] bool? resetToInStock = null)
    {
        if (file is null || file.Length == 0)
        {
            return ResultExtensions.Problem(new Error("import.empty", "No file was uploaded."), this);
        }

        if (file.Length > MaximumImportBytes)
        {
            return ResultExtensions.Problem(
                new Error("import.too_large", $"An import file may be at most {MaximumImportBytes / 1024 / 1024} MB."),
                this);
        }

        // Bounded by the check above. The byte-order mark is honoured because these files come out
        // of a spreadsheet as often as out of a database, and Excel writes one.
        using var reader = new StreamReader(
            file.OpenReadStream(),
            System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        var csv = await reader.ReadToEndAsync(ct);

        var result = await _sender.Send(
            new ImportEpcCatalogCommand(locationId, csv, dryRun, resetToInStock ?? true), ct);

        return result.ToActionResult(this);
    }

    /// <summary>A quarter of a million tags at the widths this file uses. Well past any real export.</summary>
    private const int MaximumImportBytes = 8 * 1024 * 1024;
}

public sealed record RetireTagsRequest(long LocationId, bool DryRun = false);

/// <summary>Matrix items: the dimension grid and the variants it generates (guide p.39–40).</summary>
[ApiController]
[Authorize]
[Route("api/v1/products/{productId:long}/matrix")]
[Produces("application/json")]
public sealed class MatrixController : ControllerBase
{
    private readonly ISender _sender;

    public MatrixController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Get(long productId)
        => (await _sender.Send(new GetMatrixQuery(productId))).ToActionResult(this);

    /// <summary>Defines the dimensions and generates the cross product of their values.</summary>
    [HttpPut]
    public async Task<IActionResult> Define(long productId, [FromBody] DefineMatrixRequest request)
        => (await _sender.Send(new DefineMatrixCommand(productId, request.Dimensions))).ToActionResult(this);

    /// <summary>The variant picker at the till, optionally limited to what is actually on the shelf.</summary>
    [HttpGet("variants")]
    public async Task<IActionResult> Variants(
        long productId,
        [FromQuery][BindRequired] long locationId,
        [FromQuery] bool inStockOnly = false)
        => Ok(await _sender.Send(new ListVariantsQuery(productId, locationId, inStockOnly)));
}

public sealed record ReassignTagRequest(string Epc, long ProductId, long? VariantId = null);

public sealed record CommissionTagRequest(
    string Epc,
    long ProductId,
    long LocationId,
    long? VariantId = null,
    string? SerialNumber = null);

public sealed record CommissionBatchRequest(
    long ProductId,
    long LocationId,
    IReadOnlyList<string> Epcs,
    long? VariantId = null);

public sealed record DefineMatrixRequest(IReadOnlyList<MatrixDimensionDto> Dimensions);
