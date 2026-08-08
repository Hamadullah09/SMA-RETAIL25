using System.Text;

namespace Retail25.Infrastructure.LegacyData;

/// <summary>One source row: its position in the file and its fields, left as text.</summary>
public sealed record LegacyRow(int LineNumber, IReadOnlyList<string?> Values)
{
    /// <summary>A field by position, or null when the row is short.</summary>
    public string? this[int index] => index >= 0 && index < Values.Count ? Values[index] : null;
}

/// <summary>
/// Reads the legacy CSV and <c>.DTA</c> exports (doc 09 §3).
/// <para>
/// Deliberately not a general CSV parser. These files are headerless, positional, DOS-era and
/// tolerant of things a strict parser refuses — most notably the "double comma for an empty field"
/// the guide documents, which a parser that collapses empty fields would silently shift every
/// column after it. Getting that wrong moves a price into a quantity, and nothing downstream would
/// notice.
/// </para>
/// </summary>
public static class LegacyCsvReader
{
    /// <summary>
    /// Reads a whole file. Blank lines are skipped; every other line becomes a row, however
    /// malformed, because the validation step is where a bad row gets reported with its number.
    /// </summary>
    public static IReadOnlyList<LegacyRow> Read(string content, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(content);

        var rows = new List<LegacyRow>();
        var lineNumber = 0;

        foreach (var line in SplitLines(content))
        {
            lineNumber++;

            if (line.Trim().Length == 0)
            {
                continue;
            }

            rows.Add(new LegacyRow(lineNumber, SplitLine(line, delimiter)));
        }

        return rows;
    }

    public static IReadOnlyList<LegacyRow> Read(Stream stream, Encoding? encoding = null, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        return Read(reader.ReadToEnd(), delimiter);
    }

    /// <summary>
    /// Splits one line into positional fields.
    /// <para>
    /// Two adjacent delimiters produce an empty field rather than being collapsed — that is the
    /// legacy convention and the single most important thing this reader does. Quotes are honoured
    /// because a description with a comma in it turns up in every real export, and a doubled quote
    /// inside a quoted field is one quote.
    /// </para>
    /// </summary>
    public static List<string?> SplitLine(string line, char delimiter)
    {
        var fields = new List<string?>();
        var current = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    current.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == delimiter)
            {
                fields.Add(Blank(current.ToString()));
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        fields.Add(Blank(current.ToString()));

        return fields;
    }

    /// <summary>
    /// Splits on CR, LF or CRLF. A file written by a DOS tool and copied through three systems can
    /// carry any of the three, sometimes in the same file.
    /// </summary>
    private static IEnumerable<string> SplitLines(string content)
    {
        var start = 0;

        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];

            if (character is not ('\r' or '\n'))
            {
                continue;
            }

            yield return content[start..index];

            if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
            {
                index++;
            }

            start = index + 1;
        }

        if (start < content.Length)
        {
            yield return content[start..];
        }
    }

    /// <summary>
    /// Trims and turns an empty field into null. Trailing spaces are everywhere in fixed-width-era
    /// exports and "  " is not a supplier's name.
    /// </summary>
    private static string? Blank(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
