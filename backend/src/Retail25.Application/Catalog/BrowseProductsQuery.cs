using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;

namespace Retail25.Application.Catalog;

/// <summary>Columns the inventory browse can be ordered by. A closed set, because it becomes SQL.</summary>
public enum ProductSort
{
    StockCode = 0,
    Name = 1,
    OnHand = 2,
    RegularPrice = 3,
    Margin = 4,
}

/// <summary>
/// The Browse View for inventory (guide p.23–24, doc 08 §Screen inventory).
/// <para>
/// Keyset-paged rather than offset-paged. A store with fifty thousand items is exactly where
/// <c>OFFSET</c> stops being acceptable, and it is also where a row inserted mid-scroll would shift
/// every page after it — so the user sees one item twice and never sees another.
/// </para>
/// <para>
/// <see cref="DeletedOnly"/> is what makes this query serve the legacy "Undelete Items" screen
/// (guide p.24) too: deleted rows are hidden by default and listed on demand, rather than living in a
/// separate table that could drift out of step with this one.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Read)]
public sealed record BrowseProductsQuery(
    long LocationId,
    string? Search = null,
    long? DepartmentId = null,
    long? CategoryId = null,
    ProductType? Type = null,
    bool BelowReorderPoint = false,
    bool DeletedOnly = false,
    ProductSort Sort = ProductSort.StockCode,
    bool Descending = false,
    string? Cursor = null,
    int PageSize = 50) : IRequest<CursorPage<ProductRowDto>>;

/// <summary>The full Form View record for one item.</summary>
[RequiresPermission(PermissionKeys.Catalog.Read)]
public sealed record GetProductFormQuery(long ProductId) : IRequest<Result<ProductFormDto>>;

public sealed class BrowseProductsHandlers
    : IRequestHandler<BrowseProductsQuery, CursorPage<ProductRowDto>>,
      IRequestHandler<GetProductFormQuery, Result<ProductFormDto>>
{
    public static readonly Error NotFound = new("product.not_found", "No such item.");

    private readonly IApplicationDbContext _db;

    public BrowseProductsHandlers(IApplicationDbContext db) => _db = db;

    public async Task<CursorPage<ProductRowDto>> Handle(BrowseProductsQuery request, CancellationToken ct)
    {
        var pageSize = Cursor.PageSize(request.PageSize);
        var query = _db.Products.AsNoTracking()
            .Where(p => p.LocationId == request.LocationId && p.IsDeleted == request.DeletedOnly);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // The same three ways a cashier already identifies an item, so the back office and the
            // till agree on what "search" means.
            var term = request.Search.Trim();
            query = query.Where(p =>
                p.StockCode.Contains(term) ||
                p.Name.Contains(term) ||
                (p.Upc != null && p.Upc.Contains(term)));
        }

        if (request.DepartmentId is { } departmentId)
        {
            query = query.Where(p => p.DepartmentId == departmentId);
        }

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (request.Type is { } type)
        {
            query = query.Where(p => p.Type == type);
        }

        if (request.BelowReorderPoint)
        {
            // The legacy reorder report, as a browse filter: what needs buying, right now.
            query = query.Where(p => p.ReorderPoint > 0 && p.OnHand + p.OnOrder <= p.ReorderPoint);
        }

        // One extra row answers "is there another page" without a second count query.
        var products = await Paginate(query, request).Take(pageSize + 1).ToListAsync(ct);

        var hasMore = products.Count > pageSize;
        if (hasMore)
        {
            products.RemoveAt(products.Count - 1);
        }

        var rows = await ProjectAsync(products, ct);

        var nextCursor = hasMore && products.Count > 0
            ? Cursor.Encode(SortKeyOf(products[^1], request.Sort), products[^1].StockCode)
            : null;

        return new CursorPage<ProductRowDto>(rows, nextCursor, hasMore);
    }

    /// <summary>
    /// Applies the sort and, when a cursor is present, the predicate that resumes immediately after
    /// the previous page's last row.
    /// <para>
    /// Written out per column rather than through a generic key selector. A compiled delegate called
    /// inside a <c>Where</c> cannot be translated to SQL — it would silently work against the
    /// in-memory provider used by the tests and then pull the whole table into memory in production.
    /// </para>
    /// <para>
    /// The stock code is the tie-breaker throughout because it is unique per location and is the
    /// column the grid's index is on. The primary key would also be unique, but <c>uuid</c> ordering
    /// is arbitrary and matches no index the browse uses.
    /// </para>
    /// </summary>
    private static IQueryable<Product> Paginate(IQueryable<Product> query, BrowseProductsQuery request)
    {
        var after = Cursor.Decode(request.Cursor);
        var tie = after?.TieBreak ?? string.Empty;
        var descending = request.Descending;

        switch (request.Sort)
        {
            case ProductSort.Name:
            {
                if (after is { } position)
                {
                    var key = position.SortKey;
                    query = descending
                        ? query.Where(p => p.Name.CompareTo(key) < 0 || (p.Name == key && p.StockCode.CompareTo(tie) < 0))
                        : query.Where(p => p.Name.CompareTo(key) > 0 || (p.Name == key && p.StockCode.CompareTo(tie) > 0));
                }

                return descending
                    ? query.OrderByDescending(p => p.Name).ThenByDescending(p => p.StockCode)
                    : query.OrderBy(p => p.Name).ThenBy(p => p.StockCode);
            }

            case ProductSort.OnHand:
            {
                if (Cursor.Decimal(after?.SortKey) is { } key)
                {
                    query = descending
                        ? query.Where(p => p.OnHand < key || (p.OnHand == key && p.StockCode.CompareTo(tie) < 0))
                        : query.Where(p => p.OnHand > key || (p.OnHand == key && p.StockCode.CompareTo(tie) > 0));
                }

                return descending
                    ? query.OrderByDescending(p => p.OnHand).ThenByDescending(p => p.StockCode)
                    : query.OrderBy(p => p.OnHand).ThenBy(p => p.StockCode);
            }

            case ProductSort.RegularPrice:
            {
                if (Cursor.Decimal(after?.SortKey) is { } key)
                {
                    query = descending
                        ? query.Where(p => p.RegularPrice < key || (p.RegularPrice == key && p.StockCode.CompareTo(tie) < 0))
                        : query.Where(p => p.RegularPrice > key || (p.RegularPrice == key && p.StockCode.CompareTo(tie) > 0));
                }

                return descending
                    ? query.OrderByDescending(p => p.RegularPrice).ThenByDescending(p => p.StockCode)
                    : query.OrderBy(p => p.RegularPrice).ThenBy(p => p.StockCode);
            }

            case ProductSort.Margin:
            {
                if (Cursor.Decimal(after?.SortKey) is { } key)
                {
                    query = descending
                        ? query.Where(p => p.GrossMarginPct < key || (p.GrossMarginPct == key && p.StockCode.CompareTo(tie) < 0))
                        : query.Where(p => p.GrossMarginPct > key || (p.GrossMarginPct == key && p.StockCode.CompareTo(tie) > 0));
                }

                return descending
                    ? query.OrderByDescending(p => p.GrossMarginPct).ThenByDescending(p => p.StockCode)
                    : query.OrderBy(p => p.GrossMarginPct).ThenBy(p => p.StockCode);
            }

            default:
            {
                if (after is { } position)
                {
                    var key = position.SortKey;
                    query = descending
                        ? query.Where(p => p.StockCode.CompareTo(key) < 0)
                        : query.Where(p => p.StockCode.CompareTo(key) > 0);
                }

                return descending
                    ? query.OrderByDescending(p => p.StockCode)
                    : query.OrderBy(p => p.StockCode);
            }
        }
    }

    private static string SortKeyOf(Product product, ProductSort sort) => sort switch
    {
        ProductSort.Name => product.Name,
        ProductSort.OnHand => Cursor.Number(product.OnHand),
        ProductSort.RegularPrice => Cursor.Number(product.RegularPrice),
        ProductSort.Margin => Cursor.Number(product.GrossMarginPct),
        _ => product.StockCode,
    };

    public async Task<Result<ProductFormDto>> Handle(GetProductFormQuery request, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null)
        {
            return Result.Failure<ProductFormDto>(NotFound.With("productId", request.ProductId));
        }

        var levels = await _db.ProductPrices.AsNoTracking()
            .Where(p => p.ProductId == product.Id).OrderBy(p => p.Level).ToListAsync(ct);

        var breaks = await _db.PriceBreaks.AsNoTracking()
            .Where(b => b.ProductId == product.Id).OrderBy(b => b.MinQuantity).ToListAsync(ct);

        var sale = await _db.SalePricings.AsNoTracking().FirstOrDefaultAsync(s => s.ProductId == product.Id, ct);
        var bonus = await _db.BonusPricings.AsNoTracking().FirstOrDefaultAsync(b => b.ProductId == product.Id, ct);

        var links = await _db.ProductSuppliers.AsNoTracking()
            .Where(s => s.ProductId == product.Id).OrderBy(s => s.Rank).ToListAsync(ct);

        var supplierIds = links.Select(s => s.SupplierId).Distinct().ToList();
        var supplierNames = supplierIds.Count == 0
            ? []
            : await _db.Suppliers.AsNoTracking()
                .Where(s => supplierIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Company, ct);

        var components = await _db.KitComponents.AsNoTracking()
            .Where(k => k.KitProductId == product.Id).ToListAsync(ct);

        var referencedIds = new List<long>();
        AddIfPresent(referencedIds, product.SubstituteProductId);
        AddIfPresent(referencedIds, product.TagAlongProductId);
        AddIfPresent(referencedIds, product.ParentProductId);
        referencedIds.AddRange(components.Select(c => c.ComponentProductId));

        var referenced = referencedIds.Count == 0
            ? []
            : await _db.Products.AsNoTracking()
                .Where(p => referencedIds.Contains(p.Id))
                .Select(p => new LinkedProductDto(p.Id, p.StockCode, p.Name))
                .ToDictionaryAsync(p => p.Id, p => p, ct);

        var departmentName = product.DepartmentId is { } deptId
            ? await _db.Departments.AsNoTracking().Where(d => d.Id == deptId).Select(d => d.Name).FirstOrDefaultAsync(ct)
            : null;

        var categoryName = product.CategoryId is { } catId
            ? await _db.Categories.AsNoTracking().Where(c => c.Id == catId).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : null;

        return Result.Success(new ProductFormDto(
            product.Id,
            product.LocationId,
            product.StockCode,
            product.Name,
            product.Description,
            product.Type,
            product.Upc,
            product.Tax1Applies,
            product.Tax2Applies,
            product.RegularPrice,
            product.LastCost,
            product.AvgCost,
            product.GrossMarginPct,
            product.BaseStock,
            product.ReorderPoint,
            product.ReorderQty,
            product.OnHand,
            product.OnOrder,
            product.CaseQty,
            product.ShipWeight,
            product.BinLocation,
            product.PosMessage,
            product.InvoiceMessage,
            product.Notes,
            product.DepartmentId,
            departmentName,
            product.CategoryId,
            categoryName,
            Lookup(referenced, product.SubstituteProductId),
            Lookup(referenced, product.TagAlongProductId),
            Lookup(referenced, product.ParentProductId),
            levels.Select(l => new ProductPriceDto(l.Level, l.Price)).ToList(),
            breaks.Select(b => new PriceBreakDto(b.Level, b.MinQuantity)).ToList(),
            sale is null ? null : new SalePricingDto(sale.DiscountPct, sale.StartsOn, sale.EndsOn),
            bonus is null ? null : new BonusPricingDto(bonus.BuyQty, bonus.FreeQty),
            links.Select(s => new ProductSupplierDto(
                s.SupplierId,
                supplierNames.TryGetValue(s.SupplierId, out var company) ? company : string.Empty,
                s.Rank,
                s.Cost,
                s.ReorderNumber,
                s.CaseQty,
                s.MinimumOrderQty)).ToList(),
            components.Select(c => new KitComponentDto(
                c.ComponentProductId,
                referenced.TryGetValue(c.ComponentProductId, out var component) ? component.StockCode : string.Empty,
                referenced.TryGetValue(c.ComponentProductId, out var named) ? named.Name : string.Empty,
                c.Quantity)).ToList(),
            product.HasImage,
            product.IsDeleted,
            product.CreatedAt,
            product.ModifiedAt));
    }

    private async Task<List<ProductRowDto>> ProjectAsync(List<Product> products, CancellationToken ct)
    {
        if (products.Count == 0)
        {
            return [];
        }

        var departmentIds = products.Where(p => p.DepartmentId.HasValue).Select(p => p.DepartmentId!.Value).Distinct().ToList();
        var departments = departmentIds.Count == 0
            ? []
            : await _db.Departments.AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var categoryIds = products.Where(p => p.CategoryId.HasValue).Select(p => p.CategoryId!.Value).Distinct().ToList();
        var categories = categoryIds.Count == 0
            ? []
            : await _db.Categories.AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return products.Select(p => ToRow(p, departments, categories)).ToList();
    }

    /// <summary>
    /// Builds one grid row. Shared with the commands so a row patched over SignalR after an edit is
    /// byte-for-byte the row the grid would have fetched — otherwise a live update and a refresh
    /// disagree, and the user learns not to trust the grid.
    /// </summary>
    internal static ProductRowDto ToRow(
        Product product,
        IReadOnlyDictionary<long, string> departments,
        IReadOnlyDictionary<long, string> categories)
        => new(
            product.Id,
            product.StockCode,
            product.Name,
            product.Type,
            product.DepartmentId is { } d && departments.TryGetValue(d, out var deptName) ? deptName : null,
            product.CategoryId is { } c && categories.TryGetValue(c, out var catName) ? catName : null,
            product.RegularPrice,
            product.AvgCost,
            product.GrossMarginPct,
            product.OnHand,
            product.OnOrder,
            product.ReorderPoint,
            product.Tax1Applies,
            product.Tax2Applies,
            product.Upc,
            product.IsDeleted);

    private static void AddIfPresent(List<long> target, long? id)
    {
        if (id is { } value)
        {
            target.Add(value);
        }
    }

    private static LinkedProductDto? Lookup(IReadOnlyDictionary<long, LinkedProductDto> map, long? id)
        => id is { } value && map.TryGetValue(value, out var dto) ? dto : null;
}
