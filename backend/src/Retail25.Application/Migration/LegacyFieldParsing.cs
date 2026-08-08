using System.Globalization;

namespace Retail25.Application.Migration;

/// <summary>
/// Turning legacy text into figures, and saying so when it cannot.
/// <para>
/// Every one of these returns whether it worked rather than a default, because a price that fails to
/// parse and silently becomes zero is the single worst thing a migration can do. The caller turns a
/// failure into a row-addressable finding.
/// </para>
/// </summary>
public static class LegacyFieldParsing
{
    /// <summary>
    /// A money or quantity field.
    /// <para>
    /// Tolerates what DOS-era exports actually contain: currency symbols, thousands separators,
    /// trailing and leading spaces, a row of asterisks where a value overflowed its column, and
    /// accounting-style parentheses for a negative.
    /// </para>
    /// </summary>
    public static bool TryDecimal(string? raw, out decimal value)
    {
        value = 0m;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();

        // A field of asterisks is how the old system rendered a number too wide for its column. It
        // is not a zero, and treating it as one would understate a valuation.
        if (text.All(c => c == '*'))
        {
            return false;
        }

        var negative = false;

        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            negative = true;
            text = text[1..^1];
        }

        text = new string(text.Where(c => char.IsDigit(c) || c is '.' or '-' or '+').ToArray());

        if (text.Length == 0)
        {
            return false;
        }

        if (!decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        if (negative)
        {
            value = -value;
        }

        return true;
    }

    public static bool TryInt(string? raw, out int value)
    {
        value = 0;

        if (!TryDecimal(raw, out var asDecimal))
        {
            return false;
        }

        if (asDecimal is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        value = (int)Math.Round(asDecimal, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>
    /// A date, in any of the shapes a legacy export produces.
    /// <para>
    /// The ISO form comes from the DBF reader, which normalises as it goes. The rest come from CSV
    /// exports typed or configured by whoever set the machine up twenty years ago. Day-first is
    /// tried before month-first because the legacy system is Canadian; an ambiguous date like
    /// 03/04/2010 is reported rather than guessed at.
    /// </para>
    /// </summary>
    public static bool TryDate(string? raw, out DateOnly value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();

        string[] formats =
        [
            "yyyy-MM-dd", "yyyyMMdd", "yyyy/MM/dd",
            "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy",
            "MM/dd/yyyy", "MM-dd-yyyy",
            "dd/MM/yy", "MM/dd/yy",
        ];

        return DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    /// <summary>
    /// True when a date could be read either way round and the two readings differ — so the caller
    /// can warn rather than silently import the wrong one.
    /// </summary>
    public static bool IsAmbiguousDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Trim().Split('/', '-', '.');

        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var first)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var second))
        {
            return false;
        }

        // Both readable as a month, and not the same number — 03/04 could be March or April.
        return first is >= 1 and <= 12 && second is >= 1 and <= 12 && first != second;
    }

    /// <summary>
    /// A stock code, normalised the way the catalogue stores them. Returns null for a blank, which
    /// the caller reports as a missing key rather than importing an item with no code.
    /// </summary>
    public static string? NormaliseCode(string? raw)
    {
        var trimmed = raw?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }

    /// <summary>
    /// Cuts a legacy field to the length the modern column allows, and says whether it had to.
    /// Truncating silently is how a supplier ends up in the system under half its name.
    /// </summary>
    public static string? Fit(string? raw, int maxLength, out bool truncated)
    {
        truncated = false;

        var trimmed = raw?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        truncated = true;
        return trimmed[..maxLength];
    }
}
