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

    /// <summary>
    /// Streams a backup off the server.
    /// <para>
    /// This is what makes the rest of it a backup. An archive sitting on the same machine as the
    /// database it protects is one power supply away from being no backup at all, so the operator
    /// has to be able to take a copy away — and a shopkeeper's way of doing that is a download.
    /// </para>
    /// <para>
    /// Behind <c>system.backup</c> like everything else here: the file is the whole shop's data.
    /// </para>
    /// </summary>
    [HttpGet("backups/{fileName}")]
    public async Task<IActionResult> Download(string fileName, CancellationToken ct)
    {
        var allowed = await _sender.Send(new AuthorizeBackupDownloadQuery(fileName), ct);
        if (allowed.IsFailure)
        {
            return ResultExtensions.Problem(allowed.Error, this);
        }

        // FileStream rather than a byte array: a year of sales should not have to fit in memory to
        // be downloaded, and the framework disposes the stream once the response is written.
        return File(
            new FileStream(allowed.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true),
            "application/zip",
            fileName);
    }
}
