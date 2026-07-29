using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Purchasing;
using Retail25.Domain.ValueObjects;

namespace Retail25.Application.Purchasing;

public enum SupplierSort
{
    Number = 0,
    Company = 1,
}

public sealed record SupplierRowDto(
    Guid Id,
    string SupplierNumber,
    string Company,
    string? ContactName,
    string? City,
    string? StateOrProvince,
    string? Phone,
    string? Email,
    int SuppliedItemCount,
    bool IsDeleted);

public sealed record SupplierFormDto(
    Guid Id,
    Guid LocationId,
    string SupplierNumber,
    string Company,
    string? ContactFirstName,
    string? ContactLastName,
    string? Title,
    Address Address,
    ContactDetails Contact,
    int SuppliedItemCount,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

public sealed record SupplierSection(
    string Company,
    string? ContactFirstName,
    string? ContactLastName,
    string? Title,
    Address Address,
    ContactDetails Contact);

/// <summary>The supplier Browse View (guide p.59–62), sharing the catalogue browse's paging shape.</summary>
[RequiresPermission(PermissionKeys.Purchasing.Read)]
public sealed record BrowseSuppliersQuery(
    Guid LocationId,
    string? Search = null,
    bool DeletedOnly = false,
    SupplierSort Sort = SupplierSort.Company,
    bool Descending = false,
    string? Cursor = null,
    int PageSize = 50) : IRequest<CursorPage<SupplierRowDto>>;

[RequiresPermission(PermissionKeys.Purchasing.Read)]
public sealed record GetSupplierFormQuery(Guid SupplierId) : IRequest<Result<SupplierFormDto>>;

/// <summary>
/// Creates a supplier. Like the customer number, the supplier number comes from the location's
/// administered sequence so a migrated store carries on from where it left off.
/// </summary>
[RequiresPermission(PermissionKeys.Purchasing.Write)]
public sealed record CreateSupplierCommand(Guid LocationId, SupplierSection Details, string? SupplierNumber = null)
    : IRequest<Result<SupplierFormDto>>;

[RequiresPermission(PermissionKeys.Purchasing.Write)]
public sealed record UpdateSupplierCommand(Guid SupplierId, SupplierSection Details) : IRequest<Result<SupplierFormDto>>;

[RequiresPermission(PermissionKeys.Purchasing.Write)]
public sealed record DeleteSupplierCommand(Guid SupplierId) : IRequest<Result>;

[RequiresPermission(PermissionKeys.Purchasing.Write)]
public sealed record RestoreSupplierCommand(Guid SupplierId) : IRequest<Result>;

public sealed class SupplierHandlers
    : IRequestHandler<BrowseSuppliersQuery, CursorPage<SupplierRowDto>>,
      IRequestHandler<GetSupplierFormQuery, Result<SupplierFormDto>>,
      IRequestHandler<CreateSupplierCommand, Result<SupplierFormDto>>,
      IRequestHandler<UpdateSupplierCommand, Result<SupplierFormDto>>,
      IRequestHandler<DeleteSupplierCommand, Result>,
      IRequestHandler<RestoreSupplierCommand, Result>
{
    public static readonly Error NotFound = new("supplier.not_found", "No such supplier.");
    public static readonly Error DuplicateNumber = new("supplier.duplicate_number", "A supplier with this number already exists at this location.");
    public static readonly Error StillSupplies = new("supplier.still_supplies_items", "Items are still linked to this supplier. Unlink them first.");
    public static readonly Error HasOpenOrders = new("supplier.has_open_orders", "This supplier has purchase orders that are not closed.");

    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;
    private readonly ISequenceGenerator _sequences;

    public SupplierHandlers(IApplicationDbContext db, IPosNotifier notifier, ISequenceGenerator sequences)
    {
        _db = db;
        _notifier = notifier;
        _sequences = sequences;
    }

    public async Task<CursorPage<SupplierRowDto>> Handle(BrowseSuppliersQuery request, CancellationToken ct)
    {
        var pageSize = Cursor.PageSize(request.PageSize);

        var query = _db.Suppliers.AsNoTracking()
            .Where(s => s.LocationId == request.LocationId && s.IsDeleted == request.DeletedOnly);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(s =>
                s.Company.Contains(term) ||
                s.SupplierNumber.Contains(term) ||
                (s.ContactLastName != null && s.ContactLastName.Contains(term)) ||
                (s.Contact.Phone != null && s.Contact.Phone.Contains(term)));
        }

        var after = Cursor.Decode(request.Cursor);
        var tie = after?.TieBreak ?? string.Empty;

        if (request.Sort == SupplierSort.Number)
        {
            if (after is { } byNumber)
            {
                var key = byNumber.SortKey;
                query = request.Descending
                    ? query.Where(s => s.SupplierNumber.CompareTo(key) < 0)
                    : query.Where(s => s.SupplierNumber.CompareTo(key) > 0);
            }

            query = request.Descending
                ? query.OrderByDescending(s => s.SupplierNumber)
                : query.OrderBy(s => s.SupplierNumber);
        }
        else
        {
            if (after is { } byCompany)
            {
                var key = byCompany.SortKey;
                query = request.Descending
                    ? query.Where(s => s.Company.CompareTo(key) < 0 || (s.Company == key && s.SupplierNumber.CompareTo(tie) < 0))
                    : query.Where(s => s.Company.CompareTo(key) > 0 || (s.Company == key && s.SupplierNumber.CompareTo(tie) > 0));
            }

            query = request.Descending
                ? query.OrderByDescending(s => s.Company).ThenByDescending(s => s.SupplierNumber)
                : query.OrderBy(s => s.Company).ThenBy(s => s.SupplierNumber);
        }

        var suppliers = await query.Take(pageSize + 1).ToListAsync(ct);

        var hasMore = suppliers.Count > pageSize;
        if (hasMore)
        {
            suppliers.RemoveAt(suppliers.Count - 1);
        }

        var counts = await ItemCountsAsync(suppliers.Select(s => s.Id).ToList(), ct);
        var rows = suppliers.Select(s => ToRow(s, counts.GetValueOrDefault(s.Id))).ToList();

        var last = suppliers.Count > 0 ? suppliers[^1] : null;
        var nextCursor = hasMore && last is not null
            ? Cursor.Encode(request.Sort == SupplierSort.Number ? last.SupplierNumber : last.Company, last.SupplierNumber)
            : null;

        return new CursorPage<SupplierRowDto>(rows, nextCursor, hasMore);
    }

    public async Task<Result<SupplierFormDto>> Handle(GetSupplierFormQuery request, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct);
        if (supplier is null)
        {
            return Result.Failure<SupplierFormDto>(NotFound.With("supplierId", request.SupplierId));
        }

        var count = await _db.ProductSuppliers.AsNoTracking().CountAsync(p => p.SupplierId == supplier.Id, ct);
        return Result.Success(ToForm(supplier, count));
    }

    public async Task<Result<SupplierFormDto>> Handle(CreateSupplierCommand request, CancellationToken ct)
    {
        var number = string.IsNullOrWhiteSpace(request.SupplierNumber)
            ? (await _sequences.NextAsync(SequenceKind.Supplier, request.LocationId, ct)).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : request.SupplierNumber.Trim();

        if (await _db.Suppliers.AsNoTracking()
            .AnyAsync(s => s.LocationId == request.LocationId && !s.IsDeleted && s.SupplierNumber == number, ct))
        {
            return Result.Failure<SupplierFormDto>(DuplicateNumber.With("supplierNumber", number));
        }

        var created = Supplier.Create(request.LocationId, request.Details.Company, number);
        if (created.IsFailure)
        {
            return Result.Failure<SupplierFormDto>(created.Error);
        }

        var supplier = created.Value;
        Apply(supplier, request.Details);

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(ct);
        await PublishAsync(supplier, 0, ct);

        return Result.Success(ToForm(supplier, 0));
    }

    public async Task<Result<SupplierFormDto>> Handle(UpdateSupplierCommand request, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct);
        if (supplier is null)
        {
            return Result.Failure<SupplierFormDto>(NotFound.With("supplierId", request.SupplierId));
        }

        Apply(supplier, request.Details);
        await _db.SaveChangesAsync(ct);

        var count = await _db.ProductSuppliers.AsNoTracking().CountAsync(p => p.SupplierId == supplier.Id, ct);
        await PublishAsync(supplier, count, ct);

        return Result.Success(ToForm(supplier, count));
    }

    public async Task<Result> Handle(DeleteSupplierCommand request, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct);
        if (supplier is null)
        {
            return Result.Failure(NotFound.With("supplierId", request.SupplierId));
        }

        if (await _db.ProductSuppliers.AsNoTracking().AnyAsync(p => p.SupplierId == supplier.Id, ct))
        {
            // A supplier that still sources items is what automatic reorder generation reads. Hiding
            // it would leave those items with a preferred supplier that no longer exists.
            return Result.Failure(StillSupplies.With("company", supplier.Company));
        }

        if (await _db.PurchaseOrders.AsNoTracking()
            .AnyAsync(o => o.SupplierId == supplier.Id && o.Status != PurchaseOrderStatus.Closed && o.Status != PurchaseOrderStatus.Cancelled, ct))
        {
            return Result.Failure(HasOpenOrders.With("company", supplier.Company));
        }

        _db.Suppliers.Remove(supplier);
        await _db.SaveChangesAsync(ct);
        await _notifier.RowRemovedAsync(supplier.LocationId, GridKeys.Supplier, supplier.Id, ct);

        return Result.Success();
    }

    public async Task<Result> Handle(RestoreSupplierCommand request, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct);
        if (supplier is null)
        {
            return Result.Failure(NotFound.With("supplierId", request.SupplierId));
        }

        if (supplier.IsDeleted)
        {
            supplier.Restore();
            await _db.SaveChangesAsync(ct);
            await PublishAsync(supplier, 0, ct);
        }

        return Result.Success();
    }

    private static void Apply(Supplier supplier, SupplierSection details)
    {
        supplier.Company = details.Company.Trim();
        supplier.ContactFirstName = Blank(details.ContactFirstName);
        supplier.ContactLastName = Blank(details.ContactLastName);
        supplier.Title = Blank(details.Title);

        // Fresh records: an owned value object claimed by two owners is a persistence-layer error.
        supplier.Address = details.Address with { };
        supplier.Contact = details.Contact with { };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<Dictionary<Guid, int>> ItemCountsAsync(List<Guid> supplierIds, CancellationToken ct)
        => supplierIds.Count == 0
            ? []
            : await _db.ProductSuppliers.AsNoTracking()
                .Where(p => supplierIds.Contains(p.SupplierId))
                .GroupBy(p => p.SupplierId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    /// <summary>
    /// A blank address for a supplier that has never had one. An owned value object with no stored
    /// columns materialises as <c>null</c> whatever the C# property initialiser says: the initialiser
    /// runs on <c>new</c>, not on the constructor EF uses to rehydrate.
    /// </summary>
    private static readonly Address NoAddress = new();

    private static readonly ContactDetails NoContact = new();

    private static SupplierRowDto ToRow(Supplier supplier, int itemCount)
        => new(
            supplier.Id,
            supplier.SupplierNumber,
            supplier.Company,
            string.IsNullOrWhiteSpace(supplier.FullName) ? null : supplier.FullName,
            (supplier.Address ?? NoAddress).City,
            (supplier.Address ?? NoAddress).StateOrProvince,
            (supplier.Contact ?? NoContact).Phone,
            (supplier.Contact ?? NoContact).Email,
            itemCount,
            supplier.IsDeleted);

    private static SupplierFormDto ToForm(Supplier supplier, int itemCount)
        => new(
            supplier.Id,
            supplier.LocationId,
            supplier.SupplierNumber,
            supplier.Company,
            supplier.ContactFirstName,
            supplier.ContactLastName,
            supplier.Title,
            supplier.Address ?? NoAddress,
            supplier.Contact ?? NoContact,
            itemCount,
            supplier.IsDeleted,
            supplier.CreatedAt,
            supplier.ModifiedAt);

    private Task PublishAsync(Supplier supplier, int itemCount, CancellationToken ct)
        => _notifier.RowChangedAsync(supplier.LocationId, GridKeys.Supplier, supplier.Id, ToRow(supplier, itemCount), ct);
}
