using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Catalog;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;

namespace Retail25.Api.Controllers;

/// <summary>
/// The back-office catalogue: browse, form view, and the reference lists both pick from.
/// <para>
/// Separate from <c>ProductsController</c>, which serves the till. The till needs one item resolved
/// from a scan as fast as possible; the back office needs pages, filters and whole records. Serving
/// both from one endpoint would mean every scan paid for fields a cashier never sees.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/catalog")]
[Produces("application/json")]
public sealed class CatalogController : ControllerBase
{
    private readonly ISender _sender;

    public CatalogController(ISender sender) => _sender = sender;

    [HttpGet("products")]
    public async Task<IActionResult> Browse(
        [FromQuery] long locationId,
        [FromQuery] string? search,
        [FromQuery] long? departmentId,
        [FromQuery] long? categoryId,
        [FromQuery] ProductType? type,
        [FromQuery] bool belowReorderPoint = false,
        [FromQuery] bool deletedOnly = false,
        [FromQuery] ProductSort sort = ProductSort.StockCode,
        [FromQuery] bool descending = false,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await _sender.Send(
            new BrowseProductsQuery(
                locationId, search, departmentId, categoryId, type,
                belowReorderPoint, deletedOnly, sort, descending, cursor, pageSize),
            ct));

    [HttpGet("products/{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
        => (await _sender.Send(new GetProductFormQuery(id), ct)).ToActionResult(this);

    [HttpPost("products")]
    public async Task<IActionResult> Create([FromBody] CreateProductCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPut("products/{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateProductCommand command, CancellationToken ct)
        => (await _sender.Send(command with { ProductId = id }, ct)).ToActionResult(this);

    [HttpPost("products/{id:long}/clone")]
    public async Task<IActionResult> Clone(long id, [FromBody] CloneProductRequest request, CancellationToken ct)
        => (await _sender.Send(new CloneProductCommand(id, request.NewStockCode, request.NewName), ct)).ToActionResult(this);

    [HttpDelete("products/{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => (await _sender.Send(new DeleteProductCommand(id), ct)).ToActionResult(this);

    [HttpPost("products/{id:long}/restore")]
    public async Task<IActionResult> Restore(long id, CancellationToken ct)
        => (await _sender.Send(new RestoreProductCommand(id), ct)).ToActionResult(this);

    [HttpGet("departments")]
    public async Task<IActionResult> Departments([FromQuery] long locationId, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _sender.Send(new ListDepartmentsQuery(locationId, includeInactive), ct));

    [HttpPost("departments")]
    public async Task<IActionResult> SaveDepartment([FromBody] SaveDepartmentCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpDelete("departments/{id:long}")]
    public async Task<IActionResult> DeleteDepartment(long id, CancellationToken ct)
        => (await _sender.Send(new DeleteDepartmentCommand(id), ct)).ToActionResult(this);

    [HttpGet("categories")]
    public async Task<IActionResult> Categories([FromQuery] long locationId, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _sender.Send(new ListCategoriesQuery(locationId, includeInactive), ct));

    [HttpPost("categories")]
    public async Task<IActionResult> SaveCategory([FromBody] SaveCategoryCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpDelete("categories/{id:long}")]
    public async Task<IActionResult> DeleteCategory(long id, CancellationToken ct)
        => (await _sender.Send(new DeleteCategoryCommand(id), ct)).ToActionResult(this);

    /// <summary>The legacy "Undelete Items" screen (guide p.24), across every soft-deleted record.</summary>
    [HttpGet("deleted")]
    public async Task<IActionResult> Deleted(
        [FromQuery] long locationId,
        [FromQuery] DeletedEntityKind? kind,
        [FromQuery] string? search,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new BrowseDeletedQuery(locationId, kind, search, take), ct));

    [HttpPost("deleted/{kind}/{id:long}/restore")]
    public async Task<IActionResult> RestoreDeleted(DeletedEntityKind kind, long id, CancellationToken ct)
    {
        // Each aggregate restores through its own command because each has its own precondition —
        // a stock code reused since the delete, an outstanding balance, a supplier still on an order.
        var result = kind switch
        {
            DeletedEntityKind.Product => await _sender.Send(new RestoreProductCommand(id), ct),
            DeletedEntityKind.Customer => await _sender.Send(new Application.Customers.RestoreCustomerCommand(id), ct),
            DeletedEntityKind.Supplier => await _sender.Send(new Application.Purchasing.RestoreSupplierCommand(id), ct),
            _ => await _sender.Send(new RestoreReferenceRowCommand(kind, id), ct),
        };

        return result.ToActionResult(this);
    }
}

public sealed record CloneProductRequest(string NewStockCode, string? NewName);
