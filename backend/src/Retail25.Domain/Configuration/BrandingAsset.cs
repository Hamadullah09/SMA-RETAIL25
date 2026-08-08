using Retail25.Domain.Common;

namespace Retail25.Domain.Configuration;

/// <summary>Where a branding image appears. One image per slot per location.</summary>
public enum BrandingSlot
{
    /// <summary>
    /// The large mark behind the working area, centred and faint. The legacy screens carried one and
    /// staff read it as "this is the system, and it is running".
    /// </summary>
    Watermark = 0,

    /// <summary>
    /// The shop's own mark, in the corner of the chrome and on printed documents. This is the one
    /// that makes an installation belong to the customer rather than to the vendor.
    /// </summary>
    CompanyLogo = 1,
}

/// <summary>
/// A branding image, held per location so one deployment can serve several shops.
/// <para>
/// In the database rather than on disk, for the same reasons as <c>ProductImage</c>: a back office,
/// a till and a second register are separate processes that may not share a filesystem, and a logo
/// that only appears on the machine it was uploaded from is a support call. It also means the
/// backup already covers it.
/// </para>
/// <para>
/// Uploaded rather than configured. White-labelling that requires a rebuild is white-labelling that
/// happens once, badly — the point of holding it here is that a reseller can stand up a new customer
/// without a release.
/// </para>
/// </summary>
public sealed class BrandingAsset : Entity, IAuditable
{
    public static readonly Error OpacityOutOfRange = new(
        "branding.opacity_out_of_range",
        "Opacity must be between 0 and 100 per cent.");

    /// <summary>
    /// Faint enough to read text through, present enough to see. The legacy screens sat around here
    /// and it is the figure the specification asks for; it is a default rather than a constant
    /// because a dark logo and a pale one do not carry at the same weight.
    /// </summary>
    public const int DefaultWatermarkOpacityPct = 20;

    /// <summary>A corner mark is meant to be read, so it is opaque unless somebody says otherwise.</summary>
    public const int DefaultLogoOpacityPct = 100;

    private BrandingAsset()
    {
    }

    public long LocationId { get; private set; }

    public BrandingSlot Slot { get; private set; }

    public byte[] Content { get; private set; } = [];

    public string ContentType { get; private set; } = "image/png";

    /// <summary>Changes whenever the bytes do, so a browser cache cannot serve the old logo.</summary>
    public string ETag { get; private set; } = string.Empty;

    public int OpacityPct { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static Result<BrandingAsset> Create(long locationId, BrandingSlot slot, byte[] content, string contentType)
    {
        var validated = ImageContent.Validate(content, contentType);
        if (validated.IsFailure)
        {
            return Result.Failure<BrandingAsset>(validated.Error);
        }

        var asset = new BrandingAsset
        {
            LocationId = locationId,
            Slot = slot,
            OpacityPct = slot == BrandingSlot.Watermark ? DefaultWatermarkOpacityPct : DefaultLogoOpacityPct,
        };

        asset.Replace(content, contentType);

        return Result.Success(asset);
    }

    public Result Replace(byte[] content, string contentType)
    {
        var validated = ImageContent.Validate(content, contentType);
        if (validated.IsFailure)
        {
            return validated;
        }

        Content = content;
        ContentType = contentType.ToLowerInvariant();
        ETag = ImageContent.ETagFor(content);

        return Result.Success();
    }

    public Result SetOpacity(int opacityPct)
    {
        if (opacityPct is < 0 or > 100)
        {
            return Result.Failure(OpacityOutOfRange.With("value", opacityPct));
        }

        OpacityPct = opacityPct;
        return Result.Success();
    }
}
