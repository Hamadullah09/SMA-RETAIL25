using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;

namespace Retail25.Application.Catalog;

/// <summary>
/// Departments and categories (guide p.31) — the two lists every product form picks from and every
/// sales report groups by.
/// <para>
/// They are rows, not enums, because a hardware store's departments and a boutique's have nothing in
/// common and neither should require a release. That is the same reason they are soft-deleted: a
/// department removed today still names last year's sales.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Read)]
public sealed record ListDepartmentsQuery(long LocationId, bool IncludeInactive = false)
    : IRequest<IReadOnlyList<ReferenceRowDto>>;

[RequiresPermission(PermissionKeys.Catalog.Read)]
public sealed record ListCategoriesQuery(long LocationId, bool IncludeInactive = false)
    : IRequest<IReadOnlyList<ReferenceRowDto>>;

[RequiresPermission(PermissionKeys.Catalog.Write)]
public sealed record SaveDepartmentCommand(long LocationId, long? Id, string Name, string? Code, int SortOrder, bool IsActive)
    : IRequest<Result<ReferenceRowDto>>;

[RequiresPermission(PermissionKeys.Catalog.Write)]
public sealed record SaveCategoryCommand(long LocationId, long? Id, string Name, string? Code, int SortOrder, bool IsActive)
    : IRequest<Result<ReferenceRowDto>>;

[RequiresPermission(PermissionKeys.Catalog.Delete)]
public sealed record DeleteDepartmentCommand(long Id) : IRequest<Result>;

[RequiresPermission(PermissionKeys.Catalog.Delete)]
public sealed record DeleteCategoryCommand(long Id) : IRequest<Result>;

public sealed class ReferenceDataHandlers
    : IRequestHandler<ListDepartmentsQuery, IReadOnlyList<ReferenceRowDto>>,
      IRequestHandler<ListCategoriesQuery, IReadOnlyList<ReferenceRowDto>>,
      IRequestHandler<SaveDepartmentCommand, Result<ReferenceRowDto>>,
      IRequestHandler<SaveCategoryCommand, Result<ReferenceRowDto>>,
      IRequestHandler<DeleteDepartmentCommand, Result>,
      IRequestHandler<DeleteCategoryCommand, Result>
{
    public static readonly Error DepartmentNotFound = new("department.not_found", "No such department.");
    public static readonly Error CategoryNotFound = new("category.not_found", "No such category.");
    public static readonly Error StillInUse = new("reference.still_in_use", "Items are still assigned to this. Reassign them first.");

    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;

    public ReferenceDataHandlers(IApplicationDbContext db, IPosNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
    }

    public async Task<IReadOnlyList<ReferenceRowDto>> Handle(ListDepartmentsQuery request, CancellationToken ct)
    {
        var departments = await _db.Departments.AsNoTracking()
            .Where(d => d.LocationId == request.LocationId && !d.IsDeleted && (request.IncludeInactive || d.IsActive))
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .ToListAsync(ct);

        var counts = await CountsAsync(request.LocationId, byDepartment: true, ct);

        return departments
            .Select(d => new ReferenceRowDto(d.Id, d.Name, d.Code, d.SortOrder, d.IsActive, counts.GetValueOrDefault(d.Id)))
            .ToList();
    }

    public async Task<IReadOnlyList<ReferenceRowDto>> Handle(ListCategoriesQuery request, CancellationToken ct)
    {
        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.LocationId == request.LocationId && !c.IsDeleted && (request.IncludeInactive || c.IsActive))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);

        var counts = await CountsAsync(request.LocationId, byDepartment: false, ct);

        return categories
            .Select(c => new ReferenceRowDto(c.Id, c.Name, c.Code, c.SortOrder, c.IsActive, counts.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<Result<ReferenceRowDto>> Handle(SaveDepartmentCommand request, CancellationToken ct)
    {
        Department? department = null;

        if (request.Id is { } id)
        {
            department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (department is null)
            {
                return Result.Failure<ReferenceRowDto>(DepartmentNotFound.With("id", id));
            }

            department.Update(request.Name, request.Code, request.SortOrder);
        }
        else
        {
            var created = Department.Create(request.LocationId, request.Name, request.Code, request.SortOrder);
            if (created.IsFailure)
            {
                return Result.Failure<ReferenceRowDto>(created.Error);
            }

            department = created.Value;
            _db.Departments.Add(department);
        }

        department.SetActive(request.IsActive);
        await _db.SaveChangesAsync(ct);

        var row = new ReferenceRowDto(department.Id, department.Name, department.Code, department.SortOrder, department.IsActive, 0);
        await _notifier.RowChangedAsync(request.LocationId, GridKeys.Department, department.Id, row, ct);

        return Result.Success(row);
    }

    public async Task<Result<ReferenceRowDto>> Handle(SaveCategoryCommand request, CancellationToken ct)
    {
        Category? category = null;

        if (request.Id is { } id)
        {
            category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (category is null)
            {
                return Result.Failure<ReferenceRowDto>(CategoryNotFound.With("id", id));
            }

            category.Update(request.Name, request.Code, request.SortOrder);
        }
        else
        {
            var created = Category.Create(request.LocationId, request.Name, request.Code, request.SortOrder);
            if (created.IsFailure)
            {
                return Result.Failure<ReferenceRowDto>(created.Error);
            }

            category = created.Value;
            _db.Categories.Add(category);
        }

        category.SetActive(request.IsActive);
        await _db.SaveChangesAsync(ct);

        var row = new ReferenceRowDto(category.Id, category.Name, category.Code, category.SortOrder, category.IsActive, 0);
        await _notifier.RowChangedAsync(request.LocationId, GridKeys.Category, category.Id, row, ct);

        return Result.Success(row);
    }

    public async Task<Result> Handle(DeleteDepartmentCommand request, CancellationToken ct)
    {
        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == request.Id, ct);
        if (department is null)
        {
            return Result.Failure(DepartmentNotFound.With("id", request.Id));
        }

        // Refused rather than cascaded. Nulling every product's department silently would destroy the
        // grouping every sales report by department depends on, with no way back.
        if (await _db.Products.AsNoTracking().AnyAsync(p => p.DepartmentId == department.Id && !p.IsDeleted, ct))
        {
            return Result.Failure(StillInUse.With("name", department.Name));
        }

        _db.Departments.Remove(department);
        await _db.SaveChangesAsync(ct);
        await _notifier.RowRemovedAsync(department.LocationId, GridKeys.Department, department.Id, ct);

        return Result.Success();
    }

    public async Task<Result> Handle(DeleteCategoryCommand request, CancellationToken ct)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == request.Id, ct);
        if (category is null)
        {
            return Result.Failure(CategoryNotFound.With("id", request.Id));
        }

        if (await _db.Products.AsNoTracking().AnyAsync(p => p.CategoryId == category.Id && !p.IsDeleted, ct))
        {
            return Result.Failure(StillInUse.With("name", category.Name));
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync(ct);
        await _notifier.RowRemovedAsync(category.LocationId, GridKeys.Category, category.Id, ct);

        return Result.Success();
    }

    /// <summary>
    /// How many live items each grouping holds. Shown in the settings list so an administrator can
    /// see what a rename or a deactivation will affect before doing it.
    /// </summary>
    private async Task<Dictionary<long, int>> CountsAsync(long locationId, bool byDepartment, CancellationToken ct)
    {
        var products = _db.Products.AsNoTracking().Where(p => p.LocationId == locationId && !p.IsDeleted);

        return byDepartment
            ? await products.Where(p => p.DepartmentId != null)
                .GroupBy(p => p.DepartmentId!.Value)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct)
            : await products.Where(p => p.CategoryId != null)
                .GroupBy(p => p.CategoryId!.Value)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }
}
