using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Retail25.Api.Common;
using Retail25.Application.Trolleys.Commands;
using Retail25.Application.Trolleys.Queries;

namespace Retail25.Api.Controllers;

/// <summary>
/// The shop's self-checkout trolleys, for the people who set the shop up.
/// <para>
/// Staff-authenticated, unlike <c>ShopperTrolleyController</c>, which a shopper's phone talks to.
/// The two never share a route for that reason: one is the fixture list, the other is a basket.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/trolleys")]
public sealed class TrolleysController : ControllerBase
{
    private readonly ISender _sender;

    public TrolleysController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery][BindRequired] long locationId, CancellationToken ct)
        => (await _sender.Send(new ListTrolleysQuery(locationId), ct)).ToActionResult(this);

    /// <summary>
    /// Creates every counter in the self-checkout block that does not exist yet, and gives each one a
    /// starting weight spread across the fleet's band.
    /// <para>
    /// Idempotent, so pressing it twice is safe -- which matters, because pressing it again is the
    /// obvious thing to do when a page looks wrong.
    /// </para>
    /// </summary>
    [HttpPost("provision")]
    public async Task<IActionResult> Provision([FromBody] ProvisionRequest? request, CancellationToken ct)
        => (await _sender.Send(new ProvisionTrolleysCommand(request?.LocationId), ct)).ToActionResult(this);

    /// <summary>
    /// Records what a trolley weighs empty. A null weight clears it back to unknown, which is not
    /// the same as zero and is the right answer for a trolley that has been rebuilt.
    /// </summary>
    [HttpPut("{id:long}/tare")]
    public async Task<IActionResult> SetTare(long id, [FromBody] SetTareRequest request, CancellationToken ct)
        => (await _sender.Send(new SetTrolleyTareCommand(id, request?.TareWeightKg), ct)).ToActionResult(this);
}

public sealed record SetTareRequest(decimal? TareWeightKg);

public sealed record ProvisionRequest(long? LocationId = null);
