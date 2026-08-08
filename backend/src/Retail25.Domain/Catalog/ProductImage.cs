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
    /// <inheritdoc cref="ImageContent.MaximumBytes"/>
    public const int MaximumBytes = ImageContent.MaximumBytes;

    public static readonly Error TooLarge = ImageContent.TooLarge;

    public static readonly Error UnsupportedType = ImageContent.UnsupportedType;

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
        var validated = ImageContent.Validate(content, contentType);
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
}
