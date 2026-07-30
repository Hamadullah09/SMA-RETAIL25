using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Receivables;

namespace Retail25.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/gift-cards")]
[Produces("application/json")]
public sealed class GiftCardsController : ControllerBase
{
    private readonly ISender _sender;

    public GiftCardsController(ISender sender) => _sender = sender;

    [HttpPost]
    public async Task<IActionResult> Issue([FromBody] IssueGiftCardCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpGet("{serialNumber}")]
    public async Task<IActionResult> Balance(string serialNumber, CancellationToken ct)
        => (await _sender.Send(new GiftCardBalanceQuery(serialNumber), ct)).ToActionResult(this);
}
