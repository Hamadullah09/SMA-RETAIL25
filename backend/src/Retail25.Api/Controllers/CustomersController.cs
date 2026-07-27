using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Customers;

namespace Retail25.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
public class CustomersController : ControllerBase
{
    private readonly IApplicationDbContext _db;

    public CustomersController(IApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] Guid? locationId)
    {
        var query = _db.Customers.Where(c => !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.FirstName.Contains(search) || c.LastName.Contains(search) || c.Company!.Contains(search));

        if (locationId.HasValue)
            query = query.Where(c => c.LocationId == locationId.Value);

        var customers = await query.Take(100).ToListAsync();
        return Ok(customers);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var customer = await _db.Customers.FindAsync(id);
        if (customer is null) return NotFound();
        return Ok(customer);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequest request)
    {
        var result = Customer.Create(request.LocationId, request.CustomerNumber, request.FirstName, request.LastName);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error.Code });

        _db.Customers.Add(result.Value);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }
}

public record CreateCustomerRequest(Guid LocationId, long CustomerNumber, string FirstName, string LastName);
