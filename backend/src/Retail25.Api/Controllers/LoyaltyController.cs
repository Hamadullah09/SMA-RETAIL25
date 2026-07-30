using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Loyalty;

namespace Retail25.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/loyalty")]
[Produces("application/json")]
public sealed class LoyaltyController : ControllerBase
{
    private readonly ISender _sender;

    public LoyaltyController(ISender sender) => _sender = sender;

    [HttpGet("policy")]
    public async Task<IActionResult> GetPolicy([FromQuery] Guid locationId, CancellationToken ct)
        => Ok(await _sender.Send(new GetLoyaltyPolicyQuery(locationId), ct));

    [HttpPut("policy")]
    public async Task<IActionResult> SavePolicy([FromBody] SaveLoyaltyPolicyCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpGet("customers/{customerId:guid}/balance")]
    public async Task<IActionResult> Balance(Guid customerId, CancellationToken ct)
        => (await _sender.Send(new GetLoyaltyBalanceQuery(customerId), ct)).ToActionResult(this);

    [HttpGet("customers/{customerId:guid}/ledger")]
    public async Task<IActionResult> Ledger(Guid customerId, CancellationToken ct)
        => Ok(await _sender.Send(new GetLoyaltyLedgerQuery(customerId), ct));

    [HttpPost("customers/{customerId:guid}/adjust")]
    public async Task<IActionResult> Adjust(Guid customerId, [FromBody] AdjustLoyaltyRequest request, CancellationToken ct)
        => (await _sender.Send(new AdjustLoyaltyPointsCommand(customerId, request.PointsDelta, request.Reason), ct))
            .ToActionResult(this);
}

public sealed record AdjustLoyaltyRequest(int PointsDelta, string Reason);
