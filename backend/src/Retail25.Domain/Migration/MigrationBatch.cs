using Retail25.Domain.Common;

namespace Retail25.Domain.Migration;

/// <summary>Where a batch has got to in the pipeline (doc 09 §3).</summary>
public enum MigrationStage
{
    /// <summary>Rows are in staging, 1:1 with the source, nothing checked yet.</summary>
    Staged = 0,

    /// <summary>Validation has run. Whether it passed is <see cref="MigrationBatch.BlockingErrors"/>.</summary>
    Validated = 1,

    /// <summary>A dry run has produced reconciliation totals. Nothing was written to the live schema.</summary>
    DryRun = 2,

    /// <summary>Imported into the live schema.</summary>
    Imported = 3,

    Cancelled = 4,
}

/// <summary>
/// One upload of one legacy file, and everything the pipeline learned about it (doc 09 §3).
/// <para>
/// A batch is the unit of everything: analysis, validation, the dry run, the import and the
/// reconciliation all belong to one, which is what makes a cutover resumable rather than a single
/// irreversible push.
/// </para>
/// </summary>
public sealed class MigrationBatch : AggregateRoot, IAuditable
{
    public static readonly Error NotFound = new("migration.batch_not_found", "No such migration batch.");

    public static readonly Error NothingStaged = new(
        "migration.nothing_staged",
        "That file produced no rows.");

    public static readonly Error AlreadyImported = new(
        "migration.already_imported",
        "That batch has already been imported.");

    public static readonly Error NotValidated = new(
        "migration.not_validated",
        "Validate the batch before running it.");

    /// <summary>
    /// The doc's rule, made a precondition rather than a convention: no import without a passing dry
    /// run for the same source. A cutover is the one operation where "we were in a hurry" is the
    /// most expensive sentence anyone says.
    /// </summary>
    public static readonly Error DryRunRequired = new(
        "migration.dry_run_required",
        "Run a dry run first. An import cannot be started without one that passed.");

    public static readonly Error HasBlockingErrors = new(
        "migration.has_blocking_errors",
        "That batch has errors that must be fixed before it can be imported.");

    private MigrationBatch()
    {
    }

    public Guid LocationId { get; set; }

    public string SourceFileName { get; set; } = string.Empty;

    /// <summary>What kind of thing this file holds — decides which layout and importer apply.</summary>
    public string Entity { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 of the file as uploaded. Re-uploading the same file is recognised rather than
    /// silently importing it twice, and it is what ties a dry run to the import that follows it.
    /// </summary>
    public string SourceHash { get; set; } = string.Empty;

    public MigrationStage Stage { get; set; } = MigrationStage.Staged;

    public int RowsStaged { get; set; }

    /// <summary>Rows the source itself marked deleted. Counted, never imported.</summary>
    public int RowsDeletedInSource { get; set; }

    public int BlockingErrors { get; set; }

    public int Warnings { get; set; }

    public int RowsImported { get; set; }

    public int RowsSkipped { get; set; }

    /// <summary>The analysis, validation and reconciliation reports, as JSON.</summary>
    public string? AnalysisJson { get; set; }

    public string? ValidationJson { get; set; }

    public string? ReconciliationJson { get; set; }

    public DateTimeOffset? ValidatedAt { get; set; }

    public DateTimeOffset? DryRunAt { get; set; }

    public DateTimeOffset? ImportedAt { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public bool CanImport => Stage == MigrationStage.DryRun && BlockingErrors == 0;

    public static MigrationBatch Stage_(
        Guid locationId, string sourceFileName, string entity, string sourceHash, int rowsStaged, int rowsDeleted)
        => new()
        {
            LocationId = locationId,
            SourceFileName = sourceFileName,
            Entity = entity,
            SourceHash = sourceHash,
            Stage = MigrationStage.Staged,
            RowsStaged = rowsStaged,
            RowsDeletedInSource = rowsDeleted,
        };

    public void RecordValidation(DateTimeOffset at, int blockingErrors, int warnings, string validationJson)
    {
        Stage = MigrationStage.Validated;
        ValidatedAt = at;
        BlockingErrors = blockingErrors;
        Warnings = warnings;
        ValidationJson = validationJson;

        // A dry run is invalidated by re-validating: the figures it produced were for a state that
        // has since been re-examined, and an import must not lean on them.
        DryRunAt = null;
        ReconciliationJson = null;
    }

    public Result RecordDryRun(DateTimeOffset at, string reconciliationJson)
    {
        if (Stage == MigrationStage.Imported)
        {
            return Result.Failure(AlreadyImported);
        }

        if (Stage == MigrationStage.Staged)
        {
            return Result.Failure(NotValidated);
        }

        Stage = MigrationStage.DryRun;
        DryRunAt = at;
        ReconciliationJson = reconciliationJson;
        return Result.Success();
    }

    public Result RecordImport(DateTimeOffset at, int imported, int skipped, string reconciliationJson)
    {
        if (Stage == MigrationStage.Imported)
        {
            return Result.Failure(AlreadyImported);
        }

        if (Stage != MigrationStage.DryRun)
        {
            return Result.Failure(DryRunRequired);
        }

        if (BlockingErrors > 0)
        {
            return Result.Failure(HasBlockingErrors.With("errors", BlockingErrors));
        }

        Stage = MigrationStage.Imported;
        ImportedAt = at;
        RowsImported = imported;
        RowsSkipped = skipped;
        ReconciliationJson = reconciliationJson;
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Stage == MigrationStage.Imported)
        {
            return Result.Failure(AlreadyImported);
        }

        Stage = MigrationStage.Cancelled;
        return Result.Success();
    }
}

/// <summary>
/// One source row, held exactly as it arrived (doc 09 §3: "1:1 with source, everything text").
/// <para>
/// Everything is text on purpose. A staging table that parses is a staging table that can refuse a
/// row before anyone has seen it — and the whole value of staging is being able to look at what the
/// old system actually held, including the parts of it that are wrong.
/// </para>
/// </summary>
public sealed class MigrationStagingRow : Entity
{
    public MigrationStagingRow()
    {
    }

    public Guid BatchId { get; set; }

    /// <summary>Line number in the source file, so every report is row-addressable.</summary>
    public int RowNumber { get; set; }

    /// <summary>The row's fields as JSON, keyed by the layout's column names.</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>The source's own deletion flag. Kept and counted; never imported.</summary>
    public bool IsDeletedInSource { get; set; }

    /// <summary>The legacy key this row claims — stock code, customer number, invoice number.</summary>
    public string? LegacyKey { get; set; }

    /// <summary>Set once the row has been through validation; null while it is only staged.</summary>
    public bool? IsValid { get; set; }

    /// <summary>What validation found, one message per line. Null when the row is clean.</summary>
    public string? Problems { get; set; }

    /// <summary>What the import did with it, once it has run.</summary>
    public string? Outcome { get; set; }
}
