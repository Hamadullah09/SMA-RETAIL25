using System.Globalization;

namespace Retail25.TerminalAgent.Peripherals;

/// <summary>
/// Turns a configured escape string into bytes.
/// <para>
/// The legacy system stored these as comma-separated decimal ASCII — <c>27,112,0,50,250</c> is the
/// Epson drawer kick — and staff who have been maintaining these stores for years read and write them
/// in exactly that form. Keeping the same notation means a printer profile can be copied straight out
/// of the old system's setup screen, and it keeps the values administrable instead of compiled in.
/// </para>
/// </summary>
public static class EscapeSequence
{
    /// <summary>
    /// Parses <c>27,112,0,50,250</c> into bytes. Whitespace is tolerated; anything that is not a
    /// byte value is skipped rather than throwing, because a malformed cutter code should mean
    /// "the paper does not cut", not "the receipt does not print".
    /// </summary>
    public static byte[] Parse(string? sequence)
    {
        if (string.IsNullOrWhiteSpace(sequence))
        {
            return [];
        }

        var parts = sequence.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bytes = new List<byte>(parts.Length);

        foreach (var part in parts)
        {
            if (byte.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                bytes.Add(value);
            }
        }

        return [.. bytes];
    }

    /// <summary>Renders bytes back to the stored notation, for logs and the settings UI.</summary>
    public static string Format(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var parts = new string[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            parts[i] = bytes[i].ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(',', parts);
    }
}
