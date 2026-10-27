using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Sync.Tests;

namespace RetailPlus.Server.Tests;

/// <summary>Master data changed on the web reaches the tills: the server changes it, the till downloads and applies it.</summary>
public sealed class MasterDataDownTests(SyncWorld world) : IClassFixture<SyncWorld>
{
    private void Require() => Skip.IfNot(world.Available, world.UnavailableReason);

    [SkippableFact]
    public async Task Changes_made_on_the_web_are_applied_on_a_sqlite_till_at_its_next_download()
    {
        Require();
        var store = await world.AddStoreAsync();
        var till = await world.NewJoinedTillAsync(store.Id);
        var session = await till.SessionAsync();
        var item = await till.Inventory.SaveAsync(new Item { LocationId = session.LocationId, Plu = "DOWN1", Name = "Coffee beans", Price1 = 14m, OnHand = 12 }, null, null, null);
        await till.SyncAsync();

        using var web = await world.SignInAsync();
        web.DefaultRequestHeaders.Add(CsrfGuard.Header, CsrfGuard.Value);
        // A price change, a new client, a new department, the store's name and receipt footer, and a new web user.
        var detail = (await web.GetFromJsonAsync<ItemDetail>($"/api/items/{item.Id}", SyncWorld.Json))!;
        using (var put = await web.PutAsJsonAsync($"/api/items/{item.Id}", detail with { Price1 = 15.5m, Name = "Coffee beans, dark" }))
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var newcomer = new ClientDetail(null, null, null, "Dana", "Down" + Guid.NewGuid().ToString("N")[..4], null, null, null, null, null, null, null, null, null,
            null, 0, 0, 0, null);
        using var clientResponse = await web.PostAsJsonAsync($"/api/clients?storeId={store.Id}", newcomer);
        var client = (await clientResponse.Content.ReadFromJsonAsync<ClientDetail>(SyncWorld.Json))!;
        var department = (await (await web.PostAsJsonAsync($"/api/departments?storeId={store.Id}", new NameRequest("Coffee"))).Content.ReadFromJsonAsync<NamedRow>(SyncWorld.Json))!;
        var settings = (await web.GetFromJsonAsync<StoreSettingsInfo>($"/api/stores/{store.Id}/settings", SyncWorld.Json))!;
        using (var put = await web.PutAsJsonAsync($"/api/stores/{store.Id}/settings", settings with { CompanyName = "Down the Road Coffee", ReceiptFooter = "See you soon" }))
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var cashierRole = (await new SetupService(world.ServerData).GetRolesAsync()).Single(r => r.Name == Permissions.CashierRole).Id;
        var userName = "down-" + Guid.NewGuid().ToString("N")[..6];
        using (var post = await web.PostAsJsonAsync("/api/users", new UserBody(null, userName, "Dee Down", cashierRole, store.Id, true, "Down-pass-1")))
            Assert.Equal(HttpStatusCode.Created, post.StatusCode);

        // Nothing has reached the till yet; one synchronisation later it has all of it.
        Assert.Equal(14m, (await till.Inventory.GetAsync(item.Id))!.Price1);
        var run = await till.SyncAsync();
        Assert.True(run.Received!.Changed > 0);

        var atTill = (await till.Inventory.GetAsync(item.Id))!;
        Assert.Equal((15.5m, "Coffee beans, dark"), (atTill.Price1, atTill.Name));
        Assert.Equal(12m, atTill.OnHand);
        Assert.Equal(client.LastName, (await till.Parties.GetClientAsync(client.Id!.Value))!.LastName);
        var tillSettings = await till.Setup.GetSettingsAsync();
        Assert.Equal("Down the Road Coffee", tillSettings.CompanyName);
        Assert.Equal("See you soon", await till.Setup.GetTextAsync(DocumentTextKind.InvoiceFooter));
        await using var db = till.Data.Create();
        Assert.True(await db.Departments.AnyAsync(d => d.Id == department.Id && d.Name == "COFFEE"));
        // The web user can sign in at the till with the password set on the web.
        Assert.True(await db.UserAccounts.AnyAsync(u => u.UserName == userName && u.StoreId == store.Id));
        Assert.NotNull((await till.Setup.SignInAsync(userName, "Down-pass-1")).User);

        // And the till's own change after that still goes up.
        atTill.Price2 = 13m;
        await till.Inventory.SaveAsync(atTill, null, null, null);
        await till.SyncAsync();
        await using var server = world.ServerData.Create();
        var onServer = await server.Items.AsNoTracking().SingleAsync(i => i.Id == item.Id);
        Assert.Equal((15.5m, 13m), (onServer.Price1, onServer.Price2));
    }
}

