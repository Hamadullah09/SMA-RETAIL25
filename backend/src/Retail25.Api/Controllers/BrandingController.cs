using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Retail25.Api.Common;
using Retail25.Application.Settings;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;

namespace Retail25.Api.Controllers;

/// <summary>
/// The two marks that make an installation belong to a shop: the watermark behind the working area
/// and the company logo in the corner of the chrome.
/// <para>
/// Both are uploaded, not configured. A reseller standing up a new customer changes them here and
/// nowhere else — no rebuild, no image checked into the repository, nothing per-customer in the
/// bundle.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/locations/{locationId:long}/branding")]
public sealed class BrandingController : ControllerBase
{
    private readonly ISender _sender;

    public BrandingController(ISender sender) => _sender = sender;

    /// <summary>Which slots are filled, their cache tags and their opacity. Runs on every page load.</summary>
    [HttpGet]
    [Produces("application/json")]
    public async Task<IActionResult> Get(long locationId, CancellationToken ct)
        => (await _sender.Send(new GetBrandingQuery(locationId), ct)).ToActionResult(this);

    /// <summary>
    /// Serves a branding image.
    /// <para>
    /// Cached hard and revalidated by ETag, like product pictures: this is on every page of the
    /// application, and re-sending it on each navigation would be the most-transferred bytes in the
    /// system. The tag changes with the bytes, so a replaced logo still appears at once.
    /// </para>
    /// </summary>
    [HttpGet("{slot}")]
    [Produces("image/png", "image/jpeg", "image/webp")]
    public async Task<IActionResult> GetImage(long locationId, BrandingSlot slot, CancellationToken ct)
    {
        var result = await _sender.Send(new GetBrandingImageQuery(locationId, slot), ct);

        if (result.IsFailure)
        {
            return ResultExtensions.Problem(result.Error, this);
        }

        var image = result.Value;

        Response.Headers.CacheControl = "private, max-age=86400, must-revalidate";

        // Content-Type is chosen from an allow-list at upload, never echoed from the request, so it
        // cannot be turned into a script type. nosniff stops a browser second-guessing that.
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(
            image.Content,
            image.ContentType,
            fileDownloadName: null,
            lastModified: null,
            entityTag: new EntityTagHeaderValue($"\"{image.ETag}\""));
    }

    /// <summary>Uploads or replaces the image in a slot.</summary>
    [HttpPut("{slot}")]
    [Produces("application/json")]
    [RequestSizeLimit(ImageContent.MaximumBytes + 4096)]
    public async Task<IActionResult> SetImage(
        long locationId,
        BrandingSlot slot,
        IFormFile file,
        CancellationToken ct,
        [FromForm] int? opacityPct = null)
    {
        if (file is null || file.Length == 0)
        {
            return ResultExtensions.Problem(ImageContent.Empty, this);
        }

        if (file.Length > ImageContent.MaximumBytes)
        {
            return ResultExtensions.Problem(ImageContent.TooLarge, this);
        }

        // Bounded by the check above, so this cannot be used to exhaust memory.
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);

        var result = await _sender.Send(
            new SetBrandingImageCommand(locationId, slot, buffer.ToArray(), file.ContentType ?? string.Empty, opacityPct),
            ct);

        return result.ToActionResult(this);
    }

    /// <summary>
    /// Changes how faint the mark is without re-uploading it. A dark logo and a pale one do not
    /// carry at the same weight, and finding the figure that works is a matter of looking at it.
    /// </summary>
    [HttpPatch("{slot}/opacity")]
    [Produces("application/json")]
    public async Task<IActionResult> SetOpacity(
        long locationId,
        BrandingSlot slot,
        [FromBody] SetOpacityRequest request,
        CancellationToken ct)
        => (await _sender.Send(new SetBrandingOpacityCommand(locationId, slot, request.OpacityPct), ct))
            .ToActionResult(this);

    [HttpDelete("{slot}")]
    public async Task<IActionResult> Remove(long locationId, BrandingSlot slot, CancellationToken ct)
    {
        var result = await _sender.Send(new RemoveBrandingImageCommand(locationId, slot), ct);

        return result.IsFailure ? ResultExtensions.Problem(result.Error, this) : NoContent();
    }
}

public sealed record SetOpacityRequest(int OpacityPct);
