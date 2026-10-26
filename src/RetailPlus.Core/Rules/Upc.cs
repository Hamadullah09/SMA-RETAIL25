using System.Globalization;

namespace RetailPlus.Core.Rules;

/// <summary>
/// Product barcodes: UPC-A (12 digits), EAN-13, EAN-8 and GTIN-14, each ending in a check digit. An item without a
/// maker's barcode gets an in-store UPC-A: number system 4 (kept for in-store use, so it never clashes with a maker's
/// code; 2 is for weighed goods), ten digits of the shop's own numbering, and the check digit, e.g. 4 0000001001 7.
/// </summary>
public static class Upc
{
    /// <summary>8, 12, 13 or 14 digits whose last digit is the right check digit.</summary>
    public static bool IsValid(string? code) =>
        code is { Length: 8 or 12 or 13 or 14 } && code.All(char.IsAsciiDigit) && Epc.CheckDigit(code[..^1]) == code[^1];

    /// <summary>The in-store UPC-A with the given number (1 to 9,999,999,999).</summary>
    public static string InStore(long number)
    {
        if (number is < 1 or > 9_999_999_999) throw new ArgumentOutOfRangeException(nameof(number));
        var body = "4" + number.ToString("D10", CultureInfo.InvariantCulture);
        return body + Epc.CheckDigit(body);
    }

    /// <summary>The shop's number inside an in-store UPC-A, or null when the code is not one.</summary>
    public static long? InStoreNumber(string? code) =>
        code is { Length: 12 } && code[0] == '4' && IsValid(code) ? long.Parse(code[1..11], CultureInfo.InvariantCulture) : null;

    /// <summary>
    /// The forms a scanned code may be stored under: as scanned, and for a UPC-A also as EAN-13 (a leading 0), since
    /// scanners send either.
    /// </summary>
    public static IReadOnlyList<string> Forms(string code)
    {
        var forms = new List<string> { code };
        if (code.All(char.IsAsciiDigit))
        {
            if (code.Length == 12) forms.Add("0" + code);
            if (code.Length == 13 && code[0] == '0') forms.Add(code[1..]);
        }
        return forms;
    }
}
