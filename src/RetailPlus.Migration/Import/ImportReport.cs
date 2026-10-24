using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetailPlus.Migration.Import;

/// <summary>Warnings and progress of one import run. Messages name files, records and fields, never record contents.</summary>
public sealed class ImportLog(Action<string>? progress = null)
{
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public void Info(string message) => progress?.Invoke(message);

    public void Warn(string where, string message)
    {
        lock (_warnings) _warnings.Add($"{where}: {message}");
    }

    public void Warn(string message)
    {
        lock (_warnings) _warnings.Add(message);
    }
}

/// <summary>How one source table was imported.</summary>
public sealed class TableResult(string sourceFile, string entity)
{
    public string SourceFile { get; } = sourceFile;
    public string Entity { get; } = entity;
    /// <summary>Records present in the file.</summary>
    public int Records { get; set; }
    /// <summary>Records marked deleted (imported as deleted where the entity supports it, otherwise only traced).</summary>
    public int Deleted { get; set; }
    /// <summary>Empty template records, skipped.</summary>
    public int Blank { get; set; }
    /// <summary>Rows written to the database.</summary>
    public int Imported { get; set; }
}

/// <summary>A total computed from the source files and again from the database after the import.</summary>
public sealed record ControlTotal(string Name, decimal Source, decimal Database)
{
    public bool Matches => Source == Database;
}

/// <summary>The import report: what was read, what was written, the control totals and every warning.</summary>
public sealed class ImportReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public string SourceFolder { get; set; } = "";
    public string Database { get; set; } = "";
    public string ToolVersion { get; set; } = "";
    public int ImportBatchId { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public List<TableResult> Tables { get; } = [];
    public List<ControlTotal> Totals { get; } = [];
    /// <summary>Source files that were found but are deliberately not imported, with the reason.</summary>
    public List<string> NotImported { get; } = [];
    public List<string> Warnings { get; } = [];

    public bool VerificationPassed => Totals.All(t => t.Matches);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public string ToMarkdown()
    {
        var c = CultureInfo.InvariantCulture;
        var md = new StringBuilder();
        md.AppendLine("# Retail Plus import report").AppendLine();
        md.AppendLine(c, $"- Source: `{SourceFolder}`");
        md.AppendLine(c, $"- Database: `{Database}`");
        md.AppendLine(c, $"- Started: {StartedAt:yyyy-MM-dd HH:mm:ss}, finished: {FinishedAt:yyyy-MM-dd HH:mm:ss}");
        md.AppendLine(c, $"- Tool version: {ToolVersion}; import batch {ImportBatchId}");
        md.AppendLine(c, $"- Result: **{(Succeeded ? (VerificationPassed ? "succeeded, all control totals match" : "imported, but control totals differ") : "failed")}**");
        if (Error is not null) md.AppendLine(c, $"- Error: {Error}");
        md.AppendLine();

        md.AppendLine("## Tables").AppendLine();
        md.AppendLine("| Source file | Imported as | Records | Deleted | Blank | Rows written |");
        md.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var t in Tables)
            md.AppendLine(c, $"| {t.SourceFile} | {t.Entity} | {t.Records} | {t.Deleted} | {t.Blank} | {t.Imported} |");
        md.AppendLine();

        md.AppendLine("## Control totals").AppendLine();
        md.AppendLine("| Total | Source | Database | |");
        md.AppendLine("|---|---:|---:|---|");
        foreach (var t in Totals)
            md.AppendLine(c, $"| {t.Name} | {t.Source:0.###} | {t.Database:0.###} | {(t.Matches ? "OK" : "**DIFFERS**")} |");
        md.AppendLine();

        if (NotImported.Count > 0)
        {
            md.AppendLine("## Not imported").AppendLine();
            foreach (var n in NotImported) md.AppendLine(c, $"- {n}");
            md.AppendLine();
        }

        md.AppendLine(c, $"## Warnings ({Warnings.Count})").AppendLine();
        if (Warnings.Count == 0) md.AppendLine("None.");
        foreach (var w in Warnings) md.AppendLine(c, $"- {w}");
        return md.ToString();
    }

    /// <summary>Writes report.md and report.json into <paramref name="folder"/> and returns the Markdown path.</summary>
    public string WriteTo(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "report.json"), ToJson(), Encoding.UTF8);
        var markdown = Path.Combine(folder, "report.md");
        File.WriteAllText(markdown, ToMarkdown(), Encoding.UTF8);
        return markdown;
    }
}
