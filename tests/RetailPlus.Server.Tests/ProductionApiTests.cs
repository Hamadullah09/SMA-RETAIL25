using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Sync.Tests;
using static RetailPlus.Server.Tests.BackOffice;
using static RetailPlus.Server.Tests.WebClients;

namespace RetailPlus.Server.Tests;

/// <summary>
/// Manufacturing through the API, with tills of its own: work centres, bills and routings made on the web reach the till,
/// an order released on the web is worked on the till's shop floor and its movements and reports come back up and are
/// posted to the ledger; planning, capacity, quality and the reports; and for every group the permission, the store
/// scope and the 409 on an old version.
/// </summary>
public sealed class ProductionApiTests(SyncWorld world) : IClassFixture<SyncWorld>
{
    private static readonly string[] PostedSteps = ["conversion", "issue", "receipt"];
    private static readonly string[] PageFields = ["items", "page", "pageSize", "total"];
    private static readonly string[] ReportInfoFields = ["group", "key", "title", "usesDates"];

    private void Require() => Skip.IfNot(world.Available, world.UnavailableReason);

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<Guid> PosOfAsync(Guid store)
    {
        await using var db = world.ServerData.Create();
        return await db.Locations.AsNoTracking().Where(l => l.StoreId == store && l.Code == "POS").Select(l => l.Id).SingleAsync();
    }

    /// <summary>A user of a store whose role has only these permissions, signed in with the CSRF header.</summary>
    private async Task<HttpClient> UserWithAsync(Guid? store, params string[] permissions)
    {
        var setup = new SetupService(world.ServerData);
        var role = await setup.SaveRoleAsync(new Role { Name = "Role " + Guid.NewGuid().ToString("N")[..6], Level = 1 }, permissions);
        var name = "user-" + Guid.NewGuid().ToString("N")[..8];
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

    [SkippableFact]
    public async Task An_order_made_on_the_web_is_worked_at_the_till_and_its_costs_reach_the_ledger()
    {
        Require();
        var storeA = await world.AddStoreAsync();
        var storeB = await world.AddStoreAsync();
        var till = await world.NewJoinedTillAsync(storeA.Id);
        var at = await till.SessionAsync();
        string flourCode = Unique("FL"), breadCode = Unique("BR");
        var flour = await till.Inventory.SaveAsync(new Item { LocationId = at.LocationId, Plu = flourCode, Name = "Flour", AverageCost = 2m, LastCost = 2m, OnHand = 50 }, null, null, null);
        var bread = await till.Inventory.SaveAsync(new Item { LocationId = at.LocationId, Plu = breadCode, Name = "Bread", Price1 = 9m }, null, null, null);
        await till.SyncAsync();
        var pos = await PosOfAsync(storeA.Id);

        using var super = await world.SuperAsync();
        using var adminA = await world.AdminOfAsync(storeA.Id);
        using var adminB = await world.AdminOfAsync(storeB.Id);

        // A work centre (a store's: its admin makes it) and a bill (company-wide: only the super administrator, with two stores).
        var oven = await PostAsync<WorkCentreInfo>(adminA, "/api/work-centres",
            new WorkCentreRequest(null, pos, Unique("OV"), "Oven", 60, 30, 10, 8, 6, true, null));
        Assert.Equal(storeA.Id, oven.StoreId);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminB.PutAsJsonAsync($"/api/work-centres/{oven.Id}", new WorkCentreRequest(oven.Version, pos, oven.Code, "Mine", 1, 1, 1, 8, 6, true, null))));
        Assert.DoesNotContain(await GetAsync<List<WorkCentreInfo>>(adminB, "/api/work-centres"), w => w.Id == oven.Id);
        var bomBody = new BomDetail(null, null, breadCode, null, 0, true, null, null, 1, 0, null,
            [new BomLineInfo(flourCode, null, 2, 0, IssueMethod.Manual, 10)], [], [new RoutingOperationInfo(10, oven.Id, null, "Bake", 0, 6, "Bake at 200 degrees")]);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.PostAsJsonAsync("/api/boms", bomBody, SyncWorld.Json)));
        var bom = await PostAsync<BomDetail>(super, "/api/boms", bomBody);
        Assert.Equal((1, "Bread"), (bom.Revision, bom.ProductName));
        var renamed = await PutAsync<BomDetail>(super, $"/api/boms/{bom.Id}", bom with { Notes = "Daily loaf" });
        Assert.Equal(HttpStatusCode.Conflict, await StatusAsync(super.PutAsJsonAsync($"/api/boms/{bom.Id}", bom with { Notes = "Stale" }, SyncWorld.Json)));
        var cost = await GetAsync<CostRollUp>(adminA, $"/api/boms/{bom.Id}/cost?locationId={pos}&quantity=10");
        Assert.Equal(40m, cost.Material); // 2 kg x 10 x 2.00
        Assert.Equal(60m + 30m + 10m, cost.Labour + cost.Machine + cost.Overhead); // one hour baking
        Assert.Equal(2, (await GetAsync<List<ExplodedLine>>(adminA, $"/api/boms/{bom.Id}/explode")).Single().QuantityPerUnit);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/boms/{bom.Id}/cost?locationId={pos}")));

        // The order, made and released on the web.
        var order = await PostAsync<ProductionOrderDetail>(adminA, "/api/production-orders",
            new ProductionOrderRequest(null, breadCode, 10, pos, pos, null, DateOnly.FromDateTime(DateTime.Today), null, 2, null, null));
        Assert.Equal((ProductionOrderStatus.Planned, renamed.Id), (order.Status, order.BillOfMaterialsId!.Value));
        Assert.True(order.Number > ProductionService.ServerStation * NumberCounter.NumbersPerStation);
        Assert.Equal(HttpStatusCode.Conflict, await StatusAsync(adminA.PutAsJsonAsync($"/api/production-orders/{order.Id}",
            new ProductionOrderRequest(Guid.NewGuid().ToString("N"), breadCode, 12, pos, pos, null, null, null, 2, null, null), SyncWorld.Json)));
        order = await PutAsync<ProductionOrderDetail>(adminA, $"/api/production-orders/{order.Id}",
            new ProductionOrderRequest(order.Version, breadCode, 10, pos, pos, null, DateOnly.FromDateTime(DateTime.Today), null, 1, null, "Rush"));
        Assert.Equal((1, "Rush"), (order.Priority, order.Notes));
        var released = await PostAsync<ReleaseResponse>(adminA, $"/api/production-orders/{order.Id}/release", new { });
        Assert.Equal(ProductionOrderStatus.Released, released.Order.Status);
        Assert.Empty(released.Shortages);
        Assert.Equal(20m, released.Order.Lines.Single().Planned);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/production-orders/{order.Id}")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminB.GetAsync($"/api/production-orders?storeId={storeA.Id}")));
        Assert.Equal(0, (await GetAsync<PageOf<ProductionOrderListRow>>(adminB, "/api/production-orders")).Total);
        var board = await GetAsync<List<ProductionBoardColumn>>(adminA, "/api/production-orders/board");
        Assert.Contains(board.Single(c => c.Status == ProductionOrderStatus.Released).Orders, o => o.Id == order.Id);

        // The till gets the bill, the work centre and the released order with its copy of the bill; the floor works it offline.
        await till.SyncAsync();
        var floor = new ProductionService(till.Data);
        Assert.Contains(await floor.FloorOrdersAsync(at.LocationId), o => o.Id == order.Id);
        await using (var db = till.Data.Create())
        {
            Assert.True(await db.BillsOfMaterials.AnyAsync(b => b.Id == bom.Id!.Value));
            Assert.True(await db.WorkCentres.AnyAsync(w => w.Id == oven.Id));
            Assert.Equal("Bake at 200 degrees", (await db.ProductionOrderOperations.SingleAsync(o => o.ProductionOrderId == order.Id)).Instructions);
        }
        await floor.StartOperationAsync(order.Id, 10);
        await floor.IssueAsync(order.Id, flourCode, 20);
        await floor.ReportAsync(order.Id, 10, 60, 10, 0, operatorName: "baker");
        await floor.ReceiveAsync(order.Id, 10, complete: true);
        await till.SyncAsync();

        // Up on the server: completed, with what the floor did, and posted to the ledger after the upload.
        var detail = await GetAsync<ProductionOrderDetail>(adminA, $"/api/production-orders/{order.Id}");
        Assert.Equal((ProductionOrderStatus.Completed, 10m), (detail.Status, detail.Received));
        Assert.Equal("baker", detail.Reports.Single().UserName);
        Assert.Equal(40m + 100m, detail.Costs!.Received); // the flour and an hour of baking
        Assert.Equal(PostedSteps, detail.Postings!.Select(p => p.Step).Order().ToArray());
        Assert.Equal(50m - 20m, (await new InventoryService(world.ServerData).GetAsync(flour.Id))!.OnHand);
        Assert.Equal(10m, (await new InventoryService(world.ServerData).GetAsync(bread.Id))!.OnHand);

        // Closing needs the costing permission; a user without it does not see costs.
        using var floorOnly = await UserWithAsync(storeA.Id, Permissions.WebSignIn, Permissions.ProductionView, Permissions.ProductionFloor);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(floorOnly.PostAsJsonAsync($"/api/production-orders/{order.Id}/close", new { })));
        var seen = await GetAsync<ProductionOrderDetail>(floorOnly, $"/api/production-orders/{order.Id}");
        Assert.Null(seen.Costs);
        Assert.All(seen.Movements, m => Assert.Equal(0m, m.UnitCost));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(floorOnly.PostAsJsonAsync("/api/production-orders",
            new ProductionOrderRequest(null, breadCode, 1, pos, pos, null, null, null, 5, null, null), SyncWorld.Json)));
        var closed = await PostAsync<ProductionOrderDetail>(adminA, $"/api/production-orders/{order.Id}/close", new { });
        Assert.Equal(ProductionOrderStatus.Closed, closed.Status);
        Assert.Equal(0m, closed.Costs!.WorkInProgress);
        var posted = await PostAsync<PostCostsResponse>(adminA, "/api/production/post-costs", new { });
        Assert.Empty(posted.Problems);

        // A second order is worked from the web: issue by code, report, receive, and cancel refused once work is recorded.
        var second = await PostAsync<ProductionOrderDetail>(adminA, "/api/production-orders", new ProductionOrderRequest(null, breadCode, 2, pos, pos, null, null, null, 5, null, null));
        await PostAsync<ReleaseResponse>(adminA, $"/api/production-orders/{second.Id}/release", new { });
        await PostAsync<ProductionOrderDetail>(adminA, $"/api/production-orders/{second.Id}/operations/10/start", new { });
        await PostAsync<ProductionOrderDetail>(adminA, $"/api/production-orders/{second.Id}/issue-remaining", new IssueRemainingRequest(null));
        await PostAsync<ProductionOrderDetail>(adminA, $"/api/production-orders/{second.Id}/scrap", new ScrapRequest(flourCode, 0.5m, "Spilt"));
        await PostAsync<ProductionOrderDetail>(adminA, $"/api/production-orders/{second.Id}/report", new OperationReportRequest(10, 12, 2, 0, null));
        var received = await PostAsync<ProductionOrderDetail>(adminA, $"/api/production-orders/{second.Id}/receive", new ReceiveRequest(2, true, null));
        Assert.Equal((ProductionOrderStatus.Completed, 4.5m), (received.Status, received.Lines.Single().Done));
        using (var response = await adminA.PostAsJsonAsync($"/api/production-orders/{second.Id}/cancel", new CancelRequest("No")))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var issue = await adminA.PostAsJsonAsync($"/api/production-orders/{second.Id}/issue", new IssueRequest("NO-SUCH", 1, null));
        Assert.Equal(HttpStatusCode.BadRequest, issue.StatusCode);
        await using (var db = world.ServerData.Create())
            Assert.Equal(3, await db.JournalEntries.CountAsync(e => e.Source!.StartsWith($"prod:{second.Id:N}:")));
    }

    [SkippableFact]
    public async Task Planning_capacity_quality_and_reports_answer_per_store_and_permission()
    {
        Require();
        var storeA = await world.AddStoreAsync();
        var storeB = await world.AddStoreAsync();
        var pos = await PosOfAsync(storeA.Id);
        var onServer = world.ServerData.For(new StationIdentity(storeA.Id, ProductionService.ServerStation), null);
        string partCode = Unique("PT"), kitCode = Unique("KT");
        var inventory = new InventoryService(onServer);
        await inventory.SaveAsync(new Item { LocationId = pos, Plu = partCode, Name = "Part", AverageCost = 1m, LastCost = 1m, OnHand = 3, ReorderPoint = 0 }, null, null, null);
        await inventory.SaveAsync(new Item { LocationId = pos, Plu = kitCode, Name = "Kit", ReorderPoint = 5, ReorderQuantity = 10 }, null, null, null);
        using var super = await world.SuperAsync();
        using var adminA = await world.AdminOfAsync(storeA.Id);
        using var adminB = await world.AdminOfAsync(storeB.Id);
        var centre = await PostAsync<WorkCentreInfo>(adminA, "/api/work-centres", new WorkCentreRequest(null, pos, Unique("BN"), "Bench", 20, 0, 0, 8, 5, true, null));
        var bom = await PostAsync<BomDetail>(super, "/api/boms", new BomDetail(null, null, kitCode, null, 0, true, null, null, 1, 0, null,
            [new BomLineInfo(partCode, null, 2, 0, IssueMethod.Backflush, null)], [], [new RoutingOperationInfo(10, centre.Id, null, "Pack", 0, 30, null)]));
        var copy = await PostAsync<BomDetail>(super, $"/api/boms/{bom.Id}/copy", new CopyBomRequest(null));
        Assert.Equal((2, false), (copy.Revision, copy.IsActive));
        var routed = await PutAsync<BomDetail>(super, $"/api/boms/{copy.Id}/routing", new RoutingRequest(copy.Version, [new RoutingOperationInfo(10, centre.Id, null, "Pack", 5, 20, null)]));
        Assert.Equal(5m, routed.Operations.Single().SetupMinutes);
        var diff = await GetAsync<List<BomDifference>>(super, $"/api/boms/compare?from={bom.Id}&to={copy.Id}");
        Assert.Contains(diff, d => d.Part == "Operation");
        Assert.Contains(await GetAsync<List<BomListRow>>(adminA, $"/api/boms?search={kitCode}"), b => b.Id == copy.Id && !b.InForce);

        // Planning: the kit is below its reorder point, so 10 to make, and 20 parts for them (3 in stock) to buy.
        using var viewer = await UserWithAsync(storeA.Id, Permissions.WebSignIn, Permissions.ProductionView);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.PostAsJsonAsync("/api/mrp/run", new MrpRunRequest(storeA.Id, null, null, null), SyncWorld.Json)));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminB.PostAsJsonAsync("/api/mrp/run", new MrpRunRequest(storeA.Id, null, null, null), SyncWorld.Json)));
        var view = await PostAsync<MrpView>(adminA, "/api/mrp/run", new MrpRunRequest(null, pos, 30, 4));
        var make = view.Suggestions.Single(s => s.StockCode == kitCode);
        var buy = view.Suggestions.Single(s => s.StockCode == partCode);
        Assert.Equal((SuggestionKind.Make, 10m), (make.Kind, make.Quantity));
        Assert.Equal((SuggestionKind.Buy, 17m, 4), (buy.Kind, buy.Quantity, buy.LeadDays));
        Assert.Equal(view.Run!.Id, (await GetAsync<MrpView>(viewer, "/api/mrp/suggestions")).Run!.Id);
        Assert.Empty((await GetAsync<MrpView>(adminB, "/api/mrp/suggestions")).Suggestions);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.PostAsJsonAsync("/api/mrp/firm", new FirmRequest([make.Id], null), SyncWorld.Json)));
        var capacity = await GetAsync<List<CapacityInfo>>(viewer, "/api/capacity?suggestions=true&weeks=2");
        Assert.Equal(5m, capacity.Where(c => c.WorkCentreId == centre.Id).Sum(c => c.LoadHours)); // 10 kits x 30 minutes
        var firm = await PostAsync<FirmResult>(adminA, "/api/mrp/firm", new FirmRequest([make.Id, buy.Id], new Dictionary<Guid, decimal> { [buy.Id] = 20 }));
        Assert.Empty(firm.Problems);
        Assert.Equal(2, firm.Made.Count);
        var planned = (await GetAsync<PageOf<ProductionOrderListRow>>(adminA, $"/api/production-orders?status=Planned&search={kitCode}")).Items.Single();
        Assert.Equal(10m, planned.Quantity);
        var dismissed = await adminA.PostAsJsonAsync("/api/mrp/dismiss", new IdsRequest([make.Id]));
        Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode);

        // Lead times are company-wide.
        var supplier = await new PartyService(world.ServerData).SaveSupplierAsync(new Supplier { Company = Unique("Parts ") });
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.PutAsJsonAsync($"/api/mrp/lead-times/{supplier.Id}", new LeadTimeRequest(3))));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(super.PutAsJsonAsync($"/api/mrp/lead-times/{supplier.Id}", new LeadTimeRequest(3))));
        Assert.Equal(3, (await GetAsync<List<LeadTimeInfo>>(viewer, "/api/mrp/lead-times")).Single(t => t.SupplierId == supplier.Id).Days);

        // Quality: a plan, an order completed on the web and its output inspected (two failed, held back at another location).
        var hold = await new SetupService(onServer).AddLocationAsync("HLD", "Hold");
        var plan = await PostAsync<QualityPlanInfo>(super, "/api/quality/plans", new QualityPlanInfo(null, null, kitCode, "Kit checks", true, true, true, null,
            [new QualityCheckInfo(1, "Complete", QualityCheckKind.PassFail, null, null, null, true)]));
        Assert.Equal(HttpStatusCode.Conflict, await StatusAsync(super.PutAsJsonAsync($"/api/quality/plans/{plan.Id}", plan with { Version = "1", Name = "Old" }, SyncWorld.Json)));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.PostAsJsonAsync("/api/quality/plans", plan with { Id = null, ProductCode = partCode }, SyncWorld.Json)));
        await PostAsync<ReleaseResponse>(adminA, $"/api/production-orders/{planned.Id}/release", new { });
        await PostAsync<ProductionOrderDetail>(adminA, $"/api/production-orders/{planned.Id}/receive", new ReceiveRequest(10, true, null));
        var inspection = await PostAsync<InspectionInfo>(adminA, $"/api/production-orders/{planned.Id}/inspect",
            new OutputInspectionRequest(10, 2, InspectionDisposition.Hold, hold.Id, [new CheckAnswer(1, PassFail: true)], "Two missing a part"));
        Assert.Equal(("HLD", false), (inspection.HoldLocation, inspection.Passed));
        Assert.Single(await GetAsync<List<InspectionInfo>>(viewer, $"/api/production-orders/{planned.Id}/inspections"));
        Assert.Single(await GetAsync<List<InspectionInfo>>(viewer, "/api/quality/inspections?kind=Production"));
        Assert.Empty(await GetAsync<List<InspectionInfo>>(adminB, "/api/quality/inspections"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.PostAsJsonAsync($"/api/production-orders/{planned.Id}/inspect",
            new OutputInspectionRequest(1, 0, InspectionDisposition.None, null, [new CheckAnswer(1, PassFail: true)], null), SyncWorld.Json)));

        // Incoming: the purchase order planning made, received (here on the server), then inspected.
        var purchasing = new PurchasingService(onServer);
        var po = (await purchasing.ListAsync(pos)).First();
        var line = (await purchasing.GetAsync(po.Id))!.Lines.Single();
        await purchasing.PostShipmentAsync(po.Id, [new ReceivedLine(line.Id, 20, 1m)]);
        var receipts = await GetAsync<List<ReceivedOrderInfo>>(viewer, "/api/quality/receipts");
        Assert.Equal(partCode, receipts.Single(r => r.Id == po.Id).Lines.Single().Plu);
        var incoming = await PostAsync<InspectionInfo>(adminA, $"/api/quality/receipts/{po.Id}/inspect", new IncomingInspectionRequest(partCode, 5, 0, [], null));
        Assert.Equal((InspectionKind.Incoming, po.Number), (incoming.Kind, incoming.DocumentNumber!.Value));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.PostAsJsonAsync($"/api/quality/receipts/{po.Id}/inspect", new IncomingInspectionRequest(partCode, 1, 0, [], null))));

        // Reports: cost reports need production.costing; each runs for the store, and as CSV.
        var catalog = await GetAsync<List<ReportInfo>>(viewer, "/api/production/reports");
        Assert.DoesNotContain(catalog, r => r.Key == "production-costs");
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/production/reports/production-costs")));
        foreach (var report in await GetAsync<List<ReportInfo>>(adminA, "/api/production/reports"))
        {
            var result = await GetAsync<ReportResult>(adminA, $"/api/production/reports/{report.Key}");
            Assert.False(string.IsNullOrWhiteSpace(result.Title));
        }
        var status = await GetAsync<ReportResult>(adminA, "/api/production/reports/production-status");
        Assert.Contains(status.Rows, r => r[0]!.ToString() == "completed");
        using var csv = await adminA.GetAsync("/api/production/reports/production-wip/csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminA.GetAsync("/api/production/reports/nothing")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminB.GetAsync($"/api/production/reports/production-status?storeId={storeA.Id}")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync((await world.AdminOfAsync(storeA.Id)).DeleteAsync($"/api/boms/{copy.Id}")));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(super.DeleteAsync($"/api/boms/{copy.Id}")));
        Assert.Empty(await GetAsync<List<CustomerOrderPick>>(adminA, "/api/production/customer-orders"));
    }

    // The web app's contract ----------------------------------------------------------------------------------------------

    /// <summary>The interfaces of the web app's manufacturing types (web/src/lib/production-types.ts) and their fields; null without the web folder.</summary>
    private static Dictionary<string, HashSet<string>>? WebTypes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        static string FileIn(DirectoryInfo d) => Path.Combine(d.FullName, "web", "src", "lib", "production-types.ts");
        while (dir is not null && !File.Exists(FileIn(dir))) dir = dir.Parent;
        if (dir is null) return null;
        var types = new Dictionary<string, HashSet<string>>();
        HashSet<string>? current = null;
        foreach (var line in File.ReadLines(FileIn(dir)))
        {
            if (Regex.Match(line, @"^export interface (\w+) \{$") is { Success: true } start) types[start.Groups[1].Value] = current = [];
            else if (line == "}") current = null;
            else if (current is not null && Regex.Match(line, @"^  (\w+)\??:") is { Success: true } field) current.Add(field.Groups[1].Value);
        }
        return types;
    }

    /// <summary>An answer's object has exactly the fields of the web app's type: nothing the web does not know, nothing it reads left out.</summary>
    private static void Shape(Dictionary<string, HashSet<string>> types, string type, JsonElement element, string where)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        var keys = element.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var expected = types[type];
        Assert.True(keys.SetEquals(expected),
            $"{where} is not a {type}: the web does not know [{string.Join(", ", keys.Except(expected))}], the server leaves out [{string.Join(", ", expected.Except(keys))}].");
    }

    /// <summary>Every element of an array (at least one) has the shape of the web app's type.</summary>
    private static void Each(Dictionary<string, HashSet<string>> types, string type, JsonElement array, string where)
    {
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.True(array.GetArrayLength() > 0, $"{where} is empty, so its shape is not checked.");
        foreach (var element in array.EnumerateArray()) Shape(types, type, element, where);
    }

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url, HttpMethod? method = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        if (body is not null) request.Content = JsonContent.Create(body, options: SyncWorld.Json);
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {url}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [SkippableFact]
    public async Task Every_manufacturing_answer_has_the_shape_of_the_web_apps_types()
    {
        Require();
        var types = WebTypes();
        Skip.If(types is null, "The web app's sources are not next to the tests.");
        var store = await world.AddStoreAsync();
        var pos = await PosOfAsync(store.Id);
        var onServer = world.ServerData.For(new StationIdentity(store.Id, ProductionService.ServerStation), null);
        string partCode = Unique("PT"), kitCode = Unique("KT"), crumbCode = Unique("CR");
        var supplier = await new PartyService(world.ServerData).SaveSupplierAsync(new Supplier { Company = Unique("Shapes ") });
        var inventory = new InventoryService(onServer);
        await inventory.SaveAsync(new Item { LocationId = pos, Plu = partCode, Name = "Part", AverageCost = 1m, LastCost = 1m, OnHand = 3, SupplierId = supplier.Id }, null, null, null);
        await inventory.SaveAsync(new Item { LocationId = pos, Plu = kitCode, Name = "Kit", Price1 = 5m, ReorderPoint = 5, ReorderQuantity = 10 }, null, null, null);
        await inventory.SaveAsync(new Item { LocationId = pos, Plu = crumbCode, Name = "Offcuts" }, null, null, null);
        await using (var db = world.ServerData.Create())
        {
            db.CustomerOrders.Add(new CustomerOrder { StoreId = store.Id, LocationId = pos, Plu = kitCode, Description = "Kit", Quantity = 1, Name = "Client" });
            await db.SaveChangesAsync();
        }
        using var super = await world.SuperAsync();
        using var admin = await world.AdminOfAsync(store.Id);
        var q = $"storeId={store.Id}";

        // Work centres, bills and routings, compared and costed.
        var centre = await JsonAsync(admin, "/api/work-centres", HttpMethod.Post, new WorkCentreRequest(null, pos, Unique("BN"), "Bench", 20, 5, 2, 8, 5, true, null));
        Shape(types!, "WorkCentre", centre, "POST /api/work-centres");
        Each(types!, "WorkCentre", await JsonAsync(admin, $"/api/work-centres?{q}"), "GET /api/work-centres");
        var centreId = centre.GetProperty("id").GetGuid();
        var bom = await JsonAsync(super, "/api/boms", HttpMethod.Post, new BomDetail(null, null, kitCode, null, 0, true, null, null, 1, 5, null,
            [new BomLineInfo(partCode, null, 2, 10, IssueMethod.Manual, 10)], [new BomByProductInfo(crumbCode, null, 0.5m, 5)],
            [new RoutingOperationInfo(10, centreId, null, "Pack", 5, 30, "Pack it")]));
        Shape(types!, "BomDetail", bom, "POST /api/boms");
        var bomId = bom.GetProperty("id").GetGuid();
        Each(types!, "BomLine", bom.GetProperty("lines"), "BomDetail.lines");
        Each(types!, "BomByProduct", bom.GetProperty("byProducts"), "BomDetail.byProducts");
        Each(types!, "RoutingOperation", bom.GetProperty("operations"), "BomDetail.operations");
        var copy = await JsonAsync(super, $"/api/boms/{bomId}/copy", HttpMethod.Post, new CopyBomRequest(null));
        var copyId = copy.GetProperty("id").GetGuid();
        Shape(types!, "BomDetail", await JsonAsync(super, $"/api/boms/{copyId}/routing", HttpMethod.Put,
            new RoutingRequest(copy.GetProperty("version").GetString(), [new RoutingOperationInfo(10, centreId, null, "Pack", 10, 25, null)])), "PUT /api/boms/{id}/routing");
        Each(types!, "BomListRow", await JsonAsync(admin, $"/api/boms?search={kitCode}"), "GET /api/boms");
        Shape(types!, "BomDetail", await JsonAsync(admin, $"/api/boms/{bomId}"), "GET /api/boms/{id}");
        Each(types!, "BomDifference", await JsonAsync(admin, $"/api/boms/compare?from={bomId}&to={copyId}"), "GET /api/boms/compare");
        Each(types!, "ExplodedLine", await JsonAsync(admin, $"/api/boms/{bomId}/explode?locationId={pos}"), "GET /api/boms/{id}/explode");
        var cost = await JsonAsync(admin, $"/api/boms/{bomId}/cost?locationId={pos}&quantity=10");
        Shape(types!, "CostRollUp", cost, "GET /api/boms/{id}/cost");
        Each(types!, "CostLine", cost.GetProperty("lines"), "CostRollUp.lines");
        Each(types!, "CostOperation", cost.GetProperty("operations"), "CostRollUp.operations");

        // Planning: the kit is below its reorder point; the part to buy is firmed into a purchase order, received and inspected.
        var plan = await JsonAsync(super, "/api/quality/plans", HttpMethod.Post, new QualityPlanInfo(null, null, kitCode, "Kit checks", true, true, true, null,
            [new QualityCheckInfo(1, "Weight", QualityCheckKind.Number, 90, 110, "g", true), new QualityCheckInfo(2, "Complete", QualityCheckKind.PassFail, null, null, null, true)]));
        Shape(types!, "QualityPlan", plan, "POST /api/quality/plans");
        Each(types!, "QualityCheck", plan.GetProperty("checks"), "QualityPlan.checks");
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(super.PutAsJsonAsync($"/api/mrp/lead-times/{supplier.Id}", new LeadTimeRequest(4))));
        var run = await JsonAsync(admin, "/api/mrp/run", HttpMethod.Post, new MrpRunRequest(store.Id, pos, 30, 7));
        Shape(types!, "MrpView", run, "POST /api/mrp/run");
        Shape(types!, "MrpRunInfo", run.GetProperty("run"), "MrpView.run");
        Each(types!, "Suggestion", run.GetProperty("suggestions"), "MrpView.suggestions");
        var suggestions = await JsonAsync(admin, $"/api/mrp/suggestions?{q}");
        Shape(types!, "MrpView", suggestions, "GET /api/mrp/suggestions");
        var buy = suggestions.GetProperty("suggestions").EnumerateArray().First(s => s.GetProperty("stockCode").GetString() == partCode).GetProperty("id").GetGuid();
        Shape(types!, "FirmResult", await JsonAsync(admin, "/api/mrp/firm", HttpMethod.Post, new FirmRequest([buy], null)), "POST /api/mrp/firm");
        Each(types!, "LeadTime", await JsonAsync(admin, "/api/mrp/lead-times"), "GET /api/mrp/lead-times");
        Each(types!, "CustomerOrderPick", await JsonAsync(admin, $"/api/production/customer-orders?{q}"), "GET /api/production/customer-orders");
        var purchasing = new PurchasingService(onServer);
        var po = (await purchasing.ListAsync(pos)).First();
        await purchasing.PostShipmentAsync(po.Id, [new ReceivedLine((await purchasing.GetAsync(po.Id))!.Lines.Single().Id, 20, 1m)]);
        var receipts = await JsonAsync(admin, $"/api/quality/receipts?{q}");
        Each(types!, "ReceivedOrder", receipts, "GET /api/quality/receipts");
        Each(types!, "ReceivedLine", receipts[0].GetProperty("lines"), "ReceivedOrder.lines");

        // An order planned, released, worked, received, inspected and posted.
        var order = await JsonAsync(admin, "/api/production-orders", HttpMethod.Post, new ProductionOrderRequest(null, kitCode, 4, pos, pos, null, null, null, 3, null, "Shapes"));
        var orderId = order.GetProperty("id").GetGuid();
        Shape(types!, "ProductionOrderDetail", order, "POST /api/production-orders");
        var released = await JsonAsync(admin, $"/api/production-orders/{orderId}/release", HttpMethod.Post, new { });
        Shape(types!, "ReleaseResponse", released, "POST /api/production-orders/{id}/release");
        Assert.Equal(JsonValueKind.Array, released.GetProperty("shortages").ValueKind);
        await JsonAsync(admin, $"/api/production-orders/{orderId}/issue", HttpMethod.Post, new IssueRequest(partCode, 9, null));
        await JsonAsync(admin, $"/api/production-orders/{orderId}/report", HttpMethod.Post, new OperationReportRequest(10, 125, 4, 0, null));
        await JsonAsync(admin, $"/api/production-orders/{orderId}/receive", HttpMethod.Post, new ReceiveRequest(4, true, null));
        var inspection = await JsonAsync(admin, $"/api/production-orders/{orderId}/inspect", HttpMethod.Post,
            new OutputInspectionRequest(4, 1, InspectionDisposition.Scrap, null, [new CheckAnswer(1, Number: 100), new CheckAnswer(2, PassFail: true)], null));
        Shape(types!, "Inspection", inspection, "POST /api/production-orders/{id}/inspect");
        Each(types!, "QualityResult", inspection.GetProperty("results"), "Inspection.results");
        Each(types!, "Inspection", await JsonAsync(admin, $"/api/production-orders/{orderId}/inspections"), "GET /api/production-orders/{id}/inspections");
        Each(types!, "Inspection", await JsonAsync(admin, $"/api/quality/inspections?{q}"), "GET /api/quality/inspections");
        Each(types!, "QualityPlan", await JsonAsync(admin, "/api/quality/plans"), "GET /api/quality/plans");
        var detail = await JsonAsync(admin, $"/api/production-orders/{orderId}/close", HttpMethod.Post, new { });
        Shape(types!, "ProductionOrderDetail", detail, "POST /api/production-orders/{id}/close");
        Each(types!, "LineProgress", detail.GetProperty("lines"), "ProductionOrderDetail.lines");
        Each(types!, "ProductionOperation", detail.GetProperty("operations"), "ProductionOrderDetail.operations");
        Each(types!, "ProductionMovement", detail.GetProperty("movements"), "ProductionOrderDetail.movements");
        Each(types!, "ProductionReport", detail.GetProperty("reports"), "ProductionOrderDetail.reports");
        Shape(types!, "OrderCosts", detail.GetProperty("costs"), "ProductionOrderDetail.costs");
        Shape(types!, "OrderVariances", detail.GetProperty("variances"), "ProductionOrderDetail.variances");
        Each(types!, "MaterialUsage", detail.GetProperty("variances").GetProperty("materials"), "OrderVariances.materials");
        Each(types!, "ProductionPosting", detail.GetProperty("postings"), "ProductionOrderDetail.postings");
        Assert.Equal("Closed", detail.GetProperty("status").GetString());
        Assert.Contains(detail.GetProperty("movements").EnumerateArray(), m => m.GetProperty("kind").GetString() == "ProductionScrap");

        // Lists, the board, capacity, posting and the reports.
        var page = await JsonAsync(admin, $"/api/production-orders?{q}&status=Closed");
        Assert.Equal(PageFields, page.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Each(types!, "ProductionOrderRow", page.GetProperty("items"), "GET /api/production-orders");
        var board = await JsonAsync(admin, $"/api/production-orders/board?{q}");
        Each(types!, "ProductionBoardColumn", board, "GET /api/production-orders/board");
        Each(types!, "ProductionOrderRow", board.EnumerateArray().Single(c => c.GetProperty("status").GetString() == "Closed").GetProperty("orders"), "ProductionBoardColumn.orders");
        Each(types!, "CapacityRow", await JsonAsync(admin, $"/api/capacity?{q}&weeks=2"), "GET /api/capacity");
        Shape(types!, "PostCostsResponse", await JsonAsync(admin, $"/api/production/post-costs?{q}", HttpMethod.Post, new { }), "POST /api/production/post-costs");
        foreach (var report in (await JsonAsync(admin, "/api/production/reports")).EnumerateArray())
        {
            Assert.Equal(ReportInfoFields, report.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
            Shape(types!, "ProductionReportResult", await JsonAsync(admin, $"/api/production/reports/{report.GetProperty("key").GetString()}?{q}"), "GET /api/production/reports/{key}");
        }
    }
}
