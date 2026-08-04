using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Orders;

namespace Retail25.Application.Orders;

public sealed record PriceQuoteLineDto(long Id, long ProductId, string StockCode, string ProductName, decimal Quantity, decimal UnitPrice);

public sealed record PriceQuoteDto(
    long Id,
    long QuoteNumber,
    long CustomerId,
    string CustomerName,
    PriceQuoteStatus Status,
    DateOnly IssuedOn,
    DateOnly? ExpiresOn,
    decimal Total,
    IReadOnlyList<PriceQuoteLineDto> Lines);

public sealed record PriceQuoteLineInput(long ProductId, decimal Quantity, decimal UnitPrice);

[RequiresPermission(PermissionKeys.Customer.Write)]
public sealed record CreatePriceQuoteCommand(
    long CustomerId, long LocationId, IReadOnlyList<PriceQuoteLineInput> Lines, DateOnly? ExpiresOn = null)
    : IRequest<Result<PriceQuoteDto>>;

[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record BrowsePriceQuotesQuery(
    long LocationId, long? CustomerId = null, PriceQuoteStatus? Status = null, string? Cursor = null, int PageSize = 50)
    : IRequest<CursorPage<PriceQuoteDto>>;

[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record GetPriceQuoteQuery(long PriceQuoteId) : IRequest<Result<PriceQuoteDto>>;

/// <summary>Marks the quote Converted — the frontend rings the lines into the current cart at the held prices.</summary>
[RequiresPermission(PermissionKeys.Customer.Write)]
public sealed record ConvertPriceQuoteCommand(long PriceQuoteId) : IRequest<Result<PriceQuoteDto>>;

[RequiresPermission(PermissionKeys.Customer.Write)]
public sealed record CancelPriceQuoteCommand(long PriceQuoteId) : IRequest<Result<PriceQuoteDto>>;

public sealed class PriceQuoteHandlers :
    IRequestHandler<CreatePriceQuoteCommand, Result<PriceQuoteDto>>,
    IRequestHandler<BrowsePriceQuotesQuery, CursorPage<PriceQuoteDto>>,
    IRequestHandler<GetPriceQuoteQuery, Result<PriceQuoteDto>>,
    IRequestHandler<ConvertPriceQuoteCommand, Result<PriceQuoteDto>>,
    IRequestHandler<CancelPriceQuoteCommand, Result<PriceQuoteDto>>
{
    public static readonly Error CustomerNotFound = new("price_quote.customer_not_found", "No such customer.");
    public static readonly Error ProductNotFound = new("price_quote.product_not_found", "No such product.");
    public static readonly Error NoLines = new("price_quote.no_lines", "A price quote needs at least one line.");
    public static readonly Error NotFound = new("price_quote.not_found", "No such price quote.");
    public static readonly Error NotOpen = new("price_quote.not_open", "This quote has already been converted, expired or cancelled.");
    public static readonly Error Expired = new("price_quote.expired", "This quote has expired.");

    private readonly IApplicationDbContext _db;
    private readonly ISequenceGenerator _sequences;
    private readonly IDateTime _clock;

    public PriceQuoteHandlers(IApplicationDbContext db, ISequenceGenerator sequences, IDateTime clock)
    {
        _db = db;
        _sequences = sequences;
        _clock = clock;
    }

    public async Task<Result<PriceQuoteDto>> Handle(CreatePriceQuoteCommand request, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
        {
            return Result.Failure<PriceQuoteDto>(NoLines);
        }

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);
        if (customer is null)
        {
            return Result.Failure<PriceQuoteDto>(CustomerNotFound.With("customerId", request.CustomerId));
        }

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var knownProducts = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);

        foreach (var line in request.Lines)
        {
            if (!knownProducts.Contains(line.ProductId))
            {
                return Result.Failure<PriceQuoteDto>(ProductNotFound.With("productId", line.ProductId));
            }
        }

        var quote = new PriceQuote
        {
            QuoteNumber = await _sequences.NextAsync(SequenceKind.PriceQuote, request.LocationId, ct),
            CustomerId = customer.Id,
            LocationId = request.LocationId,
            Status = PriceQuoteStatus.Open,
            IssuedOn = _clock.Today(),
            ExpiresOn = request.ExpiresOn,
            Total = request.Lines.Sum(l => l.Quantity * l.UnitPrice),
            CreatedAt = _clock.Now,
        };
        _db.PriceQuotes.Add(quote);

        foreach (var line in request.Lines)
        {
            _db.PriceQuoteLines.Add(new PriceQuoteLine
            {
                PriceQuoteId = quote.Id,
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
            });
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(quote, ct));
    }

    public async Task<CursorPage<PriceQuoteDto>> Handle(BrowsePriceQuotesQuery request, CancellationToken ct)
    {
        var pageSize = Cursor.PageSize(request.PageSize);

        var query = _db.PriceQuotes.AsNoTracking().Where(q => q.LocationId == request.LocationId);

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(q => q.CustomerId == customerId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(q => q.Status == status);
        }

        var after = Cursor.Decode(request.Cursor);
        if (after is { } cursor && Cursor.Long(cursor.SortKey) is { } key)
        {
            query = query.Where(q => q.QuoteNumber < key);
        }

        var quotes = await query.OrderByDescending(q => q.QuoteNumber).Take(pageSize + 1).ToListAsync(ct);

        var hasMore = quotes.Count > pageSize;
        if (hasMore)
        {
            quotes.RemoveAt(quotes.Count - 1);
        }

        var dtos = new List<PriceQuoteDto>(quotes.Count);
        foreach (var quote in quotes)
        {
            dtos.Add(await ToDtoAsync(quote, ct));
        }

        var last = quotes.Count > 0 ? quotes[^1] : null;
        var nextCursor = hasMore && last is not null ? Cursor.Encode(Cursor.Number(last.QuoteNumber), string.Empty) : null;

        return new CursorPage<PriceQuoteDto>(dtos, nextCursor, hasMore);
    }

    public async Task<Result<PriceQuoteDto>> Handle(GetPriceQuoteQuery request, CancellationToken ct)
    {
        var quote = await _db.PriceQuotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == request.PriceQuoteId, ct);
        return quote is null
            ? Result.Failure<PriceQuoteDto>(NotFound.With("priceQuoteId", request.PriceQuoteId))
            : Result.Success(await ToDtoAsync(quote, ct));
    }

    public async Task<Result<PriceQuoteDto>> Handle(ConvertPriceQuoteCommand request, CancellationToken ct)
    {
        var quote = await _db.PriceQuotes.FirstOrDefaultAsync(q => q.Id == request.PriceQuoteId, ct);
        if (quote is null)
        {
            return Result.Failure<PriceQuoteDto>(NotFound.With("priceQuoteId", request.PriceQuoteId));
        }

        if (quote.Status != PriceQuoteStatus.Open)
        {
            return Result.Failure<PriceQuoteDto>(NotOpen);
        }

        if (quote.ExpiresOn is { } expires && expires < _clock.Today())
        {
            quote.Status = PriceQuoteStatus.Expired;
            quote.ModifiedAt = _clock.Now;
            await _db.SaveChangesAsync(ct);
            return Result.Failure<PriceQuoteDto>(Expired);
        }

        quote.Status = PriceQuoteStatus.Converted;
        quote.ModifiedAt = _clock.Now;

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(quote, ct));
    }

    public async Task<Result<PriceQuoteDto>> Handle(CancelPriceQuoteCommand request, CancellationToken ct)
    {
        var quote = await _db.PriceQuotes.FirstOrDefaultAsync(q => q.Id == request.PriceQuoteId, ct);
        if (quote is null)
        {
            return Result.Failure<PriceQuoteDto>(NotFound.With("priceQuoteId", request.PriceQuoteId));
        }

        if (quote.Status != PriceQuoteStatus.Open)
        {
            return Result.Failure<PriceQuoteDto>(NotOpen);
        }

        quote.Status = PriceQuoteStatus.Cancelled;
        quote.ModifiedAt = _clock.Now;

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(quote, ct));
    }

    private async Task<PriceQuoteDto> ToDtoAsync(PriceQuote quote, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == quote.CustomerId, ct);

        var lines = await _db.PriceQuoteLines.AsNoTracking().Where(l => l.PriceQuoteId == quote.Id).ToListAsync(ct);
        var productIds = lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var lineDtos = lines.Select(l =>
        {
            products.TryGetValue(l.ProductId, out var product);
            return new PriceQuoteLineDto(l.Id, l.ProductId, product?.StockCode ?? "—", product?.Name ?? "—", l.Quantity, l.UnitPrice);
        }).ToList();

        return new PriceQuoteDto(
            quote.Id, quote.QuoteNumber, quote.CustomerId, customer?.FullName ?? "—",
            quote.Status, quote.IssuedOn, quote.ExpiresOn, quote.Total, lineDtos);
    }
}
