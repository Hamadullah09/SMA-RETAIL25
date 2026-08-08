using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Inventory;
using Retail25.Domain.Orders;

namespace Retail25.Application.Orders;

public sealed record LayawayLineDto(long Id, long ProductId, string StockCode, string ProductName, decimal Quantity, decimal UnitPrice);

public sealed record LayawayDto(
    long Id,
    long LayawayNumber,
    long CustomerId,
    string CustomerName,
    LayawayStatus Status,
    decimal Total,
    decimal AmountPaid,
    DateOnly CreatedOn,
    IReadOnlyList<LayawayLineDto> Lines);

public sealed record LayawayLineInput(long ProductId, decimal Quantity, decimal UnitPrice);

[RequiresPermission(PermissionKeys.Customer.Write)]
public sealed record CreateLayawayCommand(long CustomerId, long LocationId, IReadOnlyList<LayawayLineInput> Lines)
    : IRequest<Result<LayawayDto>>;

[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record BrowseLayawaysQuery(
    long LocationId, long? CustomerId = null, LayawayStatus? Status = null, string? Cursor = null, int PageSize = 50)
    : IRequest<CursorPage<LayawayDto>>;

[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record GetLayawayQuery(long LayawayId) : IRequest<Result<LayawayDto>>;

[RequiresPermission(PermissionKeys.Customer.Write)]
public sealed record TakeLayawayPaymentCommand(long LayawayId, decimal Amount, long TenderTypeId) : IRequest<Result<LayawayDto>>;

/// <summary>Releases the reserved stock and refunds nothing automatically — a cancelled layaway's deposit is handled by the store's own policy.</summary>
[RequiresPermission(PermissionKeys.Customer.Write)]
public sealed record CancelLayawayCommand(long LayawayId) : IRequest<Result<LayawayDto>>;

public sealed class LayawayHandlers :
    IRequestHandler<CreateLayawayCommand, Result<LayawayDto>>,
    IRequestHandler<BrowseLayawaysQuery, CursorPage<LayawayDto>>,
    IRequestHandler<GetLayawayQuery, Result<LayawayDto>>,
    IRequestHandler<TakeLayawayPaymentCommand, Result<LayawayDto>>,
    IRequestHandler<CancelLayawayCommand, Result<LayawayDto>>
{
    public static readonly Error CustomerNotFound = new("layaway.customer_not_found", "No such customer.");
    public static readonly Error ProductNotFound = new("layaway.product_not_found", "No such product.");
    public static readonly Error NoLines = new("layaway.no_lines", "A layaway needs at least one line.");
    public static readonly Error NotFound = new("layaway.not_found", "No such layaway.");
    public static readonly Error AlreadyClosed = new("layaway.already_closed", "This layaway is already paid in full or cancelled.");
    public static readonly Error InvalidAmount = new("layaway.invalid_amount", "Amount must be greater than zero.");
    public static readonly Error TenderTypeUnknown = new("layaway.tender_type_unknown", "That tender type is not configured.");

    private readonly IApplicationDbContext _db;
    private readonly ISequenceGenerator _sequences;
    private readonly IDateTime _clock;

    public LayawayHandlers(IApplicationDbContext db, ISequenceGenerator sequences, IDateTime clock)
    {
        _db = db;
        _sequences = sequences;
        _clock = clock;
    }

    public async Task<Result<LayawayDto>> Handle(CreateLayawayCommand request, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
        {
            return Result.Failure<LayawayDto>(NoLines);
        }

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);
        if (customer is null)
        {
            return Result.Failure<LayawayDto>(CustomerNotFound.With("customerId", request.CustomerId));
        }

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        foreach (var line in request.Lines)
        {
            if (!products.ContainsKey(line.ProductId))
            {
                return Result.Failure<LayawayDto>(ProductNotFound.With("productId", line.ProductId));
            }
        }

        var layaway = new Layaway
        {
            LayawayNumber = await _sequences.NextAsync(SequenceKind.Layaway, request.LocationId, ct),
            CustomerId = customer.Id,
            LocationId = request.LocationId,
            Status = LayawayStatus.Open,
            Total = request.Lines.Sum(l => l.Quantity * l.UnitPrice),
            AmountPaid = 0m,
            CreatedOn = _clock.Today(),
            CreatedAt = _clock.Now,
        };
        _db.Layaways.Add(layaway);

        foreach (var line in request.Lines)
        {
            _db.LayawayLines.Add(new LayawayLine
            {
                LayawayId = layaway.Id,
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
            });

            await ReserveAsync(line.ProductId, request.LocationId, line.Quantity, ct);
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(layaway, ct));
    }

    public async Task<CursorPage<LayawayDto>> Handle(BrowseLayawaysQuery request, CancellationToken ct)
    {
        var pageSize = Cursor.PageSize(request.PageSize);

        var query = _db.Layaways.AsNoTracking().Where(l => l.LocationId == request.LocationId);

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(l => l.CustomerId == customerId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(l => l.Status == status);
        }

        var after = Cursor.Decode(request.Cursor);
        if (after is { } cursor && Cursor.Long(cursor.SortKey) is { } key)
        {
            query = query.Where(l => l.LayawayNumber < key);
        }

        var layaways = await query.OrderByDescending(l => l.LayawayNumber).Take(pageSize + 1).ToListAsync(ct);

        var hasMore = layaways.Count > pageSize;
        if (hasMore)
        {
            layaways.RemoveAt(layaways.Count - 1);
        }

        var dtos = new List<LayawayDto>(layaways.Count);
        foreach (var layaway in layaways)
        {
            dtos.Add(await ToDtoAsync(layaway, ct));
        }

        var last = layaways.Count > 0 ? layaways[^1] : null;
        var nextCursor = hasMore && last is not null ? Cursor.Encode(Cursor.Number(last.LayawayNumber), string.Empty) : null;

        return new CursorPage<LayawayDto>(dtos, nextCursor, hasMore);
    }

    public async Task<Result<LayawayDto>> Handle(GetLayawayQuery request, CancellationToken ct)
    {
        var layaway = await _db.Layaways.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.LayawayId, ct);
        return layaway is null
            ? Result.Failure<LayawayDto>(NotFound.With("layawayId", request.LayawayId))
            : Result.Success(await ToDtoAsync(layaway, ct));
    }

    public async Task<Result<LayawayDto>> Handle(TakeLayawayPaymentCommand request, CancellationToken ct)
    {
        if (request.Amount <= 0m)
        {
            return Result.Failure<LayawayDto>(InvalidAmount);
        }

        var layaway = await _db.Layaways.FirstOrDefaultAsync(l => l.Id == request.LayawayId, ct);
        if (layaway is null)
        {
            return Result.Failure<LayawayDto>(NotFound.With("layawayId", request.LayawayId));
        }

        if (layaway.Status != LayawayStatus.Open)
        {
            return Result.Failure<LayawayDto>(AlreadyClosed);
        }

        if (!await _db.TenderTypes.AsNoTracking().AnyAsync(t => t.Id == request.TenderTypeId, ct))
        {
            return Result.Failure<LayawayDto>(TenderTypeUnknown.With("tenderTypeId", request.TenderTypeId));
        }

        _db.LayawayPayments.Add(new LayawayPayment
        {
            LayawayId = layaway.Id,
            Amount = request.Amount,
            TenderTypeId = request.TenderTypeId,
            PaidOn = _clock.Today(),
            CreatedAt = _clock.Now,
        });

        layaway.AmountPaid += request.Amount;
        layaway.ModifiedAt = _clock.Now;

        if (layaway.AmountPaid >= layaway.Total)
        {
            layaway.Status = LayawayStatus.PaidInFull;

            var lines = await _db.LayawayLines.Where(l => l.LayawayId == layaway.Id).ToListAsync(ct);
            foreach (var line in lines)
            {
                await ReserveAsync(line.ProductId, layaway.LocationId, -line.Quantity, ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(layaway, ct));
    }

    public async Task<Result<LayawayDto>> Handle(CancelLayawayCommand request, CancellationToken ct)
    {
        var layaway = await _db.Layaways.FirstOrDefaultAsync(l => l.Id == request.LayawayId, ct);
        if (layaway is null)
        {
            return Result.Failure<LayawayDto>(NotFound.With("layawayId", request.LayawayId));
        }

        if (layaway.Status != LayawayStatus.Open)
        {
            return Result.Failure<LayawayDto>(AlreadyClosed);
        }

        var lines = await _db.LayawayLines.Where(l => l.LayawayId == layaway.Id).ToListAsync(ct);
        foreach (var line in lines)
        {
            await ReserveAsync(line.ProductId, layaway.LocationId, -line.Quantity, ct);
        }

        layaway.Status = LayawayStatus.Cancelled;
        layaway.ModifiedAt = _clock.Now;

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(layaway, ct));
    }

    private async Task ReserveAsync(long productId, long locationId, decimal delta, CancellationToken ct)
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

    private async Task<LayawayDto> ToDtoAsync(Layaway layaway, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == layaway.CustomerId, ct);

        var lines = await _db.LayawayLines.AsNoTracking().Where(l => l.LayawayId == layaway.Id).ToListAsync(ct);
        var productIds = lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var lineDtos = lines.Select(l =>
        {
            products.TryGetValue(l.ProductId, out var product);
            return new LayawayLineDto(l.Id, l.ProductId, product?.StockCode ?? "—", product?.Name ?? "—", l.Quantity, l.UnitPrice);
        }).ToList();

        return new LayawayDto(
            layaway.Id, layaway.LayawayNumber, layaway.CustomerId, customer?.FullName ?? "—",
            layaway.Status, layaway.Total, layaway.AmountPaid, layaway.CreatedOn, lineDtos);
    }
}
