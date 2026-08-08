using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;

namespace Retail25.Application.Settings;

/// <summary>What the chrome needs to draw a slot: whether there is one, and how to fetch it.</summary>
public sealed record BrandingSlotDto(BrandingSlot Slot, bool Present, string? ETag, int OpacityPct);

public sealed record BrandingDto(long LocationId, string BusinessName, IReadOnlyList<BrandingSlotDto> Slots);

/// <summary>The bytes, and what they are, for serving straight back to a browser.</summary>
public sealed record BrandingImageDto(byte[] Content, string ContentType, string ETag);

/// <summary>
/// What every signed-in page needs to render the watermark and the corner logo.
/// <para>
/// Deliberately carries no <c>[RequiresPermission]</c>. A cashier on the lowest access level holds
/// <c>pos.sell</c> and nothing else, and gating the chrome behind <c>settings.read</c> would blank
/// the branding for exactly the people who look at it all day. A logo is not a secret; it is on
/// the shop's sign.
/// </para>
/// </summary>
public sealed record GetBrandingQuery(long LocationId) : IRequest<Result<BrandingDto>>;

/// <summary>The image itself. Same reasoning as <see cref="GetBrandingQuery"/> on permissions.</summary>
public sealed record GetBrandingImageQuery(long LocationId, BrandingSlot Slot) : IRequest<Result<BrandingImageDto>>;

/// <summary>
/// Uploads or replaces a branding image. One per slot, so this is an upsert — a second upload is
/// somebody correcting the first, not building a gallery.
/// </summary>
[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record SetBrandingImageCommand(
    long LocationId,
    BrandingSlot Slot,
    byte[] Content,
    string ContentType,
    int? OpacityPct = null) : IRequest<Result<BrandingSlotDto>>;

[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record SetBrandingOpacityCommand(long LocationId, BrandingSlot Slot, int OpacityPct)
    : IRequest<Result<BrandingSlotDto>>;

[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record RemoveBrandingImageCommand(long LocationId, BrandingSlot Slot) : IRequest<Result>;

public sealed class BrandingHandlers :
    IRequestHandler<GetBrandingQuery, Result<BrandingDto>>,
    IRequestHandler<GetBrandingImageQuery, Result<BrandingImageDto>>,
    IRequestHandler<SetBrandingImageCommand, Result<BrandingSlotDto>>,
    IRequestHandler<SetBrandingOpacityCommand, Result<BrandingSlotDto>>,
    IRequestHandler<RemoveBrandingImageCommand, Result>
{
    public static readonly Error LocationNotFound = new("location.not_found", "No such location.");
    public static readonly Error NoImage = new("branding.not_found", "Nothing has been uploaded for that slot.");

    private static readonly BrandingSlot[] AllSlots = Enum.GetValues<BrandingSlot>();

    private readonly IApplicationDbContext _db;

    public BrandingHandlers(IApplicationDbContext db) => _db = db;

    public async Task<Result<BrandingDto>> Handle(GetBrandingQuery request, CancellationToken ct)
    {
        var location = await _db.Locations.AsNoTracking()
            .Where(l => l.Id == request.LocationId && !l.IsDeleted)
            .Select(l => l.Name)
            .FirstOrDefaultAsync(ct);

        if (location is null)
        {
            return Result.Failure<BrandingDto>(LocationNotFound.With("locationId", request.LocationId));
        }

        // The bytes are deliberately not selected. This runs on every page load of every till, and
        // pulling two megabytes through it to answer "is there a logo" would be the most expensive
        // query in the application.
        var present = await _db.BrandingAssets.AsNoTracking()
            .Where(a => a.LocationId == request.LocationId)
            .Select(a => new { a.Slot, a.ETag, a.OpacityPct })
            .ToListAsync(ct);

        var slots = AllSlots.Select(slot =>
        {
            var found = present.FirstOrDefault(a => a.Slot == slot);

            return found is null
                ? new BrandingSlotDto(slot, false, null, DefaultOpacity(slot))
                : new BrandingSlotDto(slot, true, found.ETag, found.OpacityPct);
        }).ToList();

        return Result.Success(new BrandingDto(request.LocationId, location, slots));
    }

    public async Task<Result<BrandingImageDto>> Handle(GetBrandingImageQuery request, CancellationToken ct)
    {
        var image = await _db.BrandingAssets.AsNoTracking()
            .Where(a => a.LocationId == request.LocationId && a.Slot == request.Slot)
            .Select(a => new BrandingImageDto(a.Content, a.ContentType, a.ETag))
            .FirstOrDefaultAsync(ct);

        return image is null
            ? Result.Failure<BrandingImageDto>(NoImage.With("slot", request.Slot.ToString()))
            : Result.Success(image);
    }

    public async Task<Result<BrandingSlotDto>> Handle(SetBrandingImageCommand request, CancellationToken ct)
    {
        var exists = await _db.Locations.AsNoTracking()
            .AnyAsync(l => l.Id == request.LocationId && !l.IsDeleted, ct);

        if (!exists)
        {
            return Result.Failure<BrandingSlotDto>(LocationNotFound.With("locationId", request.LocationId));
        }

        var asset = await _db.BrandingAssets
            .FirstOrDefaultAsync(a => a.LocationId == request.LocationId && a.Slot == request.Slot, ct);

        if (asset is null)
        {
            var created = BrandingAsset.Create(request.LocationId, request.Slot, request.Content, request.ContentType);
            if (created.IsFailure)
            {
                return Result.Failure<BrandingSlotDto>(created.Error);
            }

            asset = created.Value;
            _db.BrandingAssets.Add(asset);
        }
        else
        {
            var replaced = asset.Replace(request.Content, request.ContentType);
            if (replaced.IsFailure)
            {
                return Result.Failure<BrandingSlotDto>(replaced.Error);
            }
        }

        if (request.OpacityPct is { } opacity)
        {
            var set = asset.SetOpacity(opacity);
            if (set.IsFailure)
            {
                return Result.Failure<BrandingSlotDto>(set.Error);
            }
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(new BrandingSlotDto(asset.Slot, true, asset.ETag, asset.OpacityPct));
    }

    public async Task<Result<BrandingSlotDto>> Handle(SetBrandingOpacityCommand request, CancellationToken ct)
    {
        var asset = await _db.BrandingAssets
            .FirstOrDefaultAsync(a => a.LocationId == request.LocationId && a.Slot == request.Slot, ct);

        if (asset is null)
        {
            return Result.Failure<BrandingSlotDto>(NoImage.With("slot", request.Slot.ToString()));
        }

        var set = asset.SetOpacity(request.OpacityPct);
        if (set.IsFailure)
        {
            return Result.Failure<BrandingSlotDto>(set.Error);
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(new BrandingSlotDto(asset.Slot, true, asset.ETag, asset.OpacityPct));
    }

    public async Task<Result> Handle(RemoveBrandingImageCommand request, CancellationToken ct)
    {
        var asset = await _db.BrandingAssets
            .FirstOrDefaultAsync(a => a.LocationId == request.LocationId && a.Slot == request.Slot, ct);

        if (asset is not null)
        {
            _db.BrandingAssets.Remove(asset);
            await _db.SaveChangesAsync(ct);
        }

        return Result.Success();
    }

    private static int DefaultOpacity(BrandingSlot slot)
        => slot == BrandingSlot.Watermark
            ? BrandingAsset.DefaultWatermarkOpacityPct
            : BrandingAsset.DefaultLogoOpacityPct;
}
