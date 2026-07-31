namespace Retail25.Application.Migration;

/// <summary>How badly a validation finding matters.</summary>
public enum FindingSeverity
{
    /// <summary>Worth knowing. The row still imports.</summary>
    Warning = 0,

    /// <summary>The row cannot be imported, and neither can the batch until it is dealt with.</summary>
    Blocking = 1,
}

/// <summary>
/// One thing validation found, addressed to a row (doc 09 §3: "row-addressable").
/// <para>
/// A validation report that says "47 errors" and nothing else means someone opens the source file
/// in Notepad and starts counting lines. Every finding here names its row and its column.
/// </para>
/// </summary>
public sealed record ValidationFinding(
    int RowNumber,
    string? Column,
    FindingSeverity Severity,
    string Code,
    string Message,
    string? Value = null);

/// <summary>What one column looks like across the whole file, before anything is imported.</summary>
public sealed record ColumnProfile(
    string Name,
    int Populated,
    int Empty,
    int DistinctValues,
    string? ShortestValue,
    string? LongestValue,
    IReadOnlyList<string> Samples);

/// <summary>
/// The analysis report (doc 09 §3). Produced without writing anything, and the first thing an
/// operator sees — it answers "did we read this file correctly at all" before any mapping decision.
/// </summary>
public sealed record AnalysisReport(
    string SourceFileName,
    string Format,
    string DetectedLayout,
    string GuideReference,
    int RowCount,
    int DeletedRowCount,
    int ColumnCount,
    IReadOnlyList<ColumnProfile> Columns,
    IReadOnlyList<string> Notes);

/// <summary>
/// The reconciliation totals (doc 09 §3): what the import would produce, next to what the legacy
/// system reported. Whoever signs off the cutover reads this and nothing else.
/// </summary>
public sealed record ReconciliationLine(
    string Measure,
    decimal Imported,
    decimal? LegacyReported,
    decimal? Variance,
    bool Matches);

public sealed record ReconciliationReport(
    string Entity,
    int RowsConsidered,
    int RowsWouldImport,
    int RowsWouldSkip,
    IReadOnlyList<ReconciliationLine> Lines,
    IReadOnlyList<string> Warnings)
{
    /// <summary>True only when every measure with a legacy figure to compare against agrees.</summary>
    public bool Reconciles => Lines.All(l => l.LegacyReported is null || l.Matches);
}

/// <summary>
/// What the legacy system's own reports said, typed in by whoever is running the cutover.
/// <para>
/// There is no way to derive these — they come off a printout from the old system. Everything is
/// optional: a measure with nothing to compare against is reported as imported-only rather than
/// pretending to reconcile.
/// </para>
/// </summary>
public sealed record LegacyControlTotals(
    int? ItemCount = null,
    decimal? InventoryValue = null,
    decimal? ReceivablesBalance = null,
    decimal? YearToDateSales = null,
    int? CustomerCount = null,
    int? SupplierCount = null);
