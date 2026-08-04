using Retail25.Domain.Common;

namespace Retail25.Domain.Catalog;

/// <summary>
/// A product's picture, for the till's product grid.
/// <para>
/// A table of its own rather than a column on <see cref="Product"/>. The catalogue is queried
/// constantly — every browse, every report, every price lookup — and a megabyte of JPEG per row
/// would ride along on all of it. Here the bytes are fetched only when something actually renders
/// them, and <see cref="Product.HasImage"/> carries the one bit the grid needs to lay itself out.
/// </para>
/// <para>
/// Stored in the database rather than on disk deliberately. A shop's till, its back office and its
/// second register are separate processes that may not share a filesystem, and a picture that only
/// appears on the machine it was uploaded from is a support call. It also means the backup already
/// covers them.
/// </para>
/// </summary>
public sealed class ProductImage : Entity, IAuditable
{
    /// <summary>Anything larger is a photograph nobody needed at 96 pixels on a till.</summary>
    public const int MaximumBytes = 2 * 1024 * 1024;

    public static readonly Error TooLarge = new(
        "image.too_large",
        $"An image may be at most {MaximumBytes / 1024 / 1024} MB.");

    public static readonly Error UnsupportedType = new(
        "image.unsupported_type",
        "Images must be PNG, JPEG or WebP.");

    /// <summary>
    /// What a browser will actually render, and nothing else.
    /// <para>
    /// An allow-list rather than a block-list: this content type is echoed back on the response, and
    /// letting a caller choose it freely is how an "image" upload becomes a stored cross-site
    /// scripting vector.
    /// </para>
    /// </summary>
    private static readonly string[] Allowed = ["image/png", "image/jpeg", "image/webp"];

    private ProductImage()
    {
    }

    public long ProductId { get; private set; }

    public byte[] Content { get; private set; } = [];

    public string ContentType { get; private set; } = "image/png";

    /// <summary>Changes whenever the bytes do, so a browser cache cannot serve the old picture.</summary>
    public string ETag { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static Result<ProductImage> Create(long productId, byte[] content, string contentType)
    {
        var validated = Validate(content, contentType);
        if (validated.IsFailure)
        {
            return Result.Failure<ProductImage>(validated.Error);
        }

        var image = new ProductImage { ProductId = productId };
        image.Replace(content, contentType);

        return Result.Success(image);
    }

    public Result Replace(byte[] content, string contentType)
    {
        var validated = Validate(content, contentType);
        if (validated.IsFailure)
        {
            return validated;
        }

        Content = content;
        ContentType = contentType.ToLowerInvariant();
        ETag = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content))[..16];

        return Result.Success();
    }

    private static Result Validate(byte[] content, string contentType)
    {
        if (content is null || content.Length == 0)
        {
            return Result.Failure(new Error("image.empty", "The image is empty."));
        }

        if (content.Length > MaximumBytes)
        {
            return Result.Failure(TooLarge);
        }

        if (!Allowed.Contains(contentType?.ToLowerInvariant(), StringComparer.Ordinal))
        {
            return Result.Failure(UnsupportedType);
        }

        // The declared type is checked against the bytes. A caller can claim anything in a header;
        // the magic number is the only part of an upload that is not simply taken on trust.
        return LooksLike(content, contentType!.ToLowerInvariant())
            ? Result.Success()
            : Result.Failure(UnsupportedType.With("reason", "the file's contents do not match its declared type"));
    }

    private static bool LooksLike(byte[] content, string contentType) => contentType switch
    {
        // \x89 P N G \r \n \x1A \n
        "image/png" => content.Length > 8
            && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47,

        // JPEG always opens with the Start-of-Image marker.
        "image/jpeg" => content.Length > 3 && content[0] == 0xFF && content[1] == 0xD8,

        // RIFF....WEBP
        "image/webp" => content.Length > 12
            && content[0] == 0x52 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x46
            && content[8] == 0x57 && content[9] == 0x45 && content[10] == 0x42 && content[11] == 0x50,

        _ => false,
    };
}
