using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Reports;
using Retail25.Application.Staff;

namespace Retail25.Api.Controllers;

/// <summary>
/// Staff, the time clock and commission rules (guide p.33, p.75–76).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/staff")]
[Produces("application/json")]
public sealed class StaffController : ControllerBase
{
    private readonly ISender _sender;

    public StaffController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery] long locationId, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _sender.Send(new BrowseStaffQuery(locationId, includeInactive), ct));

    /* -----------------------------------------------------------------------------------------
     * The time clock. The three "me" routes are the punch-clock widget and need only the
     * self-service permission; everything below them is a supervisor's view of other people.
     * --------------------------------------------------------------------------------------- */

    [HttpGet("time-clock/me")]
    public async Task<IActionResult> MyTimeClock([FromQuery] long locationId, CancellationToken ct)
        => (await _sender.Send(new GetMyTimeClockQuery(locationId), ct)).ToActionResult(this);

    [HttpPost("time-clock/in")]
    public async Task<IActionResult> ClockIn([FromBody] ClockInCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("time-clock/out")]
    public async Task<IActionResult> ClockOut([FromBody] ClockOutCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpGet("time-clock")]
    public async Task<IActionResult> BrowseTimeClock(
        [FromQuery] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? staffId = null,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new BrowseTimeClockQuery(locationId, from, to, staffId), ct));

    [HttpPut("time-clock/{id:long}")]
    public async Task<IActionResult> AmendTimeClock(long id, [FromBody] AmendTimeClockRequest request, CancellationToken ct)
        => (await _sender.Send(new AmendTimeClockEntryCommand(id, request.ClockIn, request.ClockOut), ct))
            .ToActionResult(this);

    [HttpDelete("time-clock/{id:long}")]
    public async Task<IActionResult> DeleteTimeClock(long id, CancellationToken ct)
        => (await _sender.Send(new DeleteTimeClockEntryCommand(id), ct)).ToActionResult(this);

    /* -----------------------------------------------------------------------------------------
     * Commission rules
     * --------------------------------------------------------------------------------------- */

    [HttpGet("{staffId:long}/commission-rules")]
    public async Task<IActionResult> CommissionRules(long staffId, CancellationToken ct)
        => Ok(await _sender.Send(new ListCommissionRulesQuery(staffId), ct));

    [HttpPost("commission-rules")]
    public async Task<IActionResult> SaveCommissionRule([FromBody] SaveCommissionRuleCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpDelete("commission-rules/{id:long}")]
    public async Task<IActionResult> DeleteCommissionRule(long id, CancellationToken ct)
        => (await _sender.Send(new DeleteCommissionRuleCommand(id), ct)).ToActionResult(this);

    /* -----------------------------------------------------------------------------------------
     * The two staff reports. Alongside the others under api/v1/reports would be tidier, but they
     * belong to this screen and this is where the frontend looks for them.
     * --------------------------------------------------------------------------------------- */

    [HttpGet("reports/hours")]
    public async Task<IActionResult> Hours(
        [FromQuery] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? staffId = null,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new HoursReportQuery(locationId, from, to, staffId), ct));

    [HttpGet("reports/hours/export")]
    [Produces("text/csv")]
    public async Task<IActionResult> HoursExport(
        [FromQuery] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? staffId = null,
        CancellationToken ct = default)
    {
        var csv = await _sender.Send(new ExportHoursReportQuery(new HoursReportQuery(locationId, from, to, staffId)), ct);

        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"hours-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv");
    }

    [HttpGet("reports/commissions")]
    public async Task<IActionResult> Commissions(
        [FromQuery] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? staffId = null,
        [FromQuery] bool includeDetail = false,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new CommissionReportQuery(locationId, from, to, staffId, includeDetail), ct));

    [HttpGet("reports/commissions/export")]
    [Produces("text/csv")]
    public async Task<IActionResult> CommissionsExport(
        [FromQuery] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? staffId = null,
        CancellationToken ct = default)
    {
        var csv = await _sender.Send(
            new ExportCommissionReportQuery(new CommissionReportQuery(locationId, from, to, staffId)), ct);

        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"commissions-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv");
    }
}

public sealed record AmendTimeClockRequest(DateTimeOffset ClockIn, DateTimeOffset? ClockOut);
