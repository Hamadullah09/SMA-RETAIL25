using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Retail25.Api.Common;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Services;
using Retail25.Application.Catalog;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;

namespace Retail25.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/products")]
[Produces("application/json")]
public sealed class ProductsController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly IdentifierResolver _resolver;
    private readonly ISender _sender;

    public ProductsController(IApplicationDbContext db, IdentifierResolver resolver, ISender sender)
    {
        _db = db;
        _resolver = resolver;
        _sender = sender;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] long? locationId,
        [FromQuery] long? departmentId,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        var query = _db.Products.AsNoTracking().Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Name.Contains(search) || p.StockCode.Contains(search) || p.Upc == search);
        }

        if (locationId is { } location)
        {
            query = query.Where(p => p.LocationId == location);
        }

        if (departmentId is { } department)
        {
            query = query.Where(p => p.DepartmentId == department);
        }

        return Ok(await query.OrderBy(p => p.StockCode).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct));
    }

    /// <summary>
    /// The F2 pick list (guide p.5). A prefix search rather than a full-text one, because a cashier
    /// types the first characters of a code and expects the list to narrow as they go.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string term,
        [FromQuery][BindRequired] long locationId,
        [FromQuery] int take = 25,
        CancellationToken ct = default)
        => Ok(await _resolver.SearchAsync(term, locationId, Math.Clamp(take, 1, 100), ct));

    /// <summary>
    /// Resolves any identifier the way the till would — EPC, stock code, UPC, weighed barcode,
    /// variant code or serial — without adding anything to a cart. Used by lookup dialogs.
    /// </summary>
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup(
        [FromQuery] string identifier,
        [FromQuery][BindRequired] long locationId,
        [FromQuery] bool scanRandomWeightBarcodes = false,
        CancellationToken ct = default)
    {
        var result = await _resolver.ResolveAsync(identifier, locationId, scanRandomWeightBarcodes, ct);

        if (result.IsFailure)
        {
            return ResultExtensions.Problem(result.Error, this);
        }

        var item = result.Value;

        return Ok(new
        {
            product = item.Product,
            variantId = item.Variant?.Id,
            variantCode = item.Variant?.VariantCode,
            serializedUnitId = item.Unit?.Id,
            epc = item.Unit?.Epc,
            serialNumber = item.Unit?.SerialNumber,
            source = item.Source.ToString(),
            embeddedPrice = item.EmbeddedPrice,
        });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var result = Product.Create(
            request.LocationId, request.StockCode, request.Name,
            request.Type, request.RegularPrice, request.Tax1Applies, request.Tax2Applies);

        if (result.IsFailure)
        {
            return ResultExtensions.Problem(result.Error, this);
        }

        // Applied here rather than through a second call, because a counted item without its
        // barcode cannot be rung up: the till would find nothing to scan until somebody went back
        // and edited it.
        if (request.Upc is not null || request.Description is not null || request.BinLocation is not null)
        {
            result.Value.UpdateDetails(
                result.Value.Name,
                request.Description,
                request.Upc,
                request.BinLocation,
                result.Value.Notes);
        }

        _db.Products.Add(result.Value);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return NotFound();
        }

        product.UpdateDetails(request.Name ?? product.Name, request.Description, request.Upc, request.BinLocation, request.Notes);

        if (request.RegularPrice.HasValue)
        {
            product.UpdatePricing(request.RegularPrice.Value, product.LastCost, product.AvgCost);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(product);
    }

    /// <summary>
    /// The till's product grid: a page of items, the headings above them, and whether anything in
    /// this filter has a picture to show.
    /// </summary>
    [HttpGet("grid")]
    public async Task<IActionResult> Grid(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] long? departmentId,
        [FromQuery] long? categoryId,
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 60,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(
            new PosGridQuery(locationId, departmentId, categoryId, search, skip, take), ct);

        return result.IsFailure ? ResultExtensions.Problem(result.Error, this) : Ok(result.Value);
    }

    /// <summary>
    /// Serves an item's picture.
    /// <para>
    /// Cached hard and revalidated by ETag: a till redraws the same forty tiles on every category
    /// change, and re-sending a megabyte of JPEG each time is the difference between a grid that
    /// feels instant and one that does not. The tag changes with the bytes, so a replaced picture
    /// still appears at once.
    /// </para>
    /// </summary>
    [HttpGet("{id:long}/image")]
    [Produces("image/png", "image/jpeg", "image/webp")]
    public async Task<IActionResult> GetImage(long id, CancellationToken ct)
    {
        var result = await _sender.Send(new GetProductImageQuery(id), ct);

        if (result.IsFailure)
        {
            return ResultExtensions.Problem(result.Error, this);
        }

        var image = result.Value;
        var etag = new EntityTagHeaderValue($"\"{image.ETag}\"");

        Response.Headers.CacheControl = "private, max-age=86400, must-revalidate";

        // Content-Type is chosen from an allow-list at upload, never echoed from the request, so it
        // cannot be turned into a script type. nosniff stops a browser second-guessing that.
        Response.Headers.XContentTypeOptions = "nosniff";

        // No download name: this is rendered in an <img>, not saved, and naming it would invite a
        // Content-Disposition the browser might act on.
        return File(image.Content, image.ContentType, fileDownloadName: null, lastModified: null, entityTag: etag);
    }

    /// <summary>Attaches or replaces the picture shown on the till's product grid.</summary>
    [HttpPut("{id:long}/image")]
    [RequestSizeLimit(ProductImage.MaximumBytes + 4096)]
    public async Task<IActionResult> SetImage(long id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return ResultExtensions.Problem(new Error("image.empty", "No file was uploaded."), this);
        }

        if (file.Length > ProductImage.MaximumBytes)
        {
            return ResultExtensions.Problem(ProductImage.TooLarge, this);
        }

        // Bounded by the check above, so this cannot be used to exhaust memory.
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);

        var result = await _sender.Send(
            new SetProductImageCommand(id, buffer.ToArray(), file.ContentType ?? string.Empty), ct);

        return result.IsFailure ? ResultExtensions.Problem(result.Error, this) : NoContent();
    }

    [HttpDelete("{id:long}/image")]
    public async Task<IActionResult> RemoveImage(long id, CancellationToken ct)
    {
        var result = await _sender.Send(new RemoveProductImageCommand(id), ct);
        return result.IsFailure ? ResultExtensions.Problem(result.Error, this) : NoContent();
    }
}

public sealed record CreateProductRequest(
    long LocationId,
    string StockCode,
    string Name,
    ProductType Type,
    decimal RegularPrice,
    bool Tax1Applies = true,
    bool Tax2Applies = true,
    /// <summary>
    /// The barcode. Optional because a tagged item is identified by its EPC and a service has
    /// nothing to scan — but a counted item that cannot be given one at creation has to be created
    /// and then edited before the till can ring it, which is not a workflow anybody should have to
    /// discover.
    /// </summary>
    string? Upc = null,
    string? Description = null,
    string? BinLocation = null);

public sealed record UpdateProductRequest(
    string? Name,
    string? Description,
    string? Upc,
    decimal? RegularPrice,
    string? BinLocation,
    string? Notes);
