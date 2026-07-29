using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Purchasing;

namespace Retail25.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/suppliers")]
[Produces("application/json")]
public sealed class SuppliersController : ControllerBase
{
    private readonly ISender _sender;

    public SuppliersController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery] Guid locationId,
        [FromQuery] string? search,
        [FromQuery] bool deletedOnly = false,
        [FromQuery] SupplierSort sort = SupplierSort.Company,
        [FromQuery] bool descending = false,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await _sender.Send(
            new BrowseSuppliersQuery(locationId, search, deletedOnly, sort, descending, cursor, pageSize), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => (await _sender.Send(new GetSupplierFormQuery(id), ct)).ToActionResult(this);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupplierCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SupplierSection details, CancellationToken ct)
        => (await _sender.Send(new UpdateSupplierCommand(id, details), ct)).ToActionResult(this);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => (await _sender.Send(new DeleteSupplierCommand(id), ct)).ToActionResult(this);

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
        => (await _sender.Send(new RestoreSupplierCommand(id), ct)).ToActionResult(this);
}
