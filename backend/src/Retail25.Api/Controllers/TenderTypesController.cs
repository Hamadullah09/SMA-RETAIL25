using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;

namespace Retail25.Api.Controllers;

/// <summary>
/// The tender buttons, in the order an administrator arranged them (guide p.17).
/// <para>
/// The till renders whatever comes back rather than a hard-coded row of buttons, so adding a way to
/// pay is an administrative act. The capability flags travel with each row because the payment
/// dialog needs to know which tenders accept over-tender and which round to the smallest coin.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/tender-types")]
[Produces("application/json")]
public sealed class TenderTypesController : ControllerBase
{
    private readonly IApplicationDbContext _db;

    public TenderTypesController(IApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenders = await _db.TenderTypes.AsNoTracking()
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.SortOrder)
            .Select(t => new TenderTypeDto(
                t.Id,
                t.Code,
                t.DisplayName,
                t.Behaviour.ToString(),
                t.SortOrder,
                t.IconKey,
                t.OpensCashDrawer,
                t.AllowsOverTender,
                t.RoundsToMinimumTender,
                t.RequiresReference,
                t.AllowedForRefunds,
                t.IsActive))
            .ToListAsync(ct);

        return Ok(tenders);
    }
}

public sealed record TenderTypeDto(
    long Id,
    string Code,
    string DisplayName,
    string Behaviour,
    int SortOrder,
    string? IconKey,
    bool OpensCashDrawer,
    bool AllowsOverTender,
    bool RoundsToMinimumTender,
    bool RequiresReference,
    bool AllowedForRefunds,
    bool IsActive);
