using System.Text;

namespace RetailPlus.Integrations;

/// <summary>A small RFC 4180 CSV reader and writer (quoted fields, embedded commas, quotes and line breaks).</summary>
public static class Csv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add([.. row]); row.Clear();
                    break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add([.. row]); }
        return rows.Where(r => r.Length > 1 || (r.Length == 1 && r[0].Length > 0)).ToList();
    }

    public static string Escape(string? s)
    {
        s ??= "";
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;
    }

    public static string Line(IEnumerable<string?> fields) => string.Join(",", fields.Select(Escape));
}

/// <summary>A CSV row read by header name (case-insensitive).</summary>
public sealed class CsvRecord(IReadOnlyDictionary<string, int> columns, string[] values)
{
    public string? this[string name] => columns.TryGetValue(name, out var i) && i < values.Length ? values[i].Trim() : null;

    public bool Has(string name) => columns.ContainsKey(name);

    public static IEnumerable<CsvRecord> Read(string text)
    {
        var rows = Csv.Parse(text);
        if (rows.Count == 0) yield break;
        var columns = rows[0].Select((h, i) => (h.Trim(), i)).GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Skip(1)) yield return new CsvRecord(columns, row);
    }
}
