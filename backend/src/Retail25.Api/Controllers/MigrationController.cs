using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Migration;

namespace Retail25.Api.Controllers;

/// <summary>
/// The legacy migration pipeline (doc 09 §3): analyze → stage → validate → dry-run → import.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/migration")]
[Produces("application/json")]
public sealed class MigrationController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILegacySourceReader _reader;

    public MigrationController(ISender sender, ILegacySourceReader reader)
    {
        _sender = sender;
        _reader = reader;
    }

    /// <summary>The legacy file types this deployment can read, with their documented field orders.</summary>
    [HttpGet("kinds")]
    public IActionResult Kinds() => Ok(_reader.Kinds);

    [HttpGet("batches")]
    public async Task<IActionResult> Batches([FromQuery] Guid locationId, CancellationToken ct)
        => Ok(await _sender.Send(new ListMigrationBatchesQuery(locationId), ct));

    [HttpGet("batches/{id:guid}")]
    public async Task<IActionResult> Batch(Guid id, CancellationToken ct)
        => (await _sender.Send(new GetMigrationBatchQuery(id), ct)).ToActionResult(this);

    [HttpGet("batches/{id:guid}/analysis")]
    public async Task<IActionResult> Analysis(Guid id, CancellationToken ct)
        => (await _sender.Send(new GetAnalysisQuery(id), ct)).ToActionResult(this);

    [HttpGet("batches/{id:guid}/validation")]
    public async Task<IActionResult> Validation(Guid id, CancellationToken ct)
        => (await _sender.Send(new GetValidationQuery(id), ct)).ToActionResult(this);

    [HttpGet("batches/{id:guid}/reconciliation")]
    public async Task<IActionResult> Reconciliation(Guid id, CancellationToken ct)
        => (await _sender.Send(new GetReconciliationQuery(id), ct)).ToActionResult(this);

    [HttpGet("batches/{id:guid}/rows")]
    public async Task<IActionResult> Rows(
        Guid id,
        [FromQuery] bool problemsOnly = true,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
        => (await _sender.Send(new BrowseStagingQuery(id, problemsOnly, skip, take), ct)).ToActionResult(this);

    /// <summary>
    /// Uploads a file and stages it. The content is base64 so a DBF survives the round trip — a
    /// binary format put through a text field comes out unreadable.
    /// </summary>
    [HttpPost("stage")]
    public async Task<IActionResult> Stage([FromBody] StageMigrationFileCommand command, CancellationToken ct)
        => (await _sender.Send(command, ct)).ToActionResult(this);

    [HttpPost("batches/{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken ct)
        => (await _sender.Send(new ValidateMigrationBatchCommand(id), ct)).ToActionResult(this);

    /// <summary>Transforms every row and writes nothing, reporting the totals the import would produce.</summary>
    [HttpPost("batches/{id:guid}/dry-run")]
    public async Task<IActionResult> DryRun(Guid id, [FromBody] LegacyControlTotals? totals, CancellationToken ct)
        => (await _sender.Send(new DryRunMigrationCommand(id, totals), ct)).ToActionResult(this);

    /// <summary>Refuses without a passing dry run for the same batch.</summary>
    [HttpPost("batches/{id:guid}/import")]
    public async Task<IActionResult> Import(Guid id, [FromBody] LegacyControlTotals? totals, CancellationToken ct)
        => (await _sender.Send(new ImportMigrationBatchCommand(id, totals), ct)).ToActionResult(this);

    [HttpPost("batches/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => (await _sender.Send(new CancelMigrationBatchCommand(id), ct)).ToActionResult(this);
}
