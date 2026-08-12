using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Inventory;

namespace Retail25.Application.Inventory;

/// <summary>One row of the stock-levels grid. <c>Id</c> is the product's id — there is no separate
/// stock-level entity identity, so the row's key is the thing it is a projection of.</summary>
public sealed record StockLevelRowDto(
    long Id,
    string StockCode,
    string ProductName,
    decimal OnHand,
    decimal OnOrder,
    decimal Committed,
    decimal Available,
    int ReorderPoint,
    int ReorderQty,
    decimal AvgCost);

[RequiresPermission(PermissionKeys.Catalog.Read)]
public sealed record BrowseStockLevelsQuery(
    long LocationId,
    string? Search = null,
    bool BelowReorderOnly = false,
    string? Cursor = null,
    int PageSize = 50) : IRequest<CursorPage<StockLevelRowDto>>;

/// <summary>Manual, item-by-item receipt with no purchase order behind it (guide p.20).</summary>
[RequiresPermission(PermissionKeys.Inventory.Receive)]
public sealed record ReceiveStockCommand(
    long ProductId,
    long LocationId,
    decimal Quantity,
    decimal UnitCost) : IRequest<Result<StockLevelRowDto>>;

/// <summary>
/// A signed on-hand correction with a reason (guide p.22) — found stock, shrinkage, damage. Unlike a
/// receipt, an adjustment never touches cost: the legacy behaviour is that correcting a count does not
/// reprice the shelf, only a real purchase does.
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.Adjust)]
public sealed record AdjustStockCommand(
    long ProductId,
    long LocationId,
    decimal QuantityDelta,
    string Reason) : IRequest<Result<StockLevelRowDto>>;

/// <summary>
/// Breaks whole cases of a parent item into its sellable child units (guide p.43). The child is the
/// product whose <see cref="Product.ParentProductId"/> points at the case being broken; each case
/// yields <see cref="Product.CaseQty"/> units of it.
/// </summary>
[RequiresPermission(PermissionKeys.Inventory.Adjust)]
public sealed record BreakCaseCommand(long ParentProductId, long LocationId, decimal CasesToBreak) : IRequest<Result>;

public sealed class InventoryHandlers :
    IRequestHandler<BrowseStockLevelsQuery, CursorPage<StockLevelRowDto>>,
    IRequestHandler<ReceiveStockCommand, Result<StockLevelRowDto>>,
    IRequestHandler<AdjustStockCommand, Result<StockLevelRowDto>>,
    IRequestHandler<BreakCaseCommand, Result>
{
    public static readonly Error ProductNotFound = new("inventory.product_not_found", "No such product.");
    public static readonly Error InvalidQuantity = new("inventory.invalid_quantity", "Quantity must be greater than zero.");
    public static readonly Error AdjustmentIsZero = new("inventory.adjustment_is_zero", "An adjustment must change the quantity.");
    public static readonly Error NoChildProduct = new("inventory.no_child_product", "No product is linked to this one as its case-break unit.");
    public static readonly Error InsufficientCases = new("inventory.insufficient_cases", "Not enough whole cases on hand to break that many.");
    public static readonly Error CaseQtyNotSet = new("inventory.case_qty_not_set", "This item's case quantity is not set.");

    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    public InventoryHandlers(IApplicationDbContext db, IPosNotifier notifier, ICurrentUser currentUser, IDateTime clock)
    {
        _db = db;
        _notifier = notifier;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<CursorPage<StockLevelRowDto>> Handle(BrowseStockLevelsQuery request, CancellationToken ct)
    {
        var pageSize = Cursor.PageSize(request.PageSize);

        var query = _db.Products.AsNoTracking().Where(p => p.LocationId == request.LocationId && !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(p => p.StockCode.Contains(term) || p.Name.Contains(term));
        }

        if (request.BelowReorderOnly)
        {
            // One rule, shared with the stock-position report and the catalogue browse, which each
            // used to ask this question differently. Committed is zero here because it lives on
            // stock_levels rather than on the product row.
            query = query.Where(ReorderPolicy.NeedsReorderingWhere<Domain.Catalog.Product>(
                p => p.OnHand, p => p.OnOrder, _ => 0m, p => p.ReorderPoint));
        }

        var after = Cursor.Decode(request.Cursor);
        if (after is { } cursor)
        {
            var tie = cursor.TieBreak;
            query = query.Where(p => p.StockCode.CompareTo(tie) > 0);
        }

        var products = await query.OrderBy(p => p.StockCode).Take(pageSize + 1).ToListAsync(ct);

        var hasMore = products.Count > pageSize;
        if (hasMore)
        {
            products.RemoveAt(products.Count - 1);
        }

        var productIds = products.Select(p => p.Id).ToList();

        // Grouped rather than ToDictionaryAsync keyed by ProductId: that throws outright if more
        // than one StockLevel row ever exists for the same (product, null variant, location) —
        // which is supposed to be impossible (every writer does find-or-create) but isn't a safe
        // assumption for a read screen to crash on if it ever happens anyway. Summing Committed
        // across duplicates is the correct number regardless of how they got there.
        var committed = await _db.StockLevels.AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId) && s.VariantId == null && s.LocationId == request.LocationId)
            .GroupBy(s => s.ProductId)
            .ToDictionaryAsync(g => g.Key, g => g.Sum(s => s.Committed), ct);

        var rows = products.Select(p => ToRow(p, committed.GetValueOrDefault(p.Id))).ToList();

        var last = products.Count > 0 ? products[^1] : null;
        var nextCursor = hasMore && last is not null ? Cursor.Encode(last.StockCode, last.StockCode) : null;

        return new CursorPage<StockLevelRowDto>(rows, nextCursor, hasMore);
    }

    public async Task<Result<StockLevelRowDto>> Handle(ReceiveStockCommand request, CancellationToken ct)
    {
        if (request.Quantity <= 0m)
        {
            return Result.Failure<StockLevelRowDto>(InvalidQuantity);
        }

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null)
        {
            return Result.Failure<StockLevelRowDto>(ProductNotFound.With("productId", request.ProductId));
        }

        product.ReceiveStock(request.Quantity, request.UnitCost, allocatedFreight: 0m);

        await WriteMovementAsync(product.Id, request.LocationId, MovementType.Receipt, request.Quantity, request.UnitCost, null, ct);

        await _db.SaveChangesAsync(ct);

        return Result.Success(await PublishAndReturnAsync(product, request.LocationId, ct));
    }

    public async Task<Result<StockLevelRowDto>> Handle(AdjustStockCommand request, CancellationToken ct)
    {
        if (request.QuantityDelta == 0m)
        {
            return Result.Failure<StockLevelRowDto>(AdjustmentIsZero);
        }

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null)
        {
            return Result.Failure<StockLevelRowDto>(ProductNotFound.With("productId", request.ProductId));
        }

        product.UpdateStockLevels(product.OnHand + request.QuantityDelta, product.OnOrder);

        await WriteMovementAsync(
            product.Id, request.LocationId, MovementType.Adjustment, request.QuantityDelta, product.AvgCost, request.Reason, ct);

        await _db.SaveChangesAsync(ct);

        return Result.Success(await PublishAndReturnAsync(product, request.LocationId, ct));
    }

    public async Task<Result> Handle(BreakCaseCommand request, CancellationToken ct)
    {
        if (request.CasesToBreak <= 0m)
        {
            return Result.Failure(InvalidQuantity);
        }

        var parent = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ParentProductId, ct);
        if (parent is null)
        {
            return Result.Failure(ProductNotFound.With("productId", request.ParentProductId));
        }

        if (parent.CaseQty <= 0m)
        {
            return Result.Failure(CaseQtyNotSet.With("productId", parent.Id));
        }

        if (parent.OnHand < request.CasesToBreak)
        {
            return Result.Failure(InsufficientCases.With("onHand", parent.OnHand).With("requested", request.CasesToBreak));
        }

        var child = await _db.Products.FirstOrDefaultAsync(p => p.ParentProductId == parent.Id, ct);
        if (child is null)
        {
            return Result.Failure(NoChildProduct.With("productId", parent.Id));
        }

        var unitsProduced = request.CasesToBreak * parent.CaseQty;

        parent.UpdateStockLevels(parent.OnHand - request.CasesToBreak, parent.OnOrder);
        child.UpdateStockLevels(child.OnHand + unitsProduced, child.OnOrder);

        await WriteMovementAsync(parent.Id, request.LocationId, MovementType.CaseBreak, -request.CasesToBreak, parent.AvgCost, null, ct);
        await WriteMovementAsync(child.Id, request.LocationId, MovementType.CaseBreak, unitsProduced, child.AvgCost, null, ct);

        await _db.SaveChangesAsync(ct);
        await PublishAndReturnAsync(parent, request.LocationId, ct);
        await PublishAndReturnAsync(child, request.LocationId, ct);

        return Result.Success();
    }

    private async Task WriteMovementAsync(
        long productId, long locationId, MovementType type, decimal signedQuantity, decimal unitCost, string? reason, CancellationToken ct)
    {
        _db.StockLedgerEntries.Add(new StockLedgerEntry
        {
            ProductId = productId,
            LocationId = locationId,
            MovementType = type,
            Quantity = signedQuantity,
            UnitCost = unitCost,
            Reason = reason,
            OccurredAt = _clock.Now,
            StaffId = _currentUser.StaffId,
        });

        var level = await _db.StockLevels
            .FirstOrDefaultAsync(s => s.ProductId == productId && s.VariantId == null && s.LocationId == locationId, ct);

        if (level is null)
        {
            level = StockLevel.Create(productId, null, locationId);
            _db.StockLevels.Add(level);
        }

        level.OnHand += signedQuantity;
    }

    private async Task<StockLevelRowDto> PublishAndReturnAsync(Product product, long locationId, CancellationToken ct)
    {
        var committed = await _db.StockLevels.AsNoTracking()
            .Where(s => s.ProductId == product.Id && s.VariantId == null && s.LocationId == locationId)
            .Select(s => s.Committed)
            .FirstOrDefaultAsync(ct);

        var row = ToRow(product, committed);

        await _notifier.RowChangedAsync(locationId, GridKeys.StockLevel, product.Id, row, ct);
        await _notifier.StockLevelChangedAsync(locationId, product.Id, product.OnHand, ct);

        return row;
    }

    private static StockLevelRowDto ToRow(Product product, decimal committed) => new(
        product.Id,
        product.StockCode,
        product.Name,
        product.OnHand,
        product.OnOrder,
        committed,
        product.OnHand - committed,
        product.ReorderPoint,
        product.ReorderQty,
        product.AvgCost);
}
