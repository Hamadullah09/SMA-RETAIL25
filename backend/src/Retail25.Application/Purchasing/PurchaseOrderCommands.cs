using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Inventory;
using Retail25.Domain.Purchasing;
using Retail25.Domain.Sales;

namespace Retail25.Application.Purchasing;

public sealed record PurchaseOrderRowDto(
    Guid Id,
    long PoNumber,
    Guid SupplierId,
    string SupplierCompany,
    PurchaseOrderStatus Status,
    OrderQuantityStrategy QuantityStrategy,
    DateOnly? PostedOn,
    DateOnly? DueOn,
    decimal Total,
    int LineCount);

public sealed record PurchaseOrderLineDto(
    Guid Id,
    Guid ProductId,
    string StockCode,
    string ProductName,
    decimal OrderQty,
    decimal CaseQty,
    decimal CostEach,
    decimal OrderCost,
    decimal QtyReceived,
    decimal InStockAtGeneration,
    decimal OnOrderAtGeneration,
    decimal BackOrders);

public sealed record PurchaseOrderReceiptDto(Guid Id, DateOnly ReceivedOn, decimal FreightTotal, Guid StaffId);

public sealed record PurchaseOrderDetailDto(
    Guid Id,
    long PoNumber,
    Guid LocationId,
    Guid SupplierId,
    string SupplierCompany,
    PurchaseOrderStatus Status,
    OrderQuantityStrategy QuantityStrategy,
    string? HeaderText,
    DateOnly? PostedOn,
    DateOnly? DueOn,
    decimal Total,
    IReadOnlyList<PurchaseOrderLineDto> Lines,
    IReadOnlyList<PurchaseOrderReceiptDto> Receipts);

[RequiresPermission(PermissionKeys.Purchasing.Read)]
public sealed record BrowsePurchaseOrdersQuery(
    Guid LocationId,
    Guid? SupplierId = null,
    PurchaseOrderStatus? Status = null,
    string? Cursor = null,
    int PageSize = 50) : IRequest<CursorPage<PurchaseOrderRowDto>>;

[RequiresPermission(PermissionKeys.Purchasing.Read)]
public sealed record GetPurchaseOrderQuery(Guid PurchaseOrderId) : IRequest<Result<PurchaseOrderDetailDto>>;

/// <summary>
/// Creates a draft PO for one supplier and, unless the strategy is <see cref="OrderQuantityStrategy.Blank"/>,
/// populates it from every product this supplier is the top-ranked (<c>Rank == 1</c>) source for
/// (guide p.64). "Blank" yields an empty draft for manual entry — the same on-screen review-and-edit
/// grid every other strategy's output lands in.
/// </summary>
[RequiresPermission(PermissionKeys.Purchasing.Write)]
public sealed record GeneratePurchaseOrderCommand(Guid LocationId, Guid SupplierId, OrderQuantityStrategy Strategy)
    : IRequest<Result<PurchaseOrderDetailDto>>;

[RequiresPermission(PermissionKeys.Purchasing.Write)]
public sealed record AddPurchaseOrderLineCommand(Guid PurchaseOrderId, Guid ProductId, decimal OrderQty, decimal CostEach, decimal CaseQty)
    : IRequest<Result<PurchaseOrderDetailDto>>;

[RequiresPermission(PermissionKeys.Purchasing.Write)]
public sealed record UpdatePurchaseOrderLineCommand(Guid LineId, decimal OrderQty, decimal CostEach)
    : IRequest<Result<PurchaseOrderDetailDto>>;

[RequiresPermission(PermissionKeys.Purchasing.Write)]
public sealed record RemovePurchaseOrderLineCommand(Guid LineId) : IRequest<Result<PurchaseOrderDetailDto>>;

/// <summary>Draft → Posted. Reserves the ordered quantity on every affected product's <c>OnOrder</c>.</summary>
[RequiresPermission(PermissionKeys.Purchasing.PostOrder)]
public sealed record PostPurchaseOrderCommand(Guid PurchaseOrderId) : IRequest<Result<PurchaseOrderDetailDto>>;

public sealed record ReceivePurchaseOrderLine(Guid LineId, decimal QtyReceived);

/// <summary>
/// Records a shipment against a Posted or PartiallyReceived PO. Freight is allocated pro-rata by each
/// received line's share of this receipt's total cost (guide p.68), then rolled into the moving-average
/// cost via <see cref="Domain.Catalog.Product.ReceiveStock"/> — the same call for a full or a partial
/// receipt, so a PO can be received in as many shipments as the supplier actually sends.
/// </summary>
[RequiresPermission(PermissionKeys.Purchasing.PostShipment)]
public sealed record ReceivePurchaseOrderCommand(
    Guid PurchaseOrderId,
    DateOnly ReceivedOn,
    decimal FreightTotal,
    IReadOnlyList<ReceivePurchaseOrderLine> Lines) : IRequest<Result<PurchaseOrderDetailDto>>;

[RequiresPermission(PermissionKeys.Purchasing.Write)]
public sealed record CancelPurchaseOrderCommand(Guid PurchaseOrderId) : IRequest<Result<PurchaseOrderDetailDto>>;

public sealed class PurchaseOrderHandlers :
    IRequestHandler<BrowsePurchaseOrdersQuery, CursorPage<PurchaseOrderRowDto>>,
    IRequestHandler<GetPurchaseOrderQuery, Result<PurchaseOrderDetailDto>>,
    IRequestHandler<GeneratePurchaseOrderCommand, Result<PurchaseOrderDetailDto>>,
    IRequestHandler<AddPurchaseOrderLineCommand, Result<PurchaseOrderDetailDto>>,
    IRequestHandler<UpdatePurchaseOrderLineCommand, Result<PurchaseOrderDetailDto>>,
    IRequestHandler<RemovePurchaseOrderLineCommand, Result<PurchaseOrderDetailDto>>,
    IRequestHandler<PostPurchaseOrderCommand, Result<PurchaseOrderDetailDto>>,
    IRequestHandler<ReceivePurchaseOrderCommand, Result<PurchaseOrderDetailDto>>,
    IRequestHandler<CancelPurchaseOrderCommand, Result<PurchaseOrderDetailDto>>
{
    public static readonly Error SupplierNotFound = new("purchase_order.supplier_not_found", "No such supplier.");
    public static readonly Error NotFound = new("purchase_order.not_found", "No such purchase order.");
    public static readonly Error LineNotFound = new("purchase_order.line_not_found", "No such purchase order line.");
    public static readonly Error ProductNotFound = new("purchase_order.product_not_found", "No such product.");
    public static readonly Error NotDraft = new("purchase_order.not_draft", "The purchase order must be Draft to edit its lines.");
    public static readonly Error NoLines = new("purchase_order.no_lines", "Add at least one line before posting.");
    public static readonly Error InvalidQuantity = new("purchase_order.invalid_quantity", "Order quantity must be greater than zero.");
    public static readonly Error NotReceivable = new("purchase_order.not_receivable", "The purchase order must be Posted or Partially Received to record a receipt.");
    public static readonly Error ReceiptExceedsOrder = new("purchase_order.receipt_exceeds_order", "Cannot receive more than remains on a line.");
    public static readonly Error AlreadyClosed = new("purchase_order.already_closed", "This purchase order is already closed or cancelled.");
    public static readonly Error CannotCancelReceived = new("purchase_order.cannot_cancel_received", "A purchase order with a receipt against it cannot be cancelled — close it instead.");

    /// <summary>Trailing window the OneWeek/TwoWeeks/MonthlySales strategies read sales velocity from.</summary>
    private const int SalesLookbackDays = 30;

    private readonly IApplicationDbContext _db;
    private readonly ISequenceGenerator _sequences;
    private readonly IPosNotifier _notifier;
    private readonly IDateTime _clock;
    private readonly ICurrentUser _currentUser;

    public PurchaseOrderHandlers(
        IApplicationDbContext db,
        ISequenceGenerator sequences,
        IPosNotifier notifier,
        IDateTime clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _sequences = sequences;
        _notifier = notifier;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<CursorPage<PurchaseOrderRowDto>> Handle(BrowsePurchaseOrdersQuery request, CancellationToken ct)
    {
        var pageSize = Cursor.PageSize(request.PageSize);

        var query = _db.PurchaseOrders.AsNoTracking().Where(o => o.LocationId == request.LocationId);

        if (request.SupplierId is { } supplierId)
        {
            query = query.Where(o => o.SupplierId == supplierId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(o => o.Status == status);
        }

        var after = Cursor.Decode(request.Cursor);
        if (after is { } cursor && Cursor.Long(cursor.SortKey) is { } key)
        {
            query = query.Where(o => o.PoNumber < key);
        }

        var orders = await query.OrderByDescending(o => o.PoNumber).Take(pageSize + 1).ToListAsync(ct);

        var hasMore = orders.Count > pageSize;
        if (hasMore)
        {
            orders.RemoveAt(orders.Count - 1);
        }

        var supplierIds = orders.Select(o => o.SupplierId).Distinct().ToList();
        var suppliers = await _db.Suppliers.AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Company, ct);

        var orderIds = orders.Select(o => o.Id).ToList();
        var lineCounts = await _db.PurchaseOrderLines.AsNoTracking()
            .Where(l => orderIds.Contains(l.PurchaseOrderId))
            .GroupBy(l => l.PurchaseOrderId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var rows = orders.Select(o => new PurchaseOrderRowDto(
            o.Id,
            o.PoNumber,
            o.SupplierId,
            suppliers.GetValueOrDefault(o.SupplierId, "—"),
            o.Status,
            o.QuantityStrategy,
            o.PostedOn,
            o.DueOn,
            o.Total,
            lineCounts.GetValueOrDefault(o.Id))).ToList();

        var last = orders.Count > 0 ? orders[^1] : null;
        var nextCursor = hasMore && last is not null ? Cursor.Encode(Cursor.Number(last.PoNumber), string.Empty) : null;

        return new CursorPage<PurchaseOrderRowDto>(rows, nextCursor, hasMore);
    }

    public async Task<Result<PurchaseOrderDetailDto>> Handle(GetPurchaseOrderQuery request, CancellationToken ct)
    {
        var order = await _db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == request.PurchaseOrderId, ct);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDetailDto>(NotFound.With("purchaseOrderId", request.PurchaseOrderId));
        }

        return Result.Success(await ToDetailAsync(order, ct));
    }

    public async Task<Result<PurchaseOrderDetailDto>> Handle(GeneratePurchaseOrderCommand request, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId && s.LocationId == request.LocationId && !s.IsDeleted, ct);
        if (supplier is null)
        {
            return Result.Failure<PurchaseOrderDetailDto>(SupplierNotFound.With("supplierId", request.SupplierId));
        }

        var order = new PurchaseOrder
        {
            PoNumber = await _sequences.NextAsync(SequenceKind.PurchaseOrder, request.LocationId, ct),
            SupplierId = supplier.Id,
            LocationId = request.LocationId,
            Status = PurchaseOrderStatus.Draft,
            QuantityStrategy = request.Strategy,
            CreatedAt = _clock.Now,
        };
        _db.PurchaseOrders.Add(order);

        var createdLines = request.Strategy == OrderQuantityStrategy.Blank
            ? []
            : await GenerateLinesAsync(order, request.Strategy, ct);

        // Summed from the lines just built, not queried back — they are only pending adds until
        // SaveChanges runs, and a DbSet query hits the database, not the change tracker.
        order.Total = createdLines.Sum(l => l.OrderCost);

        await _db.SaveChangesAsync(ct);
        var dto = await ToDetailAsync(order, ct);
        await _notifier.RowChangedAsync(request.LocationId, GridKeys.PurchaseOrder, order.Id, dto, ct);

        return Result.Success(dto);
    }

    /// <summary>
    /// The six legacy calculation methods (guide p.64), read off the top-ranked (<c>Rank == 1</c>)
    /// products this supplier sources. Sales velocity for OneWeek/TwoWeeks/MonthlySales comes from a
    /// live trailing-30-day aggregate over <see cref="Domain.Sales.SaleLine"/> rather than a
    /// pre-computed monthly snapshot — that snapshot is Phase 6 scope and does not exist yet, and the
    /// live aggregate answers the same question (what actually sold recently) without depending on it.
    /// </summary>
    private async Task<List<PurchaseOrderLine>> GenerateLinesAsync(PurchaseOrder order, OrderQuantityStrategy strategy, CancellationToken ct)
    {
        var created = new List<PurchaseOrderLine>();

        var candidates = await (
            from ps in _db.ProductSuppliers.AsNoTracking()
            join p in _db.Products.AsNoTracking() on ps.ProductId equals p.Id
            where ps.SupplierId == order.SupplierId && ps.Rank == 1 && p.LocationId == order.LocationId && !p.IsDeleted
            select new { Supplier = ps, Product = p }).ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return created;
        }

        var since = _clock.Now.AddDays(-SalesLookbackDays);
        var productIds = candidates.Select(c => c.Product.Id).ToList();

        var recentSales = await (
            from line in _db.SaleLines.AsNoTracking()
            join tx in _db.SalesTransactions.AsNoTracking() on line.TransactionId equals tx.Id
            where productIds.Contains(line.ProductId) && line.LineType == LineType.Sale && tx.CompletedAt >= since
            group line by line.ProductId into g
            select new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, ct);

        foreach (var candidate in candidates)
        {
            var product = candidate.Product;
            var onHandPlusOnOrder = product.OnHand + product.OnOrder;
            var monthlyVelocity = recentSales.GetValueOrDefault(product.Id, 0m);

            var orderQty = strategy switch
            {
                OrderQuantityStrategy.OneWeek => monthlyVelocity / SalesLookbackDays * 7m - onHandPlusOnOrder,
                OrderQuantityStrategy.TwoWeeks => monthlyVelocity / SalesLookbackDays * 14m - onHandPlusOnOrder,
                OrderQuantityStrategy.ReorderPointFixed => onHandPlusOnOrder <= product.ReorderPoint ? product.ReorderQty : 0m,
                OrderQuantityStrategy.ReorderPointToBase => onHandPlusOnOrder <= product.ReorderPoint ? product.BaseStock - onHandPlusOnOrder : 0m,
                OrderQuantityStrategy.MonthlySales => monthlyVelocity - onHandPlusOnOrder,
                _ => 0m,
            };

            orderQty = Math.Ceiling(Math.Max(0m, orderQty));
            if (orderQty <= 0m)
            {
                continue;
            }

            var costEach = candidate.Supplier.Cost;

            var line = new PurchaseOrderLine
            {
                PurchaseOrderId = order.Id,
                ProductId = product.Id,
                OrderQty = orderQty,
                CaseQty = candidate.Supplier.CaseQty > 0m ? candidate.Supplier.CaseQty : product.CaseQty,
                CostEach = costEach,
                OrderCost = orderQty * costEach,
                QtyReceived = 0m,
                InStockAtGeneration = product.OnHand,
                OnOrderAtGeneration = product.OnOrder,
                BackOrders = 0m,
                CreatedAt = _clock.Now,
            };

            _db.PurchaseOrderLines.Add(line);
            created.Add(line);
        }

        return created;
    }

    public async Task<Result<PurchaseOrderDetailDto>> Handle(AddPurchaseOrderLineCommand request, CancellationToken ct)
    {
        var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == request.PurchaseOrderId, ct);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDetailDto>(NotFound.With("purchaseOrderId", request.PurchaseOrderId));
        }

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return Result.Failure<PurchaseOrderDetailDto>(NotDraft.With("status", order.Status.ToString()));
        }

        if (request.OrderQty <= 0m)
        {
            return Result.Failure<PurchaseOrderDetailDto>(InvalidQuantity);
        }

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null)
        {
            return Result.Failure<PurchaseOrderDetailDto>(ProductNotFound.With("productId", request.ProductId));
        }

        _db.PurchaseOrderLines.Add(new PurchaseOrderLine
        {
            PurchaseOrderId = order.Id,
            ProductId = product.Id,
            OrderQty = request.OrderQty,
            CaseQty = request.CaseQty,
            CostEach = request.CostEach,
            OrderCost = request.OrderQty * request.CostEach,
            QtyReceived = 0m,
            InStockAtGeneration = product.OnHand,
            OnOrderAtGeneration = product.OnOrder,
            BackOrders = 0m,
            CreatedAt = _clock.Now,
        });

        // The new line has to actually be in the database before RecalculateTotalAsync's query can
        // see it — a DbSet query hits Postgres, not the pending change tracker.
        await _db.SaveChangesAsync(ct);
        await RecalculateTotalAsync(order, ct);
        await _db.SaveChangesAsync(ct);

        return await PublishAndReturnAsync(order, ct);
    }

    public async Task<Result<PurchaseOrderDetailDto>> Handle(UpdatePurchaseOrderLineCommand request, CancellationToken ct)
    {
        var line = await _db.PurchaseOrderLines.FirstOrDefaultAsync(l => l.Id == request.LineId, ct);
        if (line is null)
        {
            return Result.Failure<PurchaseOrderDetailDto>(LineNotFound.With("lineId", request.LineId));
        }

        var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == line.PurchaseOrderId, ct);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDetailDto>(NotFound);
        }

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return Result.Failure<PurchaseOrderDetailDto>(NotDraft.With("status", order.Status.ToString()));
        }

        if (request.OrderQty <= 0m)
        {
            return Result.Failure<PurchaseOrderDetailDto>(InvalidQuantity);
        }

        line.OrderQty = request.OrderQty;
        line.CostEach = request.CostEach;
        line.OrderCost = request.OrderQty * request.CostEach;
        line.ModifiedAt = _clock.Now;

        await _db.SaveChangesAsync(ct);
        await RecalculateTotalAsync(order, ct);
        await _db.SaveChangesAsync(ct);

        return await PublishAndReturnAsync(order, ct);
    }

    public async Task<Result<PurchaseOrderDetailDto>> Handle(RemovePurchaseOrderLineCommand request, CancellationToken ct)
    {
        var line = await _db.PurchaseOrderLines.FirstOrDefaultAsync(l => l.Id == request.LineId, ct);
        if (line is null)
        {
            return Result.Failure<PurchaseOrderDetailDto>(LineNotFound.With("lineId", request.LineId));
        }

        var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == line.PurchaseOrderId, ct);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDetailDto>(NotFound);
        }

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return Result.Failure<PurchaseOrderDetailDto>(NotDraft.With("status", order.Status.ToString()));
        }

        _db.PurchaseOrderLines.Remove(line);
        await _db.SaveChangesAsync(ct);
        await RecalculateTotalAsync(order, ct);
        await _db.SaveChangesAsync(ct);

        return await PublishAndReturnAsync(order, ct);
    }

    public async Task<Result<PurchaseOrderDetailDto>> Handle(PostPurchaseOrderCommand request, CancellationToken ct)
    {
        var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == request.PurchaseOrderId, ct);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDetailDto>(NotFound.With("purchaseOrderId", request.PurchaseOrderId));
        }

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return Result.Failure<PurchaseOrderDetailDto>(NotDraft.With("status", order.Status.ToString()));
        }

        var lines = await LinesForAsync(order.Id, ct).ToListAsync(ct);
        if (lines.Count == 0)
        {
            return Result.Failure<PurchaseOrderDetailDto>(NoLines);
        }

        var products = await _db.Products
            .Where(p => lines.Select(l => l.ProductId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var line in lines)
        {
            if (products.TryGetValue(line.ProductId, out var product))
            {
                product.UpdateStockLevels(product.OnHand, product.OnOrder + line.OrderQty);
            }
        }

        var now = _clock.Now;
        order.Status = PurchaseOrderStatus.Posted;
        order.PostedOn = _clock.Today();
        order.DueOn = _clock.Today().AddDays(30);
        order.Total = lines.Sum(l => l.OrderCost);
        order.ModifiedAt = now;

        await _db.SaveChangesAsync(ct);

        return await PublishAndReturnAsync(order, ct);
    }

    public async Task<Result<PurchaseOrderDetailDto>> Handle(ReceivePurchaseOrderCommand request, CancellationToken ct)
    {
        var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == request.PurchaseOrderId, ct);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDetailDto>(NotFound.With("purchaseOrderId", request.PurchaseOrderId));
        }

        if (order.Status is not (PurchaseOrderStatus.Posted or PurchaseOrderStatus.PartiallyReceived))
        {
            return Result.Failure<PurchaseOrderDetailDto>(NotReceivable.With("status", order.Status.ToString()));
        }

        if (request.Lines.Count == 0)
        {
            return Result.Success(await ToDetailAsync(order, ct));
        }

        var lineIds = request.Lines.Select(l => l.LineId).ToList();
        var lines = await _db.PurchaseOrderLines
            .Where(l => lineIds.Contains(l.Id) && l.PurchaseOrderId == order.Id)
            .ToDictionaryAsync(l => l.Id, ct);

        foreach (var receipt in request.Lines)
        {
            if (!lines.TryGetValue(receipt.LineId, out var line))
            {
                return Result.Failure<PurchaseOrderDetailDto>(LineNotFound.With("lineId", receipt.LineId));
            }

            if (receipt.QtyReceived <= 0m || line.QtyReceived + receipt.QtyReceived > line.OrderQty)
            {
                return Result.Failure<PurchaseOrderDetailDto>(ReceiptExceedsOrder
                    .With("lineId", receipt.LineId)
                    .With("remaining", line.OrderQty - line.QtyReceived));
            }
        }

        // Freight is allocated by each received line's share of this receipt's own total cost — a
        // receipt that only covers half a PO's lines does not split freight against lines that were
        // not in the box (guide p.68).
        var totalReceivedCost = request.Lines.Sum(r => r.QtyReceived * lines[r.LineId].CostEach);

        var receiptEntity = new PurchaseOrderReceipt
        {
            PurchaseOrderId = order.Id,
            ReceivedOn = request.ReceivedOn,
            FreightTotal = request.FreightTotal,
            StaffId = _currentUser.StaffId ?? Guid.Empty,
            CreatedAt = _clock.Now,
        };
        _db.PurchaseOrderReceipts.Add(receiptEntity);

        var productIds = request.Lines.Select(r => lines[r.LineId].ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        foreach (var receipt in request.Lines)
        {
            var line = lines[receipt.LineId];
            var lineCost = receipt.QtyReceived * line.CostEach;
            var allocatedFreight = totalReceivedCost > 0m
                ? decimal.Round(request.FreightTotal * (lineCost / totalReceivedCost), 2, MidpointRounding.AwayFromZero)
                : 0m;

            if (products.TryGetValue(line.ProductId, out var product))
            {
                product.ReceiveStock(receipt.QtyReceived, line.CostEach, allocatedFreight);
            }

            line.QtyReceived += receipt.QtyReceived;
            line.ModifiedAt = _clock.Now;

            _db.StockLedgerEntries.Add(new StockLedgerEntry
            {
                ProductId = line.ProductId,
                LocationId = order.LocationId,
                MovementType = MovementType.Receipt,
                Quantity = receipt.QtyReceived,
                UnitCost = line.CostEach,
                ReferenceType = nameof(PurchaseOrderReceipt),
                ReferenceId = receiptEntity.Id,
                OccurredAt = _clock.Now,
                StaffId = receiptEntity.StaffId,
            });

            var stockLevel = await _db.StockLevels.FirstOrDefaultAsync(
                s => s.ProductId == line.ProductId && s.VariantId == null && s.LocationId == order.LocationId, ct);
            if (stockLevel is null)
            {
                stockLevel = StockLevel.Create(line.ProductId, null, order.LocationId);
                _db.StockLevels.Add(stockLevel);
            }

            stockLevel.OnHand += receipt.QtyReceived;
        }

        var allLines = await LinesForAsync(order.Id, ct).ToListAsync(ct);
        order.Status = allLines.All(l => l.QtyReceived >= l.OrderQty)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
        order.ModifiedAt = _clock.Now;

        await _db.SaveChangesAsync(ct);

        return await PublishAndReturnAsync(order, ct);
    }

    public async Task<Result<PurchaseOrderDetailDto>> Handle(CancelPurchaseOrderCommand request, CancellationToken ct)
    {
        var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == request.PurchaseOrderId, ct);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDetailDto>(NotFound.With("purchaseOrderId", request.PurchaseOrderId));
        }

        if (order.Status is PurchaseOrderStatus.Closed or PurchaseOrderStatus.Cancelled)
        {
            return Result.Failure<PurchaseOrderDetailDto>(AlreadyClosed);
        }

        var lines = await LinesForAsync(order.Id, ct).ToListAsync(ct);
        if (lines.Any(l => l.QtyReceived > 0m))
        {
            return Result.Failure<PurchaseOrderDetailDto>(CannotCancelReceived);
        }

        if (order.Status == PurchaseOrderStatus.Posted)
        {
            var products = await _db.Products
                .Where(p => lines.Select(l => l.ProductId).Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

            foreach (var line in lines)
            {
                if (products.TryGetValue(line.ProductId, out var product))
                {
                    product.UpdateStockLevels(product.OnHand, Math.Max(0m, product.OnOrder - line.OrderQty));
                }
            }
        }

        order.Status = PurchaseOrderStatus.Cancelled;
        order.ModifiedAt = _clock.Now;

        await _db.SaveChangesAsync(ct);

        return await PublishAndReturnAsync(order, ct);
    }

    private IQueryable<PurchaseOrderLine> LinesForAsync(Guid purchaseOrderId, CancellationToken ct)
        => _db.PurchaseOrderLines.Where(l => l.PurchaseOrderId == purchaseOrderId);

    /// <summary>
    /// Re-sums <see cref="PurchaseOrder.Total"/> from the database. Only safe to call after the line
    /// change it is meant to reflect has already been saved — this queries Postgres, which does not
    /// see a pending add/update/remove still sitting in the change tracker.
    /// </summary>
    private async Task RecalculateTotalAsync(PurchaseOrder order, CancellationToken ct)
    {
        order.Total = await LinesForAsync(order.Id, ct).SumAsync(l => l.OrderCost, ct);
        order.ModifiedAt = _clock.Now;
    }

    private async Task<Result<PurchaseOrderDetailDto>> PublishAndReturnAsync(PurchaseOrder order, CancellationToken ct)
    {
        var dto = await ToDetailAsync(order, ct);
        await _notifier.RowChangedAsync(order.LocationId, GridKeys.PurchaseOrder, order.Id, dto, ct);
        return Result.Success(dto);
    }

    private async Task<PurchaseOrderDetailDto> ToDetailAsync(PurchaseOrder order, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == order.SupplierId, ct);

        var lines = await LinesForAsync(order.Id, ct).ToListAsync(ct);
        var productIds = lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var lineDtos = lines.Select(l =>
        {
            products.TryGetValue(l.ProductId, out var product);
            return new PurchaseOrderLineDto(
                l.Id,
                l.ProductId,
                product?.StockCode ?? "—",
                product?.Name ?? "—",
                l.OrderQty,
                l.CaseQty,
                l.CostEach,
                l.OrderCost,
                l.QtyReceived,
                l.InStockAtGeneration,
                l.OnOrderAtGeneration,
                l.BackOrders);
        }).ToList();

        var receipts = await _db.PurchaseOrderReceipts.AsNoTracking()
            .Where(r => r.PurchaseOrderId == order.Id)
            .Select(r => new PurchaseOrderReceiptDto(r.Id, r.ReceivedOn, r.FreightTotal, r.StaffId))
            .ToListAsync(ct);

        return new PurchaseOrderDetailDto(
            order.Id,
            order.PoNumber,
            order.LocationId,
            order.SupplierId,
            supplier?.Company ?? "—",
            order.Status,
            order.QuantityStrategy,
            order.HeaderText,
            order.PostedOn,
            order.DueOn,
            order.Total,
            lineDtos,
            receipts);
    }
}
