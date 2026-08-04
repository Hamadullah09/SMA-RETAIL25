using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;

namespace Retail25.Application.Catalog;

public sealed record MatrixDimensionDto(int Position, string Name, IReadOnlyList<string> Values);

public sealed record ProductVariantDto(
    long Id,
    string VariantCode,
    string Dim1Value,
    string? Dim2Value,
    string? Dim3Value,
    string? Upc,
    decimal OnHand,
    bool IsActive);

public sealed record MatrixDto(
    long ProductId,
    string StockCode,
    string Name,
    IReadOnlyList<MatrixDimensionDto> Dimensions,
    IReadOnlyList<ProductVariantDto> Variants);

/// <summary>
/// Defines a matrix and generates the variants it implies (guide p.39–40).
/// <para>
/// Colour × size is a grid, and typing every cell by hand is how a catalogue ends up with "Med" and
/// "Medium" as different variants of the same shirt. Generating the cross product means the codes are
/// consistent by construction. Regenerating is additive: variants that already exist keep their
/// stock and their identity, because a variant id is referenced by sale history.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Write)]
public sealed record DefineMatrixCommand(
    long ProductId,
    IReadOnlyList<MatrixDimensionDto> Dimensions) : IRequest<Result<MatrixDto>>;

[RequiresPermission(PermissionKeys.Catalog.Read)]
public sealed record GetMatrixQuery(long ProductId) : IRequest<Result<MatrixDto>>;

/// <summary>Variants with stock at a location — the picker the till shows for a matrix item.</summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record ListVariantsQuery(long ProductId, long LocationId, bool InStockOnly = false)
    : IRequest<IReadOnlyList<ProductVariantDto>>;

public sealed class MatrixHandlers
    : IRequestHandler<DefineMatrixCommand, Result<MatrixDto>>,
      IRequestHandler<GetMatrixQuery, Result<MatrixDto>>,
      IRequestHandler<ListVariantsQuery, IReadOnlyList<ProductVariantDto>>
{
    public static readonly Error ProductNotFound = new("product.not_found", "No such item.");
    public static readonly Error DimensionsRequired = new("matrix.dimensions_required", "A matrix needs at least one dimension with values.");
    public static readonly Error TooManyVariants = new("matrix.too_many_variants", "That combination would create more variants than a matrix can usefully hold.");

    /// <summary>
    /// A guard, not a limit anyone should reach. Three dimensions of twenty values is eight thousand
    /// variants; past that the grid stops being something a person can manage and the generation is
    /// almost certainly a mistake in the values.
    /// </summary>
    private const int MaxVariants = 2000;

    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;

    public MatrixHandlers(IApplicationDbContext db, IPosNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
    }

    public async Task<Result<MatrixDto>> Handle(DefineMatrixCommand request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted, ct);
        if (product is null)
        {
            return Result.Failure<MatrixDto>(ProductNotFound.With("productId", request.ProductId));
        }

        var dimensions = (request.Dimensions ?? [])
            .Where(d => d.Values.Count > 0)
            .OrderBy(d => d.Position)
            .Take(3)
            .ToList();

        if (dimensions.Count == 0)
        {
            return Result.Failure<MatrixDto>(DimensionsRequired);
        }

        var combinations = dimensions.Aggregate(1, (total, d) => total * d.Values.Count);
        if (combinations > MaxVariants)
        {
            return Result.Failure<MatrixDto>(TooManyVariants.With("combinations", combinations));
        }

        // The item becomes a matrix item by being given a matrix; the type is not a separate decision.
        if (product.Type != ProductType.Matrix)
        {
            product.SetType(ProductType.Matrix);
        }

        await ReplaceDimensionsAsync(product.Id, dimensions, ct);
        await GenerateVariantsAsync(product.Id, dimensions, ct);

        await _db.SaveChangesAsync(ct);
        await _notifier.ProductChangedAsync(product.LocationId, product.Id, ct);

        return await BuildAsync(product, ct);
    }

    public async Task<Result<MatrixDto>> Handle(GetMatrixQuery request, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted, ct);

        return product is null
            ? Result.Failure<MatrixDto>(ProductNotFound.With("productId", request.ProductId))
            : await BuildAsync(product, ct);
    }

    public async Task<IReadOnlyList<ProductVariantDto>> Handle(ListVariantsQuery request, CancellationToken ct)
    {
        var variants = await _db.ProductVariants.AsNoTracking()
            .Where(v => v.ProductId == request.ProductId && v.IsActive)
            .OrderBy(v => v.Dim1Value).ThenBy(v => v.Dim2Value).ThenBy(v => v.Dim3Value)
            .ToListAsync(ct);

        // Stock is per location, so the picker at one shop must not offer another shop's inventory.
        var levels = await _db.StockLevels.AsNoTracking()
            .Where(s => s.ProductId == request.ProductId && s.LocationId == request.LocationId && s.VariantId != null)
            .ToDictionaryAsync(s => s.VariantId!.Value, s => s.OnHand, ct);

        var dtos = variants
            .Select(v => ToDto(v, levels.TryGetValue(v.Id, out var onHand) ? onHand : v.OnHand))
            .ToList();

        return request.InStockOnly ? dtos.Where(v => v.OnHand > 0).ToList() : dtos;
    }

    private async Task ReplaceDimensionsAsync(long productId, List<MatrixDimensionDto> dimensions, CancellationToken ct)
    {
        var existing = await _db.MatrixDimensions.Where(d => d.ProductId == productId).ToListAsync(ct);
        _db.MatrixDimensions.RemoveRange(existing);

        foreach (var dimension in dimensions)
        {
            var created = MatrixDimension.Create(productId, dimension.Position, dimension.Name);
            if (created.IsSuccess)
            {
                _db.MatrixDimensions.Add(created.Value);
            }
        }
    }

    /// <summary>
    /// Adds the combinations that do not exist yet and deactivates the ones no longer in the grid.
    /// Deactivating rather than deleting is deliberate: a variant that has ever been sold is named by
    /// sale lines, and removing it would orphan history.
    /// </summary>
    private async Task GenerateVariantsAsync(long productId, List<MatrixDimensionDto> dimensions, CancellationToken ct)
    {
        var existing = await _db.ProductVariants.Where(v => v.ProductId == productId).ToListAsync(ct);
        var byCode = existing.ToDictionary(v => v.VariantCode, StringComparer.OrdinalIgnoreCase);

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var combination in CrossProduct(dimensions))
        {
            var code = string.Join('-', combination.Select(Slug)).ToUpperInvariant();
            wanted.Add(code);

            if (byCode.ContainsKey(code))
            {
                continue;
            }

            var created = ProductVariant.Create(
                productId,
                combination[0],
                code,
                combination.Count > 1 ? combination[1] : null,
                combination.Count > 2 ? combination[2] : null);

            if (created.IsSuccess)
            {
                _db.ProductVariants.Add(created.Value);
            }
        }

        foreach (var variant in existing.Where(v => !wanted.Contains(v.VariantCode) && v.IsActive))
        {
            variant.SetActive(false);
        }
    }

    private static IEnumerable<List<string>> CrossProduct(List<MatrixDimensionDto> dimensions)
    {
        IEnumerable<List<string>> combinations = [[]];

        foreach (var dimension in dimensions)
        {
            combinations = combinations.SelectMany(
                _ => dimension.Values,
                (existing, value) => new List<string>(existing) { value });
        }

        return combinations;
    }

    /// <summary>Keeps generated codes scannable: letters and digits only, so "X-Large" becomes XLARGE.</summary>
    private static string Slug(string value)
        => new([.. value.Trim().Where(char.IsLetterOrDigit)]);

    private async Task<Result<MatrixDto>> BuildAsync(Product product, CancellationToken ct)
    {
        var dimensions = await _db.MatrixDimensions.AsNoTracking()
            .Where(d => d.ProductId == product.Id)
            .OrderBy(d => d.Position)
            .ToListAsync(ct);

        var variants = await _db.ProductVariants.AsNoTracking()
            .Where(v => v.ProductId == product.Id)
            .OrderBy(v => v.Dim1Value).ThenBy(v => v.Dim2Value).ThenBy(v => v.Dim3Value)
            .ToListAsync(ct);

        var dimensionDtos = dimensions.Select(d => new MatrixDimensionDto(
            d.Position,
            d.Name,
            ValuesFor(variants, d.Position))).ToList();

        return Result.Success(new MatrixDto(
            product.Id,
            product.StockCode,
            product.Name,
            dimensionDtos,
            variants.Select(v => ToDto(v, v.OnHand)).ToList()));
    }

    private static IReadOnlyList<string> ValuesFor(IReadOnlyList<ProductVariant> variants, int position)
        => variants
            .Select(v => position switch
            {
                1 => v.Dim1Value,
                2 => v.Dim2Value,
                _ => v.Dim3Value,
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static ProductVariantDto ToDto(ProductVariant variant, decimal onHand)
        => new(
            variant.Id,
            variant.VariantCode,
            variant.Dim1Value,
            variant.Dim2Value,
            variant.Dim3Value,
            variant.Upc,
            onHand,
            variant.IsActive);
}
