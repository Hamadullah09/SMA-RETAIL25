using System.Globalization;
using System.Net;
using System.Text;

namespace RetailPlus.Reports;

public enum ColumnKind
{
    Text,
    Count,
    Quantity,
    Money,
    Percent,
    Date,
}

public sealed record ReportColumn(string Header, ColumnKind Kind = ColumnKind.Text, bool Total = false)
{
    public bool RightAligned => Kind is not ColumnKind.Text and not ColumnKind.Date;

    public string Format(object? value, CultureInfo culture) => value switch
    {
        null => "",
        decimal d when Kind == ColumnKind.Money => d.ToString("N2", culture),
        decimal d when Kind == ColumnKind.Quantity => d.ToString("0.###", culture),
        decimal d when Kind == ColumnKind.Percent => d.ToString("0.0", culture) + "%",
        decimal d => d.ToString("0.##", culture),
        int i => i.ToString(culture),
        long l => l.ToString(culture),
        DateOnly date => date.ToString("d", culture),
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("d", culture) : dt.ToString("g", culture),
        bool b => b ? "Yes" : "",
        _ => Convert.ToString(value, culture) ?? "",
    };
}

/// <summary>A tabular report: what every Reports screen produces, printed, previewed or exported from here.</summary>
public sealed class ReportTable
{
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public List<ReportColumn> Columns { get; init; } = [];
    public List<object?[]> Rows { get; init; } = [];
    public DateTime GeneratedAt { get; init; } = DateTime.Now;

    public bool HasTotals => Columns.Any(c => c.Total);

    public object?[] TotalsRow()
    {
        var totals = new object?[Columns.Count];
        for (int c = 0; c < Columns.Count; c++)
        {
            if (!Columns[c].Total) continue;
            decimal sum = 0;
            foreach (var row in Rows)
                sum += row[c] switch { decimal d => d, int i => i, long l => l, _ => 0 };
            totals[c] = Columns[c].Kind == ColumnKind.Count ? (int)sum : sum;
        }
        if (Columns.Count > 0 && !Columns[0].Total) totals[0] = "Total";
        return totals;
    }

    public string ToCsv(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Columns.Select(c => Escape(c.Header))));
        foreach (var row in Rows)
            sb.AppendLine(string.Join(",", row.Select((v, i) => Escape(Raw(v, Columns[i], culture)))));
        return sb.ToString();
    }

    private static string Raw(object? value, ReportColumn column, CultureInfo culture) => value switch
    {
        null => "",
        decimal d => d.ToString(culture),
        DateOnly date => date.ToString("yyyy-MM-dd", culture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", culture),
        bool b => b ? "1" : "0",
        _ => column.Format(value, culture),
    };

    private static string Escape(string s) =>
        s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;

    /// <summary>A self-contained HTML page of the report (used for Save as HTML and e-mail).</summary>
    public string ToHtml(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(WebUtility.HtmlEncode(Title)).Append("</title>")
          .Append("<style>body{font-family:Segoe UI,Arial,sans-serif;font-size:12px;margin:24px}h1{font-size:18px;margin:0}")
          .Append("p{color:#555;margin:4px 0 12px}table{border-collapse:collapse;width:100%}th,td{padding:3px 6px;border-bottom:1px solid #ddd}")
          .Append("th{background:#f0f0f0;text-align:left}.r{text-align:right}tfoot td{font-weight:bold;border-top:2px solid #333}</style></head><body>");
        sb.Append("<h1>").Append(WebUtility.HtmlEncode(Title)).Append("</h1><p>")
          .Append(WebUtility.HtmlEncode(Subtitle ?? "")).Append(" · ").Append(WebUtility.HtmlEncode(GeneratedAt.ToString("g", culture))).Append("</p>");
        sb.Append("<table><thead><tr>");
        foreach (var c in Columns) sb.Append(c.RightAligned ? "<th class=\"r\">" : "<th>").Append(WebUtility.HtmlEncode(c.Header)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in Rows) AppendRow(sb, row, culture);
        sb.Append("</tbody>");
        if (HasTotals) { sb.Append("<tfoot>"); AppendRow(sb, TotalsRow(), culture); sb.Append("</tfoot>"); }
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private void AppendRow(StringBuilder sb, object?[] row, CultureInfo culture)
    {
        sb.Append("<tr>");
        for (int i = 0; i < Columns.Count; i++)
            sb.Append(Columns[i].RightAligned ? "<td class=\"r\">" : "<td>").Append(WebUtility.HtmlEncode(Columns[i].Format(row[i], culture))).Append("</td>");
        sb.Append("</tr>");
    }
}
