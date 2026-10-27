using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

public sealed record AuditRow(Guid Id, DateTime AtUtc, string StoreCode, int StationNumber, string? UserName, string Action, string? EntityName, Guid? EntityId,
    string? Details, JsonElement? Before, JsonElement? After, Guid? UserId);
public sealed record TimeClockRow(Guid Id, Guid UserId, string UserName, string StoreCode, int StationNumber, DateTime InAt, DateTime? OutAt, decimal Hours,
    string? EditedBy, string? EditReason);
public sealed record HoursRowInfo(Guid UserId, string UserName, int Days, decimal Hours);
public sealed record CommissionInfo(string Staff, int Sales, decimal Units, decimal NetSales, decimal FromRules, decimal FromItems, decimal Commission, Guid? UserId);
public sealed record CommissionRuleRow(Guid? Id, string? Version, string Name, Guid? UserId, string? UserName, CommissionScope Scope, Guid? DepartmentId,
    Guid? CategoryId, Guid? ItemId, string? Target, decimal Percent, DateOnly? StartsOn, DateOnly? EndsOn, bool IsActive,
    CommissionBasis Basis = CommissionBasis.PercentOfNetSales);

/// <summary>The audit log, the time clock, hours and commission, and the commission rules.</summary>
public static partial class Endpoints
{
    private static void MapStaff(RouteGroupBuilder api)
    {
        api.MapGet("/audit", AuditAsync).RequireAuthorization(Need(Permissions.AuditView));
        api.MapGet("/time-clock", TimeClockAsync).RequireAuthorization(Need(Permissions.ReportsStaff));
        api.MapGet("/time-clock/hours", HoursAsync).RequireAuthorization(Need(Permissions.ReportsStaff));
        api.MapGet("/commission", CommissionAsync).RequireAuthorization(Need(Permissions.ReportsStaff));
        api.MapGet("/commission-rules", RulesAsync).RequireAuthorization(Need(Permissions.ReportsStaff));
        api.MapPost("/commission-rules", (CommissionRuleRow body, ApiRequest api) => SaveRuleAsync(null, body, api)).RequireAuthorization(Need(Permissions.TimeClockEdit));
        api.MapPut("/commission-rules/{id:guid}", (Guid id, CommissionRuleRow body, ApiRequest api) => SaveRuleAsync(id, body, api)).RequireAuthorization(Need(Permissions.TimeClockEdit));
        api.MapDelete("/commission-rules/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            await using (var db = api.Root.Create())
                if (!await db.CommissionRules.AnyAsync(r => r.Id == id)) throw ApiException.NotFound("That rule");
            await new StaffService(await api.ForChangeAsync()).DeleteRuleAsync(id, api.Session);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.TimeClockEdit));
    }

    private static JsonElement? Json(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<IResult> AuditAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var rows = db.AuditEntries.AsNoTracking();
        if (store is { } s) rows = rows.Where(a => a.StoreId == s);
        if (api.Text("from") is not null || api.Text("to") is not null)
        {
            // The log keeps UTC times; the days asked for are the store's business days.
            var (start, end) = await api.UtcPeriodAsync(store);
            rows = rows.Where(a => a.AtUtc >= start && a.AtUtc < end);
        }
        if (api.Id("userId") is { } user) rows = rows.Where(a => a.UserId == user);
        if (api.Text("action") is { } action) rows = rows.Where(a => a.Action == action);
        if (api.Text("search") is { } t)
            rows = rows.Where(a => (a.Details ?? "").Contains(t) || (a.UserName ?? "").Contains(t) || (a.EntityName ?? "").Contains(t));
        var codes = await StoreCodesAsync(db);
        return Results.Ok(await api.PageAsync(rows.OrderByDescending(a => a.AtUtc), a => new AuditRow(a.Id, ApiRequest.Utc(a.AtUtc), codes.GetValueOrDefault(a.StoreId) ?? "",
            a.StationNumber, a.UserName, a.Action, a.EntityName, a.EntityId, a.Details, Json(a.Before), Json(a.After), a.UserId)));
    }

    private static async Task<IResult> TimeClockAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        var entries = await new StaffService(api.For(store)).GetEntriesAsync(from, to, api.Id("userId"));
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        return Results.Ok(entries.Select(e => new TimeClockRow(e.Id, e.UserId, e.UserName, codes.GetValueOrDefault(e.StoreId) ?? "", e.StationNumber, e.ClockIn, e.ClockOut,
            e.ClockOut is { } done ? Math.Round((decimal)(done - e.ClockIn).TotalHours, 2) : 0m, e.EditedBy, e.EditReason)));
    }

    private static async Task<IResult> HoursAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        var days = await new StaffService(api.For(store)).HoursByDayAsync(from, to, api.Id("userId"));
        return Results.Ok(days.GroupBy(d => (d.UserId, d.UserName)).Select(g => new HoursRowInfo(g.Key.UserId, g.Key.UserName, g.Count(), g.Sum(d => d.Hours)))
            .OrderBy(r => r.UserName, StringComparer.CurrentCultureIgnoreCase));
    }

    private static async Task<IResult> CommissionAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        var rows = await new StaffService(api.For(store)).CommissionByStaffAsync(from, to);
        return Results.Ok(rows.Select(r => new CommissionInfo(r.Staff, r.Sales, r.Units, r.NetSales, r.FromRules, r.FromItems, r.Commission, r.StaffId)));
    }

    // Commission rules ------------------------------------------------------------------------------------------------

    private static async Task<IResult> RulesAsync(ApiRequest api)
    {
        await using var db = api.Root.Create();
        var rules = await db.CommissionRules.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
        var lookup = await RuleNamesAsync(db, rules);
        return Results.Ok(rules.Select(r => ToRow(r, lookup)));
    }

    private sealed record RuleNames(Dictionary<Guid, string> Users, Dictionary<Guid, string> Departments, Dictionary<Guid, string> Categories, Dictionary<Guid, string> Items);

    private static async Task<RuleNames> RuleNamesAsync(RetailPlusDbContext db, List<CommissionRule> rules)
    {
        var itemIds = rules.Where(r => r.ItemId is not null).Select(r => r.ItemId!.Value).ToList();
        return new RuleNames(
            await db.UserAccounts.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.UserName),
            await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name),
            await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name),
            await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Plu + " " + i.Name));
    }

    private static CommissionRuleRow ToRow(CommissionRule r, RuleNames names)
    {
        string target = r.Scope switch
        {
            CommissionScope.Department => r.DepartmentId is { } d ? "Department " + names.Departments.GetValueOrDefault(d) : "Department",
            CommissionScope.Category => r.CategoryId is { } c ? "Category " + names.Categories.GetValueOrDefault(c) : "Category",
            CommissionScope.Item => r.ItemId is { } i ? "Item " + names.Items.GetValueOrDefault(i) : "Item",
            _ => "All items",
        };
        return new CommissionRuleRow(r.Id, ApiRequest.Version(r.UpdatedAtUtc), r.Name, r.UserId, r.UserId is { } u ? names.Users.GetValueOrDefault(u) : null, r.Scope,
            r.DepartmentId, r.CategoryId, r.ItemId, target, r.Rate, r.EffectiveFrom, r.EffectiveTo, r.IsActive, r.Basis);
    }

    private static async Task<IResult> SaveRuleAsync(Guid? id, CommissionRuleRow body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        if (id is { } existing)
        {
            await using var db = api.Root.Create();
            var stored = await db.CommissionRules.AsNoTracking().SingleOrDefaultAsync(r => r.Id == existing) ?? throw ApiException.NotFound("That rule");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
        }
        var rule = new CommissionRule
        {
            Id = id ?? Guid.Empty, Name = body.Name ?? "", UserId = body.UserId, Scope = body.Scope, DepartmentId = body.DepartmentId, CategoryId = body.CategoryId,
            ItemId = body.ItemId, Basis = body.Basis, Rate = body.Percent, EffectiveFrom = body.StartsOn, EffectiveTo = body.EndsOn, IsActive = body.IsActive,
        };
        var saved = await new StaffService(await api.ForChangeAsync()).SaveRuleAsync(rule, api.Session);
        await using var check = api.Root.Create();
        var row = await check.CommissionRules.AsNoTracking().SingleAsync(r => r.Id == saved.Id);
        var result = ToRow(row, await RuleNamesAsync(check, [row]));
        return id is null ? Results.Created($"/api/commission-rules/{row.Id}", result) : Results.Ok(result);
    }
}
