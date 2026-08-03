using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Carts.Queries;
using Retail25.Application.Terminals;
using Retail25.Domain.Terminals;

namespace Retail25.Api.Controllers;

/// <summary>
/// Stations and their peripherals. The browser drives hardware through here rather than directly,
/// so every drawer pop and reader-mode change is permission-checked and auditable (doc 06 §5).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/terminals")]
[Produces("application/json")]
public sealed class TerminalsController : ControllerBase
{
    private readonly ISender _sender;

    public TerminalsController(ISender sender) => _sender = sender;

    /// <summary>The station's effective POS settings, after station overrides are folded over policy.</summary>
    [HttpGet("{stationId:guid}/policy")]
    public async Task<IActionResult> Policy(Guid stationId)
        => (await _sender.Send(new GetStationPolicyQuery(stationId))).ToActionResult(this);

    /// <summary>The device profile bundle the agent pulls on connect (doc 06 §7).</summary>
    [HttpGet("{stationId:guid}/profile")]
    public async Task<IActionResult> Profile(Guid stationId)
        => (await _sender.Send(new GetTerminalProfileQuery(stationId))).ToActionResult(this);

    // --- RFID reader configuration -----------------------------------------------------------
    //
    // Everything the vendor's Windows demo can set, set from here instead. The point is not to
    // replace a tool that works: it is that a reader configured by hand is configured on one device,
    // and a shop that swaps a failed unit for a spare then has a till nobody can explain. These
    // settings live in the database, and the agent pushes them into whatever hardware it finds.

    [HttpGet("readers")]
    public async Task<IActionResult> Readers([FromQuery] Guid locationId)
        => Ok(await _sender.Send(new ListReaderProfilesQuery(locationId)));

    [HttpGet("readers/{id:guid}")]
    public async Task<IActionResult> Reader(Guid id)
        => (await _sender.Send(new GetReaderProfileQuery(id))).ToActionResult(this);

    [HttpPut("readers/{id:guid}")]
    public async Task<IActionResult> UpdateReader(Guid id, [FromBody] UpdateReaderProfileCommand request)
        => (await _sender.Send(request with { Id = id })).ToActionResult(this);

    [HttpPut("{stationId:guid}/reader-mode")]
    public async Task<IActionResult> SetReaderMode(Guid stationId, [FromBody] ReaderModeRequest request)
        => (await _sender.Send(new SetReaderModeCommand(stationId, request.Mode))).ToActionResult(this);

    [HttpPost("{stationId:guid}/drawer/open")]
    public async Task<IActionResult> OpenDrawer(Guid stationId)
        => (await _sender.Send(new OpenStationDrawerCommand(stationId))).ToActionResult(this);

    /// <summary>Asks the scale for a weight; the answer arrives over SignalR as <c>WeightReported</c>.</summary>
    [HttpPost("{stationId:guid}/scale/weight")]
    public async Task<IActionResult> RequestWeight(Guid stationId)
        => (await _sender.Send(new RequestWeightCommand(stationId))).ToActionResult(this);

    [HttpPost("{stationId:guid}/scale/zero")]
    public async Task<IActionResult> ZeroScale(Guid stationId)
        => (await _sender.Send(new ZeroScaleCommand(stationId))).ToActionResult(this);

    [HttpPost("{stationId:guid}/pole-display")]
    public async Task<IActionResult> DisplayPole(Guid stationId, [FromBody] PoleDisplayRequest request)
        => (await _sender.Send(new DisplayOnPoleCommand(stationId, request.Line1, request.Line2))).ToActionResult(this);

    /// <summary>Agent heartbeat over HTTP, for agents that cannot hold a hub connection open.</summary>
    [AllowAnonymous]
    [HttpPost("{stationId:guid}/status")]
    public async Task<IActionResult> ReportStatus(Guid stationId, [FromBody] AgentStatusRequest request)
        => (await _sender.Send(new ReportAgentStatusCommand(
            stationId,
            request.AgentVersion,
            request.ReaderOnline,
            request.PrinterOnline,
            request.ScaleOnline,
            request.DrawerOnline,
            request.PoleDisplayOnline,
            request.ReadRate))).ToActionResult(this);
}

public sealed record ReaderModeRequest(ReaderMode Mode);

public sealed record PoleDisplayRequest(string Line1, string Line2);

public sealed record AgentStatusRequest(
    string? AgentVersion,
    bool ReaderOnline,
    bool PrinterOnline,
    bool ScaleOnline,
    bool DrawerOnline,
    bool PoleDisplayOnline,
    int ReadRate);
