using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
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
