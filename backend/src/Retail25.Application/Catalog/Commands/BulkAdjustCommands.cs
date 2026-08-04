using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;

namespace Retail25.Application.Catalog.Commands;

/// <summary>Which figure a batch adjustment moves.</summary>
public enum BulkPriceTarget
{
    /// <summary>The shelf price.</summary>
    RegularPrice = 0,

    /// <summary>The buying cost. Moves the margin, not what the customer pays.</summary>
    LastCost = 1,
}

public enum BulkAdjustMethod
{
    /// <summary>Up or down by a percentage of the current figure.</summary>
    Percentage = 0,

    /// <summary>Up or down by a fixed amount of money.</summary>
    FixedAmount = 1,

    /// <summary>Set every matching item to the same figure.</summary>
    SetTo = 2,

    /// <summary>Price from cost: cost × (1 + margin%). Only valid for <see cref="BulkPriceTarget.RegularPrice"/>.</summary>
    MarkupOnCost = 3,
}

/// <summary>How prices land after the arithmetic — a shelf full of £4.8737 is nobody's intent.</summary>
public enum PriceRounding
{
    None = 0,

    /// <summary>Nearest penny.</summary>
    NearestCent = 1,

    /// <summary>x.x9 — the classic charm price.</summary>
    EndsIn99 = 2,

    /// <summary>x.x5.</summary>
    EndsIn95 = 3,

    /// <summary>Whole units.</summary>
    WholeNumber = 4,
}

/// <summary>Which items a batch operation touches. Empty means every item at the location.</summary>
public sealed record BulkFilter(
    long LocationId,
    long? DepartmentId = null,
    long? CategoryId = null,
    long? SupplierId = null,
    string? Search = null,
    ProductType? Type = null);

/// <summary>
/// One item's before-and-after, for the preview. The operator sees this before anything is written —
/// a batch reprice is the single most destructive thing in the back office, and "undo" is a restore
/// from backup.
/// </summary>
public sealed record BulkPricePreviewRow(
    long ProductId,
    string StockCode,
    string Name,
    decimal Current,
    decimal Proposed,
    decimal AvgCost,
    decimal ProposedMarginPct);

public sealed record BulkPricePreview(
    IReadOnlyList<BulkPricePreviewRow> Rows,
    int MatchedCount,
    int ShownCount,
    int WouldGoNegative);

/// <summary>
/// What a batch reprice would do, without doing it (guide p.45).
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.BulkAdjust)]
public sealed record PreviewBulkPriceChangeQuery(
    BulkFilter Filter,
    BulkPriceTarget Target,
    BulkAdjustMethod Method,
    decimal Amount,
    PriceRounding Rounding = PriceRounding.NearestCent,
    int Take = 200) : IRequest<Result<BulkPricePreview>>;

/// <summary>The same calculation, written. Returns how many rows changed.</summary>
[RequiresPermission(PermissionKeys.Catalog.BulkAdjust)]
public sealed record ApplyBulkPriceChangeCommand(
    BulkFilter Filter,
    BulkPriceTarget Target,
    BulkAdjustMethod Method,
    decimal Amount,
    PriceRounding Rounding = PriceRounding.NearestCent) : IRequest<Result<int>>;

/// <summary>
/// Switches the two taxability flags across a selection (guide p.31). Null leaves a flag alone, so
/// "make this department tax-1 exempt" does not silently reset tax 2 as well.
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.BulkAdjust)]
public sealed record ApplyBulkTaxChangeCommand(
    BulkFilter Filter,
    bool? Tax1Applies,
    bool? Tax2Applies) : IRequest<Result<int>>;

public sealed class BulkAdjustHandlers :
    IRequestHandler<PreviewBulkPriceChangeQuery, Result<BulkPricePreview>>,
    IRequestHandler<ApplyBulkPriceChangeCommand, Result<int>>,
    IRequestHandler<ApplyBulkTaxChangeCommand, Result<int>>
{
    public static readonly Error NothingMatched = new(
        "bulk.nothing_matched",
        "No items match that selection.");

    public static readonly Error MarkupNeedsPrice = new(
        "bulk.markup_needs_price",
        "Pricing from cost only applies to the shelf price.");

    public static readonly Error NoChangeRequested = new(
        "bulk.no_change_requested",
        "Choose at least one tax flag to change.");

    public static readonly Error WouldGoNegative = new(
        "bulk.would_go_negative",
        "That would take one or more items below zero. Narrow the selection or reduce the amount.");

    /// <summary>
    /// How many rows are updated between SaveChanges calls. A whole-catalogue reprice can be tens of
    /// thousands of rows; one flush at the end holds every change tracker entry in memory and sends
    /// one enormous statement, and one flush per row sends tens of thousands of them.
    /// </summary>
    private const int ChunkSize = 500;

    private readonly IApplicationDbContext _db;

    public BulkAdjustHandlers(IApplicationDbContext db) => _db = db;

    public async Task<Result<BulkPricePreview>> Handle(PreviewBulkPriceChangeQuery request, CancellationToken ct)
    {
        if (request.Method == BulkAdjustMethod.MarkupOnCost && request.Target != BulkPriceTarget.RegularPrice)
        {
            return Result.Failure<BulkPricePreview>(MarkupNeedsPrice);
        }

        var query = await BuildQueryAsync(request.Filter, ct);

        var matched = await query.CountAsync(ct);

        if (matched == 0)
        {
            return Result.Failure<BulkPricePreview>(NothingMatched);
        }

        var take = Math.Clamp(request.Take, 1, 500);

        var sample = await query.AsNoTracking().OrderBy(p => p.StockCode).Take(take).ToListAsync(ct);

        var rows = sample.Select(product =>
        {
            var current = Current(product, request.Target);
            var proposed = Apply(current, product, request.Target, request.Method, request.Amount, request.Rounding);

            return new BulkPricePreviewRow(
                product.Id,
                product.StockCode,
                product.Name,
                current,
                proposed,
                product.AvgCost,
                MarginPct(request.Target == BulkPriceTarget.RegularPrice ? proposed : product.RegularPrice, product.AvgCost));
        }).ToList();

        // Counted across the whole selection rather than the sample: the operator needs to know a
        // hundred items would go negative even when only two hundred are shown. Charm rounding is
        // not expressible in SQL, so the figures have to come back — but only the three columns the
        // arithmetic reads, not whole entities.
        var figures = await query.AsNoTracking()
            .Select(p => new { p.RegularPrice, p.LastCost, p.AvgCost })
            .ToListAsync(ct);

        var negative = figures.Count(f => Apply(
            request.Target == BulkPriceTarget.RegularPrice ? f.RegularPrice : f.LastCost,
            f.AvgCost,
            request.Method,
            request.Amount,
            request.Rounding) < 0m);

        return Result.Success(new BulkPricePreview(rows, matched, rows.Count, negative));
    }

    public async Task<Result<int>> Handle(ApplyBulkPriceChangeCommand request, CancellationToken ct)
    {
        if (request.Method == BulkAdjustMethod.MarkupOnCost && request.Target != BulkPriceTarget.RegularPrice)
        {
            return Result.Failure<int>(MarkupNeedsPrice);
        }

        var query = await BuildQueryAsync(request.Filter, ct);
        var products = await query.ToListAsync(ct);

        if (products.Count == 0)
        {
            return Result.Failure<int>(NothingMatched);
        }

        // Checked before anything is written: a batch that fails halfway leaves a catalogue where
        // some shelves are repriced and some are not, which is worse than a batch that refuses.
        if (products.Any(p => Apply(
                Current(p, request.Target), p, request.Target, request.Method, request.Amount, request.Rounding) < 0m))
        {
            return Result.Failure<int>(WouldGoNegative);
        }

        var changed = 0;

        foreach (var product in products)
        {
            var current = Current(product, request.Target);
            var proposed = Apply(current, product, request.Target, request.Method, request.Amount, request.Rounding);

            if (proposed == current)
            {
                continue;
            }

            if (request.Target == BulkPriceTarget.RegularPrice)
            {
                product.UpdatePricing(proposed, product.LastCost, product.AvgCost);
            }
            else
            {
                product.UpdatePricing(product.RegularPrice, proposed, product.AvgCost);
            }

            changed++;

            if (changed % ChunkSize == 0)
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(changed);
    }

    public async Task<Result<int>> Handle(ApplyBulkTaxChangeCommand request, CancellationToken ct)
    {
        if (request.Tax1Applies is null && request.Tax2Applies is null)
        {
            return Result.Failure<int>(NoChangeRequested);
        }

        var query = await BuildQueryAsync(request.Filter, ct);
        var products = await query.ToListAsync(ct);

        if (products.Count == 0)
        {
            return Result.Failure<int>(NothingMatched);
        }

        var changed = 0;

        foreach (var product in products)
        {
            var tax1 = request.Tax1Applies ?? product.Tax1Applies;
            var tax2 = request.Tax2Applies ?? product.Tax2Applies;

            if (tax1 == product.Tax1Applies && tax2 == product.Tax2Applies)
            {
                continue;
            }

            // SetTaxFlags refuses to make a gift card taxable, so this can be a no-op even when the
            // flags asked for differ. Counting after the call keeps the reported figure honest.
            product.SetTaxFlags(tax1, tax2);

            if (product.Tax1Applies == tax1 && product.Tax2Applies == tax2)
            {
                changed++;
            }

            if (changed > 0 && changed % ChunkSize == 0)
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(changed);
    }

    /// <summary>
    /// The selection. Supplier is resolved to a set of product ids first — it lives on a link table,
    /// and joining through it inside the main predicate turns the whole thing into a subquery per row.
    /// </summary>
    private async Task<IQueryable<Product>> BuildQueryAsync(BulkFilter filter, CancellationToken ct)
    {
        var query = _db.Products.Where(p => p.LocationId == filter.LocationId && !p.IsDeleted);

        if (filter.DepartmentId is { } departmentId)
        {
            query = query.Where(p => p.DepartmentId == departmentId);
        }

        if (filter.CategoryId is { } categoryId)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(p => p.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(p => p.StockCode.Contains(term) || p.Name.Contains(term));
        }

        if (filter.SupplierId is { } supplierId)
        {
            var productIds = await _db.ProductSuppliers.AsNoTracking()
                .Where(link => link.SupplierId == supplierId)
                .Select(link => link.ProductId)
                .ToListAsync(ct);

            query = query.Where(p => productIds.Contains(p.Id));
        }

        return query;
    }

    private static decimal Current(Product product, BulkPriceTarget target)
        => target == BulkPriceTarget.RegularPrice ? product.RegularPrice : product.LastCost;

    private static decimal Apply(
        decimal current,
        Product product,
        BulkPriceTarget target,
        BulkAdjustMethod method,
        decimal amount,
        PriceRounding rounding)
        => Apply(current, product.AvgCost, method, amount, rounding);

    /// <summary>
    /// The arithmetic, over just the figures it reads. Taking the three numbers rather than a
    /// <c>Product</c> is what lets the preview count negatives across a whole catalogue from a
    /// projection instead of materialising every entity.
    /// </summary>
    public static decimal Apply(
        decimal current,
        decimal avgCost,
        BulkAdjustMethod method,
        decimal amount,
        PriceRounding rounding)
    {
        var raw = method switch
        {
            BulkAdjustMethod.Percentage => current * (1m + (amount / 100m)),
            BulkAdjustMethod.FixedAmount => current + amount,
            BulkAdjustMethod.SetTo => amount,

            // Priced off average cost rather than last cost: last cost is one delivery's price and
            // can be an outlier, while average is what the stock on the shelf actually cost.
            BulkAdjustMethod.MarkupOnCost => avgCost * (1m + (amount / 100m)),
            _ => current,
        };

        return Round(raw, rounding);
    }

    public static decimal Round(decimal value, PriceRounding rounding)
    {
        if (rounding == PriceRounding.None)
        {
            return value;
        }

        // Charm rounding on a negative number is meaningless, and forcing -4.99 out of -5.02 would
        // be a strange thing to do quietly. Everything negative just rounds to the penny; the caller
        // refuses the batch anyway.
        if (value < 0m)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        return rounding switch
        {
            PriceRounding.NearestCent => Math.Round(value, 2, MidpointRounding.AwayFromZero),
            PriceRounding.WholeNumber => Math.Round(value, 0, MidpointRounding.AwayFromZero),
            PriceRounding.EndsIn99 => CharmPrice(value, 0.99m),
            PriceRounding.EndsIn95 => CharmPrice(value, 0.95m),
            _ => value,
        };
    }

    /// <summary>
    /// Snaps to the nearest x.99 (or x.95). Rounding 4.20 up to 4.99 would be a 19% rise nobody
    /// asked for, so the whole-unit part is chosen by which candidate is closest.
    /// </summary>
    private static decimal CharmPrice(decimal value, decimal ending)
    {
        var whole = Math.Floor(value);
        var candidate = whole + ending;

        if (candidate < value)
        {
            candidate += 1m;
        }

        var lower = candidate - 1m;

        if (lower >= 0m && value - lower < candidate - value)
        {
            return lower;
        }

        return candidate;
    }

    private static decimal MarginPct(decimal price, decimal cost)
        => price <= 0m ? 0m : Math.Round((price - cost) / price * 100m, 2, MidpointRounding.AwayFromZero);
}
