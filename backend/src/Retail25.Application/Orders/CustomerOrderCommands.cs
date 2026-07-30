using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Inventory;
using Retail25.Domain.Orders;

namespace Retail25.Application.Orders;

public sealed record CustomerOrderLineDto(
    Guid Id, Guid ProductId, string StockCode, string ProductName, decimal OrderedQty, decimal FilledQty, decimal UnitPrice);

public sealed record CustomerOrderDto(
    Guid Id,
    long OrderNumber,
    Guid CustomerId,
    string CustomerName,
    CustomerOrderStatus Status,
    DateOnly OrderedOn,
    string? Notes,
    IReadOnlyList<CustomerOrderLineDto> Lines);

public sealed record CustomerOrderLineInput(Guid ProductId, decimal Quantity, decimal UnitPrice);

[RequiresPermission(PermissionKeys.Customer.Write)]
public sealed record CreateCustomerOrderCommand(
    Guid CustomerId, Guid LocationId, IReadOnlyList<CustomerOrderLineInput> Lines, string? Notes = null)
    : IRequest<Result<CustomerOrderDto>>;

[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record BrowseCustomerOrdersQuery(
    Guid LocationId, Guid? CustomerId = null, CustomerOrderStatus? Status = null, string? Cursor = null, int PageSize = 50)
    : IRequest<CursorPage<CustomerOrderDto>>;

[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record GetCustomerOrderQuery(Guid CustomerOrderId) : IRequest<Result<CustomerOrderDto>>;

/// <summary>
/// Fills as much of the order as current stock allows and releases the filled quantity's reservation.
/// The actual sale still happens at the till — this returns the stock codes, quantities and the
/// originally agreed price so the cashier rings them in at the price the customer was promised.
/// </summary>
[RequiresPermission(PermissionKeys.Customer.Write)]
public sealed record FillCustomerOrderCommand(Guid CustomerOrderId) : IRequest<Result<CustomerOrderDto>>;

[RequiresPermission(PermissionKeys.Customer.Write)]
public sealed record CancelCustomerOrderCommand(Guid CustomerOrderId) : IRequest<Result<CustomerOrderDto>>;

public sealed class CustomerOrderHandlers :
    IRequestHandler<CreateCustomerOrderCommand, Result<CustomerOrderDto>>,
    IRequestHandler<BrowseCustomerOrdersQuery, CursorPage<CustomerOrderDto>>,
    IRequestHandler<GetCustomerOrderQuery, Result<CustomerOrderDto>>,
    IRequestHandler<FillCustomerOrderCommand, Result<CustomerOrderDto>>,
    IRequestHandler<CancelCustomerOrderCommand, Result<CustomerOrderDto>>
{
    public static readonly Error CustomerNotFound = new("customer_order.customer_not_found", "No such customer.");
    public static readonly Error ProductNotFound = new("customer_order.product_not_found", "No such product.");
    public static readonly Error NoLines = new("customer_order.no_lines", "A customer order needs at least one line.");
    public static readonly Error NotFound = new("customer_order.not_found", "No such customer order.");
    public static readonly Error AlreadyClosed = new("customer_order.already_closed", "This order is already filled or cancelled.");

    private readonly IApplicationDbContext _db;
    private readonly ISequenceGenerator _sequences;
    private readonly IDateTime _clock;

    public CustomerOrderHandlers(IApplicationDbContext db, ISequenceGenerator sequences, IDateTime clock)
    {
        _db = db;
        _sequences = sequences;
        _clock = clock;
    }

    public async Task<Result<CustomerOrderDto>> Handle(CreateCustomerOrderCommand request, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
        {
            return Result.Failure<CustomerOrderDto>(NoLines);
        }

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);
        if (customer is null)
        {
            return Result.Failure<CustomerOrderDto>(CustomerNotFound.With("customerId", request.CustomerId));
        }

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        foreach (var line in request.Lines)
        {
            if (!products.ContainsKey(line.ProductId))
            {
                return Result.Failure<CustomerOrderDto>(ProductNotFound.With("productId", line.ProductId));
            }
        }

        var order = new CustomerOrder
        {
            OrderNumber = await _sequences.NextAsync(SequenceKind.CustomerOrder, request.LocationId, ct),
            CustomerId = customer.Id,
            LocationId = request.LocationId,
            Status = CustomerOrderStatus.Open,
            OrderedOn = _clock.Today(),
            Notes = request.Notes,
            CreatedAt = _clock.Now,
        };
        _db.CustomerOrders.Add(order);

        foreach (var line in request.Lines)
        {
            _db.CustomerOrderLines.Add(new CustomerOrderLine
            {
                CustomerOrderId = order.Id,
                ProductId = line.ProductId,
                OrderedQty = line.Quantity,
                FilledQty = 0m,
                UnitPrice = line.UnitPrice,
                CreatedAt = _clock.Now,
            });

            await ReserveAsync(line.ProductId, request.LocationId, line.Quantity, ct);
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(order, ct));
    }

    public async Task<CursorPage<CustomerOrderDto>> Handle(BrowseCustomerOrdersQuery request, CancellationToken ct)
    {
        var pageSize = Cursor.PageSize(request.PageSize);

        var query = _db.CustomerOrders.AsNoTracking().Where(o => o.LocationId == request.LocationId);

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(o => o.CustomerId == customerId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(o => o.Status == status);
        }

        var after = Cursor.Decode(request.Cursor);
        if (after is { } cursor && Cursor.Long(cursor.SortKey) is { } key)
        {
            query = query.Where(o => o.OrderNumber < key);
        }

        var orders = await query.OrderByDescending(o => o.OrderNumber).Take(pageSize + 1).ToListAsync(ct);

        var hasMore = orders.Count > pageSize;
        if (hasMore)
        {
            orders.RemoveAt(orders.Count - 1);
        }

        var dtos = new List<CustomerOrderDto>(orders.Count);
        foreach (var order in orders)
        {
            dtos.Add(await ToDtoAsync(order, ct));
        }

        var last = orders.Count > 0 ? orders[^1] : null;
        var nextCursor = hasMore && last is not null ? Cursor.Encode(Cursor.Number(last.OrderNumber), string.Empty) : null;

        return new CursorPage<CustomerOrderDto>(dtos, nextCursor, hasMore);
    }

    public async Task<Result<CustomerOrderDto>> Handle(GetCustomerOrderQuery request, CancellationToken ct)
    {
        var order = await _db.CustomerOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == request.CustomerOrderId, ct);
        return order is null
            ? Result.Failure<CustomerOrderDto>(NotFound.With("customerOrderId", request.CustomerOrderId))
            : Result.Success(await ToDtoAsync(order, ct));
    }

    public async Task<Result<CustomerOrderDto>> Handle(FillCustomerOrderCommand request, CancellationToken ct)
    {
        var order = await _db.CustomerOrders.FirstOrDefaultAsync(o => o.Id == request.CustomerOrderId, ct);
        if (order is null)
        {
            return Result.Failure<CustomerOrderDto>(NotFound.With("customerOrderId", request.CustomerOrderId));
        }

        if (order.Status is CustomerOrderStatus.Filled or CustomerOrderStatus.Cancelled)
        {
            return Result.Failure<CustomerOrderDto>(AlreadyClosed);
        }

        var lines = await _db.CustomerOrderLines.Where(l => l.CustomerOrderId == order.Id).ToListAsync(ct);
        var products = await _db.Products
            .Where(p => lines.Select(l => l.ProductId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var line in lines)
        {
            var remaining = line.OrderedQty - line.FilledQty;
            if (remaining <= 0m || !products.TryGetValue(line.ProductId, out var product))
            {
                continue;
            }

            var fillable = Math.Min(remaining, Math.Max(0m, product.OnHand));
            if (fillable <= 0m)
            {
                continue;
            }

            line.FilledQty += fillable;
            line.ModifiedAt = _clock.Now;

            await ReserveAsync(line.ProductId, order.LocationId, -fillable, ct);
        }

        order.Status = lines.All(l => l.FilledQty >= l.OrderedQty)
            ? CustomerOrderStatus.Filled
            : lines.Any(l => l.FilledQty > 0m)
                ? CustomerOrderStatus.PartiallyFilled
                : CustomerOrderStatus.Open;
        order.ModifiedAt = _clock.Now;

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(order, ct));
    }

    public async Task<Result<CustomerOrderDto>> Handle(CancelCustomerOrderCommand request, CancellationToken ct)
    {
        var order = await _db.CustomerOrders.FirstOrDefaultAsync(o => o.Id == request.CustomerOrderId, ct);
        if (order is null)
        {
            return Result.Failure<CustomerOrderDto>(NotFound.With("customerOrderId", request.CustomerOrderId));
        }

        if (order.Status is CustomerOrderStatus.Filled or CustomerOrderStatus.Cancelled)
        {
            return Result.Failure<CustomerOrderDto>(AlreadyClosed);
        }

        var lines = await _db.CustomerOrderLines.Where(l => l.CustomerOrderId == order.Id).ToListAsync(ct);

        foreach (var line in lines)
        {
            var unfilled = line.OrderedQty - line.FilledQty;
            if (unfilled > 0m)
            {
                await ReserveAsync(line.ProductId, order.LocationId, -unfilled, ct);
            }
        }

        order.Status = CustomerOrderStatus.Cancelled;
        order.ModifiedAt = _clock.Now;

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(order, ct));
    }

    /// <summary>Adjusts the soft reservation on a product's stock level. Positive reserves, negative releases.</summary>
    private async Task ReserveAsync(Guid productId, Guid locationId, decimal delta, CancellationToken ct)
    {
        var level = await _db.StockLevels.FirstOrDefaultAsync(
            s => s.ProductId == productId && s.VariantId == null && s.LocationId == locationId, ct);

        if (level is null)
        {
            level = StockLevel.Create(productId, null, locationId);
            _db.StockLevels.Add(level);
        }

        level.Committed = Math.Max(0m, level.Committed + delta);
    }

    private async Task<CustomerOrderDto> ToDtoAsync(CustomerOrder order, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CustomerId, ct);

        var lines = await _db.CustomerOrderLines.AsNoTracking().Where(l => l.CustomerOrderId == order.Id).ToListAsync(ct);
        var productIds = lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var lineDtos = lines.Select(l =>
        {
            products.TryGetValue(l.ProductId, out var product);
            return new CustomerOrderLineDto(l.Id, l.ProductId, product?.StockCode ?? "—", product?.Name ?? "—", l.OrderedQty, l.FilledQty, l.UnitPrice);
        }).ToList();

        return new CustomerOrderDto(
            order.Id, order.OrderNumber, order.CustomerId, customer?.FullName ?? "—", order.Status, order.OrderedOn, order.Notes, lineDtos);
    }
}
