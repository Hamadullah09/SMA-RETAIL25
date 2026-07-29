using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Retail25.Api.Common;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Services;
using Retail25.Domain.Catalog;

namespace Retail25.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/products")]
[Produces("application/json")]
public sealed class ProductsController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly IdentifierResolver _resolver;

    public ProductsController(IApplicationDbContext db, IdentifierResolver resolver)
    {
        _db = db;
        _resolver = resolver;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] Guid? locationId,
        [FromQuery] Guid? departmentId,
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
        [FromQuery] Guid locationId,
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
        [FromQuery] Guid locationId,
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

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
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

        _db.Products.Add(result.Value);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct)
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
}

public sealed record CreateProductRequest(
    Guid LocationId,
    string StockCode,
    string Name,
    ProductType Type,
    decimal RegularPrice,
    bool Tax1Applies = true,
    bool Tax2Applies = true);

public sealed record UpdateProductRequest(
    string? Name,
    string? Description,
    string? Upc,
    decimal? RegularPrice,
    string? BinLocation,
    string? Notes);
