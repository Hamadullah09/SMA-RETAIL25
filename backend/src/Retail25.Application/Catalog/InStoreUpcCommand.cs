using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Identification;

namespace Retail25.Application.Catalog;

/// <summary>An item and the UPC-A it was just given.</summary>
public sealed record AssignedUpc(long ProductId, string StockCode, string Upc);

/// <param name="Assigned">The items that were given a number, and the number each got.</param>
/// <param name="AlreadyHadOne">Items asked about that already carried a barcode, left exactly as they were.</param>
/// <param name="Skipped">
/// Items that could not be given their number, because another item already carries it. Reported
/// rather than worked around: two products answering to one barcode is the fault this exists to avoid.
/// </param>
public sealed record AssignedUpcs(IReadOnlyList<AssignedUpc> Assigned, int AlreadyHadOne, int Skipped);

/// <summary>
/// Gives items with no barcode an in-store UPC-A, so their labels can print as UPC-A.
/// <para>
/// A shop asked for UPC-A barcodes, and most of its catalogue has no UPC at all — its own goods,
/// numbered with alphanumeric stock codes, which UPC-A cannot carry. A manufacturer's UPC can only
/// come from the manufacturer; this issues the shop's own, in the range GS1 reserves for exactly that
/// (see <see cref="UpcA.InStore"/>). An item that already has a barcode is never touched: that number
/// may be printed on packaging the shop does not control.
/// </para>
/// <para>
/// <paramref name="ProductIds"/> limits it to the items chosen; left empty it covers every item in
/// the location that has no barcode.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Write)]
public sealed record AssignInStoreUpcsCommand(long LocationId, IReadOnlyList<long>? ProductIds = null)
    : IRequest<Result<AssignedUpcs>>;

public sealed class AssignInStoreUpcsHandler : IRequestHandler<AssignInStoreUpcsCommand, Result<AssignedUpcs>>
{
    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;

    public AssignInStoreUpcsHandler(IApplicationDbContext db, IPosNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
    }

    public async Task<Result<AssignedUpcs>> Handle(AssignInStoreUpcsCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = _db.Products.Where(p => p.LocationId == request.LocationId && !p.IsDeleted);

        if (request.ProductIds is { Count: > 0 } ids)
        {
            scope = scope.Where(p => ids.Contains(p.Id));
        }

        var alreadyHadOne = await scope.CountAsync(p => p.Upc != null && p.Upc != string.Empty, ct);

        var missing = await scope
            .Where(p => p.Upc == null || p.Upc == string.Empty)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        if (missing.Count == 0)
        {
            return Result.Success(new AssignedUpcs([], alreadyHadOne, 0));
        }

        // Every barcode the location already uses, products and variants alike, because a scan looks
        // in both and must find one thing.
        var productBarcodes = await _db.Products.AsNoTracking()
            .Where(p => p.LocationId == request.LocationId && !p.IsDeleted && p.Upc != null && p.Upc != string.Empty)
            .Select(p => p.Upc!)
            .ToListAsync(ct);

        var variantBarcodes = await _db.ProductVariants.AsNoTracking()
            .Where(v => v.Upc != null && v.Upc != string.Empty)
            .Join(_db.Products.Where(p => p.LocationId == request.LocationId), v => v.ProductId, p => p.Id, (v, _) => v.Upc!)
            .ToListAsync(ct);

        var taken = new HashSet<string>(productBarcodes.Concat(variantBarcodes), StringComparer.Ordinal);

        var assigned = new List<AssignedUpc>();
        var skipped = 0;

        foreach (var product in missing)
        {
            if (product.Id > UpcA.MaxInStoreId)
            {
                skipped++;
                continue;
            }

            var upc = UpcA.InStore(UpcA.InStoreKind.Product, product.Id);

            // Its own number in either written form: a scanner may send the EAN-13 spelling.
            if (taken.Contains(upc) || taken.Contains("0" + upc))
            {
                skipped++;
                continue;
            }

            product.UpdateDetails(product.Name, product.Description, upc, product.BinLocation, product.Notes);
            taken.Add(upc);
            assigned.Add(new AssignedUpc(product.Id, product.StockCode, upc));
        }

        await _db.SaveChangesAsync(ct);

        await PublishAsync(missing.Where(p => assigned.Any(a => a.ProductId == p.Id)).ToList(), ct);

        return Result.Success(new AssignedUpcs(assigned, alreadyHadOne, skipped));
    }

    /// <summary>The changed rows, to every open product browse, as an edit would send them.</summary>
    private async Task PublishAsync(List<Domain.Catalog.Product> products, CancellationToken ct)
    {
        if (products.Count == 0)
        {
            return;
        }

        var departmentIds = products.Where(p => p.DepartmentId.HasValue).Select(p => p.DepartmentId!.Value).Distinct().ToList();
        var categoryIds = products.Where(p => p.CategoryId.HasValue).Select(p => p.CategoryId!.Value).Distinct().ToList();

        var departments = await _db.Departments.AsNoTracking()
            .Where(d => departmentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        foreach (var product in products)
        {
            await _notifier.RowChangedAsync(
                product.LocationId,
                GridKeys.Product,
                product.Id,
                BrowseProductsHandlers.ToRow(product, departments, categories),
                ct);
        }
    }
}
