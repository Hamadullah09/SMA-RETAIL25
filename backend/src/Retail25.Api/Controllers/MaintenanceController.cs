using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Maintenance;

namespace Retail25.Api.Controllers;

/// <summary>
/// Whole-database backup and restore, for the administrator who owns the data rather than the
/// engine. Authorisation is <c>system.backup</c> on the commands themselves; restore additionally
/// drops every live session, which the UI must say out loud before calling it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/maintenance")]
[Produces("application/json")]
public sealed class MaintenanceController : ControllerBase
{
    private readonly ISender _sender;

    public MaintenanceController(ISender sender) => _sender = sender;

    [HttpGet("backups")]
    public async Task<IActionResult> List(CancellationToken ct)
        => (await _sender.Send(new ListDatabaseBackupsQuery(), ct)).ToActionResult(this);

    [HttpPost("backups")]
    public async Task<IActionResult> Create(CancellationToken ct)
        => (await _sender.Send(new CreateDatabaseBackupCommand(), ct)).ToActionResult(this);

    [HttpPost("backups/restore")]
    public async Task<IActionResult> Restore([FromBody] RestoreDatabaseBackupCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);
}
