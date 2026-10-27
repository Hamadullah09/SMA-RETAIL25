using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetailPlus.Cutover;

/// <summary>What happened to one area of the data (items, clients, sales…): rows read from Retail25, and what was written.</summary>
public sealed class AreaResult(string area, string source)
{
    public string Area { get; } = area;
    /// <summary>The Retail25 tables the area was read from.</summary>
    public string Source { get; } = source;
    public int Read { get; set; }
    public int Added { get; set; }
    public int Changed { get; set; }
    public int Unchanged { get; set; }
    /// <summary>Rows of the new database removed because Retail25 no longer has them (the children of a carried row).</summary>
    public int Removed { get; set; }
    /// <summary>Source rows not written, by reason.</summary>
    public SortedDictionary<string, int> Skipped { get; } = new(StringComparer.Ordinal);

    public int SkippedCount => Skipped.Values.Sum();

    public void Skip(string reason, int count = 1)
    {
        if (count <= 0) return;
        Skipped[reason] = Skipped.GetValueOrDefault(reason) + count;
    }
}

/// <summary>A total worked out from Retail25 and again from the new database, which must match.</summary>
public sealed record CheckResult(string Area, string Name, decimal Source, decimal Target)
{
    public bool Matches => Source == Target;
}

/// <summary>Retail25 data with no place in the new model, or deliberately left behind.</summary>
public sealed record NotCarried(string What, long Rows, string Reason);

/// <summary>The verification report of one run: areas, checks, what was not carried over, and every warning.</summary>
public sealed class CutoverReport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public bool DryRun { get; set; }
    /// <summary>For a dry run against a target that has no schema yet: the temporary database the run was staged in.</summary>
    public string? StagedIn { get; set; }
    public string? StoreCode { get; set; }
    public string ToolVersion { get; set; } = "";
    public int ImportBatchId { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public List<AreaResult> Areas { get; } = [];
    public List<CheckResult> Checks { get; } = [];
    public List<NotCarried> NotCarriedOver { get; } = [];
    /// <summary>Text values cut to the column sizes of the new model, by entity and property.</summary>
    public SortedDictionary<string, int> Truncated { get; } = new(StringComparer.Ordinal);
    /// <summary>Amounts, costs and quantities rounded to the precision of the new model, by entity and property.</summary>
    public SortedDictionary<string, int> Rounded { get; } = new(StringComparer.Ordinal);
    /// <summary>Decisions taken on this data (which role a Retail25 role became, renumbered stations…).</summary>
    public List<string> Notes { get; } = [];
    public List<string> Warnings { get; } = [];

    public bool ChecksPassed => Checks.All(c => c.Matches);

    public int Added => Areas.Sum(a => a.Added);
    public int Changed => Areas.Sum(a => a.Changed);
    public int Removed => Areas.Sum(a => a.Removed);

    public AreaResult Area(string area, string source)
    {
        var result = new AreaResult(area, source);
        Areas.Add(result);
        return result;
    }

    public void Check(string area, string name, decimal source, decimal target) => Checks.Add(new CheckResult(area, name, source, target));

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public string ToMarkdown()
    {
        var c = CultureInfo.InvariantCulture;
        var md = new StringBuilder();
        md.AppendLine("# Retail25 cutover report").AppendLine();
        md.AppendLine(c, $"- Source (Retail25): `{Source}`");
        md.AppendLine(c, $"- Target: `{Target}`{(StoreCode is null ? "" : $", store {StoreCode} only")}");
        md.AppendLine(c, $"- Mode: {(DryRun ? "**dry run**, nothing was written" + (StagedIn is null ? "" : $" (the target has no schema yet, so the run was staged in a temporary database)") : "real run")}");
        md.AppendLine(c, $"- Started {StartedAt:yyyy-MM-dd HH:mm:ss}, finished {FinishedAt:yyyy-MM-dd HH:mm:ss}; tool version {ToolVersion}{(ImportBatchId > 0 ? $"; import batch {ImportBatchId}" : "")}");
        int differ = Checks.Count(x => !x.Matches);
        string result = !Succeeded ? "failed, nothing was written"
            : differ == 0 ? $"all {Checks.Count} checks match"
            : $"{differ} of {Checks.Count} checks differ";
        md.AppendLine(c, $"- Result: **{result}**; {Added} rows {(DryRun ? "would be " : "")}added, {Changed} changed, {Removed} removed");
        if (Error is not null) md.AppendLine(c, $"- Error: {Error}");
        md.AppendLine();

        md.AppendLine("## Areas").AppendLine();
        md.AppendLine("| Area | Retail25 tables | Read | Added | Changed | Unchanged | Removed | Skipped |");
        md.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var a in Areas)
            md.AppendLine(c, $"| {a.Area} | {a.Source} | {a.Read} | {a.Added} | {a.Changed} | {a.Unchanged} | {a.Removed} | {a.SkippedCount} |");
        md.AppendLine();
        var skipped = Areas.Where(a => a.Skipped.Count > 0).ToList();
        if (skipped.Count > 0)
        {
            md.AppendLine("### Rows skipped").AppendLine();
            foreach (var a in skipped)
                foreach (var (reason, count) in a.Skipped)
                    md.AppendLine(c, $"- {a.Area}: {count} × {reason}");
            md.AppendLine();
        }

        md.AppendLine("## Checks").AppendLine();
        md.AppendLine("| Area | Check | Retail25 | New database | |");
        md.AppendLine("|---|---|---:|---:|---|");
        foreach (var x in Checks)
            md.AppendLine(c, $"| {x.Area} | {x.Name} | {x.Source:0.###} | {x.Target:0.###} | {(x.Matches ? "OK" : "**DIFFERS**")} |");
        md.AppendLine();

        md.AppendLine("## Not carried over").AppendLine();
        if (NotCarriedOver.Count == 0) md.AppendLine("Nothing.");
        else
        {
            md.AppendLine("| What | Rows | Why |");
            md.AppendLine("|---|---:|---|");
            foreach (var n in NotCarriedOver) md.AppendLine(c, $"| {n.What} | {n.Rows} | {n.Reason} |");
        }
        md.AppendLine();

        if (Truncated.Count > 0 || Rounded.Count > 0)
        {
            md.AppendLine("## Values made to fit").AppendLine();
            md.AppendLine("| Field | Values | How |");
            md.AppendLine("|---|---:|---|");
            foreach (var (field, count) in Truncated) md.AppendLine(c, $"| {field} | {count} | text cut to the column size |");
            foreach (var (field, count) in Rounded) md.AppendLine(c, $"| {field} | {count} | rounded to the column's decimals |");
            md.AppendLine();
        }

        if (Notes.Count > 0)
        {
            md.AppendLine("## Decisions on this data").AppendLine();
            foreach (var n in Notes) md.AppendLine(c, $"- {n}");
            md.AppendLine();
        }

        md.AppendLine(c, $"## Warnings ({Warnings.Count})").AppendLine();
        if (Warnings.Count == 0) md.AppendLine("None.");
        foreach (var w in Warnings.Take(500)) md.AppendLine(c, $"- {w}");
        if (Warnings.Count > 500) md.AppendLine(c, $"- … and {Warnings.Count - 500} more (all of them are in the JSON report kept with the import batch).");
        return md.ToString();
    }
}
