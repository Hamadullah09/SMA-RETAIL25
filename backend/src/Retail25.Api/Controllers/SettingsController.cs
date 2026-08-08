using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Settings;
using Retail25.Domain.Configuration;

namespace Retail25.Api.Controllers;

/// <summary>
/// The Setup screen (guide p.76–84), one route per tab.
/// <para>
/// A tab at a time rather than one save-everything endpoint. An administrator on the Hardware tab
/// must not be able to overwrite the Taxes tab with whatever the page happened to be holding, and a
/// permission that grants tax changes should not grant printer changes.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/settings")]
[Produces("application/json")]
public sealed class SettingsController : ControllerBase
{
    private readonly ISender _sender;

    public SettingsController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] long locationId, CancellationToken ct)
        => (await _sender.Send(new GetSettingsQuery(locationId), ct)).ToActionResult(this);

    [HttpPut("business")]
    public async Task<IActionResult> Business([FromBody] SaveBusinessSettingsCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("taxes")]
    public async Task<IActionResult> Taxes([FromBody] SaveTaxSettingsCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPut("pos")]
    public async Task<IActionResult> Pos([FromBody] SavePosSettingsCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPut("numbering")]
    public async Task<IActionResult> Numbering([FromBody] SaveNumberSequenceCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPut("pricing-ladder")]
    public async Task<IActionResult> PricingLadder([FromBody] SavePricingLadderCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("stations")]
    public async Task<IActionResult> SaveStation([FromBody] SaveStationCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("stations/{id:long}/deactivate")]
    public async Task<IActionResult> DeactivateStation(long id, CancellationToken ct)
        => (await _sender.Send(new DeactivateStationCommand(id), ct)).ToActionResult(this);

    [HttpPost("printers")]
    public async Task<IActionResult> Printer([FromBody] SavePrinterProfileCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("scales")]
    public async Task<IActionResult> Scale([FromBody] SaveScaleProfileCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("pole-displays")]
    public async Task<IActionResult> PoleDisplay([FromBody] SavePoleDisplayProfileCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("readers")]
    public async Task<IActionResult> Reader([FromBody] SaveReaderProfileCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("tenders")]
    public async Task<IActionResult> Tender([FromBody] SaveTenderTypeCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpDelete("tenders/{id:long}")]
    public async Task<IActionResult> DeleteTender(long id, [FromQuery] long locationId, CancellationToken ct)
        => (await _sender.Send(new DeleteTenderTypeCommand(locationId, id), ct)).ToActionResult(this);

    [HttpPost("currencies")]
    public async Task<IActionResult> Currency([FromBody] SaveCurrencyCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("staff")]
    public async Task<IActionResult> Staff([FromBody] SaveStaffCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    /// <summary>
    /// The sequence kinds and their labels, so the Numbering tab does not hard-code an enum the
    /// server owns.
    /// </summary>
    [HttpGet("sequence-kinds")]
    public IActionResult SequenceKinds()
        => Ok(Enum.GetValues<SequenceKind>().Select(k => new { value = k.ToString(), label = k.ToString() }));
}
