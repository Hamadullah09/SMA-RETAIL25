using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Shoppers.Dtos;
using Retail25.Domain.Common;
using Retail25.Domain.Trolleys;

namespace Retail25.Application.Shoppers.Queries;

/// <summary>
/// What this shopper has bought here before, newest first.
/// <para>
/// The customer's own receipts and nothing else. The shopper's id comes from their token and the
/// sales are reached only through their own <see cref="TrolleySession"/> rows — there is no sale id,
/// customer id or date range in the request, so there is nothing a caller could change to see
/// somebody else's shopping. That is the same rule the rest of the shopper API follows: a row is
/// yours if a session of yours points at it.
/// </para>
/// <para>
/// Carries no <c>[RequiresPermission]</c>, for the usual reason — a shopper token resolves to the
/// empty permission set, so an attribute would refuse every customer.
/// </para>
/// </summary>
/// <param name="Take">
/// Bounded, and clamped rather than trusted. A phone shows a short list and a loyal customer may have
/// hundreds of visits; an unbounded query here would be a way to make the server do arbitrary work.
/// </param>
public sealed record GetMyPreviousSalesQuery(int Take = 20)
    : IRequest<Result<IReadOnlyList<ShopperSaleDto>>>;

public sealed class GetMyPreviousSalesHandler
    : IRequestHandler<GetMyPreviousSalesQuery, Result<IReadOnlyList<ShopperSaleDto>>>
{
    private const int MaxTake = 100;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentShopper _shopper;

    public GetMyPreviousSalesHandler(IApplicationDbContext db, ICurrentShopper shopper)
    {
        _db = db;
        _shopper = shopper;
    }

    public async Task<Result<IReadOnlyList<ShopperSaleDto>>> Handle(
        GetMyPreviousSalesQuery request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_shopper.ShopperId is not { } shopperId)
        {
            return Result.Failure<IReadOnlyList<ShopperSaleDto>>(Trolleys.Services.TrolleyAllocator.NotSignedIn);
        }

        var take = Math.Clamp(request.Take, 1, MaxTake);

        // Joined rather than filtered by a collected list of ids: a customer with a long history would
        // otherwise put hundreds of ids into an IN clause on every visit to the screen.
        var query =
            from session in _db.TrolleySessions
            where session.ShopperId == shopperId && session.SaleId != null
            join sale in _db.SalesTransactions on session.SaleId equals sale.Id
            join trolley in _db.Trolleys on session.TrolleyId equals trolley.Id
            orderby sale.CompletedAt descending
            select new ShopperSaleDto(
                sale.Id,
                sale.TransactionNumber,
                sale.CompletedAt,
                sale.GrandTotal,
                _db.SaleLines.Count(line => line.TransactionId == sale.Id),
                trolley.Code);

        var sales = await query.Take(take).ToListAsync(ct);

        return Result.Success<IReadOnlyList<ShopperSaleDto>>(sales);
    }
}
