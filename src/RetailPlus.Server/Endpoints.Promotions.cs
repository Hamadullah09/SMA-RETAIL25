using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

public sealed record PromotionTargetInfo(Guid? DepartmentId, Guid? CategoryId, string? Plu, decimal Quantity, string? Name);
public sealed record PromotionRow(Guid Id, string Version, string Name, PromotionKind Kind, bool IsActive, DateOnly? StartsOn, DateOnly? EndsOn, int Priority,
    bool Combinable, bool AllStores, IReadOnlyList<Guid> StoreIds, string? CouponCode, int CouponMaxUses, int CouponUses, string Summary, bool Live);
public sealed record PromotionDetail(Guid Id, string Version, string Name, PromotionKind Kind, bool IsActive, DateOnly? StartsOn, DateOnly? EndsOn, int Priority,
    bool Combinable, bool AllStores, IReadOnlyList<Guid> StoreIds, string? CouponCode, int CouponMaxUses, int CouponUses, string Summary, bool Live,
    string? Description, IReadOnlyList<int> Days, string? StartTime, string? EndTime, PromotionScope Scope, IReadOnlyList<PromotionTargetInfo> Targets,
    decimal Percent, decimal Amount, int BuyQuantity, int GetQuantity, decimal GetPercent, IReadOnlyList<PromotionTargetInfo> RewardTargets, decimal BundlePrice,
    IReadOnlyList<PromotionTargetInfo> BundleParts, decimal SpendThreshold);
public sealed record PromotionRequest(string? Name, PromotionKind Kind, bool IsActive, DateOnly? StartsOn, DateOnly? EndsOn, int Priority, bool Combinable,
    bool AllStores, IReadOnlyList<Guid>? StoreIds, string? CouponCode, int CouponMaxUses, string? Description, IReadOnlyList<int>? Days, string? StartTime,
    string? EndTime, PromotionScope Scope, IReadOnlyList<PromotionTargetInfo>? Targets, decimal Percent, decimal Amount, int BuyQuantity, int GetQuantity,
    decimal GetPercent, IReadOnlyList<PromotionTargetInfo>? RewardTargets, decimal BundlePrice, IReadOnlyList<PromotionTargetInfo>? BundleParts,
    decimal SpendThreshold, string? Version);
public sealed record SalesTargetRow(Guid Id, string Version, Guid StoreId, string StoreCode, Guid? UserId, string? UserName, int Year, int Month, decimal Amount,
    decimal Actual, decimal Percent);
public sealed record SalesTargetRequest(Guid StoreId, Guid? UserId, int Year, int Month, decimal Amount);
public sealed record TargetAmountRequest(decimal Amount, string? Version);
public sealed record StoreProgress(Guid StoreId, string StoreCode, decimal Target, decimal Actual, decimal Percent);
public sealed record PersonProgress(Guid UserId, string UserName, string StoreCode, decimal Target, decimal Actual, decimal Percent);
public sealed record TargetProgress(int Year, int Month, int Day, int DaysInMonth, decimal Target, decimal Actual, decimal Percent,
    IReadOnlyList<StoreProgress> Stores, IReadOnlyList<PersonProgress> People);

/// <summary>Phase E4: promotions at the till (sent down to every till) and sales targets per store and salesperson.</summary>
public static partial class Endpoints
{
    private static void MapPromotions(RouteGroupBuilder api)
    {
        var promotions = Need(Permissions.MarketingPromotions);
        api.MapGet("/promotions", async (ApiRequest api) =>
        {
            var service = new PromotionService(api.Root);
            var list = await service.ListAsync(api.Bool("active"), api.Text("search"));
            if (!api.User.AllStores) list = [.. list.Where(p => p.AllStores || p.Stores.Any(s => s.StoreId == api.User.StoreId))];
            var names = await service.TargetNamesAsync();
            var uses = await service.CouponUsesAsync();
            var today = await api.TodayAsync(api.User.StoreId);
            return Results.Ok(list.Select(p => ToRow(p, names, uses, today)));
        }).RequireAuthorization(promotions);
        api.MapGet("/promotions/report", PromotionReportAsync).RequireAuthorization(promotions);
        api.MapGet("/promotions/{id:guid}", async (Guid id, ApiRequest api) => Results.Ok(await PromotionDetailAsync(api, id))).RequireAuthorization(promotions);
        api.MapPost("/promotions", (PromotionRequest body, ApiRequest api) => SavePromotionAsync(null, body, api)).RequireAuthorization(promotions);
        api.MapPut("/promotions/{id:guid}", (Guid id, PromotionRequest body, ApiRequest api) => SavePromotionAsync(id, body, api)).RequireAuthorization(promotions);
        api.MapDelete("/promotions/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var p = await VisiblePromotionAsync(api, id);
            await RequirePromotionChangeAsync(api, p.AllStores, [.. p.Stores.Select(s => s.StoreId)]);
            await new PromotionService(await api.ForChangeAsync(OnlyStore(p.AllStores, [.. p.Stores.Select(s => s.StoreId)]))).DeleteAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(promotions);

        var targets = Need(Permissions.SalesTargets);
        api.MapGet("/targets", TargetsAsync).RequireAuthorization(targets);
        api.MapPost("/targets", async (SalesTargetRequest body, ApiRequest api) =>
        {
            var store = await NewRecordStoreAsync(api, body.StoreId == Guid.Empty ? null : body.StoreId);
            var saved = await new TargetService(await api.ForChangeAsync(store)).SaveAsync(new SalesTarget
            {
                StoreId = store, UserId = body.UserId, Year = body.Year, Month = body.Month, Amount = body.Amount,
            });
            return Results.Created($"/api/targets/{saved.Id}", await TargetRowAsync(api, saved.Id));
        }).RequireAuthorization(targets);
        api.MapPut("/targets/{id:guid}", async (Guid id, TargetAmountRequest body, ApiRequest api) =>
        {
            var store = await StoreOfAsync<SalesTarget>(api, id, "That target");
            await new TargetService(await api.ForChangeAsync(store)).SaveAsync(new SalesTarget { Id = id, Amount = body.Amount, Version = ApiRequest.ParseVersion(body.Version) });
            return Results.Ok(await TargetRowAsync(api, id));
        }).RequireAuthorization(targets);
        api.MapDelete("/targets/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var store = await StoreOfAsync<SalesTarget>(api, id, "That target");
            await new TargetService(await api.ForChangeAsync(store)).DeleteAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(targets);
        api.MapGet("/targets/progress", ProgressAsync).RequireAuthorization(Need(Permissions.WebSignIn));
    }

    // Promotions --------------------------------------------------------------------------------------------------------

    private static PromotionTargetInfo Info(PromotionTarget t, IReadOnlyDictionary<Guid, string> names) =>
        new(t.DepartmentId, t.CategoryId, t.Plu, t.Quantity, t.Plu ?? (t.DepartmentId is { } d ? names.GetValueOrDefault(d) : t.CategoryId is { } c ? names.GetValueOrDefault(c) : null));

    private static bool Live(Promotion p, DateOnly today) => p.IsActive && (p.StartsOn is null || p.StartsOn <= today) && (p.EndsOn is null || p.EndsOn >= today);

    private static PromotionRow ToRow(Promotion p, IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<Guid, int> uses, DateOnly today) =>
        new(p.Id, ApiRequest.Version(p.Version), p.Name, p.Kind, p.IsActive, p.StartsOn, p.EndsOn, p.Priority, p.Combinable, p.AllStores,
            [.. p.Stores.Select(s => s.StoreId)], p.CouponCode, p.CouponMaxUses, uses.GetValueOrDefault(p.Id), PromotionService.Describe(p, names), Live(p, today));

    private static PromotionDetail ToDetail(Promotion p, IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<Guid, int> uses, DateOnly today)
    {
        var r = ToRow(p, names, uses, today);
        List<PromotionTargetInfo> Of(PromotionTargetRole role) => [.. p.Targets.Where(t => t.Role == role).Select(t => Info(t, names))];
        return new PromotionDetail(r.Id, r.Version, r.Name, r.Kind, r.IsActive, r.StartsOn, r.EndsOn, r.Priority, r.Combinable, r.AllStores, r.StoreIds, r.CouponCode,
            r.CouponMaxUses, r.CouponUses, r.Summary, r.Live, p.Description,
            [.. Enum.GetValues<DayOfWeek>().Where(d => (p.Days & PromotionEngine.DayBit(d)) != 0).Select(d => (int)d)],
            p.StartTime?.ToString("HH:mm", CultureInfo.InvariantCulture), p.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture), p.Scope,
            Of(PromotionTargetRole.Qualifies), p.Percent, p.Amount, p.BuyQuantity, p.GetQuantity, p.GetPercent, Of(PromotionTargetRole.Reward), p.BundlePrice,
            Of(PromotionTargetRole.BundlePart), p.SpendThreshold);
    }

    /// <summary>A promotion the user may see: one for every store, or for a store of theirs (404 otherwise).</summary>
    private static async Task<Promotion> VisiblePromotionAsync(ApiRequest api, Guid id)
    {
        var p = await new PromotionService(api.Root).GetAsync(id) ?? throw ApiException.NotFound("That promotion");
        if (!api.User.AllStores && !p.AllStores && !p.Stores.Any(s => s.StoreId == api.User.StoreId)) throw ApiException.NotFound("That promotion");
        return p;
    }

    private static Guid? OnlyStore(bool allStores, IReadOnlyList<Guid> stores) => !allStores && stores.Distinct().Count() == 1 ? stores[0] : null;

    /// <summary>A promotion for one store is changed by that store's users; one for every store or several under the company rule.</summary>
    private static async Task RequirePromotionChangeAsync(ApiRequest api, bool allStores, IReadOnlyList<Guid> stores)
    {
        if (OnlyStore(allStores, stores) is { } store)
        {
            if (!api.User.CanSee(store)) throw ApiException.Forbidden("You may not change promotions of that store.");
            return;
        }
        await api.RequireCompanyChangeAsync();
    }

    private static async Task<PromotionDetail> PromotionDetailAsync(ApiRequest api, Guid id)
    {
        var p = await VisiblePromotionAsync(api, id);
        var service = new PromotionService(api.Root);
        return ToDetail(p, await service.TargetNamesAsync(), await service.CouponUsesAsync([id]), await api.TodayAsync(api.User.StoreId));
    }

    private static TimeOnly? Time(string? text, string name) =>
        string.IsNullOrWhiteSpace(text) ? null
        : TimeOnly.TryParseExact(text.Trim(), ["HH:mm", "HH:mm:ss", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t
        : throw ApiException.Bad($"{name} must be a time like 17:30, not {text}.");

    private static async Task<IResult> SavePromotionAsync(Guid? id, PromotionRequest body, ApiRequest api)
    {
        IReadOnlyList<Guid> stores = body.AllStores ? [] : body.StoreIds ?? [];
        if (id is { } existing)
        {
            var stored = await VisiblePromotionAsync(api, existing);
            await RequirePromotionChangeAsync(api, stored.AllStores, [.. stored.Stores.Select(s => s.StoreId)]);
        }
        await RequirePromotionChangeAsync(api, body.AllStores, stores);
        int days = (body.Days ?? []).Where(d => d is >= 0 and <= 6).Aggregate(0, (bits, d) => bits | PromotionEngine.DayBit((DayOfWeek)d));
        IEnumerable<PromotionTarget> TargetRows(IReadOnlyList<PromotionTargetInfo>? list, PromotionTargetRole role) => (list ?? []).Select(t => new PromotionTarget
        {
            Role = role, DepartmentId = t.DepartmentId, CategoryId = t.CategoryId, Plu = t.Plu, Quantity = t.Quantity <= 0 ? 1 : t.Quantity,
        });
        var p = new Promotion
        {
            Id = id ?? Guid.Empty, Version = id is null ? Guid.Empty : ApiRequest.ParseVersion(body.Version),
            Name = body.Name ?? "", Description = body.Description, Kind = body.Kind, IsActive = body.IsActive, StartsOn = body.StartsOn, EndsOn = body.EndsOn,
            Days = days, StartTime = Time(body.StartTime, "startTime"), EndTime = Time(body.EndTime, "endTime"), Priority = body.Priority, Combinable = body.Combinable,
            AllStores = body.AllStores, Scope = body.Scope, Percent = body.Percent, Amount = body.Amount, BuyQuantity = body.BuyQuantity, GetQuantity = body.GetQuantity,
            GetPercent = body.GetPercent, BundlePrice = body.BundlePrice, SpendThreshold = body.SpendThreshold, CouponCode = body.CouponCode,
            CouponMaxUses = body.CouponMaxUses,
            Targets = [.. TargetRows(body.Targets, PromotionTargetRole.Qualifies), .. TargetRows(body.RewardTargets, PromotionTargetRole.Reward),
                .. TargetRows(body.BundleParts, PromotionTargetRole.BundlePart)],
            Stores = [.. stores.Select(s => new PromotionStore { StoreId = s })],
        };
        var saved = await new PromotionService(await api.ForChangeAsync(OnlyStore(body.AllStores, stores))).SaveAsync(p);
        var detail = await PromotionDetailAsync(api, saved.Id);
        return id is null ? Results.Created($"/api/promotions/{saved.Id}", detail) : Results.Ok(detail);
    }

    private static async Task<IResult> PromotionReportAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        var rows = await new PromotionService(api.Root).ReportAsync(from, to, store is { } s ? [s] : null);
        return Results.Ok(rows);
    }

    // Targets -----------------------------------------------------------------------------------------------------------

    private static async Task<List<SalesTargetRow>> TargetRowsAsync(ApiRequest api, IReadOnlyList<SalesTarget> list)
    {
        await using var db = api.Root.Create();
        var stores = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code);
        var users = await db.UserAccounts.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName);
        var service = new TargetService(api.Root);
        var rows = new List<SalesTargetRow>();
        foreach (var month in list.GroupBy(t => (t.Year, t.Month)))
        {
            var actuals = await service.ActualsAsync(month.Key.Year, month.Key.Month, [.. month.Select(t => t.StoreId).Distinct()]);
            rows.AddRange(month.Select(t =>
            {
                decimal actual = t.UserId is { } u ? actuals.ByPerson.GetValueOrDefault((t.StoreId, u)) : actuals.ByStore.GetValueOrDefault(t.StoreId);
                return new SalesTargetRow(t.Id, ApiRequest.Version(t.Version), t.StoreId, stores.GetValueOrDefault(t.StoreId) ?? "", t.UserId,
                    t.UserId is { } user ? users.GetValueOrDefault(user) : null, t.Year, t.Month, t.Amount, actual, TargetService.Percent(actual, t.Amount));
            }));
        }
        return rows;
    }

    private static async Task<SalesTargetRow> TargetRowAsync(ApiRequest api, Guid id)
    {
        await using var db = api.Root.Create();
        var t = await db.SalesTargets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("That target");
        api.RequireStore(t.StoreId, "That target");
        return (await TargetRowsAsync(api, [t]))[0];
    }

    private static async Task<IResult> TargetsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var today = await api.TodayAsync(store);
        int year = api.Number("year") ?? today.Year, month = api.Number("month") ?? today.Month;
        if (month is < 1 or > 12) throw ApiException.Bad("month is 1 to 12.");
        var list = await new TargetService(api.Root).ListAsync(year, month, store is { } s ? [s] : null);
        return Results.Ok(await TargetRowsAsync(api, list));
    }

    /// <summary>This month's targets against net sales, for the dashboard's tile (with <c>sales.targets</c> or <c>dashboard.view</c>).</summary>
    private static async Task<IResult> ProgressAsync(ApiRequest api)
    {
        if (!api.User.Can(Permissions.SalesTargets) && !api.User.Can(Permissions.DashboardView))
            throw ApiException.Forbidden("Your role cannot see the targets.");
        var store = api.ListStore();
        var today = await api.TodayAsync(store);
        var rows = await TargetRowsAsync(api, await new TargetService(api.Root).ListAsync(today.Year, today.Month, store is { } s ? [s] : null));
        var stores = rows.Where(r => r.UserId is null).Select(r => new StoreProgress(r.StoreId, r.StoreCode, r.Amount, r.Actual, r.Percent)).ToList();
        var people = rows.Where(r => r.UserId is not null)
            .Select(r => new PersonProgress(r.UserId!.Value, r.UserName ?? "", r.StoreCode, r.Amount, r.Actual, r.Percent)).OrderByDescending(p => p.Percent).ToList();
        decimal target = stores.Sum(x => x.Target), actual = stores.Sum(x => x.Actual);
        return Results.Ok(new TargetProgress(today.Year, today.Month, today.Day, DateTime.DaysInMonth(today.Year, today.Month), target, actual,
            TargetService.Percent(actual, target), stores, people));
    }
}
