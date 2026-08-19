using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Retail25.Api.Common;
using Retail25.Application.Terminals;

namespace Retail25.Api.Controllers;

/// <summary>
/// The RFID topology: which machines exist, which readers they drive, and what each antenna stands
/// for.
/// <para>
/// Separate from <c>TerminalsController</c>, which an agent talks to. This is the administrator's
/// side — the screen where a reader is registered and its antennas are pointed at tills.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/rfid-topology")]
[Produces("application/json")]
public sealed class RfidTopologyController : ControllerBase
{
    private readonly ISender _sender;

    public RfidTopologyController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery][BindRequired] long locationId, CancellationToken ct)
        => (await _sender.Send(new GetRfidTopologyQuery(locationId), ct)).ToActionResult(this);

    /// <summary>Registers a reader, or updates the one already carrying this key.</summary>
    [HttpPut("readers")]
    public async Task<IActionResult> SaveReader([FromBody] SaveReaderRequest request, CancellationToken ct)
        => (await _sender.Send(new SaveReaderCommand(
            request.LocationId,
            request.ReaderKey,
            request.SerialNumber,
            request.Model,
            request.Host,
            request.Port,
            request.Protocol,
            request.AntennaCount,
            request.DeviceId), ct)).ToActionResult(this);

    /// <summary>
    /// Points one antenna at one station. A null station clears the assignment entirely; setting
    /// <c>enabled</c> false keeps the mapping and stops the reads, which is what an antenna being
    /// worked on needs.
    /// </summary>
    [HttpPut("readers/{readerId:long}/antennas/{antennaNumber:int}")]
    public async Task<IActionResult> AssignAntenna(
        long readerId,
        int antennaNumber,
        [FromBody] AssignAntennaRequest request,
        CancellationToken ct)
        => (await _sender.Send(new AssignAntennaCommand(
            readerId,
            antennaNumber,
            request.StationId,
            request.Enabled), ct)).ToActionResult(this);

    /// <summary>
    /// Produces what an installer takes to a machine: where to connect, which machine it is, and a
    /// one-time code to prove it. No durable secret -- that is handed back at redemption, over TLS.
    /// </summary>
    [HttpPost("enrolments")]
    public async Task<IActionResult> GenerateEnrolment([FromBody] GenerateEnrolmentRequest request, CancellationToken ct)
        => (await _sender.Send(new GenerateAgentEnrolmentCommand(request.LocationId, request.DeviceKey, request.Name), ct))
            .ToActionResult(this);

    /// <summary>
    /// An agent presenting its code at first start.
    /// <para>
    /// Anonymous because the code is the credential: a machine being installed has nothing else to
    /// authenticate with, which is the problem enrolment exists to solve. The code is single-use,
    /// expires, and is checked against a stored hash.
    /// </para>
    /// </summary>
    [AllowAnonymous]
    [HttpPost("enrolments/redeem")]
    public async Task<IActionResult> RedeemEnrolment([FromBody] RedeemEnrolmentRequest request, CancellationToken ct)
        => (await _sender.Send(new RedeemAgentEnrolmentCommand(
            request.EnrolmentCode,
            request.Hostname,
            request.OperatingSystem,
            request.AgentVersion), ct)).ToActionResult(this);

    /// <summary>
    /// Turns an existing one-reader-one-station setup into the new model, without changing what any
    /// till does. Dry-runnable, and safe to press twice.
    /// </summary>
    [HttpPost("backfill")]
    public async Task<IActionResult> Backfill([FromBody] BackfillRequest request, CancellationToken ct)
        => (await _sender.Send(new BackfillRfidTopologyCommand(request.LocationId, request.DryRun), ct))
            .ToActionResult(this);
}

public sealed record SaveReaderRequest(
    long LocationId,
    string ReaderKey,
    string Host,
    int Port,
    string Protocol,
    int AntennaCount = 4,
    string? SerialNumber = null,
    string? Model = null,
    long? DeviceId = null);

public sealed record AssignAntennaRequest(long? StationId, bool Enabled = true);

public sealed record BackfillRequest(long LocationId, bool DryRun = false);

public sealed record GenerateEnrolmentRequest(long LocationId, string DeviceKey, string? Name = null);

public sealed record RedeemEnrolmentRequest(
    string EnrolmentCode,
    string? Hostname = null,
    string? OperatingSystem = null,
    string? AgentVersion = null);
