using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Rules;
using RetailPlus.Reports;

namespace RetailPlus.Server;

public sealed record ReportInfo(string Key, string Group, string Title, bool UsesDates);
public sealed record ReportColumnInfo(string Header, ColumnKind Kind, bool Total);
public sealed record ReportResult(string Title, string? Subtitle, IReadOnlyList<ReportColumnInfo> Columns, IReadOnlyList<object?[]> Rows, object?[] Totals,
    DateOnly From, DateOnly To);

public static partial class Endpoints
{
    private static void MapReports(RouteGroupBuilder api)
    {
        api.MapGet("/reports", (ApiRequest api) => Results.Ok(ReportService.All.Where(r => r.Permission is null || api.User.Can(r.Permission))
            .Select(r => new ReportInfo(r.Key, r.Group, r.Name, r.NeedsDates)))).RequireAuthorization(Need(Permissions.ReportsView));
        api.MapGet("/reports/{key}", async (string key, ApiRequest api) =>
        {
            var (report, from, to) = await RunReportAsync(key, api);
            return Results.Ok(new ReportResult(report.Title, report.Subtitle, [.. report.Columns.Select(c => new ReportColumnInfo(c.Header, c.Kind, c.Total))],
                report.Rows, report.HasTotals ? report.TotalsRow() : new object?[report.Columns.Count], from, to));
        }).RequireAuthorization(Need(Permissions.ReportsView));
        api.MapGet("/reports/{key}/csv", async (string key, ApiRequest api) =>
        {
            var (report, from, to) = await RunReportAsync(key, api);
            var name = $"{key}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv";
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(report.ToCsv())).ToArray(), "text/csv; charset=utf-8", name);
        }).RequireAuthorization(Need(Permissions.ReportsView));
    }

    /// <summary>
    /// Runs a report of <see cref="ReportService"/> over every location of the store asked for, or of the stores the user
    /// sees; reports of a store's own records (drawers, invoices, hours) cover the same store.
    /// </summary>
    private static async Task<(ReportTable Report, DateOnly From, DateOnly To)> RunReportAsync(string key, ApiRequest api)
    {
        var definition = ReportService.All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase)) ?? throw ApiException.NotFound("That report");
        if (definition.Permission is { } needed && !api.User.Can(needed)) throw ApiException.Forbidden("You may not run that report.");
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        List<Guid> locations;
        await using (var db = api.Root.Create())
            locations = await (store is { } s ? db.Locations.Where(l => l.StoreId == s) : db.Locations).Select(l => l.Id).ToListAsync();
        var report = await new ReportService(api.For(store)).RunAsync(definition.Key, locations, from, to);
        return (report, from, to);
    }
}
