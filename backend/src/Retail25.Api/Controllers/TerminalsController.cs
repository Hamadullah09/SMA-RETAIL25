using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Carts.Queries;
using Retail25.Application.Rfid;
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
    private readonly IReaderConnectionStatus _readerStatus;

    public TerminalsController(ISender sender, IReaderConnectionStatus readerStatus)
    {
        _sender = sender;
        _readerStatus = readerStatus;
    }

    /// <summary>
    /// Which readers this server holds, and whether each is answering right now.
    /// <para>
    /// The till's status strip asks here first. When <c>serverHosted</c> is false this deployment
    /// does not hold reader connections at all, and the till falls back to asking the agent on its
    /// own machine — the distinction matters, because "no reader" and "not my job to know" look
    /// identical to a cashier otherwise, and only one of them is a broken shop.
    /// </para>
    /// </summary>
    [HttpGet("reader-connections")]
    public IActionResult ReaderConnections() => Ok(_readerStatus.Current);

    /// <summary>The station's effective POS settings, after station overrides are folded over policy.</summary>
    [HttpGet("{stationId:long}/policy")]
    public async Task<IActionResult> Policy(long stationId)
        => (await _sender.Send(new GetStationPolicyQuery(stationId))).ToActionResult(this);

    /// <summary>The device profile bundle the agent pulls on connect (doc 06 §7).</summary>
    [HttpGet("{stationId:long}/profile")]
    public async Task<IActionResult> Profile(long stationId)
        => (await _sender.Send(new GetTerminalProfileQuery(stationId))).ToActionResult(this);

    // --- RFID reader configuration -----------------------------------------------------------
    //
    // Everything the vendor's Windows demo can set, set from here instead. The point is not to
    // replace a tool that works: it is that a reader configured by hand is configured on one device,
    // and a shop that swaps a failed unit for a spare then has a till nobody can explain. These
    // settings live in the database, and the agent pushes them into whatever hardware it finds.

    [HttpGet("readers")]
    public async Task<IActionResult> Readers([FromQuery] long locationId)
        => Ok(await _sender.Send(new ListReaderProfilesQuery(locationId)));

    [HttpGet("readers/{id:long}")]
    public async Task<IActionResult> Reader(long id)
        => (await _sender.Send(new GetReaderProfileQuery(id))).ToActionResult(this);

    [HttpPut("readers/{id:long}")]
    public async Task<IActionResult> UpdateReader(long id, [FromBody] UpdateReaderProfileCommand request)
        => (await _sender.Send(request with { Id = id })).ToActionResult(this);

    [HttpPut("{stationId:long}/reader-mode")]
    public async Task<IActionResult> SetReaderMode(long stationId, [FromBody] ReaderModeRequest request)
        => (await _sender.Send(new SetReaderModeCommand(stationId, request.Mode))).ToActionResult(this);

    [HttpPost("{stationId:long}/drawer/open")]
    public async Task<IActionResult> OpenDrawer(long stationId)
        => (await _sender.Send(new OpenStationDrawerCommand(stationId))).ToActionResult(this);

    /// <summary>Asks the scale for a weight; the answer arrives over SignalR as <c>WeightReported</c>.</summary>
    [HttpPost("{stationId:long}/scale/weight")]
    public async Task<IActionResult> RequestWeight(long stationId)
        => (await _sender.Send(new RequestWeightCommand(stationId))).ToActionResult(this);

    [HttpPost("{stationId:long}/scale/zero")]
    public async Task<IActionResult> ZeroScale(long stationId)
        => (await _sender.Send(new ZeroScaleCommand(stationId))).ToActionResult(this);

    [HttpPost("{stationId:long}/pole-display")]
    public async Task<IActionResult> DisplayPole(long stationId, [FromBody] PoleDisplayRequest request)
        => (await _sender.Send(new DisplayOnPoleCommand(stationId, request.Line1, request.Line2))).ToActionResult(this);

    /// <summary>Agent heartbeat over HTTP, for agents that cannot hold a hub connection open.</summary>
    [AllowAnonymous]
    [HttpPost("{stationId:long}/status")]
    public async Task<IActionResult> ReportStatus(long stationId, [FromBody] AgentStatusRequest request)
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
