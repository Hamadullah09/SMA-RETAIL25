using System.Globalization;

namespace Retail25.Domain.Identification;

/// <summary>
/// What a Type 2 barcode carried. <see cref="StockCode"/> is the five digits the scale printed,
/// which is the code stored on the product; <see cref="EmbeddedPrice"/> is the money the scale
/// worked out from the weight.
/// </summary>
public sealed record RandomWeightBarcode(string StockCode, decimal EmbeddedPrice, bool CheckDigitValid);

/// <summary>
/// Type 2 random-weight barcodes, to the letter of the legacy contract (guide p.98, doc 04 §5).
/// <para>
/// Layout <c>2ABBBBCDDDDE</c> over twelve digits: a leading <c>2</c> number-system character, a
/// package code and item identifier that together form the five-digit stock code, a price check
/// digit the legacy system ignores, the four-digit embedded price in <c>99.99</c> form, and a
/// trailing modulo check digit.
/// </para>
/// <para>
/// The check digit is computed and reported but never used to reject a scan: scales in the field do
/// print barcodes that fail it, and refusing to sell an item over a check digit is not behaviour any
/// store would accept at a queue.
/// </para>
/// </summary>
public static class RandomWeightBarcodeParser
{
    public const int ExpectedLength = 12;
    public const char NumberSystemCharacter = '2';

    private const int StockCodeStart = 1;
    private const int StockCodeLength = 5;
    private const int PriceStart = 7;
    private const int PriceLength = 4;

    /// <summary>
    /// Returns null when the candidate is not a Type 2 barcode at all — wrong length, non-numeric, or
    /// a different number-system character. Callers then fall through to the ordinary identifier path.
    /// </summary>
    public static RandomWeightBarcode? Parse(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return null;
        }

        var trimmed = barcode.Trim();

        if (trimmed.Length != ExpectedLength || trimmed[0] != NumberSystemCharacter)
        {
            return null;
        }

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiDigit(character))
            {
                return null;
            }
        }

        var stockCode = trimmed.Substring(StockCodeStart, StockCodeLength);

        if (!int.TryParse(
                trimmed.AsSpan(PriceStart, PriceLength),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var cents))
        {
            return null;
        }

        return new RandomWeightBarcode(stockCode, cents / 100m, IsCheckDigitValid(trimmed));
    }

    /// <summary>Standard UPC-A modulo 10: odd positions weigh three, even positions weigh one.</summary>
    private static bool IsCheckDigitValid(string barcode)
    {
        var sum = 0;
        for (var i = 0; i < ExpectedLength - 1; i++)
        {
            var digit = barcode[i] - '0';
            sum += i % 2 == 0 ? digit * 3 : digit;
        }

        var expected = (10 - (sum % 10)) % 10;
        return expected == barcode[ExpectedLength - 1] - '0';
    }
}
