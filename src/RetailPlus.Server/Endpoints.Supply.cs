using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

// Batches and bins
public sealed record BatchInfoRow(Guid Id, string Version, Guid ItemId, string Plu, string ItemName, string StoreCode, string LocationCode, string BatchNumber,
    DateOnly? ExpiresOn, DateOnly? MadeOn, string? Supplier, DateOnly? ReceivedOn, long? PurchaseOrderNumber, decimal OnHand, decimal UnitCost, decimal Value,
    int? DaysLeft, string? Bin, string? Note);
public sealed record BatchRequest(string? Version, Guid? ItemId, string? BatchNumber, DateOnly? ExpiresOn, DateOnly? MadeOn, Guid? SupplierId, string? Note);
public sealed record BatchAdjustRequest(decimal Quantity, bool SetTo, string? Reason);
public sealed record ExpiryReport(int Days, IReadOnlyList<BatchInfoRow> Rows, decimal Units, decimal Value);
public sealed record BinInfo(Guid Id, string Version, Guid LocationId, string StoreCode, string LocationCode, string Code, string? Zone, string? Description,
    bool IsActive, int Items);
public sealed record BinRequest(string? Version, Guid? LocationId, string? Code, string? Zone, string? Description, bool IsActive = true);
public sealed record BinAssignRequest(IReadOnlyList<Guid> ItemIds, string? BinCode);
public sealed record BinList(string Title, IReadOnlyList<BinListRow> Rows);

// Purchase orders
public sealed record PurchaseOrderListRow(Guid Id, long Number, string StoreCode, string LocationCode, PurchaseOrderStatus Status, DateTime CreatedAt,
    DateTime? PostedAt, DateTime? ReceivedAt, DateOnly? ExpectedOn, string? Supplier, string? Description, int Lines, decimal TotalCost);
public sealed record PurchaseOrderLineInfo(Guid Id, Guid? ItemId, string Plu, string Description, string? Supplier, decimal CaseQuantity, decimal OrderQuantity,
    decimal Pieces, decimal CostEach, decimal OrderedCost, decimal OrderCost, decimal QuantityReceived, bool TracksBatches, int ShelfLifeDays, string? Bin,
    IReadOnlyList<ReceivedBatchInfo> Batches);
public sealed record ReceivedBatchInfo(string BatchNumber, decimal Pieces, DateOnly? ExpiresOn, DateOnly? MadeOn);
public sealed record PurchaseOrderDetail(Guid Id, string Version, long Number, Guid StoreId, string StoreCode, Guid LocationId, string LocationCode,
    PurchaseOrderStatus Status, DateTime CreatedAt, DateTime? PostedAt, DateTime? ReceivedAt, DateOnly? ExpectedOn, Guid? SupplierId, string? Supplier,
    string? Description, string? CreatedBy, string? ReceivedBy, IReadOnlyList<PurchaseOrderLineInfo> Lines, decimal TotalCost);
public sealed record VersionRequest(string? Version, string? Note = null);
public sealed record ReceiveLineRequest(Guid LineId, decimal Pieces, decimal? CostEach, IReadOnlyList<ReceivedBatchInfo>? Batches);
public sealed record ReceiveRequest(string? Version, decimal ShippingCost, IReadOnlyList<ReceiveLineRequest> Lines);

// Requisitions
public sealed record RequisitionListRow(Guid Id, long Number, string StoreCode, string LocationCode, RequisitionStatus Status, DateTime CreatedAt,
    string? RequestedBy, DateOnly? NeededBy, int Lines, decimal Total, string? DecidedBy, DateTime? DecidedAt, string? Note);
public sealed record RequisitionLineInfo(Guid Id, int LineNumber, Guid? ItemId, string Plu, string Description, decimal Quantity, decimal CostEach,
    decimal Value, DateOnly? NeededBy, Guid? SupplierId, string? Supplier, string? Note, Guid? PurchaseOrderId, long? PurchaseOrderNumber);
public sealed record RequisitionDetail(Guid Id, string Version, long Number, Guid StoreId, string StoreCode, Guid LocationId, string LocationCode,
    RequisitionStatus Status, DateTime CreatedAt, string? RequestedBy, DateOnly? NeededBy, string? Note, DateTime? SubmittedAt, DateTime? DecidedAt,
    string? DecidedBy, string? DecisionNote, DateTime? OrderedAt, decimal Total, IReadOnlyList<RequisitionLineInfo> Lines);
public sealed record RequisitionLineRequest(Guid? ItemId, string? Plu, decimal Quantity, DateOnly? NeededBy, Guid? SupplierId, decimal? CostEach, string? Note);
public sealed record RequisitionRequest(string? Version, Guid? LocationId, DateOnly? NeededBy, string? Note, IReadOnlyList<RequisitionLineRequest>? Lines);
public sealed record OrderMade(Guid Id, long Number, string? Supplier);
public sealed record OrdersMade(IReadOnlyList<OrderMade> Orders);

// Matching
public sealed record MatchRow(Guid Id, string Version, string StoreCode, Guid PurchaseOrderId, long PurchaseOrderNumber, Guid PurchaseOrderLineId, Guid BillId,
    Guid BillLineId, string? BillNumber, DateOnly BillDate, string? Supplier, string Plu, string Description, decimal QuantityOrdered, decimal QuantityReceived,
    decimal QuantityBilled, decimal QuantityMatched, decimal OrderedCost, decimal ReceivedCost, decimal BilledCost, decimal PriceDifference, BillMatchStatus Status,
    string? Reason, DateTime CheckedAtUtc, DateTime? DecidedAtUtc, string? DecidedBy, string? DecisionNote);
public sealed record MatchReportInfo(Guid PurchaseOrderId, long PurchaseOrderNumber, Guid PurchaseOrderLineId, string StoreCode, string Plu, string Description,
    string? Supplier, decimal Ordered, decimal Received, decimal Billed, decimal Difference, decimal CostEach, decimal Value, DateTime? ReceivedAt);

// Landed costs
public sealed record LandedCostListRow(Guid Id, long Number, string StoreCode, DateOnly Date, LandedCostStatus Status, LandedCostBasis Basis, string? Reference,
    decimal Total, int Receipts, string? Note, DateTime? PostedAtUtc, string? PostedBy);
public sealed record LandedChargeInfo(LandedCostChargeKind Kind, string? Description, decimal Amount);
public sealed record LandedAllocationInfo(Guid PurchaseOrderLineId, Guid? ItemId, string Plu, string Description, decimal Quantity, decimal Value, decimal Weight,
    decimal Share, decimal OnHand, decimal ToInventory, decimal ToCostOfGoods);
public sealed record LandedReceiptInfo(Guid PurchaseOrderId, long Number, DateTime? ReceivedAt, string? Supplier, decimal Value);
public sealed record LandedCostDetail(Guid Id, string Version, long Number, Guid StoreId, string StoreCode, DateOnly Date, LandedCostStatus Status,
    LandedCostBasis Basis, string? Reference, string? Note, decimal Total, IReadOnlyList<LandedChargeInfo> Charges, IReadOnlyList<LandedReceiptInfo> Receipts,
    IReadOnlyList<LandedAllocationInfo> Allocations, string? CreatedBy, DateTime? PostedAtUtc, string? PostedBy);
public sealed record LandedCostRequest(string? Version, Guid? StoreId, DateOnly? Date, LandedCostBasis Basis, string? Reference, string? Note,
    IReadOnlyList<LandedChargeInfo>? Charges, IReadOnlyList<Guid>? PurchaseOrderIds);
public sealed record ReceiptChoiceInfo(Guid PurchaseOrderId, long Number, string StoreCode, string LocationCode, DateTime? ReceivedAt, string? Supplier,
    string? Description, int Lines, decimal Value);

// Reorder suggestions and settings
public sealed record ReorderLineRequest(Guid ItemId, decimal Pieces, Guid? SupplierId);
public sealed record ReorderRequest(Guid? LocationId, IReadOnlyList<ReorderLineRequest>? Lines, bool Submit = false, DateOnly? NeededBy = null);
public sealed record SupplySettingsInfo(string Version, Guid StoreId, bool BlockExpiredSales, int ExpiryWarningDays, decimal QuantityTolerancePercent,
    decimal QuantityToleranceUnits, decimal PriceTolerancePercent, decimal PriceToleranceAmount, int SalesWindowDays, int SafetyStockDays, int OrderCoverDays,
    int DefaultLeadTimeDays, bool ApproverMustDiffer);
public sealed record ApprovalLimitRequest(decimal? MaxAmount);

/// <summary>
/// Supply chain (phase E2): batches and expiry, bins and their lists, purchase orders received on the web, requisitions
/// with their approval, the three-way match, landed costs, reorder suggestions, supplier performance and the stores'
/// supply chain settings. Everything belongs to a store except the approval limits, which are set for every store.
/// </summary>
public static partial class Endpoints
{
    private static void MapSupply(RouteGroupBuilder api)
    {
        // Batches and expiry
        api.MapGet("/batches", BatchesAsync).RequireAuthorization(Need(Permissions.InventoryBatches));
        api.MapGet("/batches/expiring", (ApiRequest api) => ExpiryAsync(api, expired: false)).RequireAuthorization(Need(Permissions.InventoryBatches));
        api.MapGet("/batches/expired", (ApiRequest api) => ExpiryAsync(api, expired: true)).RequireAuthorization(Need(Permissions.InventoryBatches));
        api.MapGet("/items/{id:guid}/batches", ItemBatchesAsync).RequireAuthorization(Need(Permissions.InventoryBatches));
        api.MapPost("/batches", (BatchRequest body, ApiRequest api) => SaveBatchAsync(null, body, api)).RequireAuthorization(Need(Permissions.InventoryBatches));
        api.MapPut("/batches/{id:guid}", (Guid id, BatchRequest body, ApiRequest api) => SaveBatchAsync(id, body, api)).RequireAuthorization(Need(Permissions.InventoryBatches));
        api.MapPost("/batches/{id:guid}/adjust", AdjustBatchAsync).RequireAuthorization(Need(Permissions.InventoryBatches));

        // Bins
        api.MapGet("/bins", BinsAsync).RequireAuthorization(Need(Permissions.InventoryBins));
        api.MapPost("/bins", (BinRequest body, ApiRequest api) => SaveBinAsync(null, body, api)).RequireAuthorization(Need(Permissions.InventoryBins));
        api.MapPut("/bins/{id:guid}", (Guid id, BinRequest body, ApiRequest api) => SaveBinAsync(id, body, api)).RequireAuthorization(Need(Permissions.InventoryBins));
        api.MapDelete("/bins/{id:guid}", DeleteBinAsync).RequireAuthorization(Need(Permissions.InventoryBins));
        api.MapPost("/bins/assign", AssignBinAsync).RequireAuthorization(Need(Permissions.InventoryBins));
        api.MapGet("/bins/count-sheet", CountSheetAsync).RequireAuthorization(Need(Permissions.InventoryBins));
        api.MapGet("/bins/pick-orders", PickOrdersAsync).RequireAuthorization(Need(Permissions.InventoryBins));
        api.MapGet("/transfers/{id:guid}/pick", PickTransferAsync).RequireAuthorization(Need(Permissions.InventoryBins));
        api.MapGet("/purchase-orders/{id:guid}/put-away", PutAwayAsync).RequireAuthorization(Need(Permissions.PurchasingManage));

        // Purchase orders
        api.MapGet("/purchase-orders", PurchaseOrdersAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapGet("/purchase-orders/{id:guid}", async (Guid id, ApiRequest api) => Results.Ok(await PurchaseOrderDetailAsync(api, id))).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapPost("/purchase-orders/{id:guid}/post", PostPurchaseOrderAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapPost("/purchase-orders/{id:guid}/receive", ReceivePurchaseOrderAsync).RequireAuthorization(Need(Permissions.PurchasingManage));

        // Requisitions
        api.MapGet("/requisitions", RequisitionsAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapGet("/requisitions/{id:guid}", async (Guid id, ApiRequest api) => Results.Ok(await RequisitionDetailAsync(api, id))).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapPost("/requisitions", CreateRequisitionAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapPut("/requisitions/{id:guid}", SaveRequisitionAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapDelete("/requisitions/{id:guid}", DeleteRequisitionAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapPost("/requisitions/{id:guid}/submit", (Guid id, VersionRequest body, ApiRequest api) =>
            RequisitionStepAsync(id, body, api, (s, v) => s.SubmitAsync(id, v))).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapPost("/requisitions/{id:guid}/approve", (Guid id, VersionRequest body, ApiRequest api) =>
            RequisitionStepAsync(id, body, api, (s, v) => s.ApproveAsync(id, v, body.Note))).RequireAuthorization(Need(Permissions.PurchasingApprove));
        api.MapPost("/requisitions/{id:guid}/reject", (Guid id, VersionRequest body, ApiRequest api) =>
            RequisitionStepAsync(id, body, api, (s, v) => s.RejectAsync(id, body.Note ?? "", v))).RequireAuthorization(Need(Permissions.PurchasingApprove));
        api.MapPost("/requisitions/{id:guid}/order", OrderRequisitionAsync).RequireAuthorization(Need(Permissions.PurchasingManage));

        // Three-way match
        api.MapGet("/matching", MatchesAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapPost("/matching/run", RunMatchAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapPost("/matching/{id:guid}/accept", (Guid id, VersionRequest body, ApiRequest api, IBillLineSource bills) =>
            DecideMatchAsync(id, body, api, bills, accept: true)).RequireAuthorization(Need(Permissions.PurchasingApprove));
        api.MapPost("/matching/{id:guid}/reject", (Guid id, VersionRequest body, ApiRequest api, IBillLineSource bills) =>
            DecideMatchAsync(id, body, api, bills, accept: false)).RequireAuthorization(Need(Permissions.PurchasingApprove));
        api.MapGet("/matching/received-not-billed", (ApiRequest api, IBillLineSource bills) => MatchReportAsync(api, bills, billed: false))
            .RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapGet("/matching/billed-not-received", (ApiRequest api, IBillLineSource bills) => MatchReportAsync(api, bills, billed: true))
            .RequireAuthorization(Need(Permissions.PurchasingManage));

        // Landed costs
        api.MapGet("/landed-costs", LandedCostsAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapGet("/landed-costs/receipts", LandedReceiptsAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapGet("/landed-costs/{id:guid}", async (Guid id, ApiRequest api) => Results.Ok(await LandedCostDetailAsync(api, id, preview: false)))
            .RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapGet("/landed-costs/{id:guid}/preview", async (Guid id, ApiRequest api) => Results.Ok(await LandedCostDetailAsync(api, id, preview: true)))
            .RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapPost("/landed-costs", (LandedCostRequest body, ApiRequest api) => SaveLandedCostAsync(null, body, api)).RequireAuthorization(Need(Permissions.PurchasingApprove));
        api.MapPut("/landed-costs/{id:guid}", (Guid id, LandedCostRequest body, ApiRequest api) => SaveLandedCostAsync(id, body, api))
            .RequireAuthorization(Need(Permissions.PurchasingApprove));
        api.MapPost("/landed-costs/{id:guid}/post", PostLandedCostAsync).RequireAuthorization(Need(Permissions.PurchasingApprove));
        api.MapDelete("/landed-costs/{id:guid}", DeleteLandedCostAsync).RequireAuthorization(Need(Permissions.PurchasingApprove));

        // Reorder suggestions, supplier performance and settings
        api.MapGet("/reorder", ReorderAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapPost("/reorder/orders", ReorderToOrdersAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapPost("/reorder/requisition", ReorderToRequisitionAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapGet("/supplier-performance", SupplierPerformanceAsync).RequireAuthorization(Need(Permissions.ReportsView));
        api.MapGet("/supply/settings", SupplySettingsAsync).RequireAuthorization(Need(Permissions.PurchasingManage));
        api.MapPut("/supply/settings", SaveSupplySettingsAsync).RequireAuthorization(Need(Permissions.SetupStore));
        api.MapGet("/supply/approval-limits", async (ApiRequest api) => Results.Ok(await new SupplySettingsService(api.Root).GetLimitsAsync()))
            .RequireAuthorization(Need(Permissions.PurchasingApprove));
        api.MapPut("/supply/approval-limits/{roleId:guid}", SetApprovalLimitAsync).RequireAuthorization(Need(Permissions.SetupUsers));
    }

    // Helpers ---------------------------------------------------------------------------------------------------------

    /// <summary>A page of a list made in memory (the services' lists), as <see cref="ApiRequest.PageAsync{TIn, TOut}"/> pages queries.</summary>
    private static PageOf<T> Page<T>(ApiRequest api, IReadOnlyList<T> rows)
    {
        var (page, size) = api.Paging();
        return new PageOf<T>([.. rows.Skip((page - 1) * size).Take(size)], rows.Count, page, size);
    }

    /// <summary>The store a location belongs to (404 when there is none, or the user cannot see it).</summary>
    private static async Task<Guid> LocationStoreAsync(ApiRequest api, Guid locationId)
    {
        await using var db = api.Root.Create();
        var store = await db.Locations.AsNoTracking().Where(l => l.Id == locationId).Select(l => (Guid?)l.StoreId).SingleOrDefaultAsync() ?? throw ApiException.NotFound("That location");
        api.RequireStore(store, "That location");
        return store;
    }

    /// <summary>The location a request is for: the one named, else the first location of the store asked for (or of the user's store).</summary>
    private static async Task<Guid> RequestLocationAsync(ApiRequest api, Guid? named)
    {
        if (named is Guid id)
        {
            await LocationStoreAsync(api, id);
            return id;
        }
        if (api.Id("locationId") is Guid fromQuery)
        {
            await LocationStoreAsync(api, fromQuery);
            return fromQuery;
        }
        var store = api.AskedStore ?? api.User.StoreId ?? await SingleStoreAsync(api);
        if (store is not Guid s) throw ApiException.Bad("Choose the store or location.");
        api.RequireStore(s, "That store");
        await using var db = api.Root.Create();
        return await db.Locations.AsNoTracking().Where(l => l.StoreId == s).OrderBy(l => l.Code).Select(l => (Guid?)l.Id).FirstOrDefaultAsync()
               ?? throw ApiException.Bad("That store has no location yet.");
    }

    private static async Task<Guid?> SingleStoreAsync(ApiRequest api)
    {
        await using var db = api.Root.Create();
        var stores = await db.Stores.AsNoTracking().Select(s => s.Id).Take(2).ToListAsync();
        return stores.Count == 1 ? stores[0] : null;
    }

    private static Guid? VersionGuid(string? version) => string.IsNullOrWhiteSpace(version) ? null : ApiRequest.ParseVersion(version);

    private static Guid NeedVersion(string? version) => ApiRequest.ParseVersion(version);

    // Batches ---------------------------------------------------------------------------------------------------------

    private static BatchInfoRow ToInfo(BatchRow b, Dictionary<Guid, string> codes, DateTime updated) => new(b.Id, ApiRequest.Version(updated), b.ItemId, b.Plu, b.ItemName,
        codes.GetValueOrDefault(b.StoreId) ?? "", b.LocationCode, b.BatchNumber, b.ExpiresOn, b.MadeOn, b.Supplier, b.ReceivedOn, b.PurchaseOrderNumber, b.OnHand, b.UnitCost,
        b.Value, b.DaysLeft, b.Bin, b.Note);

    private static async Task<List<BatchInfoRow>> BatchInfosAsync(ApiRequest api, IEnumerable<BatchRow> rows)
    {
        var list = rows.ToList();
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        var ids = list.Select(r => r.Id).ToList();
        var stamps = await db.ItemBatches.AsNoTracking().Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.UpdatedAtUtc);
        return [.. list.Select(r => ToInfo(r, codes, stamps.GetValueOrDefault(r.Id)))];
    }

    private static async Task<IResult> BatchesAsync(ApiRequest api)
    {
        var store = api.ListStore();
        Guid? location = api.Id("locationId") is Guid l ? l : null;
        if (location is Guid loc && await LocationStoreAsync(api, loc) is var owner && store is Guid s && owner != s) throw ApiException.Forbidden("You may not see that store.");
        var rows = await new BatchService(api.Root).ListAsync(location, store, api.Text("search"), api.Bool("inStock") ?? true);
        return Results.Ok(Page(api, await BatchInfosAsync(api, rows)));
    }

    private static async Task<IResult> ExpiryAsync(ApiRequest api, bool expired)
    {
        var store = api.ListStore();
        var batches = new BatchService(api.Root);
        int days = api.Number("days") ?? (store is Guid s ? (await new SupplySettingsService(api.Root).GetAsync(s)).ExpiryWarningDays : 30);
        var today = await api.TodayAsync(store);
        var rows = expired ? await batches.ExpiredAsync(null, store, today) : await batches.ExpiringAsync(days, null, store, today);
        var infos = await BatchInfosAsync(api, rows);
        return Results.Ok(new ExpiryReport(expired ? 0 : days, infos, infos.Sum(r => r.OnHand), infos.Sum(r => r.Value)));
    }

    private static async Task<IResult> ItemBatchesAsync(Guid id, ApiRequest api)
    {
        var (item, _) = await LoadItemAsync(api, id);
        return Results.Ok(await BatchInfosAsync(api, await new BatchService(api.Root).OfItemAsync(item.Id)));
    }

    private static async Task<IResult> SaveBatchAsync(Guid? id, BatchRequest body, ApiRequest api)
    {
        Guid itemId;
        ItemBatch? existing = null;
        if (id is Guid b)
        {
            existing = await new BatchService(api.Root).GetAsync(b) ?? throw ApiException.NotFound("That batch");
            api.RequireStore(existing.StoreId, "That batch");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(existing.UpdatedAtUtc));
            itemId = existing.ItemId;
        }
        else itemId = body.ItemId ?? throw ApiException.Bad("Choose the item.");
        var (item, store) = await LoadItemAsync(api, itemId);
        if (!item.TracksBatches) throw ApiException.Bad($"{item.Plu} is not kept in batches.");
        var saved = await new BatchService(await api.ForChangeAsync(store)).SaveAsync(new ItemBatch
        {
            Id = existing?.Id ?? Guid.Empty, ItemId = item.Id, BatchNumber = body.BatchNumber ?? existing?.BatchNumber ?? "", ExpiresOn = body.ExpiresOn, MadeOn = body.MadeOn,
            SupplierId = body.SupplierId, Note = body.Note,
        });
        var row = (await new BatchService(api.Root).OfItemAsync(item.Id)).Single(r => r.Id == saved.Id);
        var info = (await BatchInfosAsync(api, [row]))[0];
        return id is null ? Results.Created($"/api/batches/{saved.Id}", info) : Results.Ok(info);
    }

    private static async Task<IResult> AdjustBatchAsync(Guid id, BatchAdjustRequest body, ApiRequest api)
    {
        var batch = await new BatchService(api.Root).GetAsync(id) ?? throw ApiException.NotFound("That batch");
        api.RequireStore(batch.StoreId, "That batch");
        if (!api.Session.Can(Permissions.InventoryAdjust)) throw ApiException.Forbidden("Adjusting stock needs the right to adjust stock.");
        await new BatchService(await api.ForChangeAsync(batch.StoreId)).AdjustAsync(id, body.Quantity, body.Reason, body.SetTo);
        var row = (await new BatchService(api.Root).OfItemAsync(batch.ItemId)).Single(r => r.Id == id);
        return Results.Ok((await BatchInfosAsync(api, [row]))[0]);
    }

    // Bins ------------------------------------------------------------------------------------------------------------

    private static async Task<IResult> BinsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        Guid? location = api.Id("locationId") is Guid l ? l : null;
        if (location is Guid loc) await LocationStoreAsync(api, loc);
        var rows = await new BinService(api.Root).ListAsync(location, store);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        var ids = rows.Select(r => r.Id).ToList();
        var bins = await db.Bins.AsNoTracking().Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => (b.UpdatedAtUtc, b.StoreId));
        return Results.Ok(rows.Select(r => new BinInfo(r.Id, ApiRequest.Version(bins[r.Id].UpdatedAtUtc), r.LocationId, codes.GetValueOrDefault(bins[r.Id].StoreId) ?? "",
            r.LocationCode, r.Code, r.Zone, r.Description, r.IsActive, r.Items)).ToList());
    }

    private static async Task<IResult> SaveBinAsync(Guid? id, BinRequest body, ApiRequest api)
    {
        Bin? existing = null;
        if (id is Guid b)
        {
            existing = await new BinService(api.Root).GetAsync(b) ?? throw ApiException.NotFound("That bin");
            api.RequireStore(existing.StoreId, "That bin");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(existing.UpdatedAtUtc));
        }
        var location = existing?.LocationId ?? body.LocationId ?? throw ApiException.Bad("Choose the location.");
        var store = await LocationStoreAsync(api, location);
        var saved = await new BinService(await api.ForChangeAsync(store)).SaveAsync(new Bin
        {
            Id = existing?.Id ?? Guid.Empty, LocationId = location, Code = body.Code ?? "", Zone = body.Zone, Description = body.Description, IsActive = body.IsActive,
        });
        var row = (await new BinService(api.Root).ListAsync(location)).Single(r => r.Id == saved.Id);
        await using var db = api.Root.Create();
        var stamp = await db.Bins.AsNoTracking().Where(x => x.Id == saved.Id).Select(x => x.UpdatedAtUtc).SingleAsync();
        var info = new BinInfo(row.Id, ApiRequest.Version(stamp), row.LocationId, (await StoreCodesAsync(db)).GetValueOrDefault(store) ?? "", row.LocationCode, row.Code,
            row.Zone, row.Description, row.IsActive, row.Items);
        return id is null ? Results.Created($"/api/bins/{saved.Id}", info) : Results.Ok(info);
    }

    private static async Task<IResult> DeleteBinAsync(Guid id, ApiRequest api)
    {
        var bin = await new BinService(api.Root).GetAsync(id) ?? throw ApiException.NotFound("That bin");
        api.RequireStore(bin.StoreId, "That bin");
        await new BinService(await api.ForChangeAsync(bin.StoreId)).DeleteAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> AssignBinAsync(BinAssignRequest body, ApiRequest api)
    {
        if (body.ItemIds is not { Count: > 0 }) throw ApiException.Bad("Choose the items.");
        Guid? store = null;
        foreach (var id in body.ItemIds.Distinct())
        {
            var (_, itemStore) = await LoadItemAsync(api, id);
            if (store is Guid s && s != itemStore) throw ApiException.Bad("Choose items of one store at a time.");
            store = itemStore;
        }
        int changed = await new BinService(await api.ForChangeAsync(store)).AssignAsync(body.ItemIds, body.BinCode);
        return Results.Ok(new { changed });
    }

    private static async Task<IResult> CountSheetAsync(ApiRequest api)
    {
        var location = await RequestLocationAsync(api, null);
        var rows = await new BinService(api.Root).CountSheetAsync(location, api.Text("zone"), api.Text("bin"));
        return Results.Ok(new BinList("Count sheet", rows));
    }

    private static async Task<IResult> PickOrdersAsync(ApiRequest api)
    {
        var location = await RequestLocationAsync(api, null);
        return Results.Ok(new BinList("Pick list: customer orders", await new BinService(api.Root).PickCustomerOrdersAsync(location)));
    }

    private static async Task<IResult> PickTransferAsync(Guid id, ApiRequest api)
    {
        await using (var db = api.Root.Create())
        {
            var t = await db.StockTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("That transfer");
            if (!api.User.CanSee(t.StoreId)) throw ApiException.NotFound("That transfer");
        }
        return Results.Ok(new BinList("Pick list: transfer", await new BinService(api.Root).PickTransferAsync(id)));
    }

    private static async Task<IResult> PutAwayAsync(Guid id, ApiRequest api)
    {
        var po = await LoadPurchaseOrderAsync(api, id);
        return Results.Ok(new BinList($"Put-away list: PO {po.Number}", await new BinService(api.Root).PutAwayAsync(id)));
    }

    // Purchase orders ---------------------------------------------------------------------------------------------------

    private static async Task<PurchaseOrder> LoadPurchaseOrderAsync(ApiRequest api, Guid id)
    {
        await using var db = api.Root.Create();
        var po = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).Include(p => p.Location).SingleOrDefaultAsync(p => p.Id == id)
                 ?? throw ApiException.NotFound("That purchase order");
        api.RequireStore(po.StoreId, "That purchase order");
        return po;
    }

    private static async Task<IResult> PurchaseOrdersAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var orders = db.PurchaseOrders.AsNoTracking();
        if (store is { } s) orders = orders.Where(p => p.StoreId == s);
        if (api.Text("status") is { } statusText)
            orders = Enum.TryParse<PurchaseOrderStatus>(statusText, true, out var status) ? orders.Where(p => p.Status == status) : throw ApiException.Bad($"{statusText} is not an order status.");
        if (api.Text("search") is { } text)
        {
            long? number = SearchNumber(text);
            orders = orders.Where(p => (number != null && p.Number == number) || (p.Description ?? "").Contains(text) || p.Lines.Any(l => (l.SupplierName ?? "").Contains(text)));
        }
        var codes = await StoreCodesAsync(db);
        var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Company);
        var rows = orders.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Number).Select(p => new
        {
            p.Id, p.Number, p.StoreId, Location = p.Location!.Code, p.Status, p.CreatedAt, p.PostedAt, p.ReceivedAt, p.ExpectedOn, p.SupplierId, p.Description,
            Lines = p.Lines.Count, Total = p.Lines.Sum(l => l.OrderCost), FirstSupplier = p.Lines.Select(l => l.SupplierName).FirstOrDefault(n => n != null),
        });
        return Results.Ok(await api.PageAsync(rows, p => new PurchaseOrderListRow(p.Id, p.Number, codes.GetValueOrDefault(p.StoreId) ?? "", p.Location, p.Status, p.CreatedAt,
            p.PostedAt, p.ReceivedAt, p.ExpectedOn, p.SupplierId is Guid sid ? suppliers.GetValueOrDefault(sid) : p.FirstSupplier, p.Description, p.Lines, p.Total)));
    }

    private static async Task<PurchaseOrderDetail> PurchaseOrderDetailAsync(ApiRequest api, Guid id)
    {
        var po = await LoadPurchaseOrderAsync(api, id);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        var itemIds = po.Lines.Where(l => l.ItemId != null).Select(l => l.ItemId!.Value).ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var batches = (await db.StockMovements.AsNoTracking()
                .Where(m => m.Kind == StockMovementKind.Receipt && m.DocumentNumber == po.Number && m.BatchId != null && itemIds.Contains(m.ItemId))
                .Join(db.ItemBatches.AsNoTracking(), m => m.BatchId, b => b.Id, (m, b) => new { m.ItemId, b.BatchNumber, m.Quantity, b.ExpiresOn, b.MadeOn }).ToListAsync())
            .ToLookup(b => b.ItemId);
        string? supplier = po.SupplierId is Guid sid ? await db.Suppliers.AsNoTracking().Where(x => x.Id == sid).Select(x => x.Company).SingleOrDefaultAsync() : null;
        var lines = po.Lines.OrderBy(l => l.SupplierName).ThenBy(l => l.Plu).Select(l =>
        {
            var item = l.ItemId is Guid i ? items.GetValueOrDefault(i) : null;
            return new PurchaseOrderLineInfo(l.Id, l.ItemId, l.Plu, l.Description, l.SupplierName, l.CaseQuantity, l.OrderQuantity, l.OrderQuantity * l.CaseQuantity,
                l.CostEach, l.OrderedCost, l.OrderCost, l.QuantityReceived, item?.TracksBatches ?? false, item?.ShelfLifeDays ?? 0, item?.BinLocation,
                [.. (l.ItemId is Guid it ? batches[it] : []).Select(b => new ReceivedBatchInfo(b.BatchNumber, b.Quantity, b.ExpiresOn, b.MadeOn))]);
        }).ToList();
        return new PurchaseOrderDetail(po.Id, ApiRequest.Version(po.Version), po.Number, po.StoreId, codes.GetValueOrDefault(po.StoreId) ?? "", po.LocationId,
            po.Location?.Code ?? "", po.Status, po.CreatedAt, po.PostedAt, po.ReceivedAt, po.ExpectedOn, po.SupplierId, supplier ?? lines.Select(l => l.Supplier).FirstOrDefault(n => n != null),
            po.Description, po.CreatedBy, po.ReceivedBy, lines, po.Lines.Sum(l => l.OrderCost));
    }

    private static async Task<IResult> PostPurchaseOrderAsync(Guid id, VersionRequest body, ApiRequest api)
    {
        var po = await LoadPurchaseOrderAsync(api, id);
        ApiRequest.CheckVersion(body.Version, ApiRequest.Version(po.Version));
        await new PurchasingService(await api.ForChangeAsync(po.StoreId)).PostOrderAsync(id);
        return Results.Ok(await PurchaseOrderDetailAsync(api, id));
    }

    /// <summary>Receives an order on the web: the pieces and cost of each line, and for items kept in batches what came of each batch.</summary>
    private static async Task<IResult> ReceivePurchaseOrderAsync(Guid id, ReceiveRequest body, ApiRequest api)
    {
        var po = await LoadPurchaseOrderAsync(api, id);
        ApiRequest.CheckVersion(body.Version, ApiRequest.Version(po.Version));
        if (body.ShippingCost < 0) throw ApiException.Bad("Shipping cannot be below zero.");
        var known = po.Lines.ToDictionary(l => l.Id);
        var lines = (body.Lines ?? []).Select(l =>
        {
            var line = known.GetValueOrDefault(l.LineId) ?? throw ApiException.Bad("A line received is not on this order.");
            return new ReceivedLine(l.LineId, l.Pieces, l.CostEach ?? line.CostEach,
                l.Batches is { Count: > 0 } b ? [.. b.Select(x => new ReceivedBatch(x.BatchNumber, x.Pieces, x.ExpiresOn, x.MadeOn))] : null);
        }).ToList();
        if (lines.All(l => l.PiecesReceived == 0)) throw ApiException.Bad("Enter the pieces received.");
        await new PurchasingService(await api.ForChangeAsync(po.StoreId)).PostShipmentAsync(id, lines, body.ShippingCost);
        return Results.Ok(await PurchaseOrderDetailAsync(api, id));
    }

    // Requisitions ------------------------------------------------------------------------------------------------------

    private static async Task<IResult> RequisitionsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        RequisitionStatus? status = api.Text("status") is { } text
            ? Enum.TryParse<RequisitionStatus>(text, true, out var st) ? st : throw ApiException.Bad($"{text} is not a requisition status.")
            : null;
        var rows = await new RequisitionService(api.Root).ListAsync(store, api.Id("locationId"), status, 5000);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        return Results.Ok(Page(api, [.. rows.Select(r => new RequisitionListRow(r.Id, r.Number, codes.GetValueOrDefault(r.StoreId) ?? "", r.LocationCode, r.Status, r.CreatedAt,
            r.RequestedBy, r.NeededBy, r.Lines, r.Total, r.DecidedBy, r.DecidedAt, r.Note))]));
    }

    private static async Task<RequisitionDetail> RequisitionDetailAsync(ApiRequest api, Guid id)
    {
        var r = await new RequisitionService(api.Root).GetAsync(id) ?? throw ApiException.NotFound("That requisition");
        api.RequireStore(r.StoreId, "That requisition");
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        var orderIds = r.Lines.Where(l => l.PurchaseOrderId != null).Select(l => l.PurchaseOrderId!.Value).Distinct().ToList();
        var numbers = await db.PurchaseOrders.AsNoTracking().Where(p => orderIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number);
        return new RequisitionDetail(r.Id, ApiRequest.Version(r.Version), r.Number, r.StoreId, codes.GetValueOrDefault(r.StoreId) ?? "", r.LocationId, r.Location?.Code ?? "",
            r.Status, r.CreatedAt, r.RequestedBy, r.NeededBy, r.Note, r.SubmittedAt, r.DecidedAt, r.DecidedBy, r.DecisionNote, r.OrderedAt, RequisitionService.Total(r),
            [.. r.Lines.Select(l => new RequisitionLineInfo(l.Id, l.LineNumber, l.ItemId, l.Plu, l.Description, l.Quantity, l.CostEach, Money.Round(l.Quantity * l.CostEach), l.NeededBy,
                l.SupplierId, l.SupplierName, l.Note, l.PurchaseOrderId, l.PurchaseOrderId is Guid p ? numbers.GetValueOrDefault(p) : null))]);
    }

    private static List<RequisitionLineInput> Inputs(RequisitionRequest body) =>
        [.. (body.Lines ?? []).Select(l => new RequisitionLineInput(l.ItemId, l.Plu, l.Quantity, l.NeededBy, l.SupplierId, l.CostEach, l.Note))];

    private static async Task<IResult> CreateRequisitionAsync(RequisitionRequest body, ApiRequest api)
    {
        var location = await RequestLocationAsync(api, body.LocationId);
        var store = await LocationStoreAsync(api, location);
        var made = await new RequisitionService(await api.ForChangeAsync(store)).CreateAsync(location, body.NeededBy, body.Note, Inputs(body));
        return Results.Created($"/api/requisitions/{made.Id}", await RequisitionDetailAsync(api, made.Id));
    }

    private static async Task<IResult> SaveRequisitionAsync(Guid id, RequisitionRequest body, ApiRequest api)
    {
        var current = await RequisitionDetailAsync(api, id);
        ApiRequest.CheckVersion(body.Version, current.Version);
        await new RequisitionService(await api.ForChangeAsync(current.StoreId)).SaveAsync(id, NeedVersion(body.Version), body.NeededBy, body.Note, Inputs(body));
        return Results.Ok(await RequisitionDetailAsync(api, id));
    }

    private static async Task<IResult> DeleteRequisitionAsync(Guid id, ApiRequest api)
    {
        var current = await RequisitionDetailAsync(api, id);
        await new RequisitionService(await api.ForChangeAsync(current.StoreId)).DeleteAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> RequisitionStepAsync(Guid id, VersionRequest body, ApiRequest api, Func<RequisitionService, Guid, Task<PurchaseRequisition>> step)
    {
        var current = await RequisitionDetailAsync(api, id);
        ApiRequest.CheckVersion(body.Version, current.Version);
        await step(new RequisitionService(await api.ForChangeAsync(current.StoreId)), NeedVersion(body.Version));
        return Results.Ok(await RequisitionDetailAsync(api, id));
    }

    private static async Task<IResult> OrderRequisitionAsync(Guid id, VersionRequest body, ApiRequest api)
    {
        var current = await RequisitionDetailAsync(api, id);
        ApiRequest.CheckVersion(body.Version, current.Version);
        var made = await new RequisitionService(await api.ForChangeAsync(current.StoreId)).OrderAsync(id, NeedVersion(body.Version));
        return Results.Ok(new OrdersMade([.. made.Orders.Select(o => new OrderMade(o.Id, o.Number, o.Supplier))]));
    }

    // Three-way match ---------------------------------------------------------------------------------------------------

    private static async Task<List<MatchRow>> MatchRowsAsync(ApiRequest api, IEnumerable<BillMatch> matches)
    {
        var list = matches.ToList();
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        var orderIds = list.Select(m => m.PurchaseOrderId).Distinct().ToList();
        var numbers = await db.PurchaseOrders.AsNoTracking().Where(p => orderIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number);
        var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Company);
        return [.. list.Select(m => new MatchRow(m.Id, ApiRequest.Version(m.Version), codes.GetValueOrDefault(m.StoreId) ?? "", m.PurchaseOrderId,
            numbers.GetValueOrDefault(m.PurchaseOrderId), m.PurchaseOrderLineId, m.BillId, m.BillLineId, m.BillNumber, m.BillDate,
            m.SupplierId is Guid s ? suppliers.GetValueOrDefault(s) : null, m.Plu, m.Description, m.QuantityOrdered, m.QuantityReceived, m.QuantityBilled, m.QuantityMatched,
            m.OrderedCost, m.ReceivedCost, m.BilledCost, Money.Round(m.QuantityMatched * (m.BilledCost - m.ReceivedCost)), m.Status, m.Reason, ApiRequest.Utc(m.CheckedAtUtc),
            ApiRequest.Utc(m.DecidedAtUtc), m.DecidedBy, m.DecisionNote))];
    }

    private static async Task<IResult> MatchesAsync(ApiRequest api, IBillLineSource bills)
    {
        var store = api.ListStore();
        BillMatchStatus? status = api.Text("status") is { } text
            ? Enum.TryParse<BillMatchStatus>(text, true, out var st) ? st : throw ApiException.Bad($"{text} is not a match status.")
            : null;
        var rows = await new PurchaseMatchService(api.Root, bills).ListAsync(store, status, api.Id("purchaseOrderId"));
        return Results.Ok(Page(api, await MatchRowsAsync(api, rows)));
    }

    /// <summary>Checks again every order of the store (or of the stores the user sees) that a supplier bill names.</summary>
    private static async Task<IResult> RunMatchAsync(ApiRequest api, IBillLineSource bills)
    {
        var store = api.ListStore();
        var ids = await bills.OrdersBilledAsync();
        await using var db = api.Root.Create();
        var orders = db.PurchaseOrders.AsNoTracking().Where(p => ids.Contains(p.Id));
        if (store is Guid s) orders = orders.Where(p => p.StoreId == s);
        var mine = await orders.Select(p => new { p.Id, p.StoreId }).ToListAsync();
        int lines = 0, matched = 0, held = 0, posted = 0;
        foreach (var group in mine.GroupBy(o => o.StoreId))
            foreach (var po in group)
            {
                var run = await new PurchaseMatchService(await api.ForChangeAsync(group.Key), bills).MatchOrderAsync(po.Id);
                lines += run.Lines; matched += run.Matched; held += run.Held; posted += run.Posted;
            }
        return Results.Ok(new MatchRun(lines, matched, held, posted));
    }

    private static async Task<IResult> DecideMatchAsync(Guid id, VersionRequest body, ApiRequest api, IBillLineSource bills, bool accept)
    {
        var match = await new PurchaseMatchService(api.Root, bills).GetAsync(id) ?? throw ApiException.NotFound("That bill line");
        api.RequireStore(match.StoreId, "That bill line");
        ApiRequest.CheckVersion(body.Version, ApiRequest.Version(match.Version));
        var service = new PurchaseMatchService(await api.ForChangeAsync(match.StoreId), bills);
        var decided = accept ? await service.AcceptAsync(id, NeedVersion(body.Version), body.Note) : await service.RejectAsync(id, body.Note ?? "", NeedVersion(body.Version));
        return Results.Ok((await MatchRowsAsync(api, [decided]))[0]);
    }

    private static async Task<IResult> MatchReportAsync(ApiRequest api, IBillLineSource bills, bool billed)
    {
        var store = api.ListStore();
        var service = new PurchaseMatchService(api.Root, bills);
        var rows = billed ? await service.BilledNotReceivedAsync(store) : await service.ReceivedNotBilledAsync(store);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        return Results.Ok(rows.Select(r => new MatchReportInfo(r.PurchaseOrderId, r.PurchaseOrderNumber, r.PurchaseOrderLineId, codes.GetValueOrDefault(r.StoreId) ?? "", r.Plu,
            r.Description, r.Supplier, r.Ordered, r.Received, r.Billed, r.Difference, r.CostEach, r.Value, r.ReceivedAt)).ToList());
    }

    // Landed costs ------------------------------------------------------------------------------------------------------

    private static async Task<IResult> LandedCostsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        LandedCostStatus? status = api.Text("status") is { } text
            ? Enum.TryParse<LandedCostStatus>(text, true, out var st) ? st : throw ApiException.Bad($"{text} is not a voucher status.")
            : null;
        var rows = await new LandedCostService(api.Root).ListAsync(store, status);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        return Results.Ok(Page(api, [.. rows.Select(v => new LandedCostListRow(v.Id, v.Number, codes.GetValueOrDefault(v.StoreId) ?? "", v.Date, v.Status, v.Basis, v.Reference,
            v.Total, v.Receipts, v.Note, ApiRequest.Utc(v.PostedAtUtc), v.PostedBy))]));
    }

    private static async Task<IResult> LandedReceiptsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var rows = await new LandedCostService(api.Root).ReceiptsAsync(store);
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        return Results.Ok(rows.Select(r => new ReceiptChoiceInfo(r.PurchaseOrderId, r.Number, codes.GetValueOrDefault(r.StoreId) ?? "", r.LocationCode, r.ReceivedAt, r.Supplier,
            r.Description, r.Lines, r.Value)).ToList());
    }

    private static async Task<LandedCostDetail> LandedCostDetailAsync(ApiRequest api, Guid id, bool preview)
    {
        var service = new LandedCostService(api.Root);
        var v = await service.GetAsync(id) ?? throw ApiException.NotFound("That voucher");
        api.RequireStore(v.StoreId, "That voucher");
        var allocations = preview && v.Status == LandedCostStatus.Draft ? await service.PreviewAsync(id) : v.Allocations;
        await using var db = api.Root.Create();
        var codes = await StoreCodesAsync(db);
        var orderIds = v.Receipts.Select(r => r.PurchaseOrderId).ToList();
        var choices = (await service.ReceiptsAsync(v.StoreId, 100_000)).Where(r => orderIds.Contains(r.PurchaseOrderId)).ToList();
        return new LandedCostDetail(v.Id, ApiRequest.Version(v.Version), v.Number, v.StoreId, codes.GetValueOrDefault(v.StoreId) ?? "", v.Date, v.Status, v.Basis, v.Reference,
            v.Note, v.Charges.Sum(c => c.Amount), [.. v.Charges.Select(c => new LandedChargeInfo(c.Kind, c.Description, c.Amount))],
            [.. choices.Select(c => new LandedReceiptInfo(c.PurchaseOrderId, c.Number, c.ReceivedAt, c.Supplier, c.Value))],
            [.. allocations.Select(a => new LandedAllocationInfo(a.PurchaseOrderLineId, a.ItemId, a.Plu, a.Description, a.Quantity, a.Value, a.Weight, a.Share, a.OnHand,
                a.ToInventory, a.ToCostOfGoods))], v.CreatedBy, ApiRequest.Utc(v.PostedAtUtc), v.PostedBy);
    }

    private static async Task<IResult> SaveLandedCostAsync(Guid? id, LandedCostRequest body, ApiRequest api)
    {
        Guid store;
        Guid? version = null;
        if (id is Guid existing)
        {
            var current = await LandedCostDetailAsync(api, existing, preview: false);
            ApiRequest.CheckVersion(body.Version, current.Version);
            store = current.StoreId;
            version = NeedVersion(body.Version);
        }
        else
        {
            store = body.StoreId ?? api.AskedStore ?? api.User.StoreId ?? await SingleStoreAsync(api) ?? throw ApiException.Bad("Choose the store.");
            api.RequireStore(store, "That store");
        }
        var saved = await new LandedCostService(await api.ForChangeAsync(store)).SaveAsync(id, version, store, body.Date ?? await api.TodayAsync(store), body.Basis,
            body.Reference, body.Note, [.. (body.Charges ?? []).Select(c => new LandedChargeInput(c.Kind, c.Description, c.Amount))], body.PurchaseOrderIds ?? []);
        var detail = await LandedCostDetailAsync(api, saved.Id, preview: true);
        return id is null ? Results.Created($"/api/landed-costs/{saved.Id}", detail) : Results.Ok(detail);
    }

    private static async Task<IResult> PostLandedCostAsync(Guid id, VersionRequest body, ApiRequest api)
    {
        var current = await LandedCostDetailAsync(api, id, preview: false);
        ApiRequest.CheckVersion(body.Version, current.Version);
        await new LandedCostService(await api.ForChangeAsync(current.StoreId)).PostAsync(id, NeedVersion(body.Version));
        return Results.Ok(await LandedCostDetailAsync(api, id, preview: false));
    }

    private static async Task<IResult> DeleteLandedCostAsync(Guid id, ApiRequest api)
    {
        var current = await LandedCostDetailAsync(api, id, preview: false);
        await new LandedCostService(await api.ForChangeAsync(current.StoreId)).DeleteAsync(id);
        return Results.NoContent();
    }

    // Reorder suggestions, supplier performance, settings -------------------------------------------------------------

    private static async Task<IResult> ReorderAsync(ApiRequest api)
    {
        var location = await RequestLocationAsync(api, null);
        var store = await LocationStoreAsync(api, location);
        var rows = await new ReorderService(api.Root).SuggestAsync(location, api.Id("supplierId"), api.Bool("all") ?? false, await api.TodayAsync(store));
        return Results.Ok(new { locationId = location, rows });
    }

    private static List<ReorderChoice> Choices(ReorderRequest body) =>
        [.. (body.Lines ?? []).Select(l => new ReorderChoice(l.ItemId, l.Pieces, l.SupplierId))];

    private static async Task<IResult> ReorderToOrdersAsync(ReorderRequest body, ApiRequest api)
    {
        var location = await RequestLocationAsync(api, body.LocationId);
        var store = await LocationStoreAsync(api, location);
        var orders = await new ReorderService(await api.ForChangeAsync(store)).ToPurchaseOrdersAsync(location, Choices(body));
        await using var db = api.Root.Create();
        var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Company);
        return Results.Ok(new OrdersMade([.. orders.Select(o => new OrderMade(o.Id, o.Number, o.SupplierId is Guid s ? suppliers.GetValueOrDefault(s) : null))]));
    }

    private static async Task<IResult> ReorderToRequisitionAsync(ReorderRequest body, ApiRequest api)
    {
        var location = await RequestLocationAsync(api, body.LocationId);
        var store = await LocationStoreAsync(api, location);
        var made = await new ReorderService(await api.ForChangeAsync(store)).ToRequisitionAsync(location, Choices(body), body.Submit, body.NeededBy);
        return Results.Created($"/api/requisitions/{made.Id}", await RequisitionDetailAsync(api, made.Id));
    }

    private static async Task<IResult> SupplierPerformanceAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        if (api.Text("from") is null && api.Text("to") is null) from = to.AddDays(-89);
        var rows = await new SupplierPerformanceService(api.Root).ReportAsync(from, to, store);
        return Results.Ok(new { from, to, rows });
    }

    private static async Task<Guid> SettingsStoreAsync(ApiRequest api)
    {
        var store = api.AskedStore ?? api.User.StoreId ?? await SingleStoreAsync(api) ?? throw ApiException.Bad("Choose the store.");
        api.RequireStore(store, "That store");
        return store;
    }

    private static async Task<SupplySettingsInfo> SettingsInfoAsync(ApiRequest api, Guid store)
    {
        var s = await new SupplySettingsService(api.Root).GetAsync(store);
        return new SupplySettingsInfo(s.Id == Guid.Empty ? "new" : ApiRequest.Version(s.UpdatedAtUtc), store, s.BlockExpiredSales, s.ExpiryWarningDays, s.QuantityTolerancePercent,
            s.QuantityToleranceUnits, s.PriceTolerancePercent, s.PriceToleranceAmount, s.SalesWindowDays, s.SafetyStockDays, s.OrderCoverDays, s.DefaultLeadTimeDays,
            s.ApproverMustDiffer);
    }

    private static async Task<IResult> SupplySettingsAsync(ApiRequest api) => Results.Ok(await SettingsInfoAsync(api, await SettingsStoreAsync(api)));

    private static async Task<IResult> SaveSupplySettingsAsync(SupplySettingsInfo body, ApiRequest api)
    {
        var store = await SettingsStoreAsync(api);
        ApiRequest.CheckVersion(body.Version, (await SettingsInfoAsync(api, store)).Version);
        await new SupplySettingsService(await api.ForChangeAsync(store)).SaveAsync(store, new SupplySettings
        {
            BlockExpiredSales = body.BlockExpiredSales, ExpiryWarningDays = body.ExpiryWarningDays, QuantityTolerancePercent = body.QuantityTolerancePercent,
            QuantityToleranceUnits = body.QuantityToleranceUnits, PriceTolerancePercent = body.PriceTolerancePercent, PriceToleranceAmount = body.PriceToleranceAmount,
            SalesWindowDays = body.SalesWindowDays, SafetyStockDays = body.SafetyStockDays, OrderCoverDays = body.OrderCoverDays, DefaultLeadTimeDays = body.DefaultLeadTimeDays,
            ApproverMustDiffer = body.ApproverMustDiffer,
        });
        return Results.Ok(await SettingsInfoAsync(api, store));
    }

    /// <summary>Approval limits hold for every store, so they follow the company-wide rule.</summary>
    private static async Task<IResult> SetApprovalLimitAsync(Guid roleId, ApprovalLimitRequest body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        await new SupplySettingsService(await api.ForChangeAsync()).SetLimitAsync(roleId, body.MaxAmount);
        return Results.Ok(await new SupplySettingsService(api.Root).GetLimitsAsync());
    }
}
