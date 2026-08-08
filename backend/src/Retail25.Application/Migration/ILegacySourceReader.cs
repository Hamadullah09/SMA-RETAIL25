using Retail25.Domain.Common;
using Retail25.Domain.Migration;

namespace Retail25.Application.Migration;

/// <summary>One row as it came out of the source file, before anything has been decided about it.</summary>
public sealed record SourceRow(
    int RowNumber,
    IReadOnlyDictionary<string, string?> Values,
    bool IsDeletedInSource,
    string? LegacyKey);

/// <summary>What reading a file produced: the rows, and the analysis of them.</summary>
public sealed record LegacySource(AnalysisReport Analysis, IReadOnlyList<SourceRow> Rows);

/// <summary>
/// Reads a legacy file into rows and profiles it (doc 09 §3, analyze).
/// <para>
/// A port because the formats — DBF, CSV, <c>.DTA</c>, <c>.ASC</c> — are an infrastructure concern
/// and the pipeline should not know a memo block from a quoted field.
/// </para>
/// </summary>
public interface ILegacySourceReader
{
    /// <summary>Whether this reader recognises the declared file type.</summary>
    bool Knows(string entity);

    /// <summary>The file types this deployment can read, for the picker.</summary>
    IReadOnlyList<LegacySourceKind> Kinds { get; }

    Result<LegacySource> Read(string entity, string fileName, byte[] content);
}

/// <summary>One readable file type, as the operator chooses it.</summary>
/// <param name="RequiresBase64">
/// True for a binary format. A DBF cannot survive a round trip through a text field, so the browser
/// sends it base64-encoded.
/// </param>
public sealed record LegacySourceKind(
    string Entity,
    string DisplayName,
    string GuideReference,
    IReadOnlyList<string> Columns,
    bool RequiresBase64);

/// <summary>A staged row, handed back to the importer to transform.</summary>
public sealed record StagedRow(
    int RowNumber,
    IReadOnlyDictionary<string, string?> Values,
    bool IsDeletedInSource,
    string? LegacyKey);

/// <summary>
/// Validates and imports staged rows (doc 09 §3, validate → dry-run → import).
/// <para>
/// <c>RunAsync</c> takes the dry-run flag rather than having two methods, because the two must do
/// exactly the same work — a dry run that takes a different path is a dry run that proves nothing.
/// </para>
/// </summary>
public interface ILegacyImporter
{
    Task<IReadOnlyList<ValidationFinding>> ValidateAsync(
        MigrationBatch batch, IReadOnlyList<StagedRow> rows, CancellationToken ct);

    Task<ReconciliationReport> RunAsync(
        MigrationBatch batch,
        IReadOnlyList<StagedRow> rows,
        LegacyControlTotals? legacyTotals,
        bool dryRun,
        CancellationToken ct);
}
