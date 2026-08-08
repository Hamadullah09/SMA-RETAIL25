namespace Retail25.Domain.Common;

/// <summary>
/// What counts as an image anywhere in this system, and the proof that a given upload is one.
/// <para>
/// Shared rather than repeated. Product pictures and branding assets take uploads from the same
/// kind of caller and serve them back from the same kind of endpoint, and a magic-number check that
/// exists in two places is a magic-number check that will eventually only be tightened in one.
/// </para>
/// </summary>
public static class ImageContent
{
    /// <summary>Anything larger is a photograph nobody needed at 96 pixels on a till.</summary>
    public const int MaximumBytes = 2 * 1024 * 1024;

    public static readonly Error TooLarge = new(
        "image.too_large",
        $"An image may be at most {MaximumBytes / 1024 / 1024} MB.");

    public static readonly Error UnsupportedType = new(
        "image.unsupported_type",
        "Images must be PNG, JPEG or WebP.");

    public static readonly Error Empty = new("image.empty", "The image is empty.");

    /// <summary>
    /// What a browser will actually render, and nothing else.
    /// <para>
    /// An allow-list rather than a block-list: this content type is echoed back on the response, and
    /// letting a caller choose it freely is how an "image" upload becomes a stored cross-site
    /// scripting vector. SVG is absent for that reason and not by oversight — it is a document that
    /// can carry script, and a logo is not worth an XSS hole on every page of the application.
    /// </para>
    /// </summary>
    private static readonly string[] Allowed = ["image/png", "image/jpeg", "image/webp"];

    public static Result Validate(byte[]? content, string? contentType)
    {
        if (content is null || content.Length == 0)
        {
            return Result.Failure(Empty);
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

    /// <summary>Changes whenever the bytes do, so a browser cache cannot serve the old picture.</summary>
    public static string ETagFor(byte[] content)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content))[..16];

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
