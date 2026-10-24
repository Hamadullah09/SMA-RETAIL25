namespace RetailPlus.Data.Import;

/// <summary>One run of the Retail Plus importer into this database.</summary>
public class ImportBatch
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>Folder the data was imported from.</summary>
    public string SourcePath { get; set; } = "";
    public string ToolVersion { get; set; } = "";
    /// <summary>Running, Succeeded or Failed.</summary>
    public string Status { get; set; } = "";
    /// <summary>Every source file with its size and SHA-256, as JSON.</summary>
    public string? ManifestJson { get; set; }
    /// <summary>The verification report, as JSON.</summary>
    public string? ReportJson { get; set; }
}

/// <summary>
/// The full source record behind an imported row, so nothing is lost even where a field or memo format isn't
/// understood yet, and every row can be traced back to its DBF file and record number.
/// </summary>
public class ImportSourceRow
{
    public long Id { get; set; }
    public int ImportBatchId { get; set; }
    /// <summary>Target entity name, e.g. "Item".</summary>
    public string Entity { get; set; } = "";
    public int? EntityId { get; set; }
    /// <summary>Source file path relative to the imported folder.</summary>
    public string SourceFile { get; set; } = "";
    public int SourceRecordNumber { get; set; }
    public string RawJson { get; set; } = "";
}
