using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Retail25.Api.Common;
using Retail25.Application.Customers;

namespace Retail25.Api.Controllers;

/// <summary>
/// The customer Browse and Form views (guide p.46–52).
/// <para>
/// Every route goes through a handler rather than touching the DbContext directly. The till attaches
/// a customer mid-sale, and their price level and tax exemptions change what the basket costs — so
/// the rules about what a customer record may contain belong in one place, not once here and once
/// wherever else a customer is written.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/customers")]
[Produces("application/json")]
public sealed class CustomersController : ControllerBase
{
    private readonly ISender _sender;

    public CustomersController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] string? search,
        [FromQuery] string? clientType,
        [FromQuery] bool withBalanceOnly = false,
        [FromQuery] bool deletedOnly = false,
        [FromQuery] CustomerSort sort = CustomerSort.Number,
        [FromQuery] bool descending = false,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await _sender.Send(
            new BrowseCustomersQuery(
                locationId, search, clientType, withBalanceOnly, deletedOnly, sort, descending, cursor, pageSize),
            ct));

    /// <summary>
    /// The till's client picker (guide p.7). A flat list of just enough to choose from, so attaching a
    /// customer mid-sale does not pull addresses, balances and pricing profiles the cashier cannot see.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string term,
        [FromQuery][BindRequired] long locationId,
        [FromQuery] int take = 25,
        CancellationToken ct = default)
    {
        var page = await _sender.Send(
            new BrowseCustomersQuery(locationId, term, PageSize: Math.Clamp(take, 1, 100)), ct);

        return Ok(page.Items.Select(c => new
        {
            id = c.Id,
            customerNumber = c.CustomerNumber,
            fullName = c.DisplayName,
            city = c.City,
            phone = c.Phone,
        }));
    }

    [HttpGet("client-types")]
    public async Task<IActionResult> ClientTypes([FromQuery][BindRequired] long locationId, CancellationToken ct)
        => Ok(await _sender.Send(new ListClientTypesQuery(locationId), ct));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
        => (await _sender.Send(new GetCustomerFormQuery(id), ct)).ToActionResult(this);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateCustomerCommand command, CancellationToken ct)
        => (await _sender.Send(command with { CustomerId = id }, ct)).ToActionResult(this);

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => (await _sender.Send(new DeleteCustomerCommand(id), ct)).ToActionResult(this);

    [HttpPost("{id:long}/restore")]
    public async Task<IActionResult> Restore(long id, CancellationToken ct)
        => (await _sender.Send(new RestoreCustomerCommand(id), ct)).ToActionResult(this);
}
