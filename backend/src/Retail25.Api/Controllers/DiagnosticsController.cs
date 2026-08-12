using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;

namespace Retail25.Api.Controllers;

/// <summary>
/// What went wrong, for whoever has to fix it.
/// <para>
/// Behind <c>audit.read</c> rather than open, because a stack trace names tables, columns and file
/// paths. That is the same permission that opens the audit log, and for the same reason: it is the
/// permission a shop gives the person who is allowed to know how the system works.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/diagnostics")]
[Produces("application/json")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly RecentErrors _errors;
    private readonly ICurrentUser _currentUser;

    public DiagnosticsController(RecentErrors errors, ICurrentUser currentUser)
    {
        _errors = errors;
        _currentUser = currentUser;
    }

    /// <summary>
    /// The most recent unhandled failures, newest first. Pass the <c>traceId</c> from a 500 response
    /// to see just that one.
    /// </summary>
    [HttpGet("errors")]
    public IActionResult Errors([FromQuery] int take = 20, [FromQuery] string? traceId = null)
    {
        if (!_currentUser.HasPermission(PermissionKeys.System.AuditRead))
        {
            return ResultExtensions.Problem(
                new Domain.Common.Error("permission.denied", $"The current user does not hold '{PermissionKeys.System.AuditRead}'."),
                this);
        }

        return Ok(_errors.Take(take, traceId));
    }
}
