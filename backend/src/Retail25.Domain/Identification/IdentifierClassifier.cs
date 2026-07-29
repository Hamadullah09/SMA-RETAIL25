using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.Identification;

/// <summary>What a string typed or scanned at the till turned out to be.</summary>
public enum IdentifierKind
{
    /// <summary>An RFID Electronic Product Code, 24–96 hex characters.</summary>
    Epc = 0,

    /// <summary>A Type 2 scale barcode carrying an embedded price (guide p.98).</summary>
    RandomWeight = 1,

    /// <summary>Anything else: a stock code, a UPC, a Code 39 scan, a serial or a variant code.</summary>
    Code = 2,

    Empty = 3,
}

/// <summary>
/// The classification, with whatever the format gave up for free.
/// </summary>
/// <param name="Value">The normalised identifier — upper-cased and trimmed.</param>
/// <param name="StockCode">Set for random-weight barcodes: the five digits that identify the product.</param>
/// <param name="EmbeddedPrice">Set for random-weight barcodes: the money the scale encoded.</param>
public sealed record IdentifierClassification(
    IdentifierKind Kind,
    string Value,
    string? StockCode = null,
    decimal? EmbeddedPrice = null,
    bool CheckDigitValid = true);

/// <summary>
/// Decides what a scanned or typed identifier is, before any database is touched (doc 05, the
/// universal <c>AddCartLineByIdentifier</c> entry point).
/// <para>
/// Order matters and is deliberate. EPCs are tried first because they are unambiguous — 24 hex
/// characters minimum, so they can never collide with a 12-digit UPC. Random-weight barcodes come
/// next but <b>only</b> when the station is configured for them, exactly as the legacy system
/// required (guide p.98): a store without scales must be able to sell a product whose UPC happens to
/// start with a 2.
/// </para>
/// </summary>
public static class IdentifierClassifier
{
    public static IdentifierClassification Classify(string? identifier, bool scanRandomWeightBarcodes)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return new IdentifierClassification(IdentifierKind.Empty, string.Empty);
        }

        var normalised = identifier.Trim().ToUpperInvariant();

        if (LooksLikeEpc(normalised))
        {
            return new IdentifierClassification(IdentifierKind.Epc, normalised);
        }

        if (scanRandomWeightBarcodes && RandomWeightBarcodeParser.Parse(normalised) is { } weighed)
        {
            return new IdentifierClassification(
                IdentifierKind.RandomWeight,
                normalised,
                weighed.StockCode,
                weighed.EmbeddedPrice,
                weighed.CheckDigitValid);
        }

        return new IdentifierClassification(IdentifierKind.Code, normalised);
    }

    private static bool LooksLikeEpc(string candidate)
    {
        if (candidate.Length is < Epc.MinLength or > Epc.MaxLength)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
