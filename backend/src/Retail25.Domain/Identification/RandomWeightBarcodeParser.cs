using Retail25.Domain.Common;

namespace Retail25.Domain.Identification;

/// <summary>
/// Type 2 random-weight barcode parser (guide p.98, doc 04 §5).
/// Format: 2ABBBBCDDDDE (12 digits).
/// ABBBB = 5-digit stock code, DDDD = embedded price (99.99 format).
/// Quantity = embeddedPrice / Price1 (where Price1 is the weight unit price).
/// </summary>
public sealed record RandomWeightParseResult(
    string StockCode,
    decimal EmbeddedPrice,
    bool IsValid)
{
    public static RandomWeightParseResult Failed => new(string.Empty, 0m, false);
}

public static class RandomWeightBarcodeParser
{
    public const int ExpectedLength = 12;

    /// <summary>
    /// Attempts to parse a Type 2 random-weight barcode. Returns null if the barcode is not
    /// a valid Type 2 format (not 12 digits, doesn't start with '2').
    /// </summary>
    public static RandomWeightParseResult? Parse(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return null;

        var trimmed = barcode.Trim();

        if (trimmed.Length != ExpectedLength)
            return null;

        if (!trimmed.All(char.IsDigit))
            return null;

        if (trimmed[0] != '2')
            return null;

        // Stock code = first 5 digits after the leading '2' → positions 1–5 (ABBBB)
        var stockCode = trimmed.Substring(1, 5);

        // Embedded price = positions 7–10 (DDDD), format 99.99
        var priceString = trimmed.Substring(6, 4);
        if (!decimal.TryParse(priceString, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var embeddedPrice))
            return null;

        // Normalise: DDDD is in cents, so 0123 = $1.23
        embeddedPrice = embeddedPrice / 100m;

        return new RandomWeightParseResult(stockCode, embeddedPrice, true);
    }

    /// <summary>
    /// Attempts to parse, in the try-pattern callers expect. Returns false for anything that is not
    /// a Type 2 barcode, which is the common case — most scans are ordinary UPCs.
    /// </summary>
    public static bool TryParse(string? barcode, out RandomWeightParseResult? result)
    {
        result = barcode is null ? null : Parse(barcode);
        return result is { IsValid: true };
    }
}
