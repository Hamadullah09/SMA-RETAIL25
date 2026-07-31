using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Inventory;
using Retail25.Domain.Configuration;

namespace Retail25.Application.Inventory;

public sealed record StockCountLineDto(
    Guid Id,
    Guid ProductId,
    string StockCode,
    string ProductName,
    decimal CountedQty,
    decimal SystemQtyAtCount,
    decimal Variance,
    decimal UnitCost,
    decimal VarianceValue,
    string? Notes);

public sealed record StockCountDto(
    Guid Id,
    long CountNumber,
    Guid LocationId,
    Guid? DepartmentId,
    string? DepartmentName,
    StockCountStatus Status,
    string? Notes,
    DateTimeOffset? PostedAt,
    DateTimeOffset CreatedAt,
    int LineCount,
    int VarianceCount,
    decimal NetVarianceValue,
    IReadOnlyList<StockCountLineDto> Lines);

public sealed record StockCountRowDto(
    Guid Id,
    long CountNumber,
    StockCountStatus Status,
    string? DepartmentName,
    int LineCount,
    int VarianceCount,
    decimal NetVarianceValue,
    DateTimeOffset? PostedAt,
    DateTimeOffset CreatedAt);

/// <summary>What an import did, line by line, so a bad file is diagnosable rather than just refused.</summary>
public sealed record CountImportResult(
    int Imported,
    int Updated,
    IReadOnlyList<string> Skipped);

[RequiresPermission(PermissionKeys.Inventory.Count)]
public sealed record BrowseStockCountsQuery(
    Guid LocationId,
    StockCountStatus? Status = null,
    int Skip = 0,
    int Take = 50) : IRequest<IReadOnlyList<StockCountRowDto>>;

/// <summary>
/// One count with its lines. <paramref name="VarianceOnly"/> is the view an operator actually works
/// from — on a full count most lines agree, and the ones that do are not the point.
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.Count)]
public sealed record GetStockCountQuery(
    Guid CountId,
    bool VarianceOnly = false,
    int Take = 500) : IRequest<Result<StockCountDto>>;

[RequiresPermission(PermissionKeys.Inventory.Count)]
public sealed record StartStockCountCommand(
    Guid LocationId,
    Guid? DepartmentId = null,
    string? Notes = null) : IRequest<Result<StockCountDto>>;

/// <summary>One counted item, keyed by stock code because that is what a handheld or a sheet gives.</summary>
public sealed record CountedItem(string StockCode, decimal CountedQty, string? Notes = null);

[RequiresPermission(PermissionKeys.Inventory.Count)]
public sealed record ImportCountLinesCommand(
    Guid CountId,
    IReadOnlyList<CountedItem> Items) : IRequest<Result<CountImportResult>>;

/// <summary>
/// Parses a two-column CSV (<c>StockCode,CountedQty[,Notes]</c>) and imports it. The parse is here
/// rather than in the browser so a file that half-works fails the same way for every client.
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.Count)]
public sealed record ImportCountCsvCommand(Guid CountId, string Csv) : IRequest<Result<CountImportResult>>;

[RequiresPermission(PermissionKeys.Inventory.Count)]
public sealed record RemoveCountLineCommand(Guid CountId, Guid LineId) : IRequest<Result<StockCountDto>>;

/// <summary>Writes the variances to stock. This is the irreversible step.</summary>
[RequiresPermission(PermissionKeys.Inventory.Count)]
public sealed record PostStockCountCommand(Guid CountId, string? Reason = null) : IRequest<Result<StockCountDto>>;

[RequiresPermission(PermissionKeys.Inventory.Count)]
public sealed record CancelStockCountCommand(Guid CountId) : IRequest<Result<StockCountDto>>;

/// <summary>The variance sheet as a CSV, for the file that goes to whoever signs off shrinkage.</summary>
[RequiresPermission(PermissionKeys.Inventory.Count)]
public sealed record ExportStockCountQuery(Guid CountId, bool VarianceOnly = true) : IRequest<Result<string>>;

/// <summary>
/// Stock counts (guide p.22). Count, review the variances, then post.
/// </summary>
public sealed class StockCountHandlers :
    IRequestHandler<BrowseStockCountsQuery, IReadOnlyList<StockCountRowDto>>,
    IRequestHandler<GetStockCountQuery, Result<StockCountDto>>,
    IRequestHandler<StartStockCountCommand, Result<StockCountDto>>,
    IRequestHandler<ImportCountLinesCommand, Result<CountImportResult>>,
    IRequestHandler<ImportCountCsvCommand, Result<CountImportResult>>,
    IRequestHandler<RemoveCountLineCommand, Result<StockCountDto>>,
    IRequestHandler<PostStockCountCommand, Result<StockCountDto>>,
    IRequestHandler<CancelStockCountCommand, Result<StockCountDto>>,
    IRequestHandler<ExportStockCountQuery, Result<string>>
{
    public static readonly Error CountNotFound = new("count.not_found", "No such stock count.");
    public static readonly Error LineNotFound = new("count.line_not_found", "That line is not on this count.");
    public static readonly Error NothingImported = new("count.nothing_imported", "Nothing in that file matched an item.");
    public static readonly Error EmptyFile = new("count.empty_file", "That file has no rows in it.");

    private const int ChunkSize = 500;

    private readonly IApplicationDbContext _db;
    private readonly ISequenceGenerator _sequences;
    private readonly ICurrentUser _currentUser;
    private readonly IPosNotifier _notifier;
    private readonly IDateTime _clock;

    public StockCountHandlers(
        IApplicationDbContext db,
        ISequenceGenerator sequences,
        ICurrentUser currentUser,
        IPosNotifier notifier,
        IDateTime clock)
    {
        _db = db;
        _sequences = sequences;
        _currentUser = currentUser;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task<IReadOnlyList<StockCountRowDto>> Handle(BrowseStockCountsQuery request, CancellationToken ct)
    {
        var query = _db.StockCounts.AsNoTracking().Where(c => c.LocationId == request.LocationId);

        if (request.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }

        var counts = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 200))
            .ToListAsync(ct);

        var ids = counts.Select(c => c.Id).ToList();

        // Variance is a computed property, so the counting has to happen in memory. Only the three
        // columns it reads come back rather than whole line entities.
        var lines = await _db.StockCountLines.AsNoTracking()
            .Where(l => ids.Contains(l.StockCountId))
            .Select(l => new { l.StockCountId, l.CountedQty, l.SystemQtyAtCount, l.UnitCost })
            .ToListAsync(ct);

        var summaries = lines
            .GroupBy(l => l.StockCountId)
            .ToDictionary(g => g.Key, g => new
            {
                Count = g.Count(),
                Variances = g.Count(l => l.CountedQty != l.SystemQtyAtCount),
                Net = g.Sum(l => (l.CountedQty - l.SystemQtyAtCount) * l.UnitCost),
            });

        var departments = await DepartmentNamesAsync(counts.Select(c => c.DepartmentId), ct);

        return counts.Select(c =>
        {
            var summary = summaries.GetValueOrDefault(c.Id);

            return new StockCountRowDto(
                c.Id,
                c.CountNumber,
                c.Status,
                c.DepartmentId is { } id ? departments.GetValueOrDefault(id) : null,
                summary?.Count ?? 0,
                summary?.Variances ?? 0,
                summary?.Net ?? 0m,
                c.PostedAt,
                c.CreatedAt);
        }).ToList();
    }

    public async Task<Result<StockCountDto>> Handle(GetStockCountQuery request, CancellationToken ct)
    {
        var count = await _db.StockCounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CountId, ct);

        return count is null
            ? Result.Failure<StockCountDto>(CountNotFound)
            : Result.Success(await ToDtoAsync(count, request.VarianceOnly, request.Take, ct));
    }

    public async Task<Result<StockCountDto>> Handle(StartStockCountCommand request, CancellationToken ct)
    {
        var number = await _sequences.NextAsync(SequenceKind.StockCount, request.LocationId, ct);

        var count = StockCount.Start(request.LocationId, number, request.DepartmentId, request.Notes);

        _db.StockCounts.Add(count);
        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(count, varianceOnly: false, take: 500, ct));
    }

    public async Task<Result<CountImportResult>> Handle(ImportCountCsvCommand request, CancellationToken ct)
    {
        var parsed = ParseCsv(request.Csv);

        if (parsed.Items.Count == 0 && parsed.Skipped.Count == 0)
        {
            return Result.Failure<CountImportResult>(EmptyFile);
        }

        var imported = await ImportAsync(request.CountId, parsed.Items, ct);

        if (imported.IsFailure)
        {
            return imported;
        }

        // Malformed rows are reported alongside unmatched codes: to the person holding the file they
        // are the same problem — a line that did not make it in.
        return Result.Success(imported.Value with
        {
            Skipped = [.. parsed.Skipped, .. imported.Value.Skipped],
        });
    }

    public Task<Result<CountImportResult>> Handle(ImportCountLinesCommand request, CancellationToken ct)
        => ImportAsync(request.CountId, request.Items, ct);

    public async Task<Result<StockCountDto>> Handle(RemoveCountLineCommand request, CancellationToken ct)
    {
        var count = await _db.StockCounts.FirstOrDefaultAsync(c => c.Id == request.CountId, ct);

        if (count is null)
        {
            return Result.Failure<StockCountDto>(CountNotFound);
        }

        var open = count.EnsureOpen();

        if (open.IsFailure)
        {
            return Result.Failure<StockCountDto>(open.Error);
        }

        var line = await _db.StockCountLines.FirstOrDefaultAsync(
            l => l.Id == request.LineId && l.StockCountId == count.Id, ct);

        if (line is null)
        {
            return Result.Failure<StockCountDto>(LineNotFound);
        }

        _db.StockCountLines.Remove(line);
        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(count, varianceOnly: false, take: 500, ct));
    }

    public async Task<Result<StockCountDto>> Handle(PostStockCountCommand request, CancellationToken ct)
    {
        var count = await _db.StockCounts.FirstOrDefaultAsync(c => c.Id == request.CountId, ct);

        if (count is null)
        {
            return Result.Failure<StockCountDto>(CountNotFound);
        }

        var lines = await _db.StockCountLines.Where(l => l.StockCountId == count.Id).ToListAsync(ct);

        var posted = count.Post(_clock.Now, lines.Count > 0);

        if (posted.IsFailure)
        {
            return Result.Failure<StockCountDto>(posted.Error);
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? $"Stock count {count.CountNumber}"
            : $"Stock count {count.CountNumber}: {request.Reason.Trim()}";

        var productIds = lines.Select(l => l.ProductId).ToList();

        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        var written = 0;

        foreach (var line in lines)
        {
            // A line that agrees with the system moves nothing. Writing a zero-quantity ledger entry
            // for every item in the shop would bury the entries that mean something.
            if (line.Variance == 0m || !products.TryGetValue(line.ProductId, out var product))
            {
                continue;
            }

            // Set rather than adjusted by the variance: the count is the authority on what is on the
            // shelf, and adding the difference would re-apply any sale that happened since the line
            // was entered on top of a figure that already accounts for it.
            product.UpdateStockLevels(line.CountedQty, product.OnOrder);

            _db.StockLedgerEntries.Add(new StockLedgerEntry
            {
                ProductId = product.Id,
                LocationId = count.LocationId,
                MovementType = MovementType.CountVariance,
                Quantity = line.Variance,
                UnitCost = line.UnitCost,
                Reason = reason,
                ReferenceType = nameof(StockCount),
                ReferenceId = count.Id,
                OccurredAt = _clock.Now,
                StaffId = _currentUser.StaffId,
            });

            var level = await _db.StockLevels.FirstOrDefaultAsync(
                s => s.ProductId == product.Id && s.VariantId == null && s.LocationId == count.LocationId, ct);

            if (level is null)
            {
                level = StockLevel.Create(product.Id, null, count.LocationId);
                _db.StockLevels.Add(level);
            }

            level.OnHand = line.CountedQty;

            await _notifier.StockLevelChangedAsync(count.LocationId, product.Id, product.OnHand, ct);

            if (++written % ChunkSize == 0)
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(count, varianceOnly: true, take: 500, ct));
    }

    public async Task<Result<StockCountDto>> Handle(CancelStockCountCommand request, CancellationToken ct)
    {
        var count = await _db.StockCounts.FirstOrDefaultAsync(c => c.Id == request.CountId, ct);

        if (count is null)
        {
            return Result.Failure<StockCountDto>(CountNotFound);
        }

        var cancelled = count.Cancel();

        if (cancelled.IsFailure)
        {
            return Result.Failure<StockCountDto>(cancelled.Error);
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(count, varianceOnly: false, take: 500, ct));
    }

    public async Task<Result<string>> Handle(ExportStockCountQuery request, CancellationToken ct)
    {
        var count = await _db.StockCounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CountId, ct);

        if (count is null)
        {
            return Result.Failure<string>(CountNotFound);
        }

        var lines = await _db.StockCountLines.AsNoTracking()
            .Where(l => l.StockCountId == count.Id)
            .OrderBy(l => l.StockCode)
            .ToListAsync(ct);

        if (request.VarianceOnly)
        {
            lines = lines.Where(l => l.Variance != 0m).ToList();
        }

        var csv = new CsvWriter().Header("Code", "Description", "Counted", "System", "Variance", "Unit cost", "Variance value", "Notes");

        foreach (var line in lines)
        {
            csv.Row(
                line.StockCode,
                line.ProductName,
                line.CountedQty,
                line.SystemQtyAtCount,
                line.Variance,
                line.UnitCost,
                line.VarianceValue,
                line.Notes);
        }

        return Result.Success(csv.ToString());
    }

    /// <summary>
    /// Matches counted rows to items and writes the lines, snapshotting on-hand as it goes.
    /// <para>
    /// A code that matches nothing is reported rather than failing the import: on a real count sheet
    /// there is always a line someone wrote down wrong, and refusing the other four hundred because
    /// of it means the whole count gets re-keyed.
    /// </para>
    /// </summary>
    private async Task<Result<CountImportResult>> ImportAsync(
        Guid countId, IReadOnlyList<CountedItem> items, CancellationToken ct)
    {
        var count = await _db.StockCounts.FirstOrDefaultAsync(c => c.Id == countId, ct);

        if (count is null)
        {
            return Result.Failure<CountImportResult>(CountNotFound);
        }

        var open = count.EnsureOpen();

        if (open.IsFailure)
        {
            return Result.Failure<CountImportResult>(open.Error);
        }

        var codes = items.Select(i => i.StockCode.Trim().ToUpperInvariant()).Distinct().ToList();

        var productQuery = _db.Products.Where(p =>
            p.LocationId == count.LocationId && !p.IsDeleted && codes.Contains(p.StockCode));

        // A count scoped to one department must not let a code from another department in — that is
        // the difference between "we are short six" and "we did not count that aisle".
        if (count.DepartmentId is { } departmentId)
        {
            productQuery = productQuery.Where(p => p.DepartmentId == departmentId);
        }

        var products = await productQuery.ToDictionaryAsync(p => p.StockCode, p => p, ct);

        var existing = await _db.StockCountLines
            .Where(l => l.StockCountId == count.Id)
            .ToDictionaryAsync(l => l.ProductId, l => l, ct);

        var imported = 0;
        var updated = 0;
        var skipped = new List<string>();

        foreach (var item in items)
        {
            var code = item.StockCode.Trim().ToUpperInvariant();

            if (!products.TryGetValue(code, out var product))
            {
                skipped.Add($"{item.StockCode}: no such item here");
                continue;
            }

            if (existing.TryGetValue(product.Id, out var line))
            {
                var recounted = line.Recount(item.CountedQty, product.OnHand, item.Notes);

                if (recounted.IsFailure)
                {
                    skipped.Add($"{item.StockCode}: {recounted.Error.Message}");
                    continue;
                }

                updated++;
            }
            else
            {
                var created = StockCountLine.Create(
                    count.Id, product.Id, product.StockCode, product.Name,
                    item.CountedQty, product.OnHand, product.AvgCost, item.Notes);

                if (created.IsFailure)
                {
                    skipped.Add($"{item.StockCode}: {created.Error.Message}");
                    continue;
                }

                _db.StockCountLines.Add(created.Value);
                existing[product.Id] = created.Value;
                imported++;
            }

            if ((imported + updated) % ChunkSize == 0)
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        if (imported == 0 && updated == 0)
        {
            return Result.Failure<CountImportResult>(NothingImported.With("skipped", skipped.Count));
        }

        return Result.Success(new CountImportResult(imported, updated, skipped));
    }

    /// <summary>
    /// <c>StockCode,CountedQty[,Notes]</c>. A leading header row is recognised and dropped, because
    /// a spreadsheet export has one and a handheld's does not.
    /// </summary>
    public static (List<CountedItem> Items, List<string> Skipped) ParseCsv(string? csv)
    {
        var items = new List<CountedItem>();
        var skipped = new List<string>();

        if (string.IsNullOrWhiteSpace(csv))
        {
            return (items, skipped);
        }

        var lines = csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lineNumber = 0;

        foreach (var raw in lines)
        {
            lineNumber++;

            var fields = SplitCsvLine(raw);

            if (fields.Count < 2)
            {
                skipped.Add($"Line {lineNumber}: needs a code and a quantity");
                continue;
            }

            var code = fields[0].Trim();

            if (code.Length == 0)
            {
                skipped.Add($"Line {lineNumber}: no stock code");
                continue;
            }

            if (!decimal.TryParse(
                    fields[1].Trim(),
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var quantity))
            {
                // The header row lands here on line 1 and nowhere else, so it is dropped silently
                // rather than reported as a fault the operator has to look at.
                if (lineNumber > 1)
                {
                    skipped.Add($"Line {lineNumber}: '{fields[1]}' is not a quantity");
                }

                continue;
            }

            items.Add(new CountedItem(code, quantity, fields.Count > 2 ? fields[2].Trim() : null));
        }

        return (items, skipped);
    }

    /// <summary>
    /// Splits one CSV line, honouring quotes and doubled quotes — a description with a comma in it
    /// is exactly what a spreadsheet round-trip produces.
    /// </summary>
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    current.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private async Task<Dictionary<Guid, string>> DepartmentNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var distinct = ids.OfType<Guid>().Distinct().ToList();

        if (distinct.Count == 0)
        {
            return [];
        }

        return await _db.Departments.AsNoTracking()
            .Where(d => distinct.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);
    }

    private async Task<StockCountDto> ToDtoAsync(StockCount count, bool varianceOnly, int take, CancellationToken ct)
    {
        var all = await _db.StockCountLines.AsNoTracking()
            .Where(l => l.StockCountId == count.Id)
            .OrderBy(l => l.StockCode)
            .ToListAsync(ct);

        var shown = varianceOnly ? all.Where(l => l.Variance != 0m).ToList() : all;

        var departments = await DepartmentNamesAsync([count.DepartmentId], ct);

        return new StockCountDto(
            count.Id,
            count.CountNumber,
            count.LocationId,
            count.DepartmentId,
            count.DepartmentId is { } id ? departments.GetValueOrDefault(id) : null,
            count.Status,
            count.Notes,
            count.PostedAt,
            count.CreatedAt,
            all.Count,
            all.Count(l => l.Variance != 0m),
            all.Sum(l => l.VarianceValue),
            shown.Take(Math.Clamp(take, 1, 2000)).Select(l => new StockCountLineDto(
                l.Id,
                l.ProductId,
                l.StockCode,
                l.ProductName,
                l.CountedQty,
                l.SystemQtyAtCount,
                l.Variance,
                l.UnitCost,
                l.VarianceValue,
                l.Notes)).ToList());
    }
}
