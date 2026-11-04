using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A role's approval limit for purchase requisitions; null for no limit.</summary>
public sealed record ApprovalLimitRow(Guid RoleId, string Role, bool MayApprove, decimal? MaxAmount);

/// <summary>
/// Each store's supply chain settings (<see cref="SupplySettings"/>: expired stock at the POS, match tolerances, reorder
/// inputs) and the approval limit of each role for requisitions.
/// </summary>
public sealed class SupplySettingsService(DataContext data)
{
    /// <summary>The store's settings, or the defaults when it has none saved.</summary>
    public async Task<SupplySettings> GetAsync(Guid storeId)
    {
        await using var db = data.Create();
        return await ForStoreAsync(db, storeId);
    }

    /// <summary>Saves a store's settings (one row per store).</summary>
    public async Task<SupplySettings> SaveAsync(Guid storeId, SupplySettings settings)
    {
        if (settings.ExpiryWarningDays is < 0 or > 3650) throw new BusinessRuleException("Warn about expiry from 0 to 3,650 days ahead.");
        if (settings.SalesWindowDays is < 1 or > 730) throw new BusinessRuleException("Average the sales over 1 to 730 days.");
        if (settings.SafetyStockDays is < 0 or > 365 || settings.OrderCoverDays is < 0 or > 365 || settings.DefaultLeadTimeDays is < 0 or > 365)
            throw new BusinessRuleException("Safety stock, cover and lead time are 0 to 365 days.");
        if (settings.QuantityTolerancePercent < 0 || settings.QuantityToleranceUnits < 0 || settings.PriceTolerancePercent < 0 || settings.PriceToleranceAmount < 0)
            throw new BusinessRuleException("Tolerances cannot be below zero.");
        if (settings.QuantityTolerancePercent > 100 || settings.PriceTolerancePercent > 100) throw new BusinessRuleException("A tolerance is at most 100%.");
        await using var db = data.Create();
        if (!await db.Stores.AnyAsync(s => s.Id == storeId)) throw new BusinessRuleException("That store does not exist.");
        var row = await db.SupplySettings.SingleOrDefaultAsync(s => s.StoreId == storeId);
        if (row is null) db.SupplySettings.Add(row = new SupplySettings { StoreId = storeId });
        row.BlockExpiredSales = settings.BlockExpiredSales;
        row.ExpiryWarningDays = settings.ExpiryWarningDays;
        row.QuantityTolerancePercent = settings.QuantityTolerancePercent;
        row.QuantityToleranceUnits = settings.QuantityToleranceUnits;
        row.PriceTolerancePercent = settings.PriceTolerancePercent;
        row.PriceToleranceAmount = settings.PriceToleranceAmount;
        row.SalesWindowDays = settings.SalesWindowDays;
        row.SafetyStockDays = settings.SafetyStockDays;
        row.OrderCoverDays = settings.OrderCoverDays;
        row.DefaultLeadTimeDays = settings.DefaultLeadTimeDays;
        row.ApproverMustDiffer = settings.ApproverMustDiffer;
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Every role, whether it may approve requisitions and up to what amount.</summary>
    public async Task<List<ApprovalLimitRow>> GetLimitsAsync()
    {
        await using var db = data.Create();
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Level).ThenBy(r => r.Name).ToListAsync();
        var limits = await db.PurchaseApprovalLimits.AsNoTracking().ToDictionaryAsync(l => l.RoleId, l => l.MaxAmount);
        return [.. roles.Select(r => new ApprovalLimitRow(r.Id, r.Name, r.Permissions.Any(p => p.Permission == Permissions.PurchasingApprove),
            limits.TryGetValue(r.Id, out var max) ? max : null))];
    }

    /// <summary>Sets a role's limit; null removes it (no limit).</summary>
    public async Task SetLimitAsync(Guid roleId, decimal? maxAmount)
    {
        if (maxAmount < 0) throw new BusinessRuleException("A limit cannot be below zero.");
        await using var db = data.Create();
        if (!await db.Roles.AnyAsync(r => r.Id == roleId)) throw new BusinessRuleException("That role no longer exists.");
        var row = await db.PurchaseApprovalLimits.SingleOrDefaultAsync(l => l.RoleId == roleId);
        if (maxAmount is null)
        {
            if (row is not null) db.PurchaseApprovalLimits.Remove(row);
        }
        else
        {
            if (row is null) db.PurchaseApprovalLimits.Add(row = new PurchaseApprovalLimit { RoleId = roleId });
            row.MaxAmount = Money.Round(maxAmount.Value);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>The settings of a store in an open context, defaults when none are saved.</summary>
    internal static async Task<SupplySettings> ForStoreAsync(RetailPlusDbContext db, Guid storeId) =>
        await db.SupplySettings.AsNoTracking().SingleOrDefaultAsync(s => s.StoreId == storeId) ?? new SupplySettings { StoreId = storeId };
}
