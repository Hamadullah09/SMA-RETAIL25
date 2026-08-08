using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Common;

namespace Retail25.Application.Common;

/// <summary>What kind of record a deleted row is. Drives the restore route and the screen's grouping.</summary>
public enum DeletedEntityKind
{
    Product = 0,
    Customer = 1,
    Supplier = 2,
    Department = 3,
    Category = 4,
}

public sealed record DeletedRowDto(
    DeletedEntityKind Kind,
    long Id,
    string Reference,
    string Name,
    DateTimeOffset? DeletedAt,
    long? DeletedBy,
    string? DeletedByName);

/// <summary>
/// The legacy "Undelete Items" screen (guide p.24), widened to everything that is soft-deleted.
/// <para>
/// One screen rather than a deleted-items tab on each browse. Someone who has just deleted the wrong
/// thing does not always remember which screen they were on, and asking them to guess is the
/// difference between a five-second recovery and a support call.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Delete)]
public sealed record BrowseDeletedQuery(
    long LocationId,
    DeletedEntityKind? Kind = null,
    string? Search = null,
    int Take = 200) : IRequest<IReadOnlyList<DeletedRowDto>>;

public sealed class RecycleBinHandler : IRequestHandler<BrowseDeletedQuery, IReadOnlyList<DeletedRowDto>>
{
    private readonly IApplicationDbContext _db;

    public RecycleBinHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<DeletedRowDto>> Handle(BrowseDeletedQuery request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 500);
        var term = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var rows = new List<DeletedRowDto>();

        if (request.Kind is null or DeletedEntityKind.Product)
        {
            var products = await _db.Products.AsNoTracking()
                .Where(p => p.LocationId == request.LocationId && p.IsDeleted
                    && (term == null || p.StockCode.Contains(term) || p.Name.Contains(term)))
                .OrderByDescending(p => p.DeletedAt)
                .Take(take)
                .Select(p => new DeletedRowDto(DeletedEntityKind.Product, p.Id, p.StockCode, p.Name, p.DeletedAt, p.DeletedBy, null))
                .ToListAsync(ct);

            rows.AddRange(products);
        }

        if (request.Kind is null or DeletedEntityKind.Customer)
        {
            var customers = await _db.Customers.AsNoTracking()
                .Where(c => c.LocationId == request.LocationId && c.IsDeleted
                    && (term == null || c.LastName.Contains(term) || c.FirstName.Contains(term) || (c.Company != null && c.Company.Contains(term))))
                .OrderByDescending(c => c.DeletedAt)
                .Take(take)
                .ToListAsync(ct);

            rows.AddRange(customers.Select(c => new DeletedRowDto(
                DeletedEntityKind.Customer,
                c.Id,
                c.CustomerNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                c.FullName,
                c.DeletedAt,
                c.DeletedBy,
                null)));
        }

        if (request.Kind is null or DeletedEntityKind.Supplier)
        {
            var suppliers = await _db.Suppliers.AsNoTracking()
                .Where(s => s.LocationId == request.LocationId && s.IsDeleted
                    && (term == null || s.Company.Contains(term) || s.SupplierNumber.Contains(term)))
                .OrderByDescending(s => s.DeletedAt)
                .Take(take)
                .Select(s => new DeletedRowDto(DeletedEntityKind.Supplier, s.Id, s.SupplierNumber, s.Company, s.DeletedAt, s.DeletedBy, null))
                .ToListAsync(ct);

            rows.AddRange(suppliers);
        }

        if (request.Kind is null or DeletedEntityKind.Department)
        {
            var departments = await _db.Departments.AsNoTracking()
                .Where(d => d.LocationId == request.LocationId && d.IsDeleted && (term == null || d.Name.Contains(term)))
                .OrderByDescending(d => d.DeletedAt)
                .Take(take)
                .Select(d => new DeletedRowDto(DeletedEntityKind.Department, d.Id, d.Code ?? string.Empty, d.Name, d.DeletedAt, d.DeletedBy, null))
                .ToListAsync(ct);

            rows.AddRange(departments);
        }

        if (request.Kind is null or DeletedEntityKind.Category)
        {
            var categories = await _db.Categories.AsNoTracking()
                .Where(c => c.LocationId == request.LocationId && c.IsDeleted && (term == null || c.Name.Contains(term)))
                .OrderByDescending(c => c.DeletedAt)
                .Take(take)
                .Select(c => new DeletedRowDto(DeletedEntityKind.Category, c.Id, c.Code ?? string.Empty, c.Name, c.DeletedAt, c.DeletedBy, null))
                .ToListAsync(ct);

            rows.AddRange(categories);
        }

        // Who deleted it matters more than what: the first question after an accidental delete is
        // always whether it was you. Resolved in one lookup across all kinds.
        var actorIds = rows.Where(r => r.DeletedBy.HasValue).Select(r => r.DeletedBy!.Value).Distinct().ToList();

        var actors = actorIds.Count == 0
            ? []
            : await _db.StaffProfiles.AsNoTracking()
                .Where(s => actorIds.Contains(s.UserId))
                .ToDictionaryAsync(s => s.UserId, s => s.FullName, ct);

        return rows
            .Select(r => r.DeletedBy is { } actor && actors.TryGetValue(actor, out var name)
                ? r with { DeletedByName = name }
                : r)
            .OrderByDescending(r => r.DeletedAt)
            .Take(take)
            .ToList();
    }
}

/// <summary>
/// Restores a deleted department or category. Products, customers and suppliers have their own
/// restore commands because each has a precondition the others do not — a reused stock code, an
/// outstanding balance — and folding them together would hide those checks behind one entry point.
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Delete)]
public sealed record RestoreReferenceRowCommand(DeletedEntityKind Kind, long Id) : IRequest<Result>;

public sealed class RestoreReferenceRowHandler : IRequestHandler<RestoreReferenceRowCommand, Result>
{
    public static readonly Error NotSupported = new("restore.kind_not_supported", "That kind of record has its own restore command.");
    public static readonly Error NotFound = new("restore.not_found", "No such deleted record.");

    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;

    public RestoreReferenceRowHandler(IApplicationDbContext db, IPosNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
    }

    public async Task<Result> Handle(RestoreReferenceRowCommand request, CancellationToken ct)
    {
        switch (request.Kind)
        {
            case DeletedEntityKind.Department:
            {
                var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == request.Id, ct);
                if (department is null)
                {
                    return Result.Failure(NotFound.With("id", request.Id));
                }

                department.Restore();
                await _db.SaveChangesAsync(ct);
                await _notifier.RowChangedAsync(
                    department.LocationId,
                    GridKeys.Department,
                    department.Id,
                    new { department.Id, department.Name, department.Code, department.SortOrder, department.IsActive },
                    ct);

                return Result.Success();
            }

            case DeletedEntityKind.Category:
            {
                var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == request.Id, ct);
                if (category is null)
                {
                    return Result.Failure(NotFound.With("id", request.Id));
                }

                category.Restore();
                await _db.SaveChangesAsync(ct);
                await _notifier.RowChangedAsync(
                    category.LocationId,
                    GridKeys.Category,
                    category.Id,
                    new { category.Id, category.Name, category.Code, category.SortOrder, category.IsActive },
                    ct);

                return Result.Success();
            }

            default:
                return Result.Failure(NotSupported.With("kind", request.Kind.ToString()));
        }
    }
}
