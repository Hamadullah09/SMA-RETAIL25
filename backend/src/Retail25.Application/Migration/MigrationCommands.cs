using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Migration;

namespace Retail25.Application.Migration;

public sealed record MigrationBatchDto(
    long Id,
    string SourceFileName,
    string Entity,
    string SourceHash,
    MigrationStage Stage,
    int RowsStaged,
    int RowsDeletedInSource,
    int BlockingErrors,
    int Warnings,
    int RowsImported,
    int RowsSkipped,
    bool CanImport,
    DateTimeOffset? ValidatedAt,
    DateTimeOffset? DryRunAt,
    DateTimeOffset? ImportedAt,
    DateTimeOffset CreatedAt);

/// <summary>A staged row as the review grid shows it.</summary>
public sealed record StagingRowDto(
    int RowNumber,
    string? LegacyKey,
    bool IsDeletedInSource,
    bool? IsValid,
    string? Problems,
    string? Outcome,
    IReadOnlyDictionary<string, string?> Values);

[RequiresPermission(PermissionKeys.System.MigrationRun)]
public sealed record ListMigrationBatchesQuery(long LocationId) : IRequest<IReadOnlyList<MigrationBatchDto>>;

[RequiresPermission(PermissionKeys.System.MigrationRun)]
public sealed record GetMigrationBatchQuery(long BatchId) : IRequest<Result<MigrationBatchDto>>;

/// <summary>The analysis report, which is written at staging time and read back here.</summary>
[RequiresPermission(PermissionKeys.System.MigrationRun)]
public sealed record GetAnalysisQuery(long BatchId) : IRequest<Result<AnalysisReport>>;

[RequiresPermission(PermissionKeys.System.MigrationRun)]
public sealed record GetValidationQuery(long BatchId) : IRequest<Result<IReadOnlyList<ValidationFinding>>>;

[RequiresPermission(PermissionKeys.System.MigrationRun)]
public sealed record GetReconciliationQuery(long BatchId) : IRequest<Result<ReconciliationReport>>;

/// <summary>
/// Rows from staging. <paramref name="ProblemsOnly"/> is what an operator actually works from —
/// a clean file is thousands of rows nobody needs to look at.
/// </summary>
[RequiresPermission(PermissionKeys.System.MigrationRun)]
public sealed record BrowseStagingQuery(
    long BatchId,
    bool ProblemsOnly = true,
    int Skip = 0,
    int Take = 200) : IRequest<Result<IReadOnlyList<StagingRowDto>>>;

/// <summary>
/// Reads a file, profiles it and holds every row in staging (doc 09 §3, analyze + stage).
/// <para>
/// Nothing outside the staging tables is touched. The file arrives as text because that is what the
/// browser has — a DBF arrives base64-encoded, which the handler recognises by the entity being
/// declared as a DBF source.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.System.MigrationRun)]
public sealed record StageMigrationFileCommand(
    long LocationId,
    string FileName,
    string Entity,
    string Content,
    bool IsBase64 = false) : IRequest<Result<MigrationBatchDto>>;

[RequiresPermission(PermissionKeys.System.MigrationRun)]
public sealed record ValidateMigrationBatchCommand(long BatchId) : IRequest<Result<MigrationBatchDto>>;

/// <summary>
/// The dry run (doc 09 §3). Transforms every row exactly as the import would and writes nothing,
/// producing the reconciliation totals that get compared against the legacy system's own reports.
/// </summary>
[RequiresPermission(PermissionKeys.System.MigrationRun)]
public sealed record DryRunMigrationCommand(
    long BatchId,
    LegacyControlTotals? LegacyTotals = null) : IRequest<Result<ReconciliationReport>>;

/// <summary>
/// The import. Refuses without a passing dry run for the same batch — the doc's rule, enforced
/// rather than documented.
/// </summary>
[RequiresPermission(PermissionKeys.System.MigrationRun)]
public sealed record ImportMigrationBatchCommand(
    long BatchId,
    LegacyControlTotals? LegacyTotals = null) : IRequest<Result<ReconciliationReport>>;

[RequiresPermission(PermissionKeys.System.MigrationRun)]
public sealed record CancelMigrationBatchCommand(long BatchId) : IRequest<Result>;

public sealed class MigrationHandlers :
    IRequestHandler<ListMigrationBatchesQuery, IReadOnlyList<MigrationBatchDto>>,
    IRequestHandler<GetMigrationBatchQuery, Result<MigrationBatchDto>>,
    IRequestHandler<GetAnalysisQuery, Result<AnalysisReport>>,
    IRequestHandler<GetValidationQuery, Result<IReadOnlyList<ValidationFinding>>>,
    IRequestHandler<GetReconciliationQuery, Result<ReconciliationReport>>,
    IRequestHandler<BrowseStagingQuery, Result<IReadOnlyList<StagingRowDto>>>,
    IRequestHandler<StageMigrationFileCommand, Result<MigrationBatchDto>>,
    IRequestHandler<ValidateMigrationBatchCommand, Result<MigrationBatchDto>>,
    IRequestHandler<DryRunMigrationCommand, Result<ReconciliationReport>>,
    IRequestHandler<ImportMigrationBatchCommand, Result<ReconciliationReport>>,
    IRequestHandler<CancelMigrationBatchCommand, Result>
{
    public static readonly Error UnknownEntity = new(
        "migration.unknown_entity",
        "That is not a legacy file type this system knows how to read.");

    public static readonly Error NothingReadable = new(
        "migration.nothing_readable",
        "Nothing in that file could be read as rows.");

    public static readonly Error NoReport = new(
        "migration.no_report",
        "That step has not been run yet.");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private const int ChunkSize = 500;

    private readonly IApplicationDbContext _db;
    private readonly ILegacySourceReader _reader;
    private readonly ILegacyImporter _importer;
    private readonly IDateTime _clock;

    public MigrationHandlers(
        IApplicationDbContext db,
        ILegacySourceReader reader,
        ILegacyImporter importer,
        IDateTime clock)
    {
        _db = db;
        _reader = reader;
        _importer = importer;
        _clock = clock;
    }

    public async Task<IReadOnlyList<MigrationBatchDto>> Handle(ListMigrationBatchesQuery request, CancellationToken ct)
        => (await _db.MigrationBatches.AsNoTracking()
                .Where(b => b.LocationId == request.LocationId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync(ct))
            .Select(ToDto)
            .ToList();

    public async Task<Result<MigrationBatchDto>> Handle(GetMigrationBatchQuery request, CancellationToken ct)
    {
        var batch = await _db.MigrationBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BatchId, ct);

        return batch is null ? Result.Failure<MigrationBatchDto>(MigrationBatch.NotFound) : Result.Success(ToDto(batch));
    }

    public async Task<Result<AnalysisReport>> Handle(GetAnalysisQuery request, CancellationToken ct)
        => await ReadReportAsync<AnalysisReport>(request.BatchId, b => b.AnalysisJson, ct);

    public async Task<Result<IReadOnlyList<ValidationFinding>>> Handle(GetValidationQuery request, CancellationToken ct)
    {
        var result = await ReadReportAsync<List<ValidationFinding>>(request.BatchId, b => b.ValidationJson, ct);

        return result.IsFailure
            ? Result.Failure<IReadOnlyList<ValidationFinding>>(result.Error)
            : Result.Success<IReadOnlyList<ValidationFinding>>(result.Value);
    }

    public async Task<Result<ReconciliationReport>> Handle(GetReconciliationQuery request, CancellationToken ct)
        => await ReadReportAsync<ReconciliationReport>(request.BatchId, b => b.ReconciliationJson, ct);

    public async Task<Result<IReadOnlyList<StagingRowDto>>> Handle(BrowseStagingQuery request, CancellationToken ct)
    {
        var exists = await _db.MigrationBatches.AsNoTracking().AnyAsync(b => b.Id == request.BatchId, ct);

        if (!exists)
        {
            return Result.Failure<IReadOnlyList<StagingRowDto>>(MigrationBatch.NotFound);
        }

        var query = _db.MigrationStagingRows.AsNoTracking().Where(r => r.BatchId == request.BatchId);

        if (request.ProblemsOnly)
        {
            query = query.Where(r => r.IsValid == false || r.Problems != null);
        }

        var rows = await query
            .OrderBy(r => r.RowNumber)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 1000))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<StagingRowDto>>(rows.Select(r => new StagingRowDto(
            r.RowNumber,
            r.LegacyKey,
            r.IsDeletedInSource,
            r.IsValid,
            r.Problems,
            r.Outcome,
            Deserialize(r.PayloadJson))).ToList());
    }

    public async Task<Result<MigrationBatchDto>> Handle(StageMigrationFileCommand request, CancellationToken ct)
    {
        if (!_reader.Knows(request.Entity))
        {
            return Result.Failure<MigrationBatchDto>(UnknownEntity.With("entity", request.Entity));
        }

        var bytes = request.IsBase64
            ? Convert.FromBase64String(request.Content)
            : Encoding.UTF8.GetBytes(request.Content);

        var read = _reader.Read(request.Entity, request.FileName, bytes);

        if (read.IsFailure)
        {
            return Result.Failure<MigrationBatchDto>(read.Error);
        }

        var source = read.Value;

        if (source.Rows.Count == 0)
        {
            return Result.Failure<MigrationBatchDto>(NothingReadable);
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var batch = MigrationBatch.Stage_(
            request.LocationId,
            request.FileName,
            request.Entity,
            hash,
            source.Rows.Count,
            source.Rows.Count(r => r.IsDeletedInSource));

        batch.AnalysisJson = JsonSerializer.Serialize(source.Analysis, Json);

        _db.MigrationBatches.Add(batch);

        // Saved before a single row references it.
        //
        // This one hid better than most. The batch's id is assigned by the database, so it is 0 until
        // a save — and the loop below saves every 500 rows. The first chunk therefore recorded
        // BatchId 0 and every row after it recorded the real id: the batch looked staged, the counts
        // on the batch itself were right, and only queries that join back by BatchId came up exactly
        // 500 short, whatever the size of the file. A twenty-thousand-row import reported 19,500
        // importable and a five-thousand-row one reported 4,500.
        await _db.SaveChangesAsync(ct);

        var written = 0;

        foreach (var row in source.Rows)
        {
            _db.MigrationStagingRows.Add(new MigrationStagingRow
            {
                BatchId = batch.Id,
                RowNumber = row.RowNumber,
                PayloadJson = JsonSerializer.Serialize(row.Values, Json),
                IsDeletedInSource = row.IsDeletedInSource,
                LegacyKey = row.LegacyKey,
            });

            if (++written % ChunkSize == 0)
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(ToDto(batch));
    }

    public async Task<Result<MigrationBatchDto>> Handle(ValidateMigrationBatchCommand request, CancellationToken ct)
    {
        var batch = await _db.MigrationBatches.FirstOrDefaultAsync(b => b.Id == request.BatchId, ct);

        if (batch is null)
        {
            return Result.Failure<MigrationBatchDto>(MigrationBatch.NotFound);
        }

        if (batch.Stage == MigrationStage.Imported)
        {
            return Result.Failure<MigrationBatchDto>(MigrationBatch.AlreadyImported);
        }

        // AsNoTracking: these rows are read to be validated, and the verdict is written back below
        // by set operations rather than by mutating each one. Tracking twenty thousand entities to
        // change two properties on each is twenty thousand UPDATE statements — which was inside the
        // command timeout on PostgreSQL, where the driver batched a thousand statements per round
        // trip, and is minutes on SQL Server, which batches tens. The engine exposed it; the shape
        // was always wrong.
        var rows = await _db.MigrationStagingRows.AsNoTracking()
            .Where(r => r.BatchId == batch.Id).OrderBy(r => r.RowNumber).ToListAsync(ct);

        var findings = await _importer.ValidateAsync(batch, rows.Select(ToStaged).ToList(), ct);
        var byRow = findings.GroupBy(f => f.RowNumber).ToDictionary(g => g.Key, g => g.ToList());

        // The common case in one statement: a file where most rows are fine.
        await _db.SetStagingVerdictAsync(batch.Id, null, isValid: true, problems: null, ct);

        // Then only the rows that actually have something to say. A file where every row is broken
        // costs a statement per row again — and that is a file whose real problem is not this loop.
        foreach (var group in byRow)
        {
            var problems = group.Value;

            await _db.SetStagingVerdictAsync(
                batch.Id,
                group.Key,
                problems.TrueForAll(p => p.Severity == FindingSeverity.Warning),
                string.Join('\n', problems.Select(p => $"{p.Column ?? "row"}: {p.Message}")),
                ct);
        }

        batch.RecordValidation(
            _clock.Now,
            findings.Count(f => f.Severity == FindingSeverity.Blocking),
            findings.Count(f => f.Severity == FindingSeverity.Warning),
            JsonSerializer.Serialize(findings, Json));

        await _db.SaveChangesAsync(ct);

        return Result.Success(ToDto(batch));
    }

    public async Task<Result<ReconciliationReport>> Handle(DryRunMigrationCommand request, CancellationToken ct)
    {
        var loaded = await LoadForRunAsync(request.BatchId, ct);

        if (loaded.IsFailure)
        {
            return Result.Failure<ReconciliationReport>(loaded.Error);
        }

        var (batch, rows) = loaded.Value;

        // Every row is transformed exactly as the import would, and nothing is written. That is what
        // makes the totals it reports the totals the import will produce.
        var report = await _importer.RunAsync(batch, rows, request.LegacyTotals, dryRun: true, ct);

        // Re-read: discarding the dry run's work detaches everything the context was tracking,
        // including the batch loaded above. Stamping the detached instance would save nothing.
        batch = await _db.MigrationBatches.FirstOrDefaultAsync(b => b.Id == request.BatchId, ct);

        if (batch is null)
        {
            return Result.Failure<ReconciliationReport>(MigrationBatch.NotFound);
        }

        var recorded = batch.RecordDryRun(_clock.Now, JsonSerializer.Serialize(report, Json));

        if (recorded.IsFailure)
        {
            return Result.Failure<ReconciliationReport>(recorded.Error);
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(report);
    }

    public async Task<Result<ReconciliationReport>> Handle(ImportMigrationBatchCommand request, CancellationToken ct)
    {
        var loaded = await LoadForRunAsync(request.BatchId, ct);

        if (loaded.IsFailure)
        {
            return Result.Failure<ReconciliationReport>(loaded.Error);
        }

        var (batch, rows) = loaded.Value;

        if (!batch.CanImport)
        {
            return Result.Failure<ReconciliationReport>(
                batch.Stage == MigrationStage.DryRun ? MigrationBatch.HasBlockingErrors : MigrationBatch.DryRunRequired);
        }

        var report = await _importer.RunAsync(batch, rows, request.LegacyTotals, dryRun: false, ct);

        var recorded = batch.RecordImport(
            _clock.Now, report.RowsWouldImport, report.RowsWouldSkip, JsonSerializer.Serialize(report, Json));

        if (recorded.IsFailure)
        {
            return Result.Failure<ReconciliationReport>(recorded.Error);
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(report);
    }

    public async Task<Result> Handle(CancelMigrationBatchCommand request, CancellationToken ct)
    {
        var batch = await _db.MigrationBatches.FirstOrDefaultAsync(b => b.Id == request.BatchId, ct);

        if (batch is null)
        {
            return Result.Failure(MigrationBatch.NotFound);
        }

        var cancelled = batch.Cancel();

        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        // Staging goes with it. Keeping thousands of rows from an abandoned attempt would make the
        // next person's duplicate check answer for a file nobody imported.
        var rows = await _db.MigrationStagingRows.Where(r => r.BatchId == batch.Id).ToListAsync(ct);
        _db.MigrationStagingRows.RemoveRange(rows);

        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    private async Task<Result<(MigrationBatch Batch, List<StagedRow> Rows)>> LoadForRunAsync(long batchId, CancellationToken ct)
    {
        var batch = await _db.MigrationBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);

        if (batch is null)
        {
            return Result.Failure<(MigrationBatch, List<StagedRow>)>(MigrationBatch.NotFound);
        }

        if (batch.Stage == MigrationStage.Imported)
        {
            return Result.Failure<(MigrationBatch, List<StagedRow>)>(MigrationBatch.AlreadyImported);
        }

        if (batch.Stage == MigrationStage.Staged)
        {
            return Result.Failure<(MigrationBatch, List<StagedRow>)>(MigrationBatch.NotValidated);
        }

        var rows = await _db.MigrationStagingRows
            .Where(r => r.BatchId == batch.Id)
            .OrderBy(r => r.RowNumber)
            .ToListAsync(ct);

        return Result.Success((batch, rows.Select(ToStaged).ToList()));
    }

    private async Task<Result<T>> ReadReportAsync<T>(
        long batchId, Func<MigrationBatch, string?> select, CancellationToken ct)
    {
        var batch = await _db.MigrationBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct);

        if (batch is null)
        {
            return Result.Failure<T>(MigrationBatch.NotFound);
        }

        var json = select(batch);

        if (string.IsNullOrWhiteSpace(json))
        {
            return Result.Failure<T>(NoReport);
        }

        var value = JsonSerializer.Deserialize<T>(json, Json);

        return value is null ? Result.Failure<T>(NoReport) : Result.Success(value);
    }

    private static StagedRow ToStaged(MigrationStagingRow row) => new(
        row.RowNumber,
        Deserialize(row.PayloadJson),
        row.IsDeletedInSource,
        row.LegacyKey);

    /// <summary>
    /// Rebuilds a staged row's fields, case-insensitively.
    /// <para>
    /// The comparer matters and is easy to lose: a DBF names its columns <c>STOCKCODE</c> while the
    /// documented layout calls it <c>StockCode</c>, and a plain deserialize produces an ordinal
    /// dictionary in which those are different keys. Every DBF row then looks like it has no stock
    /// code at all.
    /// </para>
    /// </summary>
    private static Dictionary<string, string?> Deserialize(string json)
    {
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string?>>(json, Json);

        return parsed is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(parsed, StringComparer.OrdinalIgnoreCase);
    }

    private static MigrationBatchDto ToDto(MigrationBatch b) => new(
        b.Id, b.SourceFileName, b.Entity, b.SourceHash, b.Stage,
        b.RowsStaged, b.RowsDeletedInSource, b.BlockingErrors, b.Warnings,
        b.RowsImported, b.RowsSkipped, b.CanImport,
        b.ValidatedAt, b.DryRunAt, b.ImportedAt, b.CreatedAt);
}
