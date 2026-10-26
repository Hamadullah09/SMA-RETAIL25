using System.Globalization;
using System.Text;

namespace RetailPlus.Core.Rules;

/// <summary>EPC (RFID tag code) helpers: normalising, GS1 SGTIN-96 decoding and plain-text tags.</summary>
public static class Epc
{
    /// <summary>Upper-case hex without spaces or dashes; null when the text is not hexadecimal.</summary>
    public static string? Normalize(string? epc)
    {
        if (string.IsNullOrWhiteSpace(epc)) return null;
        var sb = new StringBuilder(epc.Length);
        foreach (var c in epc)
        {
            if (c is ' ' or '-' or ':') continue;
            if (!char.IsAsciiHexDigit(c)) return null;
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.Length is > 0 and <= 128 && sb.Length % 2 == 0 ? sb.ToString() : null;
    }

    private static readonly (int CompanyBits, int CompanyDigits, int ItemBits, int ItemDigits)[] Partitions =
    [
        (40, 12, 4, 1), (37, 11, 7, 2), (34, 10, 10, 3), (30, 9, 14, 4), (27, 8, 17, 5), (24, 7, 20, 6), (20, 6, 24, 7),
    ];

    /// <summary>
    /// Decodes a GS1 SGTIN-96 tag (header 0x30) to its GTIN-14, the same number as the product's barcode, so tags
    /// written by a supplier ring up without registering each one.
    /// </summary>
    public static bool TryDecodeSgtin96(string? epc, out string gtin14, out long serial)
    {
        gtin14 = "";
        serial = 0;
        var hex = Normalize(epc);
        if (hex is null || hex.Length != 24 || !hex.StartsWith("30", StringComparison.Ordinal)) return false;
        var bits = new StringBuilder(96);
        foreach (var c in hex) bits.Append(Convert.ToString(Convert.ToInt32(c.ToString(), 16), 2).PadLeft(4, '0'));
        var b = bits.ToString();
        int partition = Convert.ToInt32(b.Substring(11, 3), 2);
        if (partition > 6) return false;
        var p = Partitions[partition];
        long company = Convert.ToInt64(b.Substring(14, p.CompanyBits), 2);
        long item = Convert.ToInt64(b.Substring(14 + p.CompanyBits, p.ItemBits), 2);
        serial = Convert.ToInt64(b.Substring(58, 38), 2);
        var companyText = company.ToString(CultureInfo.InvariantCulture).PadLeft(p.CompanyDigits, '0');
        var itemText = item.ToString(CultureInfo.InvariantCulture).PadLeft(p.ItemDigits, '0');
        if (companyText.Length != p.CompanyDigits || itemText.Length != p.ItemDigits) return false;
        // The first digit of the item reference is the GTIN indicator digit.
        var body = itemText[..1] + companyText + itemText[1..];
        gtin14 = body + CheckDigit(body);
        return true;
    }

    /// <summary>GS1 mod-10 check digit of the digits given.</summary>
    public static char CheckDigit(string digits)
    {
        int sum = 0;
        for (int i = 0; i < digits.Length; i++)
        {
            int d = digits[digits.Length - 1 - i] - '0';
            sum += i % 2 == 0 ? d * 3 : d;
        }
        return (char)('0' + (10 - sum % 10) % 10);
    }

    /// <summary>The usual barcode forms of a GTIN-14: the 14 digits, EAN-13 and UPC-A (leading zeros dropped).</summary>
    public static IEnumerable<string> BarcodeForms(string gtin14)
    {
        yield return gtin14;
        if (gtin14.StartsWith('0')) yield return gtin14[1..];
        if (gtin14.StartsWith("00", StringComparison.Ordinal)) yield return gtin14[2..];
    }

    /// <summary>A tag whose EPC holds printable ASCII (some stores write the item code itself into the tag).</summary>
    public static bool TryAscii(string? epc, out string text)
    {
        text = "";
        var hex = Normalize(epc);
        if (hex is null) return false;
        var sb = new StringBuilder();
        for (int i = 0; i < hex.Length; i += 2)
        {
            byte v = byte.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (v == 0) break;
            if (v < 0x20 || v > 0x7E) return false;
            sb.Append((char)v);
        }
        text = sb.ToString().Trim();
        return text.Length > 0;
    }
}
