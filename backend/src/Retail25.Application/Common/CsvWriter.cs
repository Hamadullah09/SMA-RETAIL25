using System.Globalization;
using System.Text;

namespace Retail25.Application.Common;

/// <summary>
/// Builds the CSV every report exports, standing in for the legacy "Open In MS-Excel" button
/// (guide p.101).
/// <para>
/// Centralised because the escaping rule is the part that quietly corrupts a file: a product named
/// "Widget, large" or a customer called O'Brien &quot;Bob&quot; splits into extra columns if it is
/// written raw, and the damage only shows up in whatever the bookkeeper opens it with. Numbers are
/// written invariant for the same reason — a decimal comma turns one column into two.
/// </para>
/// </summary>
public sealed class CsvWriter
{
    private readonly StringBuilder _builder = new();

    /// <summary>Writes the header row. Call once, before any <see cref="Row"/>.</summary>
    public CsvWriter Header(params string[] columns)
    {
        AppendRow(columns.Select(c => (object?)c));
        return this;
    }

    public CsvWriter Row(params object?[] values)
    {
        AppendRow(values);
        return this;
    }

    public override string ToString() => _builder.ToString();

    private void AppendRow(IEnumerable<object?> values)
    {
        var first = true;

        foreach (var value in values)
        {
            if (!first)
            {
                _builder.Append(',');
            }

            _builder.Append(Format(value));
            first = false;
        }

        _builder.AppendLine();
    }

    /// <summary>
    /// One place that decides how each type reaches the file. Dates go out round-trippable rather
    /// than in the server's locale, so a file exported in one region opens correctly in another.
    /// </summary>
    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        string text => Escape(text),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset moment => moment.ToString("O", CultureInfo.InvariantCulture),
        DateTime moment => moment.ToString("O", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        _ => Escape(value.ToString()),
    };

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // A newline inside a cell is as damaging as a comma — quote for it too, or the row splits.
        var needsQuoting = value.Contains(',', StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal);

        return needsQuoting
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
