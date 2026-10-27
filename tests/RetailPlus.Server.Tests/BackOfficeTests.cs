using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Sync.Tests;
using SkiaSharp;
using static RetailPlus.Server.Tests.BackOffice;

namespace RetailPlus.Server.Tests;

/// <summary>
/// The back-office API (docs/API.md) over the seeded company of <see cref="BackOffice"/>: each group of endpoints works,
/// refuses users without the permission, and shows a store administrator their own store only while a super
/// administrator sees both.
/// </summary>
public sealed class BackOfficeTests(BackOffice office) : IClassFixture<BackOffice>
{
    private void Require() => Skip.IfNot(office.Available, office.UnavailableReason);

    private string Day(int daysAgo) => office.Today.AddDays(-daysAgo).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The last three days, as most tests ask.</summary>
    private string Period => $"from={Day(2)}&to={Day(0)}";

    private static async Task<HttpStatusCode> StatusAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        return response.StatusCode;
    }

    /// <summary>Asserts the status of an answer, showing its body when it is another.</summary>
    private static async Task ExpectAsync(HttpStatusCode expected, Task<HttpResponseMessage> request)
    {
        using var response = await request;
        if (response.StatusCode != expected)
            Assert.Fail($"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    // Dashboard and tills -----------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task The_dashboard_adds_up_the_sales_of_the_stores_the_user_sees()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var viewer = await office.SignInAsync(office.ViewerA);

        var all = await GetAsync<Dashboard>(super, $"/api/dashboard?{Period}");
        var a = await GetAsync<Dashboard>(adminA, $"/api/dashboard?{Period}");
        var b = await GetAsync<Dashboard>(super, $"/api/dashboard?{Period}&storeId={office.StoreB.Id}");

        // Store A: 20 + 25 + 30 + a 50 gift card, one voided sale; store B: 21 + 14 + 7.
        Assert.Equal((125m, 4, 1), (a.Totals.NetSales, a.Totals.Transactions, a.Totals.Voids));
        Assert.Equal((42m, 3, 0), (b.Totals.NetSales, b.Totals.Transactions, b.Totals.Voids));
        Assert.Equal(a.Totals.NetSales + b.Totals.NetSales, all.Totals.NetSales);
        Assert.Equal(7, all.Totals.Transactions);
        Assert.Equal(7m, a.Totals.Units);   // 2 + 1 + 3 snacks and lamps, and the gift card
        // The three days before: one lamp at store A, four days ago.
        Assert.Equal((25m, 1), (a.Previous.NetSales, a.Previous.Transactions));

        Assert.Equal([office.StoreA.Id], a.ByStore.Select(s => s.StoreId));
        Assert.Equal(2, all.ByStore.Count);
        Assert.Equal([Day(2), Day(1), Day(0)], a.ByDay.Select(d => d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        Assert.Equal([1, 1, 2], a.ByDay.Select(d => d.Transactions));
        Assert.Equal(24, a.ByHour.Count);
        Assert.Equal(7, all.ByHour.Sum(h => h.Transactions));
        Assert.Equal(25m, all.PaymentMix.Single(p => p.Tender == "Visa").Amount);
        Assert.Equal(7m, all.PaymentMix.Single(p => p.Tender == "Gift Card").Amount);
        Assert.Equal(5m, all.TopItems.Single(i => i.Plu == "A-SNACK").Units);
        Assert.Equal(125m, a.ByCashier.Single(c => c.UserId == office.CashierA.Id).NetSales);
        Assert.DoesNotContain(a.ByCashier, c => c.UserId == office.CashierB.Id);
        Assert.Equal([office.StoreA.Code], a.ByStation.Select(s => s.StoreCode).Distinct());
        Assert.Equal(7, all.ByStation.Sum(s => s.Transactions));

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.GetAsync($"/api/dashboard?storeId={office.StoreB.Id}")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/dashboard")));
        Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(super.GetAsync("/api/dashboard?from=yesterday")));
    }

    [SkippableFact]
    public async Task The_till_monitor_shows_each_till_with_its_status()
    {
        Require();
        using var super = await office.SignInAsync();
        using var viewer = await office.SignInAsync(office.ViewerA);
        var all = await GetAsync<List<TillInfo>>(super, "/api/tills");
        var mine = await GetAsync<List<TillInfo>>(viewer, "/api/tills");

        Assert.Equal(2, all.Count(t => t.StoreId == office.StoreA.Id));
        Assert.Equal(2, all.Count(t => t.StoreId == office.StoreB.Id));
        Assert.All(mine, t => Assert.Equal(office.StoreA.Id, t.StoreId));
        var a1 = all.Single(t => t.StoreId == office.StoreA.Id && t.Number == office.A1.Data.Station!.StationNumber);
        Assert.Equal("ok", a1.Status);
        Assert.True(a1.Enrolled);
        Assert.NotNull(a1.LastUploadAtUtc);
        Assert.Equal(DateTimeKind.Utc, a1.LastUploadAtUtc!.Value.Kind);
        Assert.Equal((80m, 2), (a1.TodayNetSales, a1.TodayTransactions));   // 30 of snacks and the 50 gift card
        Assert.NotNull(a1.LastSaleAt);

        // A till set up but not joined yet has never sent anything.
        var waiting = await office.World.AddStationAsync(office.StoreB.Id);
        var after = await GetAsync<List<TillInfo>>(super, $"/api/tills?storeId={office.StoreB.Id}");
        Assert.Equal("never", after.Single(t => t.StationId == waiting.Station.Id).Status);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync($"/api/tills?storeId={office.StoreB.Id}")));
    }

    // Sales -----------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Sales_are_listed_a_page_at_a_time_and_shown_with_lines_and_payments()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var super = await office.SignInAsync();

        var first = await GetAsync<PageOf<SaleListRow>>(adminA, $"/api/sales?{Period}&pageSize=2");
        Assert.Equal((5, 2, 1), (first.Total, first.Items.Count, first.Page));   // four sales and the voided one
        var last = await GetAsync<PageOf<SaleListRow>>(adminA, $"/api/sales?{Period}&pageSize=2&page=3");
        Assert.Single(last.Items);
        Assert.All(first.Items.Concat(last.Items), s => Assert.Equal(office.StoreA.Code, s.StoreCode));

        var voided = await GetAsync<PageOf<SaleListRow>>(adminA, $"/api/sales?{Period}&status=voided");
        Assert.Equal(office.VoidedSaleId, Assert.Single(voided.Items).Id);
        Assert.True(voided.Items[0].Voided);
        var lamps = await GetAsync<PageOf<SaleListRow>>(adminA, $"/api/sales?{Period}&search=A-LAMP");
        Assert.Equal("Visa", Assert.Single(lamps.Items).Tenders);
        var byNumber = await GetAsync<PageOf<SaleListRow>>(adminA, $"/api/sales?{Period}&search={lamps.Items[0].InvoiceNumber}");
        Assert.Contains(byNumber.Items, s => s.Id == lamps.Items[0].Id);
        var both = await GetAsync<PageOf<SaleListRow>>(super, $"/api/sales?{Period}");
        Assert.Equal(8, both.Total);

        var detail = await GetAsync<SaleDetail>(adminA, $"/api/sales/{office.VoidedSaleId}");
        Assert.True(detail.Voided);
        Assert.Equal(office.CashierA.UserName, detail.VoidedBy);
        Assert.Equal("A-SNACK", Assert.Single(detail.Lines).Plu);
        Assert.Equal(10m, Assert.Single(detail.Payments).Amount);

        // Another store's sale is not found; another store's list is refused; no permission, no sales.
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminA.GetAsync($"/api/sales/{office.SaleAtBId}")));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(super.GetAsync($"/api/sales/{office.SaleAtBId}")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.GetAsync($"/api/sales?storeId={office.StoreB.Id}")));
        using var viewer = await office.SignInAsync(office.ViewerA);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/sales")));
    }

    // Items, departments, categories ------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Items_are_read_changed_deleted_and_restored_with_their_version()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var super = await office.SignInAsync();

        // The snacks are at store A and, since the transfer, at store B too.
        Assert.Equal(2, (await GetAsync<PageOf<ItemListRow>>(super, "/api/items?search=A-SNACK")).Total);
        var mine = await GetAsync<PageOf<ItemListRow>>(adminA, "/api/items?search=A-");
        Assert.Equal(["A-GIFT", "A-LAMP", "A-SNACK"], mine.Items.Select(i => i.Plu).Order(StringComparer.Ordinal));
        Assert.All(mine.Items, i => Assert.Equal(office.StoreA.Code, i.StoreCode));
        var paged = await GetAsync<PageOf<ItemListRow>>(adminA, "/api/items?pageSize=1&page=2");
        Assert.Equal((1, 2, 1), (paged.Items.Count, paged.Page, paged.PageSize));

        var all = await GetAsync<ItemDetail>(super, $"/api/items/{office.ItemA1.Id}");
        Assert.Equal([office.StoreA.Code, office.StoreB.Code], all.Stock!.Select(s => s.StoreCode));
        Assert.Equal(4m, all.Stock![1].OnHand);
        var item = await GetAsync<ItemDetail>(adminA, $"/api/items/{office.ItemA1.Id}");
        Assert.Equal(office.StoreA.Code, Assert.Single(item.Stock!).StoreCode);
        Assert.Equal(41m, item.Stock![0].OnHand);   // 50, less 2 and 3 sold and 4 sent to store B
        Assert.False(item.Taxable3);

        using (var saved = await adminA.PutAsJsonAsync($"/api/items/{item.Id}", item with { Price1 = 11m, Description = "Nuts and raisins" }))
        {
            var changed = await ReadAsync<ItemDetail>(saved);
            Assert.Equal((11m, "Nuts and raisins"), (changed.Price1, changed.Description));
            Assert.NotEqual(item.Version, changed.Version);
        }
        // The version sent back is no longer the item's: someone else changed it.
        using (var stale = await adminA.PutAsJsonAsync($"/api/items/{item.Id}", item with { Price1 = 12m }))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Contains("error", await stale.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using var created = await adminA.PostAsJsonAsync("/api/items", item with { Id = null, Version = null, Plu = "A-NEW", Name = "New on the web", Stock = null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var added = await ReadAsync<ItemDetail>(created);
        Assert.Equal(office.StoreA.Code, Assert.Single(added.Stock!).StoreCode);
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(adminA.DeleteAsync($"/api/items/{added.Id}")));
        Assert.Contains((await GetAsync<PageOf<ItemListRow>>(adminA, "/api/items?deleted=true")).Items, i => i.Id == added.Id);
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(adminA.PostAsync($"/api/items/{added.Id}/undelete", null)));
        Assert.True((await GetAsync<ItemDetail>(adminA, $"/api/items/{added.Id}")).IsActive);

        // Store B's items do not exist for store A's administrator; no permission, no items.
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminA.GetAsync($"/api/items/{office.ItemB1.Id}")));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminA.DeleteAsync($"/api/items/{office.ItemB1.Id}")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.GetAsync($"/api/items?storeId={office.StoreB.Id}")));
        using var viewer = await office.SignInAsync(office.ViewerA);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/items")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.PostAsJsonAsync("/api/items", item)));
    }

    [SkippableFact]
    public async Task Departments_and_categories_are_listed_and_changed_by_a_user_of_every_store()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var super = await office.SignInAsync();
        var departments = await GetAsync<List<NamedRow>>(adminA, "/api/departments");
        Assert.Contains(departments, d => d.Name == "FOOD");
        var food = departments.Single(d => d.Name == "FOOD");
        var home = departments.Single(d => d.Name == "HOME");
        Assert.Equal(["SNACKS"], (await GetAsync<List<CategoryRow>>(adminA, $"/api/categories?departmentId={food.Id}")).Select(c => c.Name));
        Assert.Empty(await GetAsync<List<CategoryRow>>(adminA, $"/api/categories?departmentId={home.Id}"));
        Assert.Contains(await GetAsync<List<CategoryRow>>(adminA, "/api/categories"), c => c.Name == "SNACKS");

        // Departments are shared by both stores: a store's administrator may not change them.
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.PostAsJsonAsync("/api/departments", new NameRequest("Toys"))));
        var toys = await ReadAsync<NamedRow>(await super.PostAsJsonAsync("/api/departments", new NameRequest("Toys")));
        Assert.Equal("TOYS", toys.Name);
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(super.DeleteAsync($"/api/departments/{toys.Id}")));
        var games = await ReadAsync<CategoryRow>(await super.PostAsJsonAsync("/api/categories", new NameRequest("Games")));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(super.DeleteAsync($"/api/categories/{games.Id}")));
    }

    // Clients and suppliers ---------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Clients_are_kept_by_users_of_every_store_with_their_version()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        string last = "Webber" + Guid.NewGuid().ToString("N")[..4];
        var body = new ClientDetail(null, null, null, "Wendy", last, null, "1 Main St", null, "Halifax", "NS", "B3H 1A1", "Canada", "902-555-0100", null,
            "wendy@example.com", 500m, 0, 0, "Prefers email");

        // Two stores: clients are shared, so a store's administrator reads them but does not change them.
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.PostAsJsonAsync("/api/clients", body)));
        using var created = await super.PostAsJsonAsync($"/api/clients?storeId={office.StoreA.Id}", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var client = await ReadAsync<ClientDetail>(created);
        Assert.False(string.IsNullOrEmpty(client.Number));
        Assert.Equal((last, "B3H 1A1", 500m), (client.LastName, client.PostalCode, client.CreditLimit));

        var found = await GetAsync<PageOf<ClientListRow>>(adminA, $"/api/clients?search={last}");
        Assert.Equal($"{last}, Wendy", Assert.Single(found.Items).Name);
        Assert.Equal(client, await GetAsync<ClientDetail>(adminA, $"/api/clients/{client.Id}"));

        var changed = await ReadAsync<ClientDetail>(await super.PutAsJsonAsync($"/api/clients/{client.Id}", client with { Phone = "902-555-0199" }));
        Assert.Equal("902-555-0199", changed.Phone);
        Assert.Equal(HttpStatusCode.Conflict, await StatusAsync(super.PutAsJsonAsync($"/api/clients/{client.Id}", client with { Phone = "0" })));

        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(super.DeleteAsync($"/api/clients/{client.Id}")));
        Assert.Empty((await GetAsync<PageOf<ClientListRow>>(super, $"/api/clients?search={last}")).Items);
        Assert.True(Assert.Single((await GetAsync<PageOf<ClientListRow>>(super, $"/api/clients?search={last}&deleted=true")).Items).IsDeleted);
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(super.PostAsync($"/api/clients/{client.Id}/undelete", null)));
        Assert.Single((await GetAsync<PageOf<ClientListRow>>(super, $"/api/clients?search={last}")).Items);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(super.GetAsync($"/api/clients/{Guid.NewGuid()}")));

        using var viewer = await office.SignInAsync(office.ViewerA);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/clients")));

        // The change is in the audit log under the web user and the store asked for.
        var audit = await GetAsync<PageOf<AuditRow>>(super, $"/api/audit?action=Added&pageSize=500");
        var added = audit.Items.Single(r => r.Action == "Added" && r.EntityId == client.Id);
        Assert.Equal((SyncWorld.AdminUser, office.StoreA.Code, 0), (added.UserName, added.StoreCode, added.StationNumber));
    }

    [SkippableFact]
    public async Task Suppliers_are_read_with_inventory_and_changed_with_their_own_permission()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var reporter = await office.SignInAsync(office.ReporterA);
        string company = "Acme " + Guid.NewGuid().ToString("N")[..4];
        var body = new SupplierDetail(null, null, "AC" + Guid.NewGuid().ToString("N")[..4], company, "Road Runner", "2 Desert Rd", null, "Tucson", "AZ", "85701", "USA",
            "520-555-0100", "520-555-0101", "orders@acme.example", null);

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.PostAsJsonAsync("/api/suppliers", body)));
        var supplier = await ReadAsync<SupplierDetail>(await super.PostAsJsonAsync("/api/suppliers", body));
        Assert.Equal(("Road Runner", "520-555-0101"), (supplier.Contact, supplier.Fax));
        Assert.Equal(company, Assert.Single((await GetAsync<PageOf<SupplierListRow>>(adminA, $"/api/suppliers?search={company}")).Items).Company);
        Assert.Equal(supplier, await GetAsync<SupplierDetail>(adminA, $"/api/suppliers/{supplier.Id}"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(reporter.GetAsync("/api/suppliers")));

        var changed = await ReadAsync<SupplierDetail>(await super.PutAsJsonAsync($"/api/suppliers/{supplier.Id}", supplier with { Contact = "Wile Coyote" }));
        Assert.Equal("Wile Coyote", changed.Contact);
        Assert.Equal(HttpStatusCode.Conflict, await StatusAsync(super.PutAsJsonAsync($"/api/suppliers/{supplier.Id}", supplier)));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(super.DeleteAsync($"/api/suppliers/{supplier.Id}")));
        Assert.True(Assert.Single((await GetAsync<PageOf<SupplierListRow>>(super, $"/api/suppliers?search={company}&deleted=true")).Items).IsDeleted);
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(super.PostAsync($"/api/suppliers/{supplier.Id}/undelete", null)));
        Assert.False(Assert.Single((await GetAsync<PageOf<SupplierListRow>>(super, $"/api/suppliers?search={company}")).Items).IsDeleted);
    }

    // Stock and gift cards -----------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Stock_movements_transfers_and_counts_are_shown_to_their_stores()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var adminB = await office.SignInAsync(office.AdminB);

        var moves = await GetAsync<PageOf<MovementRow>>(adminA, $"/api/stock/movements?itemId={office.ItemA1.Id}&pageSize=3");
        Assert.True(moves.Total > 3);
        Assert.Equal(3, moves.Items.Count);
        Assert.All(moves.Items, m => Assert.Equal(("A-SNACK", office.StoreA.Code), (m.Plu, m.StoreCode)));
        var sales = await GetAsync<PageOf<MovementRow>>(adminA, $"/api/stock/movements?itemId={office.ItemA1.Id}&kind=sale&pageSize=100");
        Assert.All(sales.Items, m => Assert.Equal(StockMovementKind.Sale, m.Kind));
        Assert.Equal(-6m, sales.Items.Sum(m => m.Quantity));   // 2 and 3 at till 1, 1 at till 2 (voided apart)
        Assert.DoesNotContain((await GetAsync<PageOf<MovementRow>>(adminA, "/api/stock/movements?pageSize=500")).Items, m => m.StoreCode == office.StoreB.Code);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.GetAsync($"/api/stock/movements?storeId={office.StoreB.Id}")));
        Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(adminA.GetAsync("/api/stock/movements?kind=stolen")));

        // The transfer belongs to both stores.
        foreach (var admin in new[] { adminA, adminB })
        {
            var transfer = Assert.Single(await GetAsync<List<TransferListRow>>(admin, "/api/transfers"), t => t.Id == office.TransferId);
            Assert.Equal((StockTransferStatus.Received, office.StoreA.Code, office.StoreB.Code, 4m), (transfer.Status, transfer.FromStore, transfer.ToStore, transfer.Units));
        }
        var detail = await GetAsync<TransferDetail>(adminB, $"/api/transfers/{office.TransferId}");
        var line = Assert.Single(detail.Lines);
        Assert.Equal(("A-SNACK", 4m, 4m), (line.Plu, line.QuantityShipped, line.QuantityReceived));
        Assert.Empty(await GetAsync<List<TransferListRow>>(adminA, "/api/transfers?status=shipped"));

        // The count was made at store B.
        Assert.DoesNotContain(await GetAsync<List<CountListRow>>(adminA, "/api/stock-counts"), c => c.Id == office.CountId);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminA.GetAsync($"/api/stock-counts/{office.CountId}")));
        var count = Assert.Single(await GetAsync<List<CountListRow>>(adminB, "/api/stock-counts?status=posted"), c => c.Id == office.CountId);
        Assert.Equal(office.StoreB.Code, count.StoreCode);
        var counted = await GetAsync<CountDetail>(adminB, $"/api/stock-counts/{office.CountId}");
        var tea = Assert.Single(counted.Lines);
        Assert.Equal(("B-TEA", 23m), (tea.Plu, tea.Counted));
        Assert.Equal(tea.Counted - tea.Expected, tea.Variance);
        Assert.Equal(tea.Variance, counted.VarianceUnits);

        using var viewer = await office.SignInAsync(office.ViewerA);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/transfers")));
    }

    [SkippableFact]
    public async Task A_gift_card_is_shown_to_the_store_that_issued_it_with_its_history()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var adminB = await office.SignInAsync(office.AdminB);
        using var super = await office.SignInAsync();

        var row = Assert.Single((await GetAsync<PageOf<GiftCardRow>>(adminA, $"/api/gift-cards?search={office.GiftCardNumber}")).Items);
        Assert.Equal((office.StoreA.Code, 43m, true), (row.StoreCode, row.Balance, row.IsActive));
        Assert.Single((await GetAsync<PageOf<GiftCardRow>>(super, $"/api/gift-cards?search={office.GiftCardNumber}&active=true")).Items);
        Assert.Empty((await GetAsync<PageOf<GiftCardRow>>(super, $"/api/gift-cards?search={office.GiftCardNumber}&active=false")).Items);

        var card = await GetAsync<GiftCardDetailInfo>(adminA, $"/api/gift-cards/{row.Id}");
        Assert.Equal(43m, card.Balance);
        // Newest first: spent at store B, sold at store A.
        Assert.Equal([(-7m, 43m, office.StoreB.Code), (50m, 50m, office.StoreA.Code)], card.History.Select(h => (h.Amount, h.BalanceAfter, h.StoreCode)));

        Assert.Empty((await GetAsync<PageOf<GiftCardRow>>(adminB, $"/api/gift-cards?search={office.GiftCardNumber}")).Items);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/gift-cards/{row.Id}")));
        using var viewer = await office.SignInAsync(office.ViewerA);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/gift-cards")));
    }

    // Reports -----------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Reports_run_over_the_stores_the_user_sees_as_json_and_csv()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var reporter = await office.SignInAsync(office.ReporterA);

        var catalog = await GetAsync<List<ReportInfo>>(reporter, "/api/reports");
        Assert.Contains(catalog, r => r.Key == "daily" && r.UsesDates);
        Assert.DoesNotContain(catalog, r => r.Key == "hours");
        Assert.Contains(await GetAsync<List<ReportInfo>>(adminA, "/api/reports"), r => r.Key == "hours");
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(reporter.GetAsync("/api/reports/hours")));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(super.GetAsync("/api/reports/nothing-like-it")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.GetAsync($"/api/reports/daily?storeId={office.StoreB.Id}")));

        var mine = await GetAsync<ReportResult>(adminA, $"/api/reports/items?{Period}");
        var both = await GetAsync<ReportResult>(super, $"/api/reports/items?{Period}");
        Assert.NotEmpty(mine.Columns);
        Assert.Equal(mine.Columns.Count, mine.Totals.Length);
        var codes = mine.Rows.Select(r => r[0]?.ToString()).ToList();
        Assert.Contains("A-SNACK", codes);
        Assert.DoesNotContain("B-TEA", codes);
        Assert.Contains("B-TEA", both.Rows.Select(r => r[0]?.ToString()));
        Assert.Equal(Day(2), mine.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var hours = await GetAsync<ReportResult>(adminA, $"/api/reports/hours?{Period}");
        Assert.Contains(hours.Rows, r => r.Any(v => v?.ToString() == office.CashierA.UserName));
        Assert.DoesNotContain(hours.Rows, r => r.Any(v => v?.ToString() == office.CashierB.UserName));

        using var csv = await adminA.GetAsync($"/api/reports/items/csv?{Period}");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".csv", csv.Content.Headers.ContentDisposition!.FileName!.Trim('"'), StringComparison.Ordinal);
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(mine.Columns[0].Header, lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("A-SNACK", StringComparison.Ordinal));
    }

    // Users and roles ---------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Roles_are_changed_by_a_user_of_every_store_with_their_version()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);

        var permissions = await GetAsync<List<PermissionRow>>(adminA, "/api/permissions");
        Assert.Contains(permissions, p => p.Key == Permissions.WebSignIn);
        var roles = await GetAsync<List<RoleRow>>(adminA, "/api/roles");
        var cashiers = roles.Single(r => r.Name == Permissions.CashierRole);
        Assert.True(cashiers.IsSystem);
        Assert.True(cashiers.UserCount >= 2);

        var name = "Floor lead " + Guid.NewGuid().ToString("N")[..4];
        var body = new RoleBody(null, name, "Leads the floor", 3, [Permissions.WebSignIn, Permissions.ReportsView]);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.PostAsJsonAsync("/api/roles", body)));
        using var created = await super.PostAsJsonAsync("/api/roles", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var role = await ReadAsync<RoleRow>(created);
        Assert.Equal([Permissions.ReportsView, Permissions.WebSignIn], role.Permissions);

        var changed = await ReadAsync<RoleRow>(await super.PutAsJsonAsync($"/api/roles/{role.Id}",
            body with { Version = role.Version, Permissions = [Permissions.WebSignIn, Permissions.ReportsView, Permissions.DashboardView] }));
        Assert.Contains(Permissions.DashboardView, changed.Permissions);
        Assert.NotEqual(role.Version, changed.Version);
        Assert.Equal(HttpStatusCode.Conflict, await StatusAsync(super.PutAsJsonAsync($"/api/roles/{role.Id}", body with { Version = role.Version })));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(super.DeleteAsync($"/api/roles/{role.Id}")));
        Assert.DoesNotContain(await GetAsync<List<RoleRow>>(super, "/api/roles"), r => r.Id == role.Id);

        using var viewer = await office.SignInAsync(office.ViewerA);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/roles")));
    }

    [SkippableFact]
    public async Task A_store_administrator_manages_the_users_of_their_store_only()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        var roles = await GetAsync<List<RoleRow>>(super, "/api/roles");
        var clerk = roles.Single(r => r.Name == Permissions.ClerkRole).Id;
        var superRole = roles.Single(r => r.Name == Permissions.SuperAdministratorRole).Id;

        var users = await GetAsync<List<UserRow>>(adminA, "/api/users");
        Assert.All(users, u => Assert.Equal(office.StoreA.Id, u.StoreId));
        Assert.Contains(users, u => u.UserName == office.CashierA.UserName);
        Assert.Contains(await GetAsync<List<UserRow>>(super, "/api/users"), u => u.UserName == office.CashierB.UserName);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.GetAsync($"/api/users?storeId={office.StoreB.Id}")));
        // The user list shows when a user last signed in on the web.
        Assert.NotNull((await GetAsync<List<UserRow>>(adminA, "/api/users")).Single(u => u.UserName == office.AdminA).LastSignInAtUtc);

        string name = "clerk-" + Guid.NewGuid().ToString("N")[..6];
        var body = new UserBody(null, name, "Carla Clerk", clerk, office.StoreA.Id, true, "Clerk-pass-1");
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.PostAsJsonAsync("/api/users", body with { StoreId = office.StoreB.Id })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.PostAsJsonAsync("/api/users", body with { StoreId = null })));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.PostAsJsonAsync("/api/users", body with { RoleId = superRole })));
        using var created = await adminA.PostAsJsonAsync("/api/users", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = await ReadAsync<UserRow>(created);
        Assert.Equal((office.StoreA.Code, Permissions.ClerkRole), (user.StoreCode, user.Role));

        var renamed = await ReadAsync<UserRow>(await adminA.PutAsJsonAsync($"/api/users/{user.Id}", body with { Version = user.Version, FullName = "Carla C.", Password = null }));
        Assert.Equal("Carla C.", renamed.FullName);
        Assert.Equal(HttpStatusCode.Conflict, await StatusAsync(adminA.PutAsJsonAsync($"/api/users/{user.Id}", body with { Version = user.Version })));

        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(adminA.PostAsJsonAsync($"/api/users/{user.Id}/pin", new PinBody("77", "4321"))));
        var reset = await ReadAsync<TemporaryPasswordInfo>(await adminA.PostAsync($"/api/users/{user.Id}/reset-password", null));
        Assert.False(string.IsNullOrEmpty(reset.TemporaryPassword));
        await using (var db = office.World.ServerData.Create())
            Assert.True((await db.UserAccounts.SingleAsync(u => u.Id == user.Id)).MustChangePassword);

        // Users of store B, and of every store, do not exist for store A's administrator.
        var cashierB = (await GetAsync<List<UserRow>>(super, "/api/users")).Single(u => u.UserName == office.CashierB.UserName);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminA.PostAsync($"/api/users/{cashierB.Id}/reset-password", null)));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminA.PutAsJsonAsync($"/api/users/{cashierB.Id}", body with { Version = cashierB.Version })));
        var superUser = (await GetAsync<List<UserRow>>(super, "/api/users")).Single(u => u.UserName == SyncWorld.AdminUser);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminA.DeleteAsync($"/api/users/{superUser.Id}")));

        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(adminA.DeleteAsync($"/api/users/{user.Id}")));
        Assert.DoesNotContain(await GetAsync<List<UserRow>>(adminA, "/api/users"), u => u.Id == user.Id);
    }

    // Audit, time clock and commission ----------------------------------------------------------------------------------

    [SkippableFact]
    public async Task The_audit_log_shows_a_store_its_own_lines()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var super = await office.SignInAsync();

        var mine = await GetAsync<PageOf<AuditRow>>(adminA, "/api/audit?pageSize=500");
        Assert.All(mine.Items, r => Assert.Equal(office.StoreA.Code, r.StoreCode));
        // The punches came from till 1 of store A; the web sign-in is station 0.
        Assert.Contains(mine.Items, r => r.Action == AuditActions.TimeClock && r.UserName == office.CashierA.UserName && r.StationNumber == office.A1.Data.Station!.StationNumber);
        var signIn = mine.Items.First(r => r.Action == AuditActions.SignIn && r.UserName == office.AdminA);
        Assert.Equal(0, signIn.StationNumber);
        Assert.Equal(DateTimeKind.Utc, signIn.AtUtc.Kind);

        var b = await GetAsync<PageOf<AuditRow>>(super, $"/api/audit?storeId={office.StoreB.Id}&action={Uri.EscapeDataString(AuditActions.TimeClock)}");
        Assert.NotEmpty(b.Items);
        Assert.All(b.Items, r => Assert.Equal((office.StoreB.Code, AuditActions.TimeClock), (r.StoreCode, r.Action)));
        var paged = await GetAsync<PageOf<AuditRow>>(super, "/api/audit?pageSize=2&page=2");
        Assert.Equal(2, paged.Items.Count);
        Assert.True(paged.Total > 4);
        var today = await GetAsync<PageOf<AuditRow>>(adminA, $"/api/audit?from={Day(0)}&to={Day(0)}&userId={(await office.AccountAsync(office.AdminA)).Id}");
        Assert.All(today.Items, r => Assert.Equal(office.AdminA, r.UserName));

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.GetAsync($"/api/audit?storeId={office.StoreB.Id}")));
        using var viewer = await office.SignInAsync(office.ViewerA);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/audit")));
    }

    [SkippableFact]
    public async Task Hours_and_commission_cover_the_staff_of_the_store()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var super = await office.SignInAsync();

        var entries = await GetAsync<List<TimeClockRow>>(adminA, $"/api/time-clock?{Period}");
        Assert.Equal([4m, 8m], entries.Where(e => e.UserId == office.CashierA.Id).Select(e => e.Hours).Order());
        Assert.All(entries, e => Assert.Equal(office.StoreA.Code, e.StoreCode));
        var hours = await GetAsync<List<HoursRowInfo>>(adminA, $"/api/time-clock/hours?{Period}");
        Assert.Equal((2, 12m), hours.Where(h => h.UserId == office.CashierA.Id).Select(h => (h.Days, h.Hours)).Single());
        var everyone = await GetAsync<List<HoursRowInfo>>(super, $"/api/time-clock/hours?{Period}");
        Assert.Equal(6m, everyone.Single(h => h.UserId == office.CashierB.Id).Hours);

        var commission = await GetAsync<List<CommissionInfo>>(adminA, $"/api/commission?{Period}");
        var cashier = commission.Single(c => c.UserId == office.CashierA.Id);
        Assert.True(cashier.Sales >= 3 && cashier.NetSales >= 75m, $"{cashier}");

        Assert.DoesNotContain(commission, c => c.UserId == office.CashierB.Id);

        using var reporter = await office.SignInAsync(office.ReporterA);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(reporter.GetAsync("/api/time-clock")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.GetAsync($"/api/time-clock?storeId={office.StoreB.Id}")));
    }

    [SkippableFact]
    public async Task Commission_rules_are_kept_by_a_user_of_every_store_with_their_version()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        var name = "Lamps " + Guid.NewGuid().ToString("N")[..4];
        var body = new CommissionRuleRow(null, null, name, office.CashierA.Id, null, CommissionScope.Item, null, null, office.ItemA2.Id, null, 5m, null, null, true);

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminA.PostAsJsonAsync("/api/commission-rules", body)));
        using var created = await super.PostAsJsonAsync("/api/commission-rules", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var rule = await ReadAsync<CommissionRuleRow>(created);
        Assert.Equal((5m, office.CashierA.UserName), (rule.Percent, rule.UserName));
        Assert.Contains("A-LAMP", rule.Target, StringComparison.Ordinal);
        Assert.Contains(await GetAsync<List<CommissionRuleRow>>(adminA, "/api/commission-rules"), r => r.Id == rule.Id);

        // Five percent of the 25 lamp sold yesterday.
        var commission = await GetAsync<List<CommissionInfo>>(adminA, $"/api/commission?{Period}");
        Assert.Equal(1.25m, commission.Single(c => c.UserId == office.CashierA.Id).FromRules);

        var changed = await ReadAsync<CommissionRuleRow>(await super.PutAsJsonAsync($"/api/commission-rules/{rule.Id}", rule with { Percent = 4m }));
        Assert.Equal(4m, changed.Percent);
        Assert.Equal(HttpStatusCode.Conflict, await StatusAsync(super.PutAsJsonAsync($"/api/commission-rules/{rule.Id}", rule with { Percent = 3m })));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(super.DeleteAsync($"/api/commission-rules/{rule.Id}")));
        Assert.DoesNotContain(await GetAsync<List<CommissionRuleRow>>(super, "/api/commission-rules"), r => r.Id == rule.Id);
    }

    // Store settings and pictures ---------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_store_administrator_changes_their_store_settings_with_the_version()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        var settings = await GetAsync<StoreSettingsInfo>(adminA, $"/api/stores/{office.StoreA.Id}/settings");
        var wanted = settings with
        {
            CompanyName = "Corner Store A", Address1 = "10 King St", City = "Kingston", Province = "ON", PostalCode = "K7L 1A1", Phone = "613-555-0100",
            Email = "hello@corner.example", Website = "corner.example", TaxNumber = "123456789RT0001", CurrencySymbol = "$",
            ReceiptHeader = "Welcome to the corner", ReceiptFooter = "Thank you!",
        };
        var saved = await ReadAsync<StoreSettingsInfo>(await adminA.PutAsJsonAsync($"/api/stores/{office.StoreA.Id}/settings", wanted));
        Assert.Equal(wanted with { Version = saved.Version }, saved);
        Assert.NotEqual(settings.Version, saved.Version);
        Assert.Equal(saved, await GetAsync<StoreSettingsInfo>(adminA, $"/api/stores/{office.StoreA.Id}/settings"));
        Assert.Equal(HttpStatusCode.Conflict, await StatusAsync(adminA.PutAsJsonAsync($"/api/stores/{office.StoreA.Id}/settings", wanted with { City = "Ottawa" })));

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminA.GetAsync($"/api/stores/{office.StoreB.Id}/settings")));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminA.PutAsJsonAsync($"/api/stores/{office.StoreB.Id}/settings", wanted)));
        using var reporter = await office.SignInAsync(office.ReporterA);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(reporter.GetAsync($"/api/stores/{office.StoreA.Id}/settings")));
    }

    [SkippableFact]
    public async Task A_logo_is_uploaded_resized_like_the_desktop_and_removed()
    {
        Require();
        using var adminB = await office.SignInAsync(office.AdminB);
        string url = $"/api/stores/{office.StoreB.Id}/images/logo";
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync(url)));

        // A wide transparent PNG: it comes back as a JPEG of at most 800 pixels a side, on white.
        byte[] png;
        using (var bitmap = new SKBitmap(1600, 400, SKColorType.Rgba8888, SKAlphaType.Premul))
        {
            bitmap.Erase(SKColors.Transparent);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            png = data.ToArray();
        }
        using (var form = new MultipartFormDataContent { { new ByteArrayContent(png), "file", "logo.png" } })
        {
            using var upload = await adminB.PutAsync(url, form);
            Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
        }
        using (var stored = await adminB.GetAsync(url))
        {
            Assert.Equal("image/jpeg", stored.Content.Headers.ContentType!.MediaType);
            using var jpeg = SKBitmap.Decode(await stored.Content.ReadAsByteArrayAsync());
            Assert.Equal((800, 200), (jpeg.Width, jpeg.Height));
            var corner = jpeg.GetPixel(0, 0);
            Assert.True(corner.Red > 240 && corner.Green > 240 && corner.Blue > 240, $"The background should be white, not {corner}.");
        }
        // The picture goes down to the store's tills with the master data.
        await using (var db = office.World.ServerData.Create())
            Assert.True(await db.StoreImages.AnyAsync(i => i.StoreId == office.StoreB.Id && i.Kind == StoreImageKind.Logo));

        using (var notPicture = new MultipartFormDataContent { { new ByteArrayContent("not a picture"u8.ToArray()), "file", "logo.txt" } })
            await ExpectAsync(HttpStatusCode.BadRequest, adminB.PutAsync(url, notPicture));
        using (var wrongField = new MultipartFormDataContent { { new ByteArrayContent(png), "picture", "logo.png" } })
            await ExpectAsync(HttpStatusCode.BadRequest, adminB.PutAsync(url, wrongField));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/stores/{office.StoreB.Id}/images/banner")));
        using var adminA = await office.SignInAsync(office.AdminA);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminA.GetAsync(url)));

        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(adminB.DeleteAsync(url)));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync(url)));
    }
}
