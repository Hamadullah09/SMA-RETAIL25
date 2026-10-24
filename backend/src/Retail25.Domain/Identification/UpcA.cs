namespace Retail25.Domain.Identification;

/// <summary>
/// UPC-A: the twelve-digit retail barcode, and the arithmetic that goes with it.
/// <para>
/// A shop asked for its barcodes to be UPC-A. The labels printed Code 39 of the stock code, which is
/// the legacy default (guide App. L) and reads on any scanner, but it is not what retail scanners,
/// suppliers or anyone else expect a product barcode to be.
/// </para>
/// <para>
/// The numbers themselves come from GS1, not from this system: the first digit is the "number system",
/// and two values matter here. <c>2</c> is random-weight, the scale labels the till already decodes
/// (guide p.98), so an in-store number must never start with it. <c>4</c> is set aside for a retailer's
/// own use without restriction, which is exactly what a shop numbering its own stock is — so
/// <see cref="InStore"/> issues numbers there. Neither is a shop preference; both are the standard.
/// </para>
/// </summary>
public static class UpcA
{
    public const int Length = 12;

    /// <summary>GS1's number system for a retailer's own numbering.</summary>
    public const char InStoreNumberSystem = '4';

    /// <summary>
    /// The digit after the number system, separating what an in-store number was issued for. Products
    /// and matrix variants draw on different id sequences, and without this a product and a variant
    /// with the same id would be handed the same barcode.
    /// </summary>
    public enum InStoreKind
    {
        Product = 0,
        Variant = 1,
    }

    /// <summary>The largest id an in-store number can carry: nine digits, after the two prefix digits.</summary>
    public const long MaxInStoreId = 999_999_999;

    /// <summary>Twelve digits whose last is the right check digit for the first eleven.</summary>
    public static bool IsValid(string? candidate)
    {
        if (candidate is null || candidate.Length != Length || !AllDigits(candidate))
        {
            return false;
        }

        return candidate[Length - 1] - '0' == CheckDigit(candidate.AsSpan(0, Length - 1));
    }

    /// <summary>
    /// The check digit for eleven digits: three times the digits in odd positions, plus the digits in
    /// even positions, taken up to the next multiple of ten.
    /// </summary>
    public static int CheckDigit(ReadOnlySpan<char> elevenDigits)
    {
        if (elevenDigits.Length != Length - 1)
        {
            throw new ArgumentException("A UPC-A check digit is computed over exactly eleven digits.", nameof(elevenDigits));
        }

        var sum = 0;

        for (var index = 0; index < elevenDigits.Length; index++)
        {
            var digit = elevenDigits[index] - '0';

            if (digit is < 0 or > 9)
            {
                throw new ArgumentException("A UPC-A is digits only.", nameof(elevenDigits));
            }

            // Positions count from one, so index 0 is the first odd position.
            sum += index % 2 == 0 ? digit * 3 : digit;
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>
    /// A UPC-A in the retailer's own range for something with no barcode of its own:
    /// <c>4</c>, the kind, the id in nine digits, and the check digit.
    /// <para>
    /// Built from the record's id rather than from a counter, so it needs no stored state, is the
    /// same however many times it is asked for, and cannot be issued twice within a kind.
    /// </para>
    /// </summary>
    public static string InStore(InStoreKind kind, long id)
    {
        if (id is < 1 or > MaxInStoreId)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "An in-store UPC-A carries an id of one to nine digits.");
        }

        var body = $"{InStoreNumberSystem}{(int)kind}{id:D9}";

        return body + CheckDigit(body).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The same barcode written the other way, for matching a scan against what was stored.
    /// <para>
    /// A UPC-A is an EAN-13 with a leading zero, and scanners are commonly set to send one or the
    /// other. A product stored with its twelve digits must still ring when a scanner sends thirteen,
    /// and the reverse — otherwise the same can of beans rings at one till and not at the next.
    /// Returns null when the code is neither shape.
    /// </para>
    /// </summary>
    public static string? AlternateForm(string? scanned)
    {
        if (scanned is null || !AllDigits(scanned))
        {
            return null;
        }

        if (scanned.Length == Length && IsValid(scanned))
        {
            return "0" + scanned;
        }

        if (scanned.Length == Length + 1 && scanned[0] == '0' && IsValid(scanned[1..]))
        {
            return scanned[1..];
        }

        return null;
    }

    private static bool AllDigits(string value)
    {
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return value.Length > 0;
    }
}
