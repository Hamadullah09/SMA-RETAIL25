using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Reports;

namespace RetailPlus.Server;

// Work centres, bills of materials and routings ----------------------------------------------------------------------------

public sealed record WorkCentreInfo(Guid Id, string Version, Guid StoreId, string StoreCode, Guid LocationId, string LocationCode, string Code, string Name,
    decimal LabourCostPerHour, decimal MachineCostPerHour, decimal OverheadPerHour, decimal CapacityHoursPerDay, int DaysPerWeek, bool IsActive, string? Notes);
public sealed record WorkCentreRequest(string? Version, Guid LocationId, string Code, string Name, decimal LabourCostPerHour, decimal MachineCostPerHour,
    decimal OverheadPerHour, decimal CapacityHoursPerDay, int DaysPerWeek, bool IsActive, string? Notes);
public sealed record BomListRow(Guid Id, string Version, string ProductCode, string ProductName, int Revision, bool IsActive, DateOnly? EffectiveFrom, DateOnly? EffectiveTo,
    decimal YieldQuantity, int Lines, int ByProducts, int Operations, bool InForce);
public sealed record BomLineInfo(string ComponentCode, string? Description, decimal Quantity, decimal ScrapPercent, IssueMethod IssueMethod, int? OperationSequence);
public sealed record BomByProductInfo(string StockCode, string? Description, decimal Quantity, decimal CostSharePercent);
public sealed record RoutingOperationInfo(int Sequence, Guid WorkCentreId, string? WorkCentreCode, string Name, decimal SetupMinutes, decimal RunMinutesPerUnit, string? Instructions);
public sealed record BomDetail(Guid? Id, string? Version, string ProductCode, string? ProductName, int Revision, bool IsActive, DateOnly? EffectiveFrom, DateOnly? EffectiveTo,
    decimal YieldQuantity, decimal OverheadPercent, string? Notes, IReadOnlyList<BomLineInfo> Lines, IReadOnlyList<BomByProductInfo> ByProducts,
    IReadOnlyList<RoutingOperationInfo> Operations);
public sealed record RoutingRequest(string? Version, IReadOnlyList<RoutingOperationInfo> Operations);
public sealed record CopyBomRequest(string? ProductCode);

// Production orders ----------------------------------------------------------------------------------------------------------

public sealed record ProductionOrderListRow(Guid Id, string StoreCode, long Number, ProductionOrderStatus Status, string ProductCode, string ProductName, decimal Quantity,
    decimal Received, string MaterialLocation, string OutputLocation, DateOnly? PlannedStart, DateOnly? PlannedEnd, int Priority, bool MadeToOrder, int BomRevision);
public sealed record ProductionBoardColumn(ProductionOrderStatus Status, int Total, IReadOnlyList<ProductionOrderListRow> Orders);
public sealed record ProductionOrderRequest(string? Version, string ProductCode, decimal Quantity, Guid MaterialLocationId, Guid OutputLocationId, Guid? BillOfMaterialsId,
    DateOnly? PlannedStart, DateOnly? PlannedEnd, int? Priority, Guid? CustomerOrderId, string? Notes);
public sealed record ProductionOperationInfo(int Sequence, string Name, string WorkCentre, OperationStatus Status, decimal SetupMinutes, decimal RunMinutesPerUnit,
    decimal StandardMinutes, decimal Minutes, decimal Good, decimal Scrap, DateTime? StartedAt, DateTime? FinishedAt, string? Instructions);
public sealed record ProductionMovementInfo(DateTime At, StockMovementKind Kind, string StockCode, decimal Quantity, decimal UnitCost, decimal Value, string? Reason, string? User);
public sealed record ProductionReportInfo(DateTime At, int OperationSequence, decimal Minutes, decimal Good, decimal Scrap, string? UserName, decimal Cost, string? Note);
public sealed record ProductionOrderDetail(Guid Id, string Version, Guid StoreId, string StoreCode, long Number, ProductionOrderStatus Status, string ProductCode,
    string ProductName, decimal Quantity, Guid? BillOfMaterialsId, int BomRevision, Guid MaterialLocationId, string MaterialLocation, Guid OutputLocationId,
    string OutputLocation, DateOnly? PlannedStart, DateOnly? PlannedEnd, int Priority, Guid? CustomerOrderId, string? CustomerOrder, string? Notes,
    decimal StandardUnitCost, DateTime CreatedAt, string? CreatedBy, DateTime? ReleasedAt, string? ReleasedBy, DateTime? StartedAt, DateTime? CompletedAt,
    string? CompletedBy, DateTime? ClosedAt, string? ClosedBy, decimal Received, decimal OutputScrapped, decimal ReportedScrap, IReadOnlyList<LineProgress> Lines,
    IReadOnlyList<ProductionOperationInfo> Operations, IReadOnlyList<ProductionMovementInfo> Movements, IReadOnlyList<ProductionReportInfo> Reports,
    OrderCosts? Costs, OrderVariances? Variances, IReadOnlyList<ProductionPosting>? Postings);
public sealed record ReleaseResponse(ProductionOrderDetail Order, IReadOnlyList<Shortage> Shortages);
public sealed record OperationReportRequest(int Sequence, decimal Minutes, decimal Good, decimal Scrap, string? Note);
public sealed record IssueRequest(string Code, decimal Quantity, string? Note);
public sealed record IssueRemainingRequest(int? OperationSequence);
public sealed record ScrapRequest(string Code, decimal Quantity, string? Reason);
public sealed record ReceiveRequest(decimal Quantity, bool Complete, IReadOnlyList<ByProductQuantity>? ByProducts);
public sealed record CancelRequest(string? Reason);
public sealed record PostCostsResponse(int Orders, IReadOnlyList<string> Problems);
public sealed record CustomerOrderPick(Guid Id, long InvoiceNumber, string? Name, string? Plu, string Description, decimal Quantity, DateOnly? OrderDate);

// Planning and quality -------------------------------------------------------------------------------------------------------

public sealed record MrpRunRequest(Guid? StoreId, Guid? LocationId, int? HorizonDays, int? DefaultLeadDays);
public sealed record MrpRunInfo(Guid Id, DateTime RunAtUtc, string? RunBy, string LocationCode, int HorizonDays, int DefaultLeadDays, int Suggestions);
public sealed record SuggestionInfo(Guid Id, SuggestionKind Kind, SuggestionStatus Status, string StockCode, string Description, string StoreCode, Guid LocationId,
    string LocationCode, decimal Quantity, DateOnly NeedBy, DateOnly StartBy, int LeadDays, string? SupplierName, int Level, string? Reason, string? FirmedDocument,
    bool Late, bool FromRelease);
public sealed record MrpView(MrpRunInfo? Run, IReadOnlyList<SuggestionInfo> Suggestions);
public sealed record FirmRequest(IReadOnlyList<Guid> Ids, Dictionary<Guid, decimal>? Quantities);
public sealed record IdsRequest(IReadOnlyList<Guid> Ids);
public sealed record LeadTimeInfo(Guid SupplierId, string Supplier, int? Days);
public sealed record LeadTimeRequest(int? Days);
public sealed record CapacityInfo(Guid WorkCentreId, string StoreCode, string Code, string Name, DateOnly WeekStart, decimal CapacityHours, decimal LoadHours, decimal LoadPercent);
public sealed record QualityCheckInfo(int LineNumber, string Name, QualityCheckKind Kind, decimal? Minimum, decimal? Maximum, string? Unit, bool Required);
public sealed record QualityPlanInfo(Guid? Id, string? Version, string ProductCode, string Name, bool IsActive, bool ForProduction, bool ForIncoming, string? Notes,
    IReadOnlyList<QualityCheckInfo> Checks);
public sealed record QualityResultInfo(int LineNumber, string CheckName, QualityCheckKind Kind, decimal? Minimum, decimal? Maximum, bool Required, bool? PassFail,
    decimal? NumberValue, string? TextValue, bool Passed);
public sealed record InspectionInfo(Guid Id, InspectionKind Kind, string StoreCode, long? DocumentNumber, Guid? ProductionOrderId, Guid? PurchaseOrderId, string StockCode,
    string Description, decimal QuantityInspected, decimal QuantityFailed, InspectionDisposition Disposition, string? HoldLocation, bool Passed, DateTime InspectedAt,
    string? InspectedBy, string? Notes, IReadOnlyList<QualityResultInfo> Results);
public sealed record OutputInspectionRequest(decimal QuantityInspected, decimal QuantityFailed, InspectionDisposition Disposition, Guid? HoldLocationId,
    IReadOnlyList<CheckAnswer>? Answers, string? Notes);
public sealed record IncomingInspectionRequest(string StockCode, decimal QuantityInspected, decimal QuantityFailed, IReadOnlyList<CheckAnswer>? Answers, string? Notes);
public sealed record ReceivedLineInfo(string Plu, string Description, decimal Received, bool HasPlan, int Inspections);
public sealed record ReceivedOrderInfo(Guid Id, long Number, string StoreCode, string LocationCode, DateTime? ReceivedAt, IReadOnlyList<ReceivedLineInfo> Lines);

/// <summary>The server's production log messages.</summary>
internal static partial class ProductionLog
{
    [LoggerMessage(101, LogLevel.Warning, "Production costs of store {Store} were not posted to the ledger: {Problem}")]
    public static partial void NotPosted(ILogger logger, Guid store, string problem);
}

/// <summary>
/// Manufacturing (ERP phase E5): work centres, bills of materials with their routings, production orders and the work on
/// them, planning (MRP) and capacity, quality plans and inspections, and the production reports. Bills, routings,
/// quality plans and supplier lead times are company-wide (changed by users with every store, or while there is one store);
/// work centres, orders, planning runs and inspections belong to a store.
/// </summary>
public static partial class Endpoints
{
    private static void MapProduction(RouteGroupBuilder api)
    {
        // Work centres
        api.MapGet("/work-centres", WorkCentresAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapPost("/work-centres", (WorkCentreRequest body, ApiRequest api) => SaveWorkCentreAsync(null, body, api)).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapPut("/work-centres/{id:guid}", (Guid id, WorkCentreRequest body, ApiRequest api) => SaveWorkCentreAsync(id, body, api)).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapDelete("/work-centres/{id:guid}", DeleteWorkCentreAsync).RequireAuthorization(Need(Permissions.ProductionPlan));

        // Bills of materials and routings
        api.MapGet("/boms", BomsAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapGet("/boms/compare", CompareBomsAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapGet("/boms/{id:guid}", BomAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapPost("/boms", (BomDetail body, ApiRequest api) => SaveBomAsync(null, body, api)).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapPut("/boms/{id:guid}", (Guid id, BomDetail body, ApiRequest api) => SaveBomAsync(id, body, api)).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapDelete("/boms/{id:guid}", DeleteBomAsync).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapPost("/boms/{id:guid}/copy", CopyBomAsync).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapPut("/boms/{id:guid}/routing", SaveRoutingAsync).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapGet("/boms/{id:guid}/explode", ExplodeBomAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapGet("/boms/{id:guid}/cost", BomCostAsync).RequireAuthorization(Need(Permissions.ProductionView));

        // Production orders
        api.MapGet("/production-orders", ProductionOrdersAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapGet("/production-orders/board", ProductionBoardAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapGet("/production-orders/{id:guid}", ProductionOrderAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapPost("/production-orders", (ProductionOrderRequest body, ApiRequest api) => SaveProductionOrderAsync(null, body, api)).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapPut("/production-orders/{id:guid}", (Guid id, ProductionOrderRequest body, ApiRequest api) => SaveProductionOrderAsync(id, body, api))
            .RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapPost("/production-orders/{id:guid}/release", ReleaseOrderAsync).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapPost("/production-orders/{id:guid}/cancel", (Guid id, CancelRequest body, ApiRequest api) =>
            OrderActionAsync(id, api, (p, _) => p.CancelAsync(id, body.Reason))).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapPost("/production-orders/{id:guid}/close", (Guid id, ApiRequest api) =>
            OrderActionAsync(id, api, (p, _) => p.CloseAsync(id))).RequireAuthorization(Need(Permissions.ProductionCosting));
        api.MapPost("/production-orders/{id:guid}/operations/{sequence:int}/start", (Guid id, int sequence, ApiRequest api) =>
            OrderActionAsync(id, api, (p, _) => p.StartOperationAsync(id, sequence))).RequireAuthorization(Need(Permissions.ProductionFloor));
        api.MapPost("/production-orders/{id:guid}/operations/{sequence:int}/finish", (Guid id, int sequence, ApiRequest api) =>
            OrderActionAsync(id, api, (p, _) => p.FinishOperationAsync(id, sequence))).RequireAuthorization(Need(Permissions.ProductionFloor));
        api.MapPost("/production-orders/{id:guid}/report", (Guid id, OperationReportRequest body, ApiRequest api) =>
            OrderActionAsync(id, api, (p, _) => p.ReportAsync(id, body.Sequence, body.Minutes, body.Good, body.Scrap, body.Note))).RequireAuthorization(Need(Permissions.ProductionFloor));
        api.MapPost("/production-orders/{id:guid}/issue", (Guid id, IssueRequest body, ApiRequest api) =>
            OrderActionAsync(id, api, (p, _) => p.IssueAsync(id, body.Code ?? "", body.Quantity, body.Note))).RequireAuthorization(Need(Permissions.ProductionFloor));
        api.MapPost("/production-orders/{id:guid}/issue-remaining", (Guid id, IssueRemainingRequest body, ApiRequest api) =>
            OrderActionAsync(id, api, (p, _) => p.IssueRemainingAsync(id, body.OperationSequence))).RequireAuthorization(Need(Permissions.ProductionFloor));
        api.MapPost("/production-orders/{id:guid}/scrap", (Guid id, ScrapRequest body, ApiRequest api) =>
            OrderActionAsync(id, api, (p, _) => p.ScrapMaterialAsync(id, body.Code ?? "", body.Quantity, body.Reason))).RequireAuthorization(Need(Permissions.ProductionFloor));
        api.MapPost("/production-orders/{id:guid}/receive", (Guid id, ReceiveRequest body, ApiRequest api) =>
            OrderActionAsync(id, api, (p, _) => p.ReceiveAsync(id, body.Quantity, body.Complete, body.ByProducts))).RequireAuthorization(Need(Permissions.ProductionFloor));
        api.MapPost("/production-orders/{id:guid}/post-costs", PostOrderCostsAsync).RequireAuthorization(Need(Permissions.ProductionCosting));
        api.MapGet("/production-orders/{id:guid}/inspections", OrderInspectionsAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapPost("/production-orders/{id:guid}/inspect", InspectOutputAsync).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapPost("/production/post-costs", PostStoreCostsAsync).RequireAuthorization(Need(Permissions.ProductionCosting));
        api.MapGet("/production/customer-orders", CustomerOrderPicksAsync).RequireAuthorization(Need(Permissions.ProductionPlan));

        // Planning and capacity
        api.MapPost("/mrp/run", RunMrpAsync).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapGet("/mrp/suggestions", SuggestionsAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapPost("/mrp/firm", FirmAsync).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapPost("/mrp/dismiss", DismissAsync).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapGet("/mrp/lead-times", LeadTimesAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapPut("/mrp/lead-times/{supplierId:guid}", SetLeadTimeAsync).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapGet("/capacity", CapacityAsync).RequireAuthorization(Need(Permissions.ProductionView));

        // Quality
        api.MapGet("/quality/plans", QualityPlansAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapPost("/quality/plans", (QualityPlanInfo body, ApiRequest api) => SaveQualityPlanAsync(null, body, api)).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapPut("/quality/plans/{id:guid}", (Guid id, QualityPlanInfo body, ApiRequest api) => SaveQualityPlanAsync(id, body, api)).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapDelete("/quality/plans/{id:guid}", DeleteQualityPlanAsync).RequireAuthorization(Need(Permissions.ProductionPlan));
        api.MapGet("/quality/inspections", InspectionsAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapGet("/quality/receipts", ReceivedOrdersAsync).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapPost("/quality/receipts/{id:guid}/inspect", InspectReceiptAsync).RequireAuthorization(Need(Permissions.ProductionPlan));

        // Reports
        api.MapGet("/production/reports", (ApiRequest api) => Results.Ok(ProductionReports.All.Where(r => r.Permission is null || api.User.Can(r.Permission))
            .Select(r => new ReportInfo(r.Key, r.Group, r.Name, r.NeedsDates)))).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapGet("/production/reports/{key}", async (string key, ApiRequest api) =>
        {
            var (report, from, to) = await RunProductionReportAsync(key, api);
            return Results.Ok(new ReportResult(report.Title, report.Subtitle, [.. report.Columns.Select(c => new ReportColumnInfo(c.Header, c.Kind, c.Total))],
                report.Rows, report.HasTotals ? report.TotalsRow() : new object?[report.Columns.Count], from, to));
        }).RequireAuthorization(Need(Permissions.ProductionView));
        api.MapGet("/production/reports/{key}/csv", async (string key, ApiRequest api) =>
        {
            var (report, from, to) = await RunProductionReportAsync(key, api);
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(report.ToCsv())).ToArray(), "text/csv; charset=utf-8", $"{key}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }).RequireAuthorization(Need(Permissions.ProductionView));
    }

    // Work centres --------------------------------------------------------------------------------------------------------

    private static async Task<IResult> WorkCentresAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var centres = await new ManufacturingService(api.For(store)).WorkCentresAsync(store);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        return Results.Ok(centres.Select(c => ToInfo(c, codes)));
    }

    private static WorkCentreInfo ToInfo(WorkCentre c, Dictionary<Guid, string> codes) =>
        new(c.Id, ApiRequest.Version(c.UpdatedAtUtc), c.StoreId, codes.GetValueOrDefault(c.StoreId) ?? "", c.LocationId, c.Location?.Code ?? "", c.Code, c.Name,
            c.LabourCostPerHour, c.MachineCostPerHour, c.OverheadPerHour, c.CapacityHoursPerDay, c.DaysPerWeek, c.IsActive, c.Notes);

    private static async Task<IResult> SaveWorkCentreAsync(Guid? id, WorkCentreRequest body, ApiRequest api)
    {
        Guid store;
        await using (var db = api.Root.Create())
        {
            store = await db.Locations.AsNoTracking().Where(l => l.Id == body.LocationId).Select(l => (Guid?)l.StoreId).SingleOrDefaultAsync() ?? throw ApiException.Bad("Choose the location the work centre is at.");
            if (!api.User.CanSee(store)) throw ApiException.Forbidden("You may not change another store's work centres.");
            if (id is Guid existing)
            {
                var row = await db.WorkCentres.AsNoTracking().SingleOrDefaultAsync(w => w.Id == existing);
                if (row is null || !api.User.CanSee(row.StoreId)) throw ApiException.NotFound("That work centre");
                ApiRequest.CheckVersion(body.Version, ApiRequest.Version(row.UpdatedAtUtc));
            }
        }
        var saved = await new ManufacturingService(await api.ForChangeAsync(store)).SaveWorkCentreAsync(new WorkCentre
        {
            Id = id ?? Guid.Empty, LocationId = body.LocationId, Code = body.Code ?? "", Name = body.Name ?? "", LabourCostPerHour = body.LabourCostPerHour,
            MachineCostPerHour = body.MachineCostPerHour, OverheadPerHour = body.OverheadPerHour, CapacityHoursPerDay = body.CapacityHoursPerDay, DaysPerWeek = body.DaysPerWeek,
            IsActive = body.IsActive, Notes = body.Notes,
        });
        await using var check = api.Root.Create();
        var info = ToInfo(await check.WorkCentres.AsNoTracking().Include(w => w.Location).SingleAsync(w => w.Id == saved.Id), await StoreCodesAsync(check));
        return id is null ? Results.Created($"/api/work-centres/{info.Id}", info) : Results.Ok(info);
    }

    private static async Task<IResult> DeleteWorkCentreAsync(Guid id, ApiRequest api)
    {
        Guid store;
        await using (var db = api.Root.Create())
        {
            var row = await db.WorkCentres.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id);
            if (row is null || !api.User.CanSee(row.StoreId)) throw ApiException.NotFound("That work centre");
            store = row.StoreId;
        }
        await new ManufacturingService(await api.ForChangeAsync(store)).DeleteWorkCentreAsync(id);
        return Results.NoContent();
    }

    // Bills of materials ------------------------------------------------------------------------------------------------------

    private static async Task<IResult> BomsAsync(ApiRequest api)
    {
        var rows = await new ManufacturingService(api.Root).ListAsync(api.Text("search"), api.Bool("active") ?? false);
        var today = DateOnly.FromDateTime(DateTime.Today);
        return Results.Ok(rows.Select(b => new BomListRow(b.Id, ApiRequest.Version(b.UpdatedAtUtc), b.ProductCode, b.ProductName, b.Revision, b.IsActive, b.EffectiveFrom,
            b.EffectiveTo, b.YieldQuantity, b.Lines, b.ByProducts, b.Operations,
            b.IsActive && (b.EffectiveFrom is null || b.EffectiveFrom <= today) && (b.EffectiveTo is null || b.EffectiveTo >= today))));
    }

    private static BomDetail ToDetail(BillOfMaterials b) =>
        new(b.Id, ApiRequest.Version(b.UpdatedAtUtc), b.ProductCode, b.ProductName, b.Revision, b.IsActive, b.EffectiveFrom, b.EffectiveTo, b.YieldQuantity, b.OverheadPercent,
            b.Notes, [.. b.Lines.Select(l => new BomLineInfo(l.ComponentCode, l.Description, l.Quantity, l.ScrapPercent, l.IssueMethod, l.OperationSequence))],
            [.. b.ByProducts.Select(p => new BomByProductInfo(p.StockCode, p.Description, p.Quantity, p.CostSharePercent))],
            [.. b.Operations.Select(o => new RoutingOperationInfo(o.Sequence, o.WorkCentreId, o.WorkCentre?.Code, o.Name, o.SetupMinutes, o.RunMinutesPerUnit, o.Instructions))]);

    private static async Task<IResult> BomAsync(Guid id, ApiRequest api) =>
        await new ManufacturingService(api.Root).GetAsync(id) is { } bom ? Results.Ok(ToDetail(bom)) : throw ApiException.NotFound("That bill of materials");

    private static async Task<IResult> SaveBomAsync(Guid? id, BomDetail body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        var service = new ManufacturingService(await api.ForChangeAsync());
        if (id is Guid existing)
        {
            var stored = await service.GetAsync(existing) ?? throw ApiException.NotFound("That bill of materials");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
        }
        await CheckWorkCentresAsync(api, body.Operations.Select(o => o.WorkCentreId));
        var saved = await service.SaveAsync(new BillOfMaterials
        {
            Id = id ?? Guid.Empty, ProductCode = body.ProductCode ?? "", ProductName = body.ProductName ?? "", Revision = body.Revision, IsActive = body.IsActive,
            EffectiveFrom = body.EffectiveFrom, EffectiveTo = body.EffectiveTo, YieldQuantity = body.YieldQuantity, OverheadPercent = body.OverheadPercent, Notes = body.Notes,
            Lines = [.. (body.Lines ?? []).Select(l => new BomLine
            {
                ComponentCode = l.ComponentCode ?? "", Description = l.Description, Quantity = l.Quantity, ScrapPercent = l.ScrapPercent, IssueMethod = l.IssueMethod,
                OperationSequence = l.OperationSequence,
            })],
            ByProducts = [.. (body.ByProducts ?? []).Select(p => new BomByProduct { StockCode = p.StockCode ?? "", Description = p.Description, Quantity = p.Quantity, CostSharePercent = p.CostSharePercent })],
            Operations = [.. (body.Operations ?? []).Select(Operation)],
        });
        var detail = ToDetail(saved);
        return id is null ? Results.Created($"/api/boms/{saved.Id}", detail) : Results.Ok(detail);
    }

    private static RoutingOperation Operation(RoutingOperationInfo o) => new()
    {
        Sequence = o.Sequence, WorkCentreId = o.WorkCentreId, Name = o.Name ?? "", SetupMinutes = o.SetupMinutes, RunMinutesPerUnit = o.RunMinutesPerUnit, Instructions = o.Instructions,
    };

    /// <summary>A routing may only use work centres of stores the user sees.</summary>
    private static async Task CheckWorkCentresAsync(ApiRequest api, IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0 || api.User.AllStores) return;
        await using var db = api.Root.Create();
        var stores = await db.WorkCentres.AsNoTracking().Where(w => list.Contains(w.Id)).Select(w => w.StoreId).Distinct().ToListAsync();
        if (stores.Any(s => !api.User.CanSee(s))) throw ApiException.Forbidden("A routing operation is at another store's work centre.");
    }

    private static async Task<IResult> DeleteBomAsync(Guid id, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        var service = new ManufacturingService(await api.ForChangeAsync());
        if (await service.GetAsync(id) is null) throw ApiException.NotFound("That bill of materials");
        await service.DeleteAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> CopyBomAsync(Guid id, CopyBomRequest? body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        var service = new ManufacturingService(await api.ForChangeAsync());
        if (await service.GetAsync(id) is null) throw ApiException.NotFound("That bill of materials");
        var copy = await service.CopyAsync(id, body?.ProductCode);
        return Results.Created($"/api/boms/{copy.Id}", ToDetail(copy));
    }

    private static async Task<IResult> SaveRoutingAsync(Guid id, RoutingRequest body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        var service = new ManufacturingService(await api.ForChangeAsync());
        var stored = await service.GetAsync(id) ?? throw ApiException.NotFound("That bill of materials");
        ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
        await CheckWorkCentresAsync(api, (body.Operations ?? []).Select(o => o.WorkCentreId));
        return Results.Ok(ToDetail(await service.SaveRoutingAsync(id, [.. (body.Operations ?? []).Select(Operation)])));
    }

    private static async Task<IResult> CompareBomsAsync(ApiRequest api)
    {
        var from = api.Id("from") ?? throw ApiException.Bad("Say which two versions to compare (from and to).");
        var to = api.Id("to") ?? throw ApiException.Bad("Say which two versions to compare (from and to).");
        return Results.Ok(await new ManufacturingService(api.Root).CompareAsync(from, to));
    }

    private static async Task<IResult> ExplodeBomAsync(Guid id, ApiRequest api)
    {
        var location = await VisibleLocationAsync(api, api.Id("locationId"));
        var service = new ManufacturingService(api.Root);
        if (await service.GetAsync(id) is null) throw ApiException.NotFound("That bill of materials");
        return Results.Ok(await service.ExplodeAsync(id, locationId: location));
    }

    private static async Task<IResult> BomCostAsync(Guid id, ApiRequest api)
    {
        var location = await VisibleLocationAsync(api, api.Id("locationId"));
        var service = new ManufacturingService(api.Root);
        if (await service.GetAsync(id) is null) throw ApiException.NotFound("That bill of materials");
        decimal? quantity = decimal.TryParse(api.Text("quantity"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var q) && q > 0 ? q : null;
        return Results.Ok(await service.RollUpAsync(id, quantity, location));
    }

    /// <summary>A location the user's stores have (404 otherwise); null stays null (every location's costs).</summary>
    private static async Task<Guid?> VisibleLocationAsync(ApiRequest api, Guid? location)
    {
        if (location is not Guid id) return null;
        await using var db = api.Root.Create();
        var store = await db.Locations.AsNoTracking().Where(l => l.Id == id).Select(l => (Guid?)l.StoreId).SingleOrDefaultAsync();
        if (store is not Guid s || !api.User.CanSee(s)) throw ApiException.NotFound("That location");
        return id;
    }

    // Production orders -------------------------------------------------------------------------------------------------------

    private static async Task<IResult> ProductionOrdersAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var statuses = ParseStatuses(api.Text("status"));
        await using var db = api.Root.Create();
        var q = ProductionService.Query(db, store, statuses, api.Text("search")).OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Number);
        var (page, size) = api.Paging();
        int total = await q.CountAsync();
        var orders = await q.Skip((page - 1) * size).Take(size).ToListAsync();
        var codes = await StoreCodesAsync(db);
        var rows = await ProductionService.RowsAsync(db, orders);
        return Results.Ok(new PageOf<ProductionOrderListRow>([.. rows.Select(r => ToListRow(r, codes))], total, page, size));
    }

    private static List<ProductionOrderStatus>? ParseStatuses(string? text)
    {
        if (text is null) return null;
        var list = new List<ProductionOrderStatus>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            list.Add(Enum.TryParse<ProductionOrderStatus>(part, true, out var s) ? s : throw ApiException.Bad($"{part} is not a production order status."));
        return list;
    }

    private static ProductionOrderListRow ToListRow(ProductionOrderRow r, Dictionary<Guid, string> codes) =>
        new(r.Id, codes.GetValueOrDefault(r.StoreId) ?? "", r.Number, r.Status, r.ProductCode, r.ProductName, r.Quantity, r.Received, r.MaterialLocation, r.OutputLocation,
            r.PlannedStart, r.PlannedEnd, r.Priority, r.CustomerOrderId is not null, r.BomRevision);

    /// <summary>The orders by status for the board: every open and completed one (most urgent first), closed and cancelled ones of the last 30 days.</summary>
    private static async Task<IResult> ProductionBoardAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var since = DateTime.Now.AddDays(-30);
        var orders = await ProductionService.Query(db, store, null, api.Text("search"))
            .Where(o => (o.Status != ProductionOrderStatus.Closed && o.Status != ProductionOrderStatus.Cancelled) || o.UpdatedAtUtc >= since.ToUniversalTime())
            .ToListAsync();
        var codes = await StoreCodesAsync(db);
        var rows = (await ProductionService.RowsAsync(db, orders)).Select(r => ToListRow(r, codes)).ToList();
        var columns = Enum.GetValues<ProductionOrderStatus>().Select(status =>
        {
            var mine = rows.Where(r => r.Status == status).OrderBy(r => r.Priority).ThenBy(r => r.PlannedStart ?? DateOnly.MaxValue).ThenBy(r => r.Number).ToList();
            return new ProductionBoardColumn(status, mine.Count, [.. mine.Take(200)]);
        });
        return Results.Ok(columns);
    }

    private static async Task<IResult> ProductionOrderAsync(Guid id, ApiRequest api) => Results.Ok(await OrderDetailAsync(api, id));

    private static async Task<ProductionOrderDetail> OrderDetailAsync(ApiRequest api, Guid id)
    {
        await using var db = api.Root.Create();
        var store = await db.ProductionOrders.AsNoTracking().Where(o => o.Id == id).Select(o => (Guid?)o.StoreId).SingleOrDefaultAsync();
        if (store is not Guid s || !api.User.CanSee(s)) throw ApiException.NotFound("That production order");
        var progress = await new ProductionService(api.For(s)).ProgressAsync(id) ?? throw ApiException.NotFound("That production order");
        var o = progress.Order;
        var codes = await StoreCodesAsync(db);
        string? customerOrder = o.CustomerOrderId is Guid c
            ? await db.CustomerOrders.AsNoTracking().Where(x => x.Id == c).Select(x => x.InvoiceNumber + " " + (x.Name ?? x.Company ?? "")).FirstOrDefaultAsync()
            : null;
        bool costing = api.User.Can(Permissions.ProductionCosting);
        var summary = costing ? await new ProductionCostingService(api.For(s)).SummaryAsync(id) : null;
        var ops = progress.Operations.Select(p =>
        {
            var op = o.Operations.Single(x => x.Sequence == p.Sequence);
            return new ProductionOperationInfo(p.Sequence, p.Name, p.WorkCentre, p.Status, op.SetupMinutes, op.RunMinutesPerUnit, p.StandardMinutes, p.Minutes, p.Good, p.Scrap,
                p.StartedAt, p.FinishedAt, op.Instructions);
        }).ToList();
        return new ProductionOrderDetail(o.Id, ApiRequest.Version(o.Version), o.StoreId, codes.GetValueOrDefault(o.StoreId) ?? "", o.Number, o.Status, o.ProductCode, o.ProductName,
            o.Quantity, o.BillOfMaterialsId, o.BomRevision, o.MaterialLocationId, o.MaterialLocation?.Code ?? "", o.OutputLocationId, o.OutputLocation?.Code ?? "",
            o.PlannedStart, o.PlannedEnd, o.Priority, o.CustomerOrderId, customerOrder?.Trim(), o.Notes, costing ? o.StandardUnitCost : 0, o.CreatedAt, o.CreatedBy, o.ReleasedAt,
            o.ReleasedBy, o.StartedAt, o.CompletedAt, o.CompletedBy, o.ClosedAt, o.ClosedBy, progress.Received, progress.OutputScrapped, progress.ReportedScrap,
            [.. progress.Lines.Select(l => costing ? l : l with { StandardUnitCost = 0 })], ops,
            [.. progress.Movements.Select(m => new ProductionMovementInfo(m.At, m.Kind, progress.ItemCodes.GetValueOrDefault(m.ItemId) ?? "", m.Quantity,
                costing ? m.UnitCost : 0, costing ? Money.Round(m.Quantity * m.UnitCost) : 0, m.Reason, m.User))],
            [.. progress.Reports.Select(r => new ProductionReportInfo(r.At, r.OperationSequence, r.Minutes, r.GoodQuantity, r.ScrapQuantity, r.UserName,
                costing ? r.LabourCost + r.MachineCost + r.OverheadCost : 0, r.Note))],
            summary?.Costs, summary?.Variances, summary?.Postings);
    }

    private static async Task<IResult> SaveProductionOrderAsync(Guid? id, ProductionOrderRequest body, ApiRequest api)
    {
        Guid store;
        await using (var db = api.Root.Create())
        {
            store = await db.Locations.AsNoTracking().Where(l => l.Id == body.MaterialLocationId).Select(l => (Guid?)l.StoreId).SingleOrDefaultAsync()
                ?? throw ApiException.Bad("Choose the location materials come from.");
            if (!api.User.CanSee(store)) throw ApiException.Forbidden("You may not plan production for another store.");
            if (id is Guid existing)
            {
                var row = await db.ProductionOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == existing);
                if (row is null || !api.User.CanSee(row.StoreId)) throw ApiException.NotFound("That production order");
            }
        }
        var service = new ProductionService(api.For(store));
        var request = new NewProductionOrder(body.ProductCode ?? "", body.Quantity, body.MaterialLocationId, body.OutputLocationId)
        {
            BillOfMaterialsId = body.BillOfMaterialsId, PlannedStart = body.PlannedStart, PlannedEnd = body.PlannedEnd, Priority = body.Priority ?? 5,
            CustomerOrderId = body.CustomerOrderId, Notes = body.Notes,
        };
        if (id is Guid orderId)
        {
            await service.UpdateAsync(orderId, ApiRequest.ParseVersion(body.Version), request);
            return Results.Ok(await OrderDetailAsync(api, orderId));
        }
        var order = await service.CreateAsync(request);
        return Results.Created($"/api/production-orders/{order.Id}", await OrderDetailAsync(api, order.Id));
    }

    private static async Task<IResult> ReleaseOrderAsync(Guid id, ApiRequest api)
    {
        var store = await OrderStoreAsync(api, id);
        var result = await new ProductionService(api.For(store)).ReleaseAsync(id);
        return Results.Ok(new ReleaseResponse(await OrderDetailAsync(api, id), result.Shortages));
    }

    /// <summary>An action on an order of a store the user sees; afterwards its costs are posted to the ledger (problems are logged, not shown).</summary>
    private static async Task<IResult> OrderActionAsync<T>(Guid id, ApiRequest api, Func<ProductionService, Guid, Task<T>> action)
    {
        var store = await OrderStoreAsync(api, id);
        await action(new ProductionService(api.For(store)), store);
        await PostCostsQuietlyAsync(api.Http.RequestServices, api.Root.For(new StationIdentity(store, 0), api.Session), store, id);
        return Results.Ok(await OrderDetailAsync(api, id));
    }

    private static async Task<Guid> OrderStoreAsync(ApiRequest api, Guid id)
    {
        await using var db = api.Root.Create();
        var store = await db.ProductionOrders.AsNoTracking().Where(o => o.Id == id).Select(o => (Guid?)o.StoreId).SingleOrDefaultAsync();
        return store is Guid s && api.User.CanSee(s) ? s : throw ApiException.NotFound("That production order");
    }

    /// <summary>Posts an order's (or with no order, the store's open orders') costs, logging what could not be posted.</summary>
    internal static async Task PostCostsQuietlyAsync(IServiceProvider services, DataContext data, Guid store, Guid? orderId = null)
    {
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("RetailPlus.Server.Production");
        try
        {
            var costing = new ProductionCostingService(data);
            if (orderId is Guid id)
            {
                await costing.PostAsync(id);
                return;
            }
            foreach (var problem in (await costing.PostOpenAsync(store)).Problems) ProductionLog.NotPosted(log, store, problem);
        }
        catch (BusinessRuleException ex)
        {
            ProductionLog.NotPosted(log, store, ex.Message);
        }
    }

    private static async Task<IResult> PostOrderCostsAsync(Guid id, ApiRequest api)
    {
        var store = await OrderStoreAsync(api, id);
        await new ProductionCostingService(api.For(store)).PostAsync(id);
        return Results.Ok(await OrderDetailAsync(api, id));
    }

    private static async Task<IResult> PostStoreCostsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (orders, problems) = await new ProductionCostingService(api.For(store)).PostOpenAsync(store);
        return Results.Ok(new PostCostsResponse(orders, problems));
    }

    private static async Task<IResult> CustomerOrderPicksAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var q = db.CustomerOrders.AsNoTracking().Where(c => !c.Paid);
        if (store is Guid s) q = q.Where(c => c.StoreId == s);
        var rows = await q.OrderByDescending(c => c.OrderDate).Take(500).ToListAsync();
        return Results.Ok(rows.Select(c => new CustomerOrderPick(c.Id, c.InvoiceNumber, c.Name ?? c.Company, c.Plu, c.Description, c.Quantity, c.OrderDate)));
    }

    // Quality -----------------------------------------------------------------------------------------------------------------

    private static InspectionInfo ToInfo(QualityInspection i, Dictionary<Guid, string> stores, Dictionary<Guid, string> locations) =>
        new(i.Id, i.Kind, stores.GetValueOrDefault(i.StoreId) ?? "", i.DocumentNumber, i.ProductionOrderId, i.PurchaseOrderId, i.StockCode, i.Description, i.QuantityInspected,
            i.QuantityFailed, i.Disposition, i.HoldLocationId is Guid h ? locations.GetValueOrDefault(h) : null, i.Passed, i.InspectedAt, i.InspectedBy, i.Notes,
            [.. i.Results.Select(r => new QualityResultInfo(r.LineNumber, r.CheckName, r.Kind, r.Minimum, r.Maximum, r.Required, r.PassFail, r.NumberValue, r.TextValue, r.Passed))]);

    private static async Task<List<InspectionInfo>> InspectionInfosAsync(ApiRequest api, List<QualityInspection> rows)
    {
        await using var db = api.Root.Create();
        var stores = await StoreCodesAsync(db);
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Code);
        return [.. rows.Select(i => ToInfo(i, stores, locations))];
    }

    private static async Task<IResult> OrderInspectionsAsync(Guid id, ApiRequest api)
    {
        var store = await OrderStoreAsync(api, id);
        return Results.Ok(await InspectionInfosAsync(api, await new QualityService(api.For(store)).InspectionsAsync(store, InspectionKind.Production, productionOrderId: id)));
    }

    private static async Task<IResult> InspectOutputAsync(Guid id, OutputInspectionRequest body, ApiRequest api)
    {
        var store = await OrderStoreAsync(api, id);
        var inspection = await new QualityService(api.For(store)).InspectOutputAsync(id, new InspectionRequest(body.QuantityInspected, body.QuantityFailed, body.Answers ?? [])
        {
            Disposition = body.Disposition, HoldLocationId = body.HoldLocationId, Notes = body.Notes,
        });
        await PostCostsQuietlyAsync(api.Http.RequestServices, api.Root.For(new StationIdentity(store, 0), api.Session), store, id);
        return Results.Ok((await InspectionInfosAsync(api, [inspection]))[0]);
    }

    private static async Task<IResult> QualityPlansAsync(ApiRequest api) =>
        Results.Ok((await new QualityService(api.Root).PlansAsync()).Select(ToInfo));

    private static QualityPlanInfo ToInfo(QualityPlan p) =>
        new(p.Id, ApiRequest.Version(p.UpdatedAtUtc), p.ProductCode, p.Name, p.IsActive, p.ForProduction, p.ForIncoming, p.Notes,
            [.. p.Checks.OrderBy(c => c.LineNumber).Select(c => new QualityCheckInfo(c.LineNumber, c.Name, c.Kind, c.Minimum, c.Maximum, c.Unit, c.Required))]);

    private static async Task<IResult> SaveQualityPlanAsync(Guid? id, QualityPlanInfo body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        var service = new QualityService(await api.ForChangeAsync());
        if (id is Guid existing)
        {
            var stored = (await service.PlansAsync()).SingleOrDefault(p => p.Id == existing) ?? throw ApiException.NotFound("That quality plan");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(stored.UpdatedAtUtc));
        }
        var saved = await service.SavePlanAsync(new QualityPlan
        {
            Id = id ?? Guid.Empty, ProductCode = body.ProductCode ?? "", Name = body.Name ?? "", IsActive = body.IsActive, ForProduction = body.ForProduction,
            ForIncoming = body.ForIncoming, Notes = body.Notes,
            Checks = [.. (body.Checks ?? []).Select(c => new QualityCheck { Name = c.Name ?? "", Kind = c.Kind, Minimum = c.Minimum, Maximum = c.Maximum, Unit = c.Unit, Required = c.Required })],
        });
        var info = ToInfo((await service.PlansAsync()).Single(p => p.Id == saved.Id));
        return id is null ? Results.Created($"/api/quality/plans/{saved.Id}", info) : Results.Ok(info);
    }

    private static async Task<IResult> DeleteQualityPlanAsync(Guid id, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        var service = new QualityService(await api.ForChangeAsync());
        if ((await service.PlansAsync()).All(p => p.Id != id)) throw ApiException.NotFound("That quality plan");
        await service.DeletePlanAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> InspectionsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        InspectionKind? kind = api.Text("kind") is { } k ? Enum.TryParse<InspectionKind>(k, true, out var parsed) ? parsed : throw ApiException.Bad($"{k} is not a kind of inspection.") : null;
        DateOnly? from = null, to = null;
        if (api.Text("from") is not null || api.Text("to") is not null) (from, to) = await api.PeriodAsync(store);
        return Results.Ok(await InspectionInfosAsync(api, await new QualityService(api.For(store)).InspectionsAsync(store, kind, from, to)));
    }

    /// <summary>Purchase orders received in the last 90 days, with their lines, for the incoming inspection page.</summary>
    private static async Task<IResult> ReceivedOrdersAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var since = DateTime.Now.AddDays(-(api.Number("days") ?? 90));
        var q = db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).Include(p => p.Location).Where(p => p.Status == PurchaseOrderStatus.Received && p.ReceivedAt >= since);
        if (store is Guid s) q = q.Where(p => p.StoreId == s);
        if (api.Text("search") is { } text) q = SearchNumber(text) is long n ? q.Where(p => p.Number == n) : q.Where(p => p.Lines.Any(l => l.Plu.Contains(text) || l.Description.Contains(text)));
        var orders = await q.OrderByDescending(p => p.ReceivedAt).Take(200).AsSplitQuery().ToListAsync();
        var plans = (await db.QualityPlans.AsNoTracking().Where(p => p.IsActive && p.ForIncoming).Select(p => p.ProductCode).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = orders.Select(o => o.Id).ToList();
        var done = (await db.QualityInspections.AsNoTracking().Where(i => i.PurchaseOrderId != null && ids.Contains(i.PurchaseOrderId.Value))
            .Select(i => new { Order = i.PurchaseOrderId!.Value, i.StockCode }).ToListAsync()).GroupBy(i => (i.Order, i.StockCode.ToUpperInvariant())).ToDictionary(g => g.Key, g => g.Count());
        var codes = await StoreCodesAsync(db);
        return Results.Ok(orders.Select(p => new ReceivedOrderInfo(p.Id, p.Number, codes.GetValueOrDefault(p.StoreId) ?? "", p.Location?.Code ?? "", p.ReceivedAt,
            [.. p.Lines.Where(l => l.QuantityReceived > 0).GroupBy(l => l.Plu.ToUpperInvariant()).Select(g => new ReceivedLineInfo(g.Key, g.First().Description, g.Sum(l => l.QuantityReceived),
                plans.Contains(g.Key), done.GetValueOrDefault((p.Id, g.Key))))])));
    }

    private static async Task<IResult> InspectReceiptAsync(Guid id, IncomingInspectionRequest body, ApiRequest api)
    {
        await using (var db = api.Root.Create())
        {
            var store = await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == id).Select(p => (Guid?)p.StoreId).SingleOrDefaultAsync();
            if (store is not Guid s || !api.User.CanSee(s)) throw ApiException.NotFound("That purchase order");
            var inspection = await new QualityService(api.For(s)).InspectReceiptAsync(id, body.StockCode ?? "",
                new InspectionRequest(body.QuantityInspected, body.QuantityFailed, body.Answers ?? []) { Notes = body.Notes });
            return Results.Ok((await InspectionInfosAsync(api, [inspection]))[0]);
        }
    }

    // Planning and capacity ---------------------------------------------------------------------------------------------------

    /// <summary>The one store a planning request is for: the one asked for (or the user's own); a user of every store must choose.</summary>
    private static Guid PlanningStore(ApiRequest api, Guid? asked)
    {
        if (asked is Guid s) return api.User.CanSee(s) ? s : throw ApiException.Forbidden("You may not see that store.");
        if (api.AskedStore is Guid q) return api.User.CanSee(q) ? q : throw ApiException.Forbidden("You may not see that store.");
        return api.User.StoreId ?? throw ApiException.Bad("Choose the store to plan for.");
    }

    private static async Task<IResult> RunMrpAsync(MrpRunRequest body, ApiRequest api)
    {
        var store = PlanningStore(api, body.StoreId);
        if (body.LocationId is Guid location && await VisibleLocationAsync(api, location) is null) throw ApiException.NotFound("That location");
        var result = await new MrpService(api.For(store)).RunAsync(new MrpOptions(store)
        {
            LocationId = body.LocationId, HorizonDays = body.HorizonDays ?? 60, DefaultLeadDays = body.DefaultLeadDays ?? 7,
        });
        return Results.Ok(await MrpViewAsync(api, store, result.Run));
    }

    private static async Task<MrpView> MrpViewAsync(ApiRequest api, Guid? store, MrpRun? run, SuggestionStatus? status = SuggestionStatus.Open, SuggestionKind? kind = null)
    {
        var suggestions = await new MrpService(api.For(store)).SuggestionsAsync(store, status, kind);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Code);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var runInfo = run is null ? null : new MrpRunInfo(run.Id, ApiRequest.Utc(run.RunAtUtc), run.RunBy, locations.GetValueOrDefault(run.LocationId) ?? "", run.HorizonDays,
            run.DefaultLeadDays, run.Suggestions);
        return new MrpView(runInfo, [.. suggestions.Select(s => new SuggestionInfo(s.Id, s.Kind, s.Status, s.StockCode, s.Description, codes.GetValueOrDefault(s.StoreId) ?? "",
            s.LocationId, locations.GetValueOrDefault(s.LocationId) ?? "", s.Quantity, s.NeedBy, s.StartBy, s.LeadDays, s.SupplierName, s.Level, s.Reason, s.FirmedDocument,
            s.Status == SuggestionStatus.Open && s.StartBy < today, s.RunId is null))]);
    }

    private static async Task<IResult> SuggestionsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        SuggestionStatus? status = api.Text("status") is { } st
            ? st.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : Enum.TryParse<SuggestionStatus>(st, true, out var p) ? p : throw ApiException.Bad($"{st} is not a suggestion status.")
            : SuggestionStatus.Open;
        SuggestionKind? kind = api.Text("kind") is { } k ? Enum.TryParse<SuggestionKind>(k, true, out var pk) ? pk : throw ApiException.Bad($"{k} is not make or buy.") : null;
        MrpRun? run = null;
        if (store is Guid s && s != Guid.Empty) run = await new MrpService(api.For(s)).LatestRunAsync(s);
        return Results.Ok(await MrpViewAsync(api, store, run, status, kind));
    }

    private static async Task<IReadOnlyList<Guid>> SuggestionStoresCheckedAsync(ApiRequest api, IReadOnlyList<Guid> ids)
    {
        if (ids is null || ids.Count == 0) throw ApiException.Bad("Choose the suggestions.");
        await using var db = api.Root.Create();
        var stores = await db.MrpSuggestions.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => s.StoreId).Distinct().ToListAsync();
        if (stores.Any(s => !api.User.CanSee(s))) throw ApiException.NotFound("A suggestion");
        return stores;
    }

    private static async Task<IResult> FirmAsync(FirmRequest body, ApiRequest api)
    {
        var stores = await SuggestionStoresCheckedAsync(api, body.Ids);
        var result = await new MrpService(api.For(stores.Count == 1 ? stores[0] : null)).FirmAsync(body.Ids, body.Quantities);
        return Results.Ok(result);
    }

    private static async Task<IResult> DismissAsync(IdsRequest body, ApiRequest api)
    {
        var stores = await SuggestionStoresCheckedAsync(api, body.Ids);
        return Results.Ok(new { dismissed = await new MrpService(api.For(stores.Count == 1 ? stores[0] : null)).DismissAsync(body.Ids) });
    }

    private static async Task<IResult> LeadTimesAsync(ApiRequest api)
    {
        var times = await new MrpService(api.Root).LeadTimesAsync();
        await using var db = api.Root.Create();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => !s.IsDeleted).OrderBy(s => s.Company).Select(s => new { s.Id, s.Company, s.FirstName, s.LastName }).ToListAsync();
        return Results.Ok(suppliers.Select(s => new LeadTimeInfo(s.Id, s.Company ?? $"{s.FirstName} {s.LastName}".Trim(), times.TryGetValue(s.Id, out var d) ? d : null)));
    }

    private static async Task<IResult> SetLeadTimeAsync(Guid supplierId, LeadTimeRequest body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        await using (var db = api.Root.Create())
            if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId)) throw ApiException.NotFound("That supplier");
        await new MrpService(await api.ForChangeAsync()).SetLeadTimeAsync(supplierId, body.Days);
        return Results.NoContent();
    }

    private static async Task<IResult> CapacityAsync(ApiRequest api)
    {
        var store = api.ListStore();
        DateOnly? from = api.Text("from") is not null ? (await api.PeriodAsync(store)).From : null;
        int weeks = api.Number("weeks") ?? 8;
        bool suggestions = api.Bool("suggestions") ?? false;
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        var stores = store is Guid s ? [s] : await db.Stores.AsNoTracking().OrderBy(x => x.Code).Select(x => x.Id).ToListAsync();
        var rows = new List<CapacityInfo>();
        foreach (var id in stores.Where(id => id != Guid.Empty))
            rows.AddRange((await new MrpService(api.For(id)).CapacityAsync(id, from, weeks, suggestions))
                .Select(r => new CapacityInfo(r.WorkCentreId, codes.GetValueOrDefault(id) ?? "", r.Code, r.Name, r.WeekStart, r.CapacityHours, r.LoadHours, r.LoadPercent)));
        return Results.Ok(rows);
    }

    // Reports -----------------------------------------------------------------------------------------------------------------

    private static async Task<(ReportTable Report, DateOnly From, DateOnly To)> RunProductionReportAsync(string key, ApiRequest api)
    {
        var definition = ProductionReports.All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase)) ?? throw ApiException.NotFound("That report");
        if (definition.Permission is { } needed && !api.User.Can(needed)) throw ApiException.Forbidden("You may not run that report.");
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        var report = await new ProductionReports(api.For(store)).RunAsync(definition.Key, store, from, to);
        return (report, from, to);
    }
}
