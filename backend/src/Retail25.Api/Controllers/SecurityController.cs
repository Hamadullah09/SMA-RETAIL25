using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Retail25.Api.Common;
using Retail25.Application.Audit;
using Retail25.Application.Auth;
using Retail25.Domain.Security;

namespace Retail25.Api.Controllers;

/// <summary>
/// Staff PIN switching at the till (guide p.13, doc 07 §POS fast user switching).
/// <para>
/// Rate-limited, because a four-digit secret on a machine sitting in a shop is otherwise guessable
/// in an afternoon. The per-profile lockout is the second limit; this one stops an attacker cycling
/// staff codes rather than PINs.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/staff")]
[Produces("application/json")]
[EnableRateLimiting("pin")]
public sealed class StaffSessionController : ControllerBase
{
    private readonly ISender _sender;

    public StaffSessionController(ISender sender) => _sender = sender;

    /// <summary>Switches who the next sale is attributed to, inside the station's existing session.</summary>
    [HttpPost("verify-pin")]
    public async Task<IActionResult> VerifyPin([FromBody] VerifyPinRequest request)
        => (await _sender.Send(new VerifyStaffPinCommand(request.StaffCode, request.Pin, request.StationId)))
            .ToActionResult(this);

    [HttpPost("{staffId:long}/pin")]
    public async Task<IActionResult> SetPin(long staffId, [FromBody] SetPinRequest request)
        => (await _sender.Send(new SetStaffPinCommand(staffId, request.Pin))).ToActionResult(this);

    [HttpPost("{staffId:long}/unlock")]
    public async Task<IActionResult> Unlock(long staffId)
        => (await _sender.Send(new UnlockStaffPinCommand(staffId))).ToActionResult(this);
}

/// <summary>
/// Supervisor overrides (doc 07 §Step-up). A sensitive command answers 428 with a request id; the
/// cashier either takes a supervisor PIN here, or a supervisor approves from wherever they are.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/approvals")]
[Produces("application/json")]
public sealed class ApprovalsController : ControllerBase
{
    private readonly ISender _sender;

    public ApprovalsController(ISender sender) => _sender = sender;

    [HttpPost]
    public async Task<IActionResult> Raise([FromBody] ApprovalRequest request)
        => (await _sender.Send(new RequestSupervisorApprovalCommand(
            request.Permission, request.Action, request.Context, request.StationId))).ToActionResult(this);

    /// <summary>Inline approval with a supervisor's PIN, without leaving the till.</summary>
    [HttpPost("{approvalId:long}/approve-with-pin")]
    [EnableRateLimiting("pin")]
    public async Task<IActionResult> ApproveWithPin(long approvalId, [FromBody] ApproveWithPinRequest request)
        => (await _sender.Send(new ApproveWithPinCommand(approvalId, request.StaffCode, request.Pin)))
            .ToActionResult(this);

    /// <summary>Approval by a supervisor already signed in at another station.</summary>
    [HttpPost("{approvalId:long}/approve")]
    public async Task<IActionResult> Approve(long approvalId)
        => (await _sender.Send(new ApproveSupervisorRequestCommand(approvalId))).ToActionResult(this);

    [HttpPost("{approvalId:long}/deny")]
    public async Task<IActionResult> Deny(long approvalId, [FromBody] DenyRequest? request)
        => (await _sender.Send(new DenySupervisorRequestCommand(approvalId, request?.Reason))).ToActionResult(this);

    [HttpGet("pending")]
    public async Task<IActionResult> Pending([FromQuery][BindRequired] long locationId)
        => Ok(await _sender.Send(new ListPendingApprovalsQuery(locationId)));
}

/// <summary>The audit trail, read-only and permission-gated (doc 07 §Audit).</summary>
[ApiController]
[Authorize]
[Route("api/v1/audit")]
[Produces("application/json")]
public sealed class AuditController : ControllerBase
{
    private readonly ISender _sender;

    public AuditController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] long? actorStaffId = null,
        [FromQuery] long? stationId = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? entityId = null,
        [FromQuery] AuditAction? action = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
        => Ok(await _sender.Send(new AuditLogQuery(
            from, to, actorStaffId, stationId, entityType, entityId, action, null, skip, take)));

    /// <summary>Everything one request did — the question an investigation actually asks.</summary>
    [HttpGet("request/{correlationId}")]
    public async Task<IActionResult> ForRequest(string correlationId)
        => Ok(await _sender.Send(new AuditTrailForRequestQuery(correlationId)));
}

public sealed record VerifyPinRequest(string StaffCode, string Pin, long StationId);

public sealed record SetPinRequest(string Pin);

public sealed record ApprovalRequest(string Permission, string Action, string? Context, long StationId);

public sealed record ApproveWithPinRequest(string StaffCode, string Pin);

public sealed record DenyRequest(string? Reason);
