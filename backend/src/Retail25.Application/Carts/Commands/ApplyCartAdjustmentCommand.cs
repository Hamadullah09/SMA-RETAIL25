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
/// The Credits menu (guide p.7): subtotal discount, coupon, bottle return, gift certificate and the
/// loyalty reward.
/// <para>
/// Returns and trade-ins are <i>not</i> here — they are negative lines, because they move stock and
/// need their own tax treatment. Treating a return as a sale-level credit would produce the right
/// total and the wrong tax, inventory and COGS.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record ApplyCartAdjustmentCommand(
    long CartId,
    AdjustmentType Type,
    string Label,
    decimal Amount = 0m,
    decimal Percent = 0m,
    string? Serial = null) : IRequest<Result<CartDto>>;

[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record RemoveCartAdjustmentCommand(long CartId, long AdjustmentId) : IRequest<Result<CartDto>>;

public sealed class ApplyCartAdjustmentHandler
    : IRequestHandler<ApplyCartAdjustmentCommand, Result<CartDto>>,
      IRequestHandler<RemoveCartAdjustmentCommand, Result<CartDto>>
{
    public static readonly Error NotFound = new("adjustment.not_found", "That adjustment is no longer on the cart.");
    public static readonly Error CertificateUnknown = new("gift_certificate.unknown", "No gift certificate matches that serial number.");
    public static readonly Error CertificateRedeemed = new("gift_certificate.already_redeemed", "That gift certificate has already been redeemed.");
    public static readonly Error LoyaltyUnavailable = new("loyalty.unavailable", "This sale does not qualify for a reward.");

    private readonly CartWorkflow _workflow;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ApplyCartAdjustmentHandler(CartWorkflow workflow, IApplicationDbContext db, ICurrentUser currentUser)
    {
        _workflow = workflow;
        _db = db;
        _currentUser = currentUser;
    }

    public Task<Result<CartDto>> Handle(ApplyCartAdjustmentCommand request, CancellationToken ct)
        => _workflow.MutateAsync(request.CartId, async (snapshot, context, token) =>
        {
            if (request.Type == AdjustmentType.SubtotalDiscount
                && !context.Policy.StaffMayDiscount
                && !_currentUser.HasPermission(PermissionKeys.Pos.Discount))
            {
                return Result.Failure(CartLineFactory.DiscountNotPermitted);
            }

            var validation = await ValidateAsync(request, snapshot, context, token);
            if (validation.IsFailure)
            {
                return validation;
            }

            // The subtotal discount and the loyalty reward are singular: applying one replaces the
            // one already there rather than stacking, which is how the legacy F3-F2 key behaved.
            if (request.Type is AdjustmentType.SubtotalDiscount or AdjustmentType.LoyaltyReward)
            {
                snapshot.Adjustments.RemoveAll(a => a.Type == request.Type);
            }

            var created = CartAdjustment.Create(
                snapshot.Cart.Id,
                request.Type,
                request.Label,
                request.Amount,
                request.Percent,
                _currentUser.StaffId ?? snapshot.Cart.StaffId,
                _workflow.Clock.Now,
                request.Serial);

            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            snapshot.Adjustments.Add(created.Value);
            return Result.Success();
        }, ct);

    public Task<Result<CartDto>> Handle(RemoveCartAdjustmentCommand request, CancellationToken ct)
        => _workflow.MutateAsync(request.CartId, (snapshot, _, _) =>
        {
            var removed = snapshot.Adjustments.RemoveAll(a => a.Id == request.AdjustmentId);
            return Task.FromResult(removed == 0
                ? Result.Failure(NotFound.With("adjustmentId", request.AdjustmentId))
                : Result.Success());
        }, ct);

    private async Task<Result> ValidateAsync(
        ApplyCartAdjustmentCommand request,
        CartSnapshot snapshot,
        PosContext context,
        CancellationToken ct)
    {
        switch (request.Type)
        {
            case AdjustmentType.GiftCertificate:
            {
                var certificate = await _db.GiftCertificates
                    .FirstOrDefaultAsync(g => g.SerialNumber == request.Serial, ct);

                if (certificate is null)
                {
                    return Result.Failure(CertificateUnknown.With("serial", request.Serial));
                }

                return certificate.RemainingValue <= 0m
                    ? Result.Failure(CertificateRedeemed.With("serial", request.Serial))
                    : Result.Success();
            }

            case AdjustmentType.LoyaltyReward:
            {
                if (context.Loyalty is not { IsEnabled: true } || snapshot.Cart.CustomerId is null)
                {
                    return Result.Failure(LoyaltyUnavailable);
                }

                var profile = await _db.CustomerPricingProfiles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.CustomerId == snapshot.Cart.CustomerId, ct);

                return profile is null || profile.RewardPoints < context.Loyalty.MinimumRequired
                    ? Result.Failure(LoyaltyUnavailable.With("required", context.Loyalty.MinimumRequired))
                    : Result.Success();
            }

            default:
                return Result.Success();
        }
    }
}
