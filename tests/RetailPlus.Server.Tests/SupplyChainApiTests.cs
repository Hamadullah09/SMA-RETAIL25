using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Sync.Tests;
using static RetailPlus.Server.Tests.BackOffice;
using static RetailPlus.Server.Tests.WebClients;

namespace RetailPlus.Server.Tests;

/// <summary>
/// The supply chain endpoints (phase E2) on stores and SQLite tills of their own: a requisition approved and ordered on
/// the web, another rejected, changed and approved within the role's limit, the order received there with batches, the batches reaching the till and the till's own receipt reaching the
/// server, bins and their lists, the three-way match on bills from the in-memory bill source, landed costs, reorder
/// suggestions, supplier performance and the settings; each with the permission it needs, the store scope and 409 on an
/// old version.
/// </summary>
public sealed class SupplyChainApiTests(SyncWorld world) : IClassFixture<SyncWorld>
{
    private void Require() => Skip.IfNot(world.Available, world.UnavailableReason);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>A signed-in user of a store whose role has just these permissions (and web sign-in), with the CSRF header.</summary>
    private async Task<HttpClient> UserWithAsync(Guid store, params string[] permissions)
    {
        var setup = new SetupService(world.ServerData);
        var role = await setup.SaveRoleAsync(new Role { Name = "Supply " + Guid.NewGuid().ToString("N")[..6], Level = 2 }, [Permissions.WebSignIn, .. permissions]);
        var name = "supply-" + Guid.NewGuid().ToString("N")[..8];
        await setup.SaveUserAsync(new UserAccount { UserName = name, RoleId = role.Id, StoreId = store, IsActive = true }, UserPassword);
        var client = await world.SignInAsync(name, UserPassword);
        client.DefaultRequestHeaders.Add(CsrfGuard.Header, CsrfGuard.Value);
        return client;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body, SyncWorld.Json);
        return await ReadAsync<T>(response);
    }

    private static async Task<T> PutAsync<T>(HttpClient client, string url, object body)
    {
        using var response = await client.PutAsJsonAsync(url, body, SyncWorld.Json);
        return await ReadAsync<T>(response);
    }

    private static async Task<HttpStatusCode> PostStatusAsync(HttpClient client, string url, object body) =>
        await StatusAsync(client.PostAsJsonAsync(url, body, SyncWorld.Json));

    private static async Task<HttpStatusCode> PutStatusAsync(HttpClient client, string url, object body) =>
        await StatusAsync(client.PutAsJsonAsync(url, body, SyncWorld.Json));

    /// <summary>The status of a request and the <c>error</c> it answered with, if any.</summary>
    private static async Task<(HttpStatusCode Status, string? Error)> SendAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        var text = await response.Content.ReadAsStringAsync();
        string? error = null;
        if (!response.IsSuccessStatusCode && text.StartsWith('{'))
            error = JsonDocument.Parse(text).RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        return (response.StatusCode, error);
    }

    /// <summary>A draft order of the items, made on the server for the store's POS location, as the API shows it.</summary>
    private async Task<PurchaseOrderDetail> OrderOnServerAsync(HttpClient reader, Guid store, Guid? supplierId, IReadOnlyList<OrderItem> items)
    {
        Guid location;
        await using (var db = world.ServerData.Create())
            location = await db.Locations.AsNoTracking().Where(l => l.StoreId == store && l.Code == "POS").Select(l => l.Id).SingleAsync();
        var po = await new PurchasingService(world.ServerData.For(new StationIdentity(store, 0), null)).CreateFromItemsAsync(location, supplierId, "Server order", items);
        return await GetAsync<PurchaseOrderDetail>(reader, $"/api/purchase-orders/{po.Id}");
    }

    /// <summary>Two stores; store A has a till with a supplier's items (one kept in batches) sent to the server.</summary>
    private async Task<(StoreInfo A, StoreInfo B, Till Till, Item Milk, Item Rice, Supplier Supplier)> CompanyAsync()
    {
        var a = await world.AddStoreAsync();
        var b = await world.AddStoreAsync();
        var supplier = await new PartyService(world.ServerData).SaveSupplierAsync(new Supplier { Company = "Dairy " + Guid.NewGuid().ToString("N")[..6], LeadTimeDays = 2 });
        var till = await world.NewJoinedTillAsync(a.Id);
        var at = await till.SessionAsync();
        var milk = await till.Inventory.SaveAsync(new Item
        {
            LocationId = at.LocationId, Plu = "MILK" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), Name = "Milk", Price1 = 3m, LastCost = 2m, AverageCost = 2m,
            TracksBatches = true, ShelfLifeDays = 7, SupplierId = supplier.Id,
        }, "DAIRY", null, supplier.Company);
        var rice = await till.Inventory.SaveAsync(new Item
        {
            LocationId = at.LocationId, Plu = "RICE" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), Name = "Rice", Price1 = 9m, LastCost = 6m, AverageCost = 6m,
            SupplierId = supplier.Id, ReorderPoint = 5, OnHand = 2,
        }, "DRY", null, supplier.Company);
        await till.SyncAsync();
        return (a, b, till, milk, rice, supplier);
    }

    [SkippableFact]
    public async Task A_requisition_is_approved_and_ordered_on_the_web_and_the_order_received_with_batches_reaches_the_till()
    {
        Require();
        var (a, b, till, milk, rice, supplier) = await CompanyAsync();
        using var adminA = await world.AdminOfAsync(a.Id);
        using var adminB = await world.AdminOfAsync(b.Id);
        using var viewer = await UserWithAsync(a.Id);
        using var clerk = await UserWithAsync(a.Id, Permissions.PurchasingManage, Permissions.InventoryBatches);

        // Requisitions: made by a clerk, approved by the administrator, ordered as one PO for the supplier.
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/requisitions")));
        var made = await PostAsync<RequisitionDetail>(clerk, "/api/requisitions", new RequisitionRequest(null, null, Today.AddDays(5), "Weekly",
            [new RequisitionLineRequest(milk.Id, null, 24, null, null, null, null), new RequisitionLineRequest(null, rice.Plu, 10, null, null, null, null)]));
        Assert.Equal((RequisitionStatus.Draft, 2, 108m), (made.Status, made.Lines.Count, made.Total));   // 24 × 2 + 10 × 6
        Assert.All(made.Lines, l => Assert.Equal(supplier.Id, l.SupplierId));
        var submitted = await PostAsync<RequisitionDetail>(clerk, $"/api/requisitions/{made.Id}/submit", new SupplyVersionRequest(made.Version));
        Assert.Equal(RequisitionStatus.Submitted, submitted.Status);
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(clerk, $"/api/requisitions/{made.Id}/submit", new SupplyVersionRequest(made.Version)));
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(clerk, $"/api/requisitions/{made.Id}/approve", new SupplyVersionRequest(submitted.Version)));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/requisitions/{made.Id}")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminB.GetAsync($"/api/requisitions?storeId={a.Id}")));
        Assert.Equal(0, (await GetAsync<PageOf<RequisitionListRow>>(adminB, "/api/requisitions")).Total);
        var approved = await PostAsync<RequisitionDetail>(adminA, $"/api/requisitions/{made.Id}/approve", new SupplyVersionRequest(submitted.Version, "OK"));
        Assert.Equal((RequisitionStatus.Approved, "OK"), (approved.Status, approved.DecisionNote));
        var orders = await PostAsync<OrdersMade>(clerk, $"/api/requisitions/{made.Id}/order", new SupplyVersionRequest(approved.Version));
        var order = Assert.Single(orders.Orders);
        Assert.Equal(supplier.Company, order.Supplier);
        Assert.True(order.Number > ApiRequest.ServerStation * NumberCounter.NumbersPerStation);
        Assert.All((await GetAsync<RequisitionDetail>(clerk, $"/api/requisitions/{made.Id}")).Lines, l => Assert.Equal(order.Number, l.PurchaseOrderNumber));

        // The order: listed for store A only, posted, then received with the milk in two batches.
        var list = await GetAsync<PageOf<PurchaseOrderListRow>>(adminA, "/api/purchase-orders");
        Assert.Contains(list.Items, p => p.Id == order.Id && p.Status == PurchaseOrderStatus.Draft);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/purchase-orders/{order.Id}")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/purchase-orders")));
        var po = await GetAsync<PurchaseOrderDetail>(clerk, $"/api/purchase-orders/{order.Id}");
        po = await PostAsync<PurchaseOrderDetail>(clerk, $"/api/purchase-orders/{order.Id}/post", new SupplyVersionRequest(po.Version));
        Assert.Equal(PurchaseOrderStatus.Posted, po.Status);
        var milkLine = po.Lines.Single(l => l.ItemId == milk.Id);
        var riceLine = po.Lines.Single(l => l.ItemId == rice.Id);
        Assert.True(milkLine.TracksBatches);
        Assert.Equal(7, milkLine.ShelfLifeDays);
        // Without the batches of the milk, the receipt is refused; with an old version it is a conflict.
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(clerk, $"/api/purchase-orders/{order.Id}/receive",
            new SupplyReceiveRequest(po.Version, 0, [new ReceiveLineRequest(milkLine.Id, 24, null, null)])));
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(clerk, $"/api/purchase-orders/{order.Id}/receive",
            new SupplyReceiveRequest(made.Version, 0, [new ReceiveLineRequest(riceLine.Id, 10, null, null)])));
        var received = await PostAsync<PurchaseOrderDetail>(clerk, $"/api/purchase-orders/{order.Id}/receive", new SupplyReceiveRequest(po.Version, 0,
        [
            new ReceiveLineRequest(milkLine.Id, 24, 2m, [new ReceivedBatchInfo("WEB-1", 10, Today.AddDays(-1), null), new ReceivedBatchInfo("WEB-2", 14, null, Today)]),
            new ReceiveLineRequest(riceLine.Id, 10, 6.5m, null),
        ]));
        Assert.Equal(PurchaseOrderStatus.Received, received.Status);
        Assert.Equal(["WEB-1", "WEB-2"], received.Lines.Single(l => l.ItemId == milk.Id).Batches.Select(x => x.BatchNumber).Order());
        var putAway = await GetAsync<BinList>(clerk, $"/api/purchase-orders/{order.Id}/put-away");
        Assert.Equal(3, putAway.Rows.Count);

        // Batches on the web: stock by batch, expired and expiring.
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/batches")));
        var batches = await GetAsync<PageOf<BatchInfoRow>>(clerk, "/api/batches");
        Assert.Equal(2, batches.Items.Count(x => x.ItemId == milk.Id));
        var expired = await GetAsync<ExpiryReport>(clerk, "/api/batches/expired");
        Assert.Equal(("WEB-1", 10m, 20m), (Assert.Single(expired.Rows).BatchNumber, expired.Units, expired.Value));
        var expiring = await GetAsync<ExpiryReport>(clerk, "/api/batches/expiring?days=10");
        Assert.Equal(Today.AddDays(7), Assert.Single(expiring.Rows).ExpiresOn);   // WEB-2, from the shelf life
        Assert.Equal(0, (await GetAsync<PageOf<BatchInfoRow>>(adminB, "/api/batches")).Total);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminB.GetAsync($"/api/batches?storeId={a.Id}")));
        var web1 = batches.Items.Single(x => x.BatchNumber == "WEB-1");
        var changed = await PutAsync<BatchInfoRow>(clerk, $"/api/batches/{web1.Id}", new BatchRequest(web1.Version, null, "WEB-1", Today.AddDays(1), null, null, "Checked"));
        Assert.Equal((Today.AddDays(1), "Checked"), (changed.ExpiresOn, changed.Note));
        Assert.Equal(HttpStatusCode.Conflict, await PutStatusAsync(clerk, $"/api/batches/{web1.Id}", new BatchRequest(web1.Version, null, "WEB-1", null, null, null, null)));
        Assert.Equal(HttpStatusCode.NotFound, await PutStatusAsync(adminB, $"/api/batches/{web1.Id}", new BatchRequest(changed.Version, null, "WEB-1", null, null, null, null)));
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(clerk, $"/api/batches/{web1.Id}/adjust", new BatchAdjustRequest(-1, false, "Spilt")));
        var adjusted = await PostAsync<BatchInfoRow>(adminA, $"/api/batches/{web1.Id}/adjust", new BatchAdjustRequest(-1, false, "Spilt"));
        Assert.Equal(9m, adjusted.OnHand);
        Assert.Equal(2, (await GetAsync<List<BatchInfoRow>>(clerk, $"/api/items/{milk.Id}/batches")).Count);

        // The till gets the batches made on the web with the stock movements; its own receipt goes up with its batch.
        await till.SyncAsync();
        var atTill = await new BatchService(till.Data).OfItemAsync(milk.Id);
        Assert.Equal([("WEB-2", 14m), ("WEB-1", 9m)], atTill.Select(x => (x.BatchNumber, x.OnHand)).OrderBy(x => x.BatchNumber).Reverse());
        Assert.Equal(23m, (await till.Inventory.GetAsync(milk.Id))!.OnHand);
        var purchasing = new PurchasingService(till.Data);
        var own = await purchasing.CreateFromItemsAsync((await till.SessionAsync()).LocationId, supplier.Id, "At the till", [new OrderItem(milk.Id, 6)]);
        await purchasing.PostShipmentAsync(own.Id, [new ReceivedLine(own.Lines.Single().Id, 6, 2m, [new ReceivedBatch("TILL-1", 6, Today.AddDays(9))])]);
        await till.SyncAsync();
        var onServer = await GetAsync<List<BatchInfoRow>>(clerk, $"/api/items/{milk.Id}/batches");
        Assert.Equal(6m, onServer.Single(x => x.BatchNumber == "TILL-1").OnHand);
        await using (var db = world.ServerData.Create())
            Assert.Equal(29m, await db.Items.Where(i => i.Id == milk.Id).Select(i => i.OnHand).SingleAsync());
    }

    [SkippableFact]
    public async Task Batches_and_shelf_life_are_set_on_the_web_and_batches_for_an_item_not_kept_in_them_are_refused()
    {
        Require();
        var (a, _, _, milk, rice, supplier) = await CompanyAsync();
        using var adminA = await world.AdminOfAsync(a.Id);

        // The item API carries both fields; left out (an older client) they stay as they are; a shelf life below 0 is refused.
        var milkDetail = await GetAsync<ItemDetail>(adminA, $"/api/items/{milk.Id}");
        Assert.Equal((true, 7), (milkDetail.TracksBatches, milkDetail.ShelfLifeDays));
        var kept = await PutAsync<ItemDetail>(adminA, $"/api/items/{milk.Id}", milkDetail with { Name = "Milk, full cream", TracksBatches = null, ShelfLifeDays = null });
        Assert.Equal((true, 7, "Milk, full cream"), (kept.TracksBatches, kept.ShelfLifeDays, kept.Name));
        var riceDetail = await GetAsync<ItemDetail>(adminA, $"/api/items/{rice.Id}");
        Assert.Equal((false, 0), (riceDetail.TracksBatches, riceDetail.ShelfLifeDays));
        Assert.Equal(HttpStatusCode.BadRequest, await PutStatusAsync(adminA, $"/api/items/{rice.Id}", riceDetail with { TracksBatches = true, ShelfLifeDays = -1 }));
        var made = await PostAsync<ItemDetail>(adminA, $"/api/items?storeId={a.Id}", new ItemDetail(null, null, "YOG" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            "Yoghurt", null, null, null, null, 2m, 0, 0, 1m, 1m, true, false, false, 0, 0, 1, true, null, null, TracksBatches: true, ShelfLifeDays: 21));
        Assert.Equal((true, 21), (made.TracksBatches, made.ShelfLifeDays));

        // Rice received with a batch number: refused, saying why, and nothing is received.
        var po = await OrderOnServerAsync(adminA, a.Id, supplier.Id, [new OrderItem(rice.Id, 10, 6m)]);
        var line = po.Lines.Single();
        Assert.False(line.TracksBatches);
        var withBatch = new SupplyReceiveRequest(po.Version, 0, [new ReceiveLineRequest(line.Id, 10, null, [new ReceivedBatchInfo("R-1", 10, Today.AddDays(90), null)])]);
        var (status, error) = await SendAsync(adminA.PostAsJsonAsync($"/api/purchase-orders/{po.Id}/receive", withBatch, SyncWorld.Json));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal($"{rice.Plu} Rice is not kept in batches; turn that on for the item first.", error);
        Assert.Equal(PurchaseOrderStatus.Draft, (await GetAsync<PurchaseOrderDetail>(adminA, $"/api/purchase-orders/{po.Id}")).Status);

        // Turned on on the web, with its shelf life, the same receipt goes in with its batch.
        var turnedOn = await PutAsync<ItemDetail>(adminA, $"/api/items/{rice.Id}", riceDetail with { TracksBatches = true, ShelfLifeDays = 365 });
        Assert.Equal((true, 365), (turnedOn.TracksBatches, turnedOn.ShelfLifeDays));
        po = await GetAsync<PurchaseOrderDetail>(adminA, $"/api/purchase-orders/{po.Id}");
        Assert.Equal((true, 365), (po.Lines.Single().TracksBatches, po.Lines.Single().ShelfLifeDays));
        var received = await PostAsync<PurchaseOrderDetail>(adminA, $"/api/purchase-orders/{po.Id}/receive", withBatch with { Version = po.Version });
        Assert.Equal("R-1", received.Lines.Single().Batches.Single().BatchNumber);
    }

    [SkippableFact]
    public async Task An_order_is_posted_and_received_on_the_days_given_but_not_a_future_day_nor_one_in_a_locked_period()
    {
        Require();
        var (a, _, _, milk, rice, supplier) = await CompanyAsync();
        using var adminA = await world.AdminOfAsync(a.Id);
        var po = await OrderOnServerAsync(adminA, a.Id, supplier.Id, [new OrderItem(milk.Id, 6, 2m), new OrderItem(rice.Id, 10, 6m)]);

        // Posted four days ago, not tomorrow.
        var (status, error) = await SendAsync(adminA.PostAsJsonAsync($"/api/purchase-orders/{po.Id}/post", new PostOrderRequest(po.Version, Today.AddDays(1)), SyncWorld.Json));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("is in the future", error, StringComparison.Ordinal);
        var posted = await PostAsync<PurchaseOrderDetail>(adminA, $"/api/purchase-orders/{po.Id}/post", new PostOrderRequest(po.Version, Today.AddDays(-4)));
        Assert.Equal((PurchaseOrderStatus.Posted, Today.AddDays(-4)), (posted.Status, DateOnly.FromDateTime(posted.PostedAt!.Value)));

        var milkLine = posted.Lines.Single(l => l.ItemId == milk.Id);
        var riceLine = posted.Lines.Single(l => l.ItemId == rice.Id);
        SupplyReceiveRequest Receipt(DateOnly? on) => new(posted.Version, 0,
            [new ReceiveLineRequest(milkLine.Id, 6, null, [new ReceivedBatchInfo("DAY-1", 6, null, null)]), new ReceiveLineRequest(riceLine.Id, 10, null, null)], on);
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(adminA, $"/api/purchase-orders/{po.Id}/receive", Receipt(Today.AddDays(1))));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(adminA, $"/api/purchase-orders/{po.Id}/receive", Receipt(Today.AddDays(-5))));   // before it was ordered

        // Yesterday, while its ledger period is locked: refused, until the period is open again.
        var yesterday = Today.AddDays(-1);
        var ledger = new LedgerService(world.ServerData);
        await ledger.EnsureYearAsync(yesterday);
        var period = (await ledger.GetYearsAsync()).SelectMany(y => y.Periods).Single(p => p.StartDate <= yesterday && p.EndDate >= yesterday);
        await ledger.SetPeriodStatusAsync(period.Id, FiscalStatus.Locked);
        try
        {
            (status, error) = await SendAsync(adminA.PostAsJsonAsync($"/api/purchase-orders/{po.Id}/receive", Receipt(yesterday), SyncWorld.Json));
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("locked or closed ledger period", error, StringComparison.Ordinal);
        }
        finally
        {
            await ledger.SetPeriodStatusAsync(period.Id, FiscalStatus.Open);
        }

        var received = await PostAsync<PurchaseOrderDetail>(adminA, $"/api/purchase-orders/{po.Id}/receive", Receipt(yesterday));
        Assert.Equal((PurchaseOrderStatus.Received, yesterday), (received.Status, DateOnly.FromDateTime(received.ReceivedAt!.Value)));
        // The batch arrived yesterday, and its expiry counts the shelf life from then; the match reports it as received then.
        var batch = (await GetAsync<List<BatchInfoRow>>(adminA, $"/api/items/{milk.Id}/batches")).Single(x => x.BatchNumber == "DAY-1");
        Assert.Equal((yesterday, yesterday.AddDays(7)), (batch.ReceivedOn, batch.ExpiresOn));
        Assert.Contains(await GetAsync<List<MatchReportInfo>>(adminA, "/api/matching/received-not-billed"),
            r => r.PurchaseOrderId == po.Id && DateOnly.FromDateTime(r.ReceivedAt!.Value) == yesterday);
        await using var db = world.ServerData.Create();
        var moves = await db.StockMovements.AsNoTracking()
            .Where(m => m.Kind == StockMovementKind.Receipt && m.DocumentNumber == po.Number && (m.ItemId == milk.Id || m.ItemId == rice.Id)).ToListAsync();
        Assert.Equal(2, moves.Count);
        Assert.All(moves, m => Assert.Equal(yesterday, DateOnly.FromDateTime(m.At)));
    }

    [SkippableFact]
    public async Task Locations_are_added_renamed_and_marked_for_failed_goods_and_reach_the_till()
    {
        Require();
        var (a, b, till, _, _, _) = await CompanyAsync();
        using var adminA = await world.AdminOfAsync(a.Id);
        using var adminB = await world.AdminOfAsync(b.Id);
        using var clerk = await UserWithAsync(a.Id, Permissions.PurchasingManage);
        var pos = Assert.Single(await GetAsync<List<LocationInfo>>(clerk, $"/api/stores/{a.Id}/locations"));
        Assert.Equal(("POS", false), (pos.Code, pos.IsHold));

        // Adding and renaming need stores.manage and the store; a code has 1 to 3 characters, once per store, and stays.
        var hld = new LocationRequest(null, "hld", "Quality hold", true);
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(clerk, $"/api/stores/{a.Id}/locations", hld));
        Assert.Equal(HttpStatusCode.NotFound, await PostStatusAsync(adminB, $"/api/stores/{a.Id}/locations", hld));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(adminA, $"/api/stores/{a.Id}/locations", hld with { Code = "HOLD" }));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(adminA, $"/api/stores/{a.Id}/locations", hld with { Code = "pos" }));
        var hold = await PostAsync<LocationInfo>(adminA, $"/api/stores/{a.Id}/locations", hld);
        Assert.Equal(("HLD", "Quality hold", true, 0), (hold.Code, hold.Name, hold.IsHold, hold.Items));
        var rename = new LocationRequest(hold.Version, "HLD", "Quarantine", null);
        Assert.Equal(HttpStatusCode.Forbidden, await PutStatusAsync(clerk, $"/api/locations/{hold.Id}", rename));
        Assert.Equal(HttpStatusCode.NotFound, await PutStatusAsync(adminB, $"/api/locations/{hold.Id}", rename));
        Assert.Equal(HttpStatusCode.BadRequest, await PutStatusAsync(adminA, $"/api/locations/{hold.Id}", rename with { Code = "QH" }));
        var renamed = await PutAsync<LocationInfo>(adminA, $"/api/locations/{hold.Id}", rename);
        Assert.Equal(("HLD", "Quarantine", true), (renamed.Code, renamed.Name, renamed.IsHold));
        Assert.Equal(HttpStatusCode.Conflict, await PutStatusAsync(adminA, $"/api/locations/{hold.Id}", rename with { Name = "Old" }));
        // HLD comes first by code, yet lists and new items stay at the POS location.
        var reorder = await GetAsync<JsonElement>(clerk, $"/api/reorder?storeId={a.Id}");
        Assert.Equal(pos.Id, reorder.GetProperty("locationId").GetGuid());
        var item = await PostAsync<ItemDetail>(adminA, $"/api/items?storeId={a.Id}", new ItemDetail(null, null, "BUN" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            "Bun", null, null, null, null, 1m, 0, 0, 0, 0, true, false, false, 0, 0, 1, true, null, null));
        Assert.Equal(pos.Id, item.LocationId);

        // An output inspection that holds failed goods without naming where moves them to the hold location.
        var onServer = world.ServerData.For(new StationIdentity(a.Id, ProductionService.ServerStation), null);
        string flourCode = "FL" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), cakeCode = "CK" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await new InventoryService(onServer).SaveAsync(new Item { LocationId = pos.Id, Plu = flourCode, Name = "Flour", AverageCost = 1m, LastCost = 1m, OnHand = 20 }, null, null, null);
        await new InventoryService(onServer).SaveAsync(new Item { LocationId = pos.Id, Plu = cakeCode, Name = "Cake" }, null, null, null);
        await new ManufacturingService(onServer).SaveAsync(new BillOfMaterials { ProductCode = cakeCode, Lines = [new BomLine { ComponentCode = flourCode, Quantity = 1 }] });
        var production = new ProductionService(onServer);
        var order = await production.CreateAsync(new NewProductionOrder(cakeCode, 10, pos.Id, pos.Id));
        await production.ReleaseAsync(order.Id);
        await production.IssueAsync(order.Id, flourCode, 10);
        await production.ReceiveAsync(order.Id, 10, complete: true);
        var inspection = await PostAsync<InspectionInfo>(adminA, $"/api/production-orders/{order.Id}/inspect",
            new OutputInspectionRequest(10, 3, InspectionDisposition.Hold, null, [], "Burnt"));
        Assert.Equal((InspectionDisposition.Hold, "HLD"), (inspection.Disposition, inspection.HoldLocation));
        await using (var db = world.ServerData.Create())
        {
            Assert.Equal(7m, await db.Items.Where(i => i.LocationId == pos.Id && i.Plu == cakeCode).Select(i => i.OnHand).SingleAsync());
            Assert.Equal(3m, await db.Items.Where(i => i.LocationId == hold.Id && i.Plu == cakeCode).Select(i => i.OnHand).SingleAsync());
        }
        Assert.Equal(1, (await GetAsync<List<LocationInfo>>(clerk, $"/api/stores/{a.Id}/locations")).Single(l => l.Id == hold.Id).Items);

        // Locations are master data: the new one, its name and the hold mark reach the store's till at its next download.
        await till.SyncAsync();
        await using (var tillDb = till.Data.Create())
        {
            var atTill = await tillDb.Locations.AsNoTracking().SingleAsync(l => l.Id == hold.Id);
            Assert.Equal(("HLD", "Quarantine"), (atTill.Code, atTill.Name));
        }
        Assert.Equal(hold.Id, await new LocationService(till.Data).HoldLocationAsync(a.Id));

        // The mark comes off again.
        var unmarked = await PutAsync<LocationInfo>(adminA, $"/api/locations/{hold.Id}", new LocationRequest(renamed.Version, null, "Quarantine", false));
        Assert.False(unmarked.IsHold);
        Assert.Null(await new LocationService(world.ServerData).HoldLocationAsync(a.Id));
    }

    [SkippableFact]
    public async Task Bins_are_kept_per_location_and_give_count_sheets_and_pick_lists()
    {
        Require();
        var (a, b, _, milk, rice, _) = await CompanyAsync();
        using var adminA = await world.AdminOfAsync(a.Id);
        using var adminB = await world.AdminOfAsync(b.Id);
        using var viewer = await UserWithAsync(a.Id, Permissions.InventoryView);
        Guid location;
        await using (var db = world.ServerData.Create())
            location = await db.Locations.AsNoTracking().Where(l => l.StoreId == a.Id).Select(l => l.Id).FirstAsync();

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/bins")));
        var bin = await PostAsync<BinInfo>(adminA, "/api/bins", new BinRequest(null, location, "c-01", "Cold room", null));
        Assert.Equal(("C-01", "Cold room", 0), (bin.Code, bin.Zone, bin.Items));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(adminA, "/api/bins", new BinRequest(null, location, "C-01", null, null)));
        Assert.Equal(HttpStatusCode.NotFound, await PostStatusAsync(adminB, "/api/bins", new BinRequest(null, location, "X-01", null, null)));
        await PostAsync<object>(adminA, "/api/bins/assign", new BinAssignRequest([milk.Id], "C-01"));
        var bins = await GetAsync<List<BinInfo>>(adminA, $"/api/bins?locationId={location}");
        Assert.Equal(1, Assert.Single(bins).Items);
        Assert.Empty(await GetAsync<List<BinInfo>>(adminB, "/api/bins"));

        var renamed = await PutAsync<BinInfo>(adminA, $"/api/bins/{bin.Id}", new BinRequest(bin.Version, null, "C-02", "Cold room", "Top shelf"));
        Assert.Equal(("C-02", 1), (renamed.Code, renamed.Items));
        Assert.Equal(HttpStatusCode.Conflict, await PutStatusAsync(adminA, $"/api/bins/{bin.Id}", new BinRequest(bin.Version, null, "C-03", null, null)));
        var sheet = await GetAsync<BinList>(adminA, $"/api/bins/count-sheet?locationId={location}&zone=Cold%20room");
        Assert.Equal(milk.Plu, Assert.Single(sheet.Rows).Plu);
        var all = await GetAsync<BinList>(adminA, $"/api/bins/count-sheet?locationId={location}");
        Assert.Equal([milk.Plu, rice.Plu], all.Rows.Select(r => r.Plu));   // binned first
        Assert.Empty((await GetAsync<BinList>(adminA, $"/api/bins/pick-orders?locationId={location}")).Rows);
        Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(adminA.DeleteAsync($"/api/bins/{bin.Id}")));
        await PostAsync<object>(adminA, "/api/bins/assign", new BinAssignRequest([milk.Id], null));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.DeleteAsync($"/api/bins/{bin.Id}")));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(adminA.DeleteAsync($"/api/bins/{bin.Id}")));
    }

    [SkippableFact]
    public async Task Bills_are_matched_held_lines_wait_for_approval_and_landed_costs_are_spread_and_posted()
    {
        Require();
        var (a, b, till, milk, rice, supplier) = await CompanyAsync();
        using var adminA = await world.AdminOfAsync(a.Id);
        using var adminB = await world.AdminOfAsync(b.Id);
        using var buyer = await UserWithAsync(a.Id, Permissions.PurchasingManage);

        // A received order at the till: 10 rice at 6.00.
        var purchasing = new PurchasingService(till.Data);
        var po = await purchasing.CreateFromItemsAsync((await till.SessionAsync()).LocationId, supplier.Id, "Rice", [new OrderItem(rice.Id, 10, 6m)]);
        await purchasing.PostOrderAsync(po.Id);
        po = (await purchasing.GetAsync(po.Id))!;
        await purchasing.PostShipmentAsync(po.Id, [new ReceivedLine(po.Lines.Single().Id, 10, 6m)]);
        await till.SyncAsync();
        var line = po.Lines.Single();

        // Nothing billed yet: received but not billed.
        Assert.Contains(await GetAsync<List<MatchReportInfo>>(buyer, "/api/matching/received-not-billed"), r => r.PurchaseOrderId == po.Id && r.Received == 10);
        Assert.DoesNotContain(await GetAsync<List<MatchReportInfo>>(adminB, "/api/matching/received-not-billed"), r => r.PurchaseOrderId == po.Id);

        // The supplier's bill at 7.00 each, entered and posted in payables (phase E1), is matched as it is posted and held;
        // the buyer may not accept it, the administrator may.
        var draft = await PostAsync<BillDetail>(adminA, "/api/finance/bills", new BillRequest(SupplierBillKind.Bill, supplier.Id, a.Id, Today, null, "INV-77", null, 1, null, null,
            [new BillLineRequest(null, rice.Id, "Rice", 10, 7m, 0, 0, po.Id, line.Id)]));
        var bill = await PostAsync<BillDetail>(adminA, $"/api/finance/bills/{draft.Id}/post", new { });
        Assert.Equal(SupplierBillStatus.Posted, bill.Status);
        Assert.Single((await GetAsync<PageOf<MatchRow>>(buyer, "/api/matching?status=held")).Items, m => m.BillId == bill.Id);
        var run = await PostAsync<MatchRun>(buyer, "/api/matching/run", new { });
        Assert.Equal((1, 1), (run.Lines, run.Held));
        var held = Assert.Single((await GetAsync<PageOf<MatchRow>>(buyer, "/api/matching?status=held")).Items, m => m.BillId == bill.Id);
        // A held bill is not paid until it is accepted.
        var bank = await PostAsync<BankAccountRow>(adminA, "/api/finance/bank-accounts",
            new BankAccountRequest("Supply bank " + Guid.NewGuid().ToString("N")[..5], BankAccountKind.Bank, "HBL", "002", null, null, null, a.Id, 0, null, true));
        var pay = new PaymentRequest(bank.Id, Today, [new BillToPayRequest(bill.Id, 70m)], 0m);
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(adminA, "/api/finance/payments", pay));
        Assert.Equal((10m, 6m, 7m, 10m), (held.QuantityBilled, held.ReceivedCost, held.BilledCost, held.PriceDifference));
        Assert.Equal(0, (await GetAsync<PageOf<MatchRow>>(adminB, "/api/matching")).Total);
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(buyer, $"/api/matching/{held.Id}/accept", new SupplyVersionRequest(held.Version)));
        Assert.Equal(HttpStatusCode.NotFound, await PostStatusAsync(adminB, $"/api/matching/{held.Id}/accept", new SupplyVersionRequest(held.Version)));
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(adminA, $"/api/matching/{held.Id}/accept", new SupplyVersionRequest(Guid.NewGuid().ToString("N"))));
        var accepted = await PostAsync<MatchRow>(adminA, $"/api/matching/{held.Id}/accept", new SupplyVersionRequest(held.Version, "New price list"));
        Assert.Equal((BillMatchStatus.Accepted, "New price list"), (accepted.Status, accepted.DecisionNote));
        Assert.Equal(70m, (await PostAsync<PaymentDetail>(adminA, "/api/finance/payments", pay)).Amount);
        await using (var db = world.ServerData.Create())
        {
            var entry = await db.JournalEntries.AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.Source == $"match:{line.Id}");
            Assert.Equal((10m, 10m), (entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit)));
            Assert.All(entry.Lines, l => Assert.Equal(a.Id, l.StoreId));
        }
        Assert.DoesNotContain(await GetAsync<List<MatchReportInfo>>(buyer, "/api/matching/received-not-billed"), r => r.PurchaseOrderId == po.Id);
        Assert.DoesNotContain(await GetAsync<List<MatchReportInfo>>(buyer, "/api/matching/billed-not-received"), r => r.PurchaseOrderId == po.Id);

        // Landed costs: freight over the order, by value; only those who may approve purchases make and post vouchers.
        var receipts = await GetAsync<List<ReceiptChoiceInfo>>(buyer, "/api/landed-costs/receipts");
        Assert.Contains(receipts, r => r.PurchaseOrderId == po.Id && r.Value == 60m);
        var request = new LandedCostRequest(null, a.Id, Today, LandedCostBasis.Value, "FR-1", null, [new LandedChargeInfo(LandedCostChargeKind.Freight, "Truck", 5m)], [po.Id]);
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(buyer, "/api/landed-costs", request));
        Assert.Equal(HttpStatusCode.NotFound, await PostStatusAsync(adminB, "/api/landed-costs", request));
        var voucher = await PostAsync<LandedCostDetail>(adminA, "/api/landed-costs", request);
        Assert.Equal((LandedCostStatus.Draft, 5m, 5m), (voucher.Status, voucher.Total, Assert.Single(voucher.Allocations).Share));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/landed-costs/{voucher.Id}")));
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(adminA, $"/api/landed-costs/{voucher.Id}/post", new SupplyVersionRequest(Guid.NewGuid().ToString("N"))));
        var posted = await PostAsync<LandedCostDetail>(adminA, $"/api/landed-costs/{voucher.Id}/post", new SupplyVersionRequest(voucher.Version));
        Assert.Equal(LandedCostStatus.Posted, posted.Status);
        Assert.Equal(5m, posted.Allocations.Single().ToInventory + posted.Allocations.Single().ToCostOfGoods);
        Assert.Contains((await GetAsync<PageOf<LandedCostListRow>>(buyer, "/api/landed-costs?status=posted")).Items, v => v.Id == voucher.Id);
        Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(adminA.DeleteAsync($"/api/landed-costs/{voucher.Id}")));
        await using (var db = world.ServerData.Create())
            Assert.True(await db.JournalEntries.AnyAsync(e => e.Source == $"landed:{voucher.Id}"));
    }

    [SkippableFact]
    public async Task Reorder_suggestions_become_orders_or_requisitions_and_suppliers_are_reported_with_the_settings_kept_per_store()
    {
        Require();
        var (a, b, _, _, rice, supplier) = await CompanyAsync();
        using var adminA = await world.AdminOfAsync(a.Id);
        using var adminB = await world.AdminOfAsync(b.Id);
        using var buyer = await UserWithAsync(a.Id, Permissions.PurchasingManage);
        using var viewer = await UserWithAsync(a.Id);

        // Rice is below its reorder point: it is suggested, and turned into an order and a requisition.
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync($"/api/reorder?storeId={a.Id}")));
        var suggestions = await GetAsync<ReorderAnswer>(buyer, "/api/reorder");
        var row = Assert.Single(suggestions.Rows, r => r.ItemId == rice.Id);
        Assert.True(row.SuggestedPieces >= 3);
        Assert.Equal(supplier.Id, row.SupplierId);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminB.GetAsync($"/api/reorder?storeId={a.Id}")));
        var orders = await PostAsync<OrdersMade>(buyer, "/api/reorder/orders", new ReorderRequest(null, [new ReorderLineRequest(rice.Id, row.SuggestedPieces, supplier.Id)]));
        Assert.Equal(supplier.Company, Assert.Single(orders.Orders).Supplier);
        var requisition = await PostAsync<RequisitionDetail>(buyer, "/api/reorder/requisition", new ReorderRequest(suggestions.LocationId,
            [new ReorderLineRequest(rice.Id, row.SuggestedPieces, null)], Submit: true));
        Assert.Equal(RequisitionStatus.Submitted, requisition.Status);
        Assert.Equal(HttpStatusCode.NotFound, await PostStatusAsync(adminB, "/api/reorder/orders", new ReorderRequest(suggestions.LocationId, [new ReorderLineRequest(rice.Id, 1, null)])));

        // Supplier performance needs the reports; it covers the store's received orders.
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(buyer.GetAsync("/api/supplier-performance")));
        var report = await GetAsync<PerformanceAnswer>(adminA, $"/api/supplier-performance?from={Today.AddDays(-7):yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        Assert.Equal((Today.AddDays(-7), Today), (report.From, report.To));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminB.GetAsync($"/api/supplier-performance?storeId={a.Id}")));

        // Settings per store: read with purchasing, changed with the store setup; an old version is a conflict.
        var settings = await GetAsync<SupplySettingsInfo>(buyer, "/api/supply/settings");
        Assert.Equal((a.Id, true, "new"), (settings.StoreId, settings.BlockExpiredSales, settings.Version));
        Assert.Equal(HttpStatusCode.Forbidden, await PutStatusAsync(buyer, "/api/supply/settings", settings));
        var saved = await PutAsync<SupplySettingsInfo>(adminA, "/api/supply/settings", settings with { BlockExpiredSales = false, PriceTolerancePercent = 3 });
        Assert.Equal((false, 3m), (saved.BlockExpiredSales, saved.PriceTolerancePercent));
        Assert.Equal(HttpStatusCode.Conflict, await PutStatusAsync(adminA, "/api/supply/settings", settings));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/supply/settings?storeId={a.Id}")));
        Assert.True((await GetAsync<SupplySettingsInfo>(adminB, "/api/supply/settings")).BlockExpiredSales);

        // Approval limits are set for every store: by the super administrator once there are several stores.
        using var super = await world.SuperAsync();
        var limits = await GetAsync<List<ApprovalLimitRow>>(adminA, "/api/supply/approval-limits");
        var manager = limits.Single(l => l.Role == Permissions.ManagerRole);
        Assert.Equal(HttpStatusCode.Forbidden, await PutStatusAsync(adminA, $"/api/supply/approval-limits/{manager.RoleId}", new ApprovalLimitRequest(1000)));
        var after = await PutAsync<List<ApprovalLimitRow>>(super, $"/api/supply/approval-limits/{manager.RoleId}", new ApprovalLimitRequest(1000));
        Assert.Equal(1000m, after.Single(l => l.RoleId == manager.RoleId).MaxAmount);
        await PutAsync<List<ApprovalLimitRow>>(super, $"/api/supply/approval-limits/{manager.RoleId}", new ApprovalLimitRequest(null));
    }

    [SkippableFact]
    public async Task A_rejected_requisition_says_why_is_changed_and_sent_again_and_the_role_limit_holds()
    {
        Require();
        var (a, b, _, milk, rice, _) = await CompanyAsync();
        using var adminB = await world.AdminOfAsync(b.Id);
        using var clerk = await UserWithAsync(a.Id, Permissions.PurchasingManage, Permissions.InventoryBatches);
        using var approver = await UserWithAsync(a.Id, Permissions.PurchasingManage, Permissions.PurchasingApprove);

        var made = await PostAsync<RequisitionDetail>(clerk, $"/api/requisitions?storeId={a.Id}", new RequisitionRequest(null, null, null, null,
            [new RequisitionLineRequest(rice.Id, null, 100, null, null, null, null)]));
        Assert.Equal(600m, made.Total);
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(adminB, $"/api/requisitions?storeId={a.Id}", new RequisitionRequest(null, null, null, null, [])));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(approver, $"/api/requisitions/{made.Id}/approve", new SupplyVersionRequest(made.Version)));   // not sent yet
        var sent = await PostAsync<RequisitionDetail>(clerk, $"/api/requisitions/{made.Id}/submit", new SupplyVersionRequest(made.Version));

        // A rejection needs the reason; the clerk sees it, changes the requisition and sends it again.
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(approver, $"/api/requisitions/{made.Id}/reject", new SupplyVersionRequest(sent.Version)));
        var rejected = await PostAsync<RequisitionDetail>(approver, $"/api/requisitions/{made.Id}/reject", new SupplyVersionRequest(sent.Version, "Too much rice"));
        Assert.Equal((RequisitionStatus.Rejected, "Too much rice"), (rejected.Status, rejected.DecisionNote));
        var changed = await PutAsync<RequisitionDetail>(clerk, $"/api/requisitions/{made.Id}", new RequisitionRequest(rejected.Version, null, Today.AddDays(3), "Less",
            [new RequisitionLineRequest(rice.Id, null, 40, null, null, null, null), new RequisitionLineRequest(milk.Id, null, 12, null, null, 2.5m, null)]));
        Assert.Equal((RequisitionStatus.Draft, 270m, 2), (changed.Status, changed.Total, changed.Lines.Count));   // 40 × 6 + 12 × 2.50
        Assert.Equal(HttpStatusCode.Conflict, await PutStatusAsync(clerk, $"/api/requisitions/{made.Id}", new RequisitionRequest(rejected.Version, null, null, null, [])));
        sent = await PostAsync<RequisitionDetail>(clerk, $"/api/requisitions/{made.Id}/submit", new SupplyVersionRequest(changed.Version));

        // The approver's role may approve up to 200: this one is refused, until the limit is raised.
        using var super = await world.SuperAsync();
        var me = await GetAsync<JsonElement>(approver, "/api/auth/me");
        var approverRole = (await new SetupService(world.ServerData).GetRolesAsync()).Single(r => r.Name == me.GetProperty("role").GetString()).Id;
        await PutAsync<List<ApprovalLimitRow>>(super, $"/api/supply/approval-limits/{approverRole}", new ApprovalLimitRequest(200));
        using (var refused = await approver.PostAsJsonAsync($"/api/requisitions/{made.Id}/approve", new SupplyVersionRequest(sent.Version), SyncWorld.Json))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("200.00", await refused.Content.ReadAsStringAsync());
        }
        await PutAsync<List<ApprovalLimitRow>>(super, $"/api/supply/approval-limits/{approverRole}", new ApprovalLimitRequest(500));
        var approved = await PostAsync<RequisitionDetail>(approver, $"/api/requisitions/{made.Id}/approve", new SupplyVersionRequest(sent.Version));
        Assert.Equal(RequisitionStatus.Approved, approved.Status);
        Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(clerk.DeleteAsync($"/api/requisitions/{made.Id}")));   // approved: it stays

        // A draft is deleted; another store's administrator does not find it.
        var draft = await PostAsync<RequisitionDetail>(clerk, "/api/requisitions", new RequisitionRequest(null, null, null, null, [new RequisitionLineRequest(null, rice.Plu, 1, null, null, null, null)]));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.DeleteAsync($"/api/requisitions/{draft.Id}")));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(clerk.DeleteAsync($"/api/requisitions/{draft.Id}")));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(clerk.GetAsync($"/api/requisitions/{draft.Id}")));

        // Batches are added by hand only to items kept in batches.
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(clerk, "/api/batches", new BatchRequest(null, rice.Id, "R-1", null, null, null, null)));
        var added = await PostAsync<BatchInfoRow>(clerk, "/api/batches", new BatchRequest(null, milk.Id, "hand-1", Today.AddDays(4), Today, null, null));
        Assert.Equal(("HAND-1", 0m, 4), (added.BatchNumber, added.OnHand, added.DaysLeft));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(clerk, "/api/batches", new BatchRequest(null, milk.Id, "HAND-1", null, null, null, null)));
        await PutAsync<List<ApprovalLimitRow>>(super, $"/api/supply/approval-limits/{approverRole}", new ApprovalLimitRequest(null));
    }

    private sealed record ReorderAnswer(Guid LocationId, List<ReorderRow> Rows);

    private sealed record PerformanceAnswer(DateOnly From, DateOnly To, List<SupplierPerformanceRow> Rows);
}
