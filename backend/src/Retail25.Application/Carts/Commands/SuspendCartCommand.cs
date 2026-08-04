using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Dtos;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Commands;

/// <summary>
/// Parks a sale so the till is free for the next customer (guide p.11, F11 Special → Suspend).
/// <para>
/// A suspended cart is written through to Postgres rather than left in Redis. It has to survive a
/// restart, and it has to be recallable at a different till — the customer who went back for a
/// forgotten item does not necessarily return to the same queue.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Suspend)]
public sealed record SuspendCartCommand(long CartId, string? Label = null) : IRequest<Result<SuspendedCartDto>>;

/// <summary>Brings a parked sale back, at whichever till asks for it (guide p.11).</summary>
[RequiresPermission(PermissionKeys.Pos.Recall)]
public sealed record RecallCartCommand(long CartId, long StationId) : IRequest<Result<CartDto>>;

public sealed class SuspendCartHandler
    : IRequestHandler<SuspendCartCommand, Result<SuspendedCartDto>>,
      IRequestHandler<RecallCartCommand, Result<CartDto>>
{
    public static readonly Error StationBusy = new("cart.station_busy", "That till already has a sale in progress. Finish or suspend it first.");
    public static readonly Error NotSuspended = new("cart.not_suspended", "That cart is not suspended.");

    private readonly ICartStore _store;
    private readonly IApplicationDbContext _db;
    private readonly PosContextLoader _contextLoader;
    private readonly CartPricingService _pricing;
    private readonly IPosNotifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    public SuspendCartHandler(
        ICartStore store,
        IApplicationDbContext db,
        PosContextLoader contextLoader,
        CartPricingService pricing,
        IPosNotifier notifier,
        ICurrentUser currentUser,
        IDateTime clock)
    {
        _store = store;
        _db = db;
        _contextLoader = contextLoader;
        _pricing = pricing;
        _notifier = notifier;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<SuspendedCartDto>> Handle(SuspendCartCommand request, CancellationToken ct)
    {
        var snapshot = await _store.GetAsync(request.CartId, ct);
        if (snapshot is null)
        {
            return Result.Failure<SuspendedCartDto>(Cart.NotActive.With("cartId", request.CartId));
        }

        var contextResult = await _contextLoader.LoadAsync(snapshot.Cart.StationId, ct);
        if (contextResult.IsFailure)
        {
            return Result.Failure<SuspendedCartDto>(contextResult.Error);
        }

        var quote = await _pricing.QuoteAsync(snapshot, contextResult.Value, ct);

        var staffId = _currentUser.StaffId ?? snapshot.Cart.StaffId;
        var suspend = snapshot.Cart.Suspend(request.Label, staffId, _clock.Now);
        if (suspend.IsFailure)
        {
            return Result.Failure<SuspendedCartDto>(suspend.Error);
        }

        await PersistAsync(snapshot, ct);
        await _store.RemoveAsync(snapshot.Cart.Id, snapshot.Cart.StationId, ct);

        var customerName = snapshot.Cart.CustomerId is { } customerId
            ? await _db.Customers.AsNoTracking().Where(c => c.Id == customerId).Select(c => c.FullName).FirstOrDefaultAsync(ct)
            : null;

        var dto = new SuspendedCartDto(
            snapshot.Cart.Id,
            snapshot.Cart.HeldName,
            staffId,
            customerName,
            snapshot.Lines.Count,
            quote.Pricing.GrandTotal,
            _clock.Now);

        await _notifier.CartSuspendedAsync(snapshot.Cart.LocationId, dto, ct);
        return Result.Success(dto);
    }

    public async Task<Result<CartDto>> Handle(RecallCartCommand request, CancellationToken ct)
    {
        // The destination till must be free: two sales cannot share one screen.
        var occupying = await _store.GetByStationAsync(request.StationId, ct);
        if (occupying is { Cart.IsActive: true, Lines.Count: > 0 })
        {
            return Result.Failure<CartDto>(StationBusy.With("stationId", request.StationId));
        }

        var cart = await _db.Carts.FirstOrDefaultAsync(c => c.Id == request.CartId, ct);
        if (cart is null || cart.Status != CartStatus.Suspended)
        {
            return Result.Failure<CartDto>(NotSuspended.With("cartId", request.CartId));
        }

        var contextResult = await _contextLoader.LoadAsync(request.StationId, ct);
        if (contextResult.IsFailure)
        {
            return Result.Failure<CartDto>(contextResult.Error);
        }

        var context = contextResult.Value;
        var staffId = _currentUser.StaffId ?? cart.StaffId;

        var recall = cart.Recall(request.StationId, staffId, _clock.Now, context.Policy.AbandonedCartTimeoutMinutes);
        if (recall.IsFailure)
        {
            return Result.Failure<CartDto>(recall.Error);
        }

        var snapshot = new CartSnapshot(cart)
        {
            Lines = await _db.CartLines.Where(l => l.CartId == cart.Id).ToListAsync(ct),
            Adjustments = await _db.CartAdjustments.Where(a => a.CartId == cart.Id).ToListAsync(ct),
        };
        snapshot.TaxOverride = await _db.CartTaxOverrides.FirstOrDefaultAsync(o => o.CartId == cart.Id, ct);

        cart.Touch(_clock.Now, context.Policy.AbandonedCartTimeoutMinutes);

        // The cart moves back into Redis; the Postgres copy stays as the audit trail of the hold.
        await _store.SaveAsync(snapshot, ct);
        await _db.SaveChangesAsync(ct);

        var quote = await _pricing.QuoteAsync(snapshot, context, ct);

        if (occupying is not null && occupying.Cart.Id != cart.Id)
        {
            await _store.RemoveAsync(occupying.Cart.Id, request.StationId, ct);
        }

        await _notifier.CartRecalledAsync(cart.LocationId, cart.Id, request.StationId, ct);
        await _notifier.CartUpdatedAsync(cart.LocationId, cart.Id, quote.Dto, cart.Revision, ct);

        return Result.Success(quote.Dto);
    }

    /// <summary>Write-behind: the suspended cart, its lines, adjustments and override land in Postgres.</summary>
    private async Task PersistAsync(CartSnapshot snapshot, CancellationToken ct)
    {
        var existing = await _db.Carts.FirstOrDefaultAsync(c => c.Id == snapshot.Cart.Id, ct);
        if (existing is null)
        {
            _db.Carts.Add(snapshot.Cart);
        }
        else
        {
            existing.Status = snapshot.Cart.Status;
            existing.HeldName = snapshot.Cart.HeldName;
            existing.SuspendedAt = snapshot.Cart.SuspendedAt;
            existing.SuspendedByStaffId = snapshot.Cart.SuspendedByStaffId;
            existing.CustomerId = snapshot.Cart.CustomerId;
            existing.NextLineSequence = snapshot.Cart.NextLineSequence;
            existing.Revision = snapshot.Cart.Revision;
            existing.ExpiresAt = null;
        }

        var staleLines = await _db.CartLines.Where(l => l.CartId == snapshot.Cart.Id).ToListAsync(ct);
        _db.CartLines.RemoveRange(staleLines);
        _db.CartLines.AddRange(snapshot.Lines);

        var staleAdjustments = await _db.CartAdjustments.Where(a => a.CartId == snapshot.Cart.Id).ToListAsync(ct);
        _db.CartAdjustments.RemoveRange(staleAdjustments);
        _db.CartAdjustments.AddRange(snapshot.Adjustments);

        var staleOverrides = await _db.CartTaxOverrides.Where(o => o.CartId == snapshot.Cart.Id).ToListAsync(ct);
        _db.CartTaxOverrides.RemoveRange(staleOverrides);
        if (snapshot.TaxOverride is not null)
        {
            _db.CartTaxOverrides.Add(snapshot.TaxOverride);
        }

        await _db.SaveChangesAsync(ct);
    }
}
