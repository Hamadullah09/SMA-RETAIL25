using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Retail25.Api.Common;
using Retail25.Application.Accounting;
using Retail25.Domain.Accounting;

namespace Retail25.Api.Controllers;

/// <summary>
/// The accounting link (doc 09 §1), replacing the legacy QB-XML integration that needed the company
/// file open on the same machine.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/sync/accounting")]
[Produces("application/json")]
public sealed class AccountingSyncController : ControllerBase
{
    private readonly ISender _sender;

    public AccountingSyncController(ISender sender) => _sender = sender;

    /// <summary>
    /// Runs one sync step now. The response carries the generated file so the operator can save it
    /// without hunting for it on the server — the CSV adapter's whole point is being handed to a
    /// bookkeeper.
    /// </summary>
    [HttpPost("{direction}/{entity}")]
    public async Task<IActionResult> Trigger(
        string direction,
        SyncEntity entity,
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly? businessDate = null,
        [FromQuery] long? purchaseOrderId = null,
        [FromQuery] DateOnly? dueOn = null)
    {
        var pull = string.Equals(direction, "pull", StringComparison.OrdinalIgnoreCase);

        var result = await _sender.Send(new TriggerAccountingSyncCommand(
            locationId, entity, pull, businessDate, purchaseOrderId, dueOn));

        return result.ToActionResult(this);
    }

    /// <summary>The same run, delivered as the file itself.</summary>
    [HttpGet("{entity}/export")]
    public async Task<IActionResult> Export(
        SyncEntity entity,
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly? businessDate = null,
        [FromQuery] long? purchaseOrderId = null,
        [FromQuery] DateOnly? dueOn = null)
    {
        var result = await _sender.Send(new TriggerAccountingSyncCommand(
            locationId, entity, false, businessDate, purchaseOrderId, dueOn));

        if (result.IsFailure)
        {
            return result.ToActionResult(this);
        }

        var sync = result.Value;

        if (!sync.Success)
        {
            return ResultExtensions.Problem(
                new Domain.Common.Error("sync.failed", sync.Error ?? "The sync failed."), this);
        }

        var content = Encoding.UTF8.GetBytes(sync.Output ?? string.Empty);
        return File(content, "text/csv", $"{entity.ToString().ToLowerInvariant()}.csv");
    }

    /// <summary>
    /// What is still unmapped. Worth running before the first real sync: this is exactly where the
    /// legacy integration failed silently (guide p.109–111).
    /// </summary>
    [HttpGet("preflight")]
    public async Task<IActionResult> Preflight([FromQuery][BindRequired] long locationId)
        => Ok(await _sender.Send(new PreflightAccountingSyncQuery(locationId)));

    [HttpGet("log")]
    public async Task<IActionResult> Log(
        [FromQuery] string? entity = null,
        [FromQuery] SyncStatus? status = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
        => Ok(await _sender.Send(new GetSyncLogQuery(entity, status, skip, take)));

    /// <summary>One attempt in full — the modern "Last QB Request / Last QB Response" (guide p.111).</summary>
    [HttpGet("log/{id:long}")]
    public async Task<IActionResult> LogDetail(long id)
        => (await _sender.Send(new GetSyncLogDetailQuery(id))).ToActionResult(this);

    [HttpGet("mappings")]
    public async Task<IActionResult> Mappings([FromQuery] string provider = "csv")
        => Ok(await _sender.Send(new GetExternalMapsQuery(provider)));

    [HttpPost("mappings")]
    public async Task<IActionResult> UpsertMapping([FromBody] UpsertMappingRequest request)
        => (await _sender.Send(new UpsertExternalMapCommand(
            request.Provider, request.EntityType, request.LocalId, request.LocalKey,
            request.RemoteId, request.RemoteName))).ToActionResult(this);
}

public sealed record UpsertMappingRequest(
    string Provider,
    string EntityType,
    long? LocalId,
    string? LocalKey,
    string RemoteId,
    string? RemoteName);
