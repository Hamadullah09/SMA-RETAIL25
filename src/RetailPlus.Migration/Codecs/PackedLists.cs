using System.Globalization;

namespace RetailPlus.Migration.Codecs;

/// <summary>One matrix line: up to three values and the stock count of that combination.</summary>
public sealed record MatrixRow(string? Value1, string? Value2, string? Value3, decimal OnHand);

/// <summary>
/// The MATRIX memo of an inventory record: column headings (position 1–3, null where unused) and the rows.
/// </summary>
public sealed record MatrixData(IReadOnlyList<string?> Headings, IReadOnlyList<MatrixRow> Rows)
{
    /// <summary>When the first heading is CODE, column 1 holds a scannable stock code.</summary>
    public bool HasCodeColumn => Headings.Count > 0 && string.Equals(Headings[0], "CODE", StringComparison.OrdinalIgnoreCase);

    public bool IsEmpty => Rows.Count == 0 && Headings.All(h => h is null);
}

/// <summary>One entry of the SUPLIST memo, in order of preference.</summary>
public sealed record SupplierEntry(string Name, string? StockCode, decimal Cost, decimal CaseQuantity);

/// <summary>One component of the KIT memo.</summary>
public sealed record KitEntry(string Plu, string? Description, decimal Quantity);

/// <summary>One serial number of the SERNO memo.</summary>
public sealed record SerialEntry(string SerialNumber, string? Status, DateOnly? Date);

/// <summary>
/// Readers for the delimited lists Retail Plus packs into memos and long character fields (docs/DATA_FORMATS.md,
/// section 4). They never throw on bad input: anything that can't be read is reported in <c>warnings</c> and skipped
/// or read as zero, so the import can go on and the report shows what needs a look.
/// </summary>
public static class PackedLists
{
    private static readonly string[] UsDateFormats = ["MM/dd/yyyy", "M/d/yyyy", "MM/dd/yy", "M/d/yy"];
    private static readonly string[] UkDateFormats = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy"];

    /// <summary>
    /// MATRIX: <c>h1,h2,h3,|v1,v2,v3,stock|...</c>. An unused heading is blank or <c>-</c>; <c>-,-,-,</c> is an empty
    /// matrix. Values in a column without a heading that are just <c>-</c> are read as null.
    /// </summary>
    public static MatrixData ParseMatrix(string? text, ICollection<string> warnings)
    {
        var headings = new string?[3];
        var rows = new List<MatrixRow>();
        if (string.IsNullOrWhiteSpace(text)) return new MatrixData(headings, rows);

        var segments = Clean(text).Split('|');
        var headingTokens = segments[0].Split(',');
        for (int i = 0; i < headingTokens.Length; i++)
        {
            var heading = Blank(headingTokens[i]);
            if (heading is null || heading == "-") continue;
            if (i < 3) headings[i] = heading;
            else warnings.Add($"MATRIX: heading {i + 1} ignored (at most 3 columns)");
        }

        for (int s = 1; s < segments.Length; s++)
        {
            var segment = segments[s];
            if (string.IsNullOrWhiteSpace(segment)) continue;
            var tokens = segment.Split(',');
            if (tokens.Length < 2)
            {
                warnings.Add($"MATRIX: row {s} has no stock count and was skipped");
                continue;
            }
            if (tokens.Length > 4) warnings.Add($"MATRIX: row {s} has {tokens.Length} values; the first 3 and the last were used");
            int valueCount = Math.Min(3, tokens.Length - 1);
            var values = new string?[3];
            for (int v = 0; v < valueCount; v++)
            {
                var value = Blank(tokens[v]);
                values[v] = value == "-" && headings[v] is null ? null : value;
            }
            var onHand = Number(tokens[^1], $"MATRIX: row {s} stock count", warnings);
            rows.Add(new MatrixRow(values[0], values[1], values[2], onHand));
        }
        return new MatrixData(headings, rows);
    }

    /// <summary>
    /// SUPLIST: <c>|supplier,stockCode,cost,qty|...</c>, leading bar optional. Read from the right, so a supplier name
    /// containing a comma still comes out whole.
    /// </summary>
    public static List<SupplierEntry> ParseSupplierList(string? text, ICollection<string> warnings)
    {
        var entries = new List<SupplierEntry>();
        foreach (var (segment, index) in Segments(text))
        {
            var tokens = segment.Split(',');
            if (tokens.Length < 3)
            {
                var only = Blank(segment);
                if (only is not null) entries.Add(new SupplierEntry(only, null, 0, 1));
                warnings.Add($"SUPLIST: entry {index} has {tokens.Length} values instead of 4");
                continue;
            }
            decimal caseQty, cost;
            string? code;
            int nameTokens;
            if (tokens.Length == 3)
            {
                warnings.Add($"SUPLIST: entry {index} has 3 values instead of 4; read as supplier, code, cost");
                cost = Number(tokens[2], $"SUPLIST: entry {index} cost", warnings);
                code = Blank(tokens[1]);
                caseQty = 1;
                nameTokens = 1;
            }
            else
            {
                caseQty = Number(tokens[^1], $"SUPLIST: entry {index} quantity", warnings);
                cost = Number(tokens[^2], $"SUPLIST: entry {index} cost", warnings);
                code = Blank(tokens[^3]);
                nameTokens = tokens.Length - 3;
            }
            var name = Blank(string.Join(',', tokens.Take(nameTokens)));
            if (name is null)
            {
                warnings.Add($"SUPLIST: entry {index} has no supplier name and was skipped");
                continue;
            }
            entries.Add(new SupplierEntry(name, code, cost, caseQty));
        }
        return entries;
    }

    /// <summary>KIT: <c>|plu,description,qty|...</c>. The description may contain commas.</summary>
    public static List<KitEntry> ParseKit(string? text, ICollection<string> warnings)
    {
        var entries = new List<KitEntry>();
        foreach (var (segment, index) in Segments(text))
        {
            var tokens = segment.Split(',');
            var plu = Blank(tokens[0]);
            if (plu is null)
            {
                warnings.Add($"KIT: component {index} has no stock code and was skipped");
                continue;
            }
            if (tokens.Length == 1)
            {
                warnings.Add($"KIT: component {index} has no quantity; 1 assumed");
                entries.Add(new KitEntry(plu, null, 1));
                continue;
            }
            if (!TryNumber(tokens[^1], out var quantity))
            {
                warnings.Add($"KIT: component {index} quantity is not a number; 1 assumed");
                entries.Add(new KitEntry(plu, Blank(string.Join(',', tokens.Skip(1))), 1));
                continue;
            }
            var description = tokens.Length > 2 ? Blank(string.Join(',', tokens[1..^1])) : null;
            entries.Add(new KitEntry(plu, description, quantity));
        }
        return entries;
    }

    /// <summary>SERNO: <c>|serial,status,date|...</c>. The date is MM/DD/YYYY, or DD/MM/YYYY with the UK date setting.</summary>
    public static List<SerialEntry> ParseSerials(string? text, bool ukDates, ICollection<string> warnings)
    {
        var entries = new List<SerialEntry>();
        var formats = ukDates ? UkDateFormats : UsDateFormats;
        foreach (var (segment, index) in Segments(text))
        {
            var tokens = segment.Split(',');
            var serial = Blank(tokens[0]);
            if (serial is null)
            {
                warnings.Add($"SERNO: entry {index} has no serial number and was skipped");
                continue;
            }
            DateOnly? date = null;
            int statusEnd = tokens.Length;
            if (tokens.Length >= 2 && Blank(tokens[^1]) is { } last)
            {
                if (DateOnly.TryParseExact(last, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    date = parsed;
                    statusEnd = tokens.Length - 1;
                }
                else if (tokens.Length >= 3)
                {
                    warnings.Add($"SERNO: entry {index} date is not a date");
                    statusEnd = tokens.Length - 1;
                }
            }
            var status = statusEnd > 1 ? Blank(string.Join(',', tokens[1..statusEnd])) : null;
            entries.Add(new SerialEntry(serial, status, date));
        }
        return entries;
    }

    /// <summary>
    /// WEEKLY / LASTYR: comma-separated numbers. Blank text gives an empty list. Stray NUL bytes (seen in real data)
    /// are dropped; values that still don't read are zero.
    /// </summary>
    public static List<decimal> ParseNumbers(string? text, string name, ICollection<string> warnings)
    {
        var values = new List<decimal>();
        if (text is null) return values;
        bool hadNul = text.Contains('\0', StringComparison.Ordinal);
        var cleaned = text.Replace("\0", "", StringComparison.Ordinal).Trim();
        if (cleaned.Length == 0) return values;
        if (hadNul) warnings.Add($"{name}: stray NUL bytes removed");
        var tokens = cleaned.Split(',');
        // A trailing comma leaves one empty token; that is not a value.
        int count = tokens[^1].Trim().Length == 0 ? tokens.Length - 1 : tokens.Length;
        for (int i = 0; i < count; i++)
            values.Add(Number(tokens[i], $"{name}: value {i + 1}", warnings));
        return values;
    }

    /// <summary>The text logicals of FASTKEYS.DBF and Fastkey.cfg: TRUE / FALSE (also T, Y, .T.).</summary>
    public static bool ParseTextLogical(string? text)
    {
        var t = text?.Trim().Trim('.').ToUpperInvariant();
        return t is "TRUE" or "T" or "Y" or "YES" or "1";
    }

    private static IEnumerable<(string Segment, int Index)> Segments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        int index = 0;
        foreach (var segment in Clean(text).Split('|'))
        {
            if (string.IsNullOrWhiteSpace(segment)) continue;
            yield return (segment, ++index);
        }
    }

    private static string Clean(string text) =>
        text.Replace("\0", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);

    private static string? Blank(string token)
    {
        var t = token.Trim();
        return t.Length == 0 ? null : t;
    }

    private static bool TryNumber(string token, out decimal value) =>
        decimal.TryParse(token.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture, out value);

    private static decimal Number(string token, string what, ICollection<string> warnings)
    {
        if (token.Trim().Length == 0) return 0;
        if (TryNumber(token, out var value)) return value;
        warnings.Add($"{what} is not a number; 0 used");
        return 0;
    }
}
