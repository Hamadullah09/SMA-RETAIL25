using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Dtos;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Queries;

/// <summary>Full authoritative cart state. Also what a client calls after a revision gap.</summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record GetCartQuery(long CartId) : IRequest<Result<CartDto>>;

/// <summary>
/// Runs the pricing engine and returns totals without writing anything (doc 05). This is what the
/// live totals panel calls, and its 120 ms budget is why it never touches the database for
/// configuration it could have been handed.
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record QuoteCartQuery(long CartId) : IRequest<Result<CartTotalsDto>>;

/// <summary>The active cart at a station, if there is one. Used when a browser reconnects.</summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record GetStationCartQuery(long StationId) : IRequest<Result<CartDto>>;

/// <summary>The recall list for a location (guide p.11).</summary>
[RequiresPermission(PermissionKeys.Pos.Recall)]
public sealed record ListSuspendedCartsQuery(long LocationId) : IRequest<IReadOnlyList<SuspendedCartDto>>;

/// <summary>The station's effective settings, so the till knows how to behave before the first scan.</summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record GetStationPolicyQuery(long StationId) : IRequest<Result<StationPolicyDto>>;

public sealed class CartQueryHandlers
    : IRequestHandler<GetCartQuery, Result<CartDto>>,
      IRequestHandler<QuoteCartQuery, Result<CartTotalsDto>>,
      IRequestHandler<GetStationCartQuery, Result<CartDto>>,
      IRequestHandler<ListSuspendedCartsQuery, IReadOnlyList<SuspendedCartDto>>,
      IRequestHandler<GetStationPolicyQuery, Result<StationPolicyDto>>
{
    private readonly CartWorkflow _workflow;
    private readonly ICartStore _store;
    private readonly IApplicationDbContext _db;
    private readonly PosContextLoader _contextLoader;
    private readonly CartPricingService _pricing;

    public CartQueryHandlers(
        CartWorkflow workflow,
        ICartStore store,
        IApplicationDbContext db,
        PosContextLoader contextLoader,
        CartPricingService pricing)
    {
        _workflow = workflow;
        _store = store;
        _db = db;
        _contextLoader = contextLoader;
        _pricing = pricing;
    }

    public async Task<Result<CartDto>> Handle(GetCartQuery request, CancellationToken ct)
    {
        var quote = await _workflow.QuoteAsync(request.CartId, ct);
        return quote.IsFailure ? Result.Failure<CartDto>(quote.Error) : Result.Success(quote.Value.Dto);
    }

    public async Task<Result<CartTotalsDto>> Handle(QuoteCartQuery request, CancellationToken ct)
    {
        var quote = await _workflow.QuoteAsync(request.CartId, ct);
        return quote.IsFailure ? Result.Failure<CartTotalsDto>(quote.Error) : Result.Success(quote.Value.Dto.Totals);
    }

    public async Task<Result<CartDto>> Handle(GetStationCartQuery request, CancellationToken ct)
    {
        var snapshot = await _store.GetByStationAsync(request.StationId, ct);
        if (snapshot is null)
        {
            return Result.Failure<CartDto>(Cart.NotActive.With("stationId", request.StationId));
        }

        var context = await _contextLoader.LoadAsync(request.StationId, ct);
        if (context.IsFailure)
        {
            return Result.Failure<CartDto>(context.Error);
        }

        var quote = await _pricing.QuoteAsync(snapshot, context.Value, ct);
        return Result.Success(quote.Dto);
    }

    public async Task<IReadOnlyList<SuspendedCartDto>> Handle(ListSuspendedCartsQuery request, CancellationToken ct)
    {
        var carts = await _db.Carts.AsNoTracking()
            .Where(c => c.LocationId == request.LocationId && c.Status == CartStatus.Suspended)
            .OrderByDescending(c => c.SuspendedAt)
            .ToListAsync(ct);

        if (carts.Count == 0)
        {
            return [];
        }

        var cartIds = carts.Select(c => c.Id).ToList();

        var lineTotals = await _db.CartLines.AsNoTracking()
            .Where(l => cartIds.Contains(l.CartId))
            .GroupBy(l => l.CartId)
            .Select(g => new { CartId = g.Key, Count = g.Count(), Total = g.Sum(l => l.ExtendedNet + l.Tax1Amount + l.Tax2Amount) })
            .ToDictionaryAsync(x => x.CartId, ct);

        var customerIds = carts.Where(c => c.CustomerId.HasValue).Select(c => c.CustomerId!.Value).Distinct().ToList();
        var customers = customerIds.Count == 0
            ? []
            : await _db.Customers.AsNoTracking()
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FullName, ct);

        return carts.Select(cart =>
        {
            lineTotals.TryGetValue(cart.Id, out var totals);
            return new SuspendedCartDto(
                cart.Id,
                cart.HeldName,
                cart.SuspendedByStaffId ?? cart.StaffId,
                cart.CustomerId is { } id && customers.TryGetValue(id, out var name) ? name : null,
                totals?.Count ?? 0,
                totals?.Total ?? 0m,
                cart.SuspendedAt ?? cart.CreatedAt);
        }).ToList();
    }

    public async Task<Result<StationPolicyDto>> Handle(GetStationPolicyQuery request, CancellationToken ct)
    {
        var contextResult = await _contextLoader.LoadAsync(request.StationId, ct);
        if (contextResult.IsFailure)
        {
            return Result.Failure<StationPolicyDto>(contextResult.Error);
        }

        var context = contextResult.Value;

        return Result.Success(new StationPolicyDto(
            context.Station.Id,
            context.Station.StationCode,
            context.FastScanMode,
            context.AutoSaveSales,
            context.ConfirmBeforeSaving,
            context.ScanRandomWeightBarcodes,
            context.Policy.AllowTaxOverride,
            context.Policy.StaffMayDiscount,
            context.Policy.AllowItemListEdit,
            context.Policy.RequireSupervisorToVoid,
            context.DefaultTenderTypeId,
            context.Currency.MinimumTender,
            context.Currency.Code,
            context.Currency.Symbol));
    }
}
