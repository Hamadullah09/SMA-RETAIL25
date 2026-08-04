using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;

namespace Retail25.Application.Catalog;

/// <summary>One tile, or one row. What the till's grid draws, and nothing it does not.</summary>
public sealed record PosGridItemDto(
    long Id,
    string StockCode,
    string Name,
    decimal RegularPrice,
    decimal OnHand,
    ProductType Type,
    bool HasImage,
    long? DepartmentId,
    long? CategoryId);

/// <summary>A heading the grid filters by — a department or a category, laid out the same way.</summary>
public sealed record PosGridGroupDto(long Id, string Name, string? Code, int SortOrder, int ItemCount);

/// <summary>
/// The grid's contents plus the headings above it.
/// <para>
/// <see cref="AnyImages"/> is the reason this is answered on the server. It says whether anything in
/// the <i>current filter</i> has a picture, so the till can choose tiles or rows before it draws
/// anything. Deciding that from the returned page would be wrong: thirty picture-less items at the
/// top of an alphabet is not a picture-less catalogue, and the layout would flip as the user scrolled.
/// </para>
/// </summary>
public sealed record PosGridDto(
    IReadOnlyList<PosGridItemDto> Items,
    IReadOnlyList<PosGridGroupDto> Departments,
    IReadOnlyList<PosGridGroupDto> Categories,
    int Total,
    bool AnyImages);

/// <summary>
/// The till's product picker (doc 08).
/// <para>
/// Separate from <see cref="BrowseProductsQuery"/> on purpose. That one is the back office's inventory
/// grid — keyset-paged, sortable by margin, able to list deleted rows. A cashier picking an item wants
/// none of that and does want two things it has no reason to carry: a picture flag, and the counts
/// that let the category strip grey out headings with nothing behind them.
/// </para>
/// <para>
/// Offset paging rather than keyset, because this pages by tapping "more" through a few hundred items
/// in one department, not by scrolling fifty thousand.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Read)]
public sealed record PosGridQuery(
    long LocationId,
    long? DepartmentId = null,
    long? CategoryId = null,
    string? Search = null,
    int Skip = 0,
    int Take = 60) : IRequest<Result<PosGridDto>>;

public sealed class PosGridHandler : IRequestHandler<PosGridQuery, Result<PosGridDto>>
{
    /// <summary>A till screen holds well under this; the cap stops a caller asking for the lot.</summary>
    private const int MaximumTake = 200;

    private readonly IApplicationDbContext _db;

    public PosGridHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<PosGridDto>> Handle(PosGridQuery request, CancellationToken ct)
    {
        var sellable = _db.Products.AsNoTracking()
            .Where(p => !p.IsDeleted && p.LocationId == request.LocationId);

        var filtered = sellable;

        if (request.DepartmentId is { } department)
        {
            filtered = filtered.Where(p => p.DepartmentId == department);
        }

        if (request.CategoryId is { } category)
        {
            filtered = filtered.Where(p => p.CategoryId == category);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            // The same three ways a cashier already identifies an item, so typing into the grid's box
            // and typing into the F2 dialog give the same answers.
            filtered = filtered.Where(p =>
                p.Name.Contains(term)
                || p.StockCode.Contains(term)
                || (p.Upc != null && p.Upc == term));
        }

        var total = await filtered.CountAsync(ct);

        // Asked of the whole filtered set, not of the page. See PosGridDto.AnyImages.
        var anyImages = total > 0 && await filtered.AnyAsync(p => p.HasImage, ct);

        var items = await filtered
            .OrderBy(p => p.Name)
            .ThenBy(p => p.StockCode)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, MaximumTake))
            .Select(p => new PosGridItemDto(
                p.Id, p.StockCode, p.Name, p.RegularPrice, p.OnHand, p.Type, p.HasImage,
                p.DepartmentId, p.CategoryId))
            .ToListAsync(ct);

        // Counted over everything sellable rather than over the current filter: a department strip
        // whose numbers changed every time you picked a category would be telling you what you had
        // already chosen, not what else there is.
        var departmentCounts = await sellable
            .Where(p => p.DepartmentId != null)
            .GroupBy(p => p.DepartmentId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Id, g => g.Count, ct);

        var categoryCounts = await sellable
            .Where(p => p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Id, g => g.Count, ct);

        var departments = await _db.Departments.AsNoTracking()
            .Where(d => !d.IsDeleted && d.IsActive && d.LocationId == request.LocationId)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .Select(d => new { d.Id, d.Name, d.Code, d.SortOrder })
            .ToListAsync(ct);

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive && c.LocationId == request.LocationId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.Code, c.SortOrder })
            .ToListAsync(ct);

        return Result.Success(new PosGridDto(
            items,
            departments
                .Select(d => new PosGridGroupDto(
                    d.Id, d.Name, d.Code, d.SortOrder, departmentCounts.GetValueOrDefault(d.Id)))
                .ToList(),
            categories
                .Select(c => new PosGridGroupDto(
                    c.Id, c.Name, c.Code, c.SortOrder, categoryCounts.GetValueOrDefault(c.Id)))
                .ToList(),
            total,
            anyImages));
    }
}
