using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;

namespace Retail25.Application.Loyalty;

public sealed record LoyaltyPolicyDto(
    long LocationId,
    bool IsEnabled,
    decimal PointsPerDollar,
    int MinimumRequired,
    bool PercentEnabled,
    decimal RewardPercent,
    bool FixedEnabled,
    decimal RewardFixedAmount,
    bool SuppressIfSubtotalDiscountApplied);

public sealed record LoyaltyBalanceDto(long CustomerId, string CustomerName, int RewardPoints);

public sealed record LoyaltyLedgerEntryDto(long Id, LoyaltyEntryType EntryType, int Points, DateTimeOffset OccurredAt);

[RequiresPermission(PermissionKeys.Settings.Read)]
public sealed record GetLoyaltyPolicyQuery(long LocationId) : IRequest<LoyaltyPolicyDto>;

/// <summary>Find-or-create by location — the settings screen edits one row per store, created on first save.</summary>
[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record SaveLoyaltyPolicyCommand(
    long LocationId,
    bool IsEnabled,
    decimal PointsPerDollar,
    int MinimumRequired,
    bool PercentEnabled,
    decimal RewardPercent,
    bool FixedEnabled,
    decimal RewardFixedAmount,
    bool SuppressIfSubtotalDiscountApplied) : IRequest<Result<LoyaltyPolicyDto>>;

[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record GetLoyaltyBalanceQuery(long CustomerId) : IRequest<Result<LoyaltyBalanceDto>>;

[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record GetLoyaltyLedgerQuery(long CustomerId) : IRequest<IReadOnlyList<LoyaltyLedgerEntryDto>>;

/// <summary>A supervisor's correction outside the sale flow — a goodwill grant, or fixing a miscount.</summary>
[RequiresPermission(PermissionKeys.Customer.Write)]
public sealed record AdjustLoyaltyPointsCommand(long CustomerId, int PointsDelta, string Reason) : IRequest<Result<LoyaltyBalanceDto>>;

public sealed class LoyaltyHandlers :
    IRequestHandler<GetLoyaltyPolicyQuery, LoyaltyPolicyDto>,
    IRequestHandler<SaveLoyaltyPolicyCommand, Result<LoyaltyPolicyDto>>,
    IRequestHandler<GetLoyaltyBalanceQuery, Result<LoyaltyBalanceDto>>,
    IRequestHandler<GetLoyaltyLedgerQuery, IReadOnlyList<LoyaltyLedgerEntryDto>>,
    IRequestHandler<AdjustLoyaltyPointsCommand, Result<LoyaltyBalanceDto>>
{
    public static readonly Error CustomerNotFound = new("loyalty.customer_not_found", "No such customer.");
    public static readonly Error ReasonRequired = new("loyalty.reason_required", "A reason is required for a manual adjustment.");
    public static readonly Error PointsDeltaIsZero = new("loyalty.points_delta_is_zero", "An adjustment must change the point balance.");
    public static readonly Error InsufficientBalance = new("loyalty.insufficient_balance", "This would take the customer's balance below zero.");

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public LoyaltyHandlers(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<LoyaltyPolicyDto> Handle(GetLoyaltyPolicyQuery request, CancellationToken ct)
    {
        var policy = await _db.LoyaltyPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.LocationId == request.LocationId, ct);

        return policy is null ? DefaultDto(request.LocationId) : ToDto(policy);
    }

    public async Task<Result<LoyaltyPolicyDto>> Handle(SaveLoyaltyPolicyCommand request, CancellationToken ct)
    {
        var policy = await _db.LoyaltyPolicies.FirstOrDefaultAsync(p => p.LocationId == request.LocationId, ct);

        if (policy is null)
        {
            policy = new LoyaltyPolicy { LocationId = request.LocationId, CreatedAt = _clock.Now };
            _db.LoyaltyPolicies.Add(policy);
        }

        policy.IsEnabled = request.IsEnabled;
        policy.PointsPerDollar = request.PointsPerDollar;
        policy.MinimumRequired = request.MinimumRequired;
        policy.PercentEnabled = request.PercentEnabled;
        policy.RewardPercent = request.RewardPercent;
        policy.FixedEnabled = request.FixedEnabled;
        policy.RewardFixedAmount = request.RewardFixedAmount;
        policy.SuppressIfSubtotalDiscountApplied = request.SuppressIfSubtotalDiscountApplied;
        policy.ModifiedAt = _clock.Now;

        await _db.SaveChangesAsync(ct);

        return Result.Success(ToDto(policy));
    }

    public async Task<Result<LoyaltyBalanceDto>> Handle(GetLoyaltyBalanceQuery request, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);
        if (customer is null)
        {
            return Result.Failure<LoyaltyBalanceDto>(CustomerNotFound.With("customerId", request.CustomerId));
        }

        var points = await _db.CustomerPricingProfiles.AsNoTracking()
            .Where(p => p.CustomerId == request.CustomerId)
            .Select(p => (int?)p.RewardPoints)
            .FirstOrDefaultAsync(ct) ?? 0;

        return Result.Success(new LoyaltyBalanceDto(customer.Id, customer.FullName, points));
    }

    public async Task<IReadOnlyList<LoyaltyLedgerEntryDto>> Handle(GetLoyaltyLedgerQuery request, CancellationToken ct)
        => await _db.LoyaltyLedgerEntries.AsNoTracking()
            .Where(e => e.CustomerId == request.CustomerId)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new LoyaltyLedgerEntryDto(e.Id, e.EntryType, e.Points, e.OccurredAt))
            .ToListAsync(ct);

    public async Task<Result<LoyaltyBalanceDto>> Handle(AdjustLoyaltyPointsCommand request, CancellationToken ct)
    {
        if (request.PointsDelta == 0)
        {
            return Result.Failure<LoyaltyBalanceDto>(PointsDeltaIsZero);
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Failure<LoyaltyBalanceDto>(ReasonRequired);
        }

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);
        if (customer is null)
        {
            return Result.Failure<LoyaltyBalanceDto>(CustomerNotFound.With("customerId", request.CustomerId));
        }

        var profile = await _db.CustomerPricingProfiles.FirstOrDefaultAsync(p => p.CustomerId == request.CustomerId, ct);
        if (profile is null)
        {
            profile = CustomerPricingProfile.Create(request.CustomerId);
            _db.CustomerPricingProfiles.Add(profile);
        }

        if (profile.RewardPoints + request.PointsDelta < 0)
        {
            return Result.Failure<LoyaltyBalanceDto>(InsufficientBalance.With("balance", profile.RewardPoints));
        }

        profile.RewardPoints += request.PointsDelta;

        _db.LoyaltyLedgerEntries.Add(LoyaltyLedgerEntry.Manual(request.CustomerId, request.PointsDelta, _clock.Now));

        await _db.SaveChangesAsync(ct);

        return Result.Success(new LoyaltyBalanceDto(customer.Id, customer.FullName, profile.RewardPoints));
    }

    private static LoyaltyPolicyDto DefaultDto(long locationId) => new(locationId, false, 0m, 0, false, 0m, false, 0m, true);

    private static LoyaltyPolicyDto ToDto(LoyaltyPolicy policy) => new(
        policy.LocationId,
        policy.IsEnabled,
        policy.PointsPerDollar,
        policy.MinimumRequired,
        policy.PercentEnabled,
        policy.RewardPercent,
        policy.FixedEnabled,
        policy.RewardFixedAmount,
        policy.SuppressIfSubtotalDiscountApplied);
}
