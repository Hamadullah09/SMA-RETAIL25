using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Application.Drawer.Commands;

namespace Retail25.Api.Controllers;

/// <summary>
/// Cash drawer operations (guide p.10–11).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/drawer")]
public class DrawerController : ControllerBase
{
    private readonly ISender _sender;

    public DrawerController(ISender sender) => _sender = sender;

    /// <summary>Opens a drawer for the shift with its starting float.</summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> Open([FromBody] OpenDrawerSessionCommand command)
    {
        var result = await _sender.Send(command);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Error });
    }

    /// <summary>Records a pay-in or pay-out against an open drawer.</summary>
    [HttpPost("sessions/{sessionId:guid}/movements")]
    public async Task<IActionResult> RecordMovement(Guid sessionId, [FromBody] DrawerMovementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _sender.Send(new RecordDrawerMovementCommand(
            sessionId, request.StaffId, request.Amount, request.IsPayIn, request.Reason));

        return result.Success ? Ok(result) : BadRequest(new { error = result.Error });
    }

    /// <summary>Closes the drawer against a physical count and reports the variance.</summary>
    [HttpPost("sessions/{sessionId:guid}/close")]
    public async Task<IActionResult> Close(Guid sessionId, [FromBody] CloseDrawerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _sender.Send(new CloseDrawerSessionCommand(sessionId, request.CountedCash));
        return result.Success ? Ok(result) : BadRequest(new { error = result.Error });
    }
}

/// <param name="StaffId">Who moved the cash.</param>
/// <param name="Amount">How much, always positive.</param>
/// <param name="IsPayIn">True to put cash in, false to take it out.</param>
/// <param name="Reason">Why.</param>
public sealed record DrawerMovementRequest(Guid StaffId, decimal Amount, bool IsPayIn, string Reason);

/// <param name="CountedCash">What was physically counted at close.</param>
public sealed record CloseDrawerRequest(decimal CountedCash);
