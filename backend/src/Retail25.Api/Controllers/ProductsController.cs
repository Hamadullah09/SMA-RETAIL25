using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Catalog;

namespace Retail25.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
public class ProductsController : ControllerBase
{
    private readonly IApplicationDbContext _db;

    public ProductsController(IApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] Guid? locationId)
    {
        var query = _db.Products.Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.StockCode.Contains(search));

        if (locationId.HasValue)
            query = query.Where(p => p.LocationId == locationId.Value);

        var products = await query.Take(100).ToListAsync();
        return Ok(products);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null) return NotFound();
        return Ok(product);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request)
    {
        var result = Product.Create(
            request.LocationId, request.StockCode, request.Name,
            request.Type, request.RegularPrice, request.Tax1Applies, request.Tax2Applies);

        if (result.IsFailure)
            return BadRequest(new { error = result.Error.Code });

        _db.Products.Add(result.Value);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest request)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null) return NotFound();

        product.UpdateDetails(request.Name ?? product.Name, request.Description, request.Upc, request.BinLocation, request.Notes);
        if (request.RegularPrice.HasValue)
            product.UpdatePricing(request.RegularPrice.Value, product.LastCost, product.AvgCost);

        await _db.SaveChangesAsync();
        return Ok(product);
    }
}

public record CreateProductRequest(Guid LocationId, string StockCode, string Name, ProductType Type, decimal RegularPrice, bool Tax1Applies = true, bool Tax2Applies = true);
public record UpdateProductRequest(string? Name, string? Description, string? Upc, decimal? RegularPrice, string? BinLocation, string? Notes);
