using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>What one promotion cost (its discounts) and earned (the net sales it applied to) in a period.</summary>
public sealed record PromotionReportRow(Guid PromotionId, string Name, PromotionKind? Kind, int Sales, decimal Units, decimal NetSales, decimal Discount, int CouponUses);

/// <summary>
/// Promotions: kept on the server (or on the desktop of a shop without one) and sent down to every till, where
/// <see cref="ApplyAsync"/> works them out for the sale being rung up with <see cref="PromotionEngine"/>. A sale records
/// on each line the promotion that applied to it, and the coupons it used.
/// </summary>
public sealed partial class PromotionService(DataContext data)
{
    [GeneratedRegex("^[A-Z0-9][A-Z0-9_-]{2,29}$")]
    private static partial Regex CouponPattern();

    // Reading ---------------------------------------------------------------------------------------------------------

    /// <summary>Every promotion with its targets and stores, by name; optionally only those switched on or off, or matching a text.</summary>
    public async Task<List<Promotion>> ListAsync(bool? active = null, string? search = null)
    {
        await using var db = data.Create();
        var q = db.Promotions.AsNoTracking().AsSplitQuery().Include(p => p.Targets).Include(p => p.Stores).AsQueryable();
        if (active is { } on) q = q.Where(p => p.IsActive == on);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim();
            q = q.Where(p => p.Name.Contains(t) || (p.CouponCode ?? "").Contains(t));
        }
        return await q.OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<Promotion?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.Promotions.AsNoTracking().AsSplitQuery().Include(p => p.Targets).Include(p => p.Stores).SingleOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>The promotions that may still run at a store: switched on, not ended, for every store or this one.</summary>
    public async Task<List<Promotion>> ForStoreAsync(Guid storeId, DateOnly today)
    {
        await using var db = data.Create();
        return await db.Promotions.AsNoTracking().AsSplitQuery().Include(p => p.Targets).Include(p => p.Stores)
            .Where(p => p.IsActive && (p.EndsOn == null || p.EndsOn >= today) && (p.AllStores || p.Stores.Any(s => s.StoreId == storeId)))
            .ToListAsync();
    }

    /// <summary>How often each promotion's coupon was used (every store), by promotion.</summary>
    public async Task<Dictionary<Guid, int>> CouponUsesAsync(IReadOnlyCollection<Guid>? promotionIds = null)
    {
        await using var db = data.Create();
        var q = db.CouponUses.AsNoTracking();
        if (promotionIds is not null) q = q.Where(u => promotionIds.Contains(u.PromotionId));
        return await q.GroupBy(u => u.PromotionId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
    }

    /// <summary>The names of the departments and categories targets point at, for <see cref="PromotionEngine.Describe"/>.</summary>
    public async Task<Dictionary<Guid, string>> TargetNamesAsync()
    {
        await using var db = data.Create();
        var names = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name);
        foreach (var c in await db.Categories.AsNoTracking().ToListAsync()) names[c.Id] = c.Name;
        return names;
    }

    public static string Describe(Promotion p, IReadOnlyDictionary<Guid, string> names) =>
        PromotionEngine.Describe(p, t => t.DepartmentId is { } d ? names.GetValueOrDefault(d) : t.CategoryId is { } c ? names.GetValueOrDefault(c) : null);

    /// <summary>
    /// Works the promotions out for a sale being rung up at this station's store, now (the till's local time), with the
    /// coupons entered on the draft; see <see cref="PromotionEngine.Apply"/>.
    /// </summary>
    public async Task<PromotionOutcome> ApplyAsync(SaleDraft draft, DateTime? now = null)
    {
        var store = data.Station?.StoreId ?? throw new InvalidOperationException("Promotions need the store the sale is made at.");
        var at = now ?? DateTime.Now;
        var promotions = await ForStoreAsync(store, DateOnly.FromDateTime(at));
        var uses = await CouponUsesAsync([.. promotions.Where(p => p.CouponCode != null).Select(p => p.Id)]);
        return PromotionEngine.Apply(draft, promotions, new PromotionContext(store, at, draft.CouponCodes, uses));
    }

    // Changing --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Adds or changes a promotion with its targets and stores (a stale version is a conflict). The fields a kind does not
    /// use are kept as they are sent; the checks are those of the kind.
    /// </summary>
    public async Task<Promotion> SaveAsync(Promotion p)
    {
        Validate(p);
        await using var db = data.Create();
        foreach (var t in p.Targets)
        {
            if (t.DepartmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d)) throw new BusinessRuleException("A department the promotion names does not exist.");
            if (t.CategoryId is { } c && !await db.Categories.AnyAsync(x => x.Id == c)) throw new BusinessRuleException("A category the promotion names does not exist.");
        }
        foreach (var s in p.Stores)
            if (!await db.Stores.AnyAsync(x => x.Id == s.StoreId)) throw new BusinessRuleException("A store the promotion names does not exist.");
        if (p.CouponCode is { } code && await db.Promotions.AnyAsync(x => x.CouponCode == code && x.Id != p.Id))
            throw new BusinessRuleException($"Another promotion already has the coupon code {code}.");

        Promotion row;
        if (p.Id == Guid.Empty)
        {
            row = new Promotion();
            db.Promotions.Add(row);
        }
        else
        {
            row = await db.Promotions.Include(x => x.Targets).Include(x => x.Stores).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == p.Id)
                  ?? throw new BusinessRuleException("That promotion no longer exists.");
            if (row.Version != p.Version) throw new ConcurrencyConflictException("Someone else changed this promotion. Load it again and make your changes again.");
            db.Entry(row).Property(x => x.Version).OriginalValue = p.Version;
        }
        row.Name = p.Name; row.Description = p.Description; row.Kind = p.Kind; row.IsActive = p.IsActive; row.StartsOn = p.StartsOn; row.EndsOn = p.EndsOn;
        row.Days = p.Days; row.StartTime = p.StartTime; row.EndTime = p.EndTime; row.Priority = p.Priority; row.Combinable = p.Combinable;
        row.AllStores = p.AllStores; row.Scope = p.Scope; row.Percent = p.Percent; row.Amount = p.Amount; row.BuyQuantity = p.BuyQuantity;
        row.GetQuantity = p.GetQuantity; row.GetPercent = p.GetPercent; row.BundlePrice = p.BundlePrice; row.SpendThreshold = p.SpendThreshold;
        row.CouponCode = p.CouponCode; row.CouponMaxUses = p.CouponMaxUses;

        // Targets and stores that stay keep their rows, so the tills change only what changed.
        static string Key(PromotionTarget t) => $"{t.Role}|{t.DepartmentId}|{t.CategoryId}|{t.Plu}";
        var wanted = p.Targets.GroupBy(Key).Select(g => g.First()).ToList();
        foreach (var gone in row.Targets.Where(t => wanted.All(w => Key(w) != Key(t))).ToList()) row.Targets.Remove(gone);
        foreach (var w in wanted)
        {
            var have = row.Targets.FirstOrDefault(t => Key(t) == Key(w));
            if (have is null) row.Targets.Add(new PromotionTarget { Role = w.Role, DepartmentId = w.DepartmentId, CategoryId = w.CategoryId, Plu = w.Plu, Quantity = w.Quantity });
            else if (have.Quantity != w.Quantity) have.Quantity = w.Quantity;
        }
        List<Guid> stores = p.AllStores ? [] : [.. p.Stores.Select(s => s.StoreId).Distinct()];
        foreach (var gone in row.Stores.Where(s => !stores.Contains(s.StoreId)).ToList()) row.Stores.Remove(gone);
        foreach (var s in stores.Where(s => row.Stores.All(x => x.StoreId != s))) row.Stores.Add(new PromotionStore { StoreId = s });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("Someone else changed this promotion. Load it again and make your changes again.");
        }
        return row;
    }

    /// <summary>The checks of a promotion before it is saved; also tidies its name, code and targets.</summary>
    public static void Validate(Promotion p)
    {
        p.Name = (p.Name ?? "").Trim();
        if (p.Name.Length == 0) throw new BusinessRuleException("Give the promotion a name (it is shown on the receipt).");
        if (p.Name.Length > 60) throw new BusinessRuleException("A promotion's name is at most 60 characters.");
        p.Description = string.IsNullOrWhiteSpace(p.Description) ? null : p.Description.Trim();
        if (p.StartsOn is { } s && p.EndsOn is { } e && e < s) throw new BusinessRuleException("The promotion ends before it starts.");
        if (p.Days is < 0 or > 127) throw new BusinessRuleException("Those are not days of the week.");
        if ((p.StartTime is null) != (p.EndTime is null)) throw new BusinessRuleException("A happy hour needs a start and an end time.");
        p.CouponCode = string.IsNullOrWhiteSpace(p.CouponCode) ? null : PromotionEngine.NormalizeCode(p.CouponCode);
        if (p.CouponCode is { } code && !CouponPattern().IsMatch(code))
            throw new BusinessRuleException("A coupon code has 3 to 30 letters, digits, - or _.");
        if (p.CouponMaxUses < 0) throw new BusinessRuleException("The number of uses cannot be below zero (0 means no limit).");
        if (!p.AllStores && p.Stores.Count == 0) throw new BusinessRuleException("Choose the stores the promotion runs at, or every store.");
        foreach (var t in p.Targets)
        {
            t.Plu = string.IsNullOrWhiteSpace(t.Plu) ? null : t.Plu.Trim().ToUpperInvariant();
            int named = (t.DepartmentId is null ? 0 : 1) + (t.CategoryId is null ? 0 : 1) + (t.Plu is null ? 0 : 1);
            if (named != 1) throw new BusinessRuleException("Each item a promotion names is a department, a category or a stock code.");
        }
        bool Has(PromotionTargetRole role) => p.Targets.Any(t => t.Role == role);
        if (p.Kind != PromotionKind.BundlePrice && p.Scope != PromotionScope.AllItems)
        {
            bool right = p.Targets.Any(t => t.Role == PromotionTargetRole.Qualifies && p.Scope switch
            {
                PromotionScope.Departments => t.DepartmentId is not null,
                PromotionScope.Categories => t.CategoryId is not null,
                _ => t.Plu is not null,
            });
            if (!right) throw new BusinessRuleException($"Choose the {p.Scope.ToString().ToLowerInvariant()} the promotion applies to.");
        }
        switch (p.Kind)
        {
            case PromotionKind.PercentOff when p.Percent is <= 0 or > 100:
                throw new BusinessRuleException("A percent off is more than 0 and at most 100.");
            case PromotionKind.AmountOff when p.Amount <= 0:
                throw new BusinessRuleException("Enter the amount off each unit.");
            case PromotionKind.BuyXGetY when p.BuyQuantity < 1 || p.GetQuantity < 1:
                throw new BusinessRuleException("Enter how many to buy and how many they get.");
            case PromotionKind.BuyXGetY when p.GetPercent is <= 0 or > 100:
                throw new BusinessRuleException("The discount on what they get is more than 0 and at most 100% (100 is free).");
            case PromotionKind.BundlePrice when !Has(PromotionTargetRole.BundlePart):
                throw new BusinessRuleException("Choose the items of the bundle.");
            case PromotionKind.BundlePrice when p.BundlePrice <= 0:
                throw new BusinessRuleException("Enter the bundle's price.");
            case PromotionKind.BundlePrice when p.Targets.Any(t => t.Role == PromotionTargetRole.BundlePart && t.Quantity < 1):
                throw new BusinessRuleException("Each part of a bundle has a quantity of at least 1.");
            case PromotionKind.SpendOver when p.SpendThreshold <= 0:
                throw new BusinessRuleException("Enter the spend the discount starts at.");
            case PromotionKind.SpendOver when (p.Percent is <= 0 or > 100) && p.Amount <= 0:
                throw new BusinessRuleException("Enter the discount: a percent (at most 100) or an amount.");
        }
    }

    /// <summary>Deletes a promotion that was never used; one that was is switched off instead, so its sales keep their meaning.</summary>
    public async Task DeleteAsync(Guid id)
    {
        await using var db = data.Create();
        var p = await db.Promotions.Include(x => x.Targets).Include(x => x.Stores).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id)
                ?? throw new BusinessRuleException("That promotion no longer exists.");
        if (await db.SaleLines.AnyAsync(l => l.PromotionId == id) || await db.CouponUses.AnyAsync(u => u.PromotionId == id))
            throw new BusinessRuleException("Sales used this promotion, so it is kept. Switch it off instead.");
        db.Promotions.Remove(p);
        await db.SaveChangesAsync();
    }

    // Sales -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The coupons a sale used, in the sale's transaction: one use for each promotion with a coupon code that applied
    /// to it. A coupon already used up (at any till this one has heard from) refuses the sale.
    /// </summary>
    internal static async Task RecordCouponsAsync(RetailPlusDbContext db, Sale sale, SaleDraft draft)
    {
        var ids = sale.Lines.Where(l => l.PromotionId is not null).Select(l => l.PromotionId!.Value).Distinct().ToList();
        if (ids.Count == 0) return;
        var coupons = await db.Promotions.AsNoTracking().Where(p => ids.Contains(p.Id) && p.CouponCode != null).ToListAsync();
        foreach (var p in coupons)
        {
            if (p.CouponMaxUses > 0 && await db.CouponUses.CountAsync(u => u.PromotionId == p.Id) >= p.CouponMaxUses)
                throw new BusinessRuleException($"Coupon {p.CouponCode} has been used up.");
            var code = draft.CouponCodes.Select(PromotionEngine.NormalizeCode).FirstOrDefault(c => c == p.CouponCode) ?? p.CouponCode!;
            db.CouponUses.Add(new CouponUse
            {
                PromotionId = p.Id, Code = code, SaleId = sale.Id, InvoiceNumber = sale.InvoiceNumber, ClientId = sale.ClientId, UsedAt = sale.SoldAt,
                StationNumber = sale.StationNumber,
            });
        }
    }

    /// <summary>
    /// What each promotion cost and earned on the completed sales of a period (store days), at some stores (null: every
    /// store): line promotions by their lines, sale-wide ones by the sales they applied to.
    /// </summary>
    public async Task<List<PromotionReportRow>> ReportAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? stores = null)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        await using var db = data.Create();
        var sales = db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Completed && s.SoldAt >= start && s.SoldAt < end);
        if (stores is not null) sales = sales.Where(s => stores.Contains(s.StoreId));
        var lines = await db.SaleLines.AsNoTracking().Where(l => l.PromotionId != null)
            .Join(sales, l => l.SaleId, s => s.Id, (l, s) => new
            {
                Promotion = l.PromotionId!.Value, l.PromotionName, l.Kind, l.Quantity, l.LineTotal, l.PromotionDiscount, l.SaleId,
                SaleNet = s.Subtotal - s.SubtotalDiscount,
            }).ToListAsync();
        var usesQuery = db.CouponUses.AsNoTracking().Where(u => u.UsedAt >= start && u.UsedAt < end);
        if (stores is not null) usesQuery = usesQuery.Where(u => stores.Contains(u.StoreId));
        var uses = await usesQuery.GroupBy(u => u.PromotionId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var promotions = await db.Promotions.AsNoTracking().Select(p => new { p.Id, p.Name, p.Kind }).ToDictionaryAsync(p => p.Id);
        return [.. lines.GroupBy(l => l.Promotion).Select(g =>
        {
            var known = promotions.GetValueOrDefault(g.Key);
            var wide = g.Where(l => l.Kind == SaleLineKind.Coupon).ToList();
            var items = g.Where(l => l.Kind != SaleLineKind.Coupon).ToList();
            decimal net = items.Sum(l => l.LineTotal) + wide.DistinctBy(l => l.SaleId).Sum(l => l.SaleNet);
            decimal discount = items.Sum(l => l.PromotionDiscount) + wide.Sum(l => -l.LineTotal);
            return new PromotionReportRow(g.Key, known?.Name ?? g.First().PromotionName ?? "", known?.Kind, g.Select(l => l.SaleId).Distinct().Count(),
                items.Sum(l => l.Quantity), net, discount, uses.GetValueOrDefault(g.Key));
        }).OrderByDescending(r => r.Discount)];
    }
}
