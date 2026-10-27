using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Sync.Tests;

namespace RetailPlus.Server.Tests;

/// <summary>
/// A company for the back-office tests, made once per test class the way a real one comes about: two stores with two
/// tills each (SQLite files) that sold on several days, voided a sale, sold and spent a gift card, shipped and received a
/// transfer, counted stock and punched the time clock, and sent it all to the server. Plus a user of each kind.
/// </summary>
public sealed class BackOffice : IAsyncLifetime
{
    public const string Password = "Password-1";

    public SyncWorld World { get; } = new();
    public bool Available => World.Available;
    public string UnavailableReason => World.UnavailableReason;

    public StoreInfo StoreA { get; private set; } = null!;
    public StoreInfo StoreB { get; private set; } = null!;
    public Till A1 { get; private set; } = null!;
    public Till A2 { get; private set; } = null!;
    public Till B1 { get; private set; } = null!;
    public Till B2 { get; private set; } = null!;

    /// <summary>Administrators of one store (every permission but <see cref="Permissions.StoresAll"/>).</summary>
    public string AdminA { get; private set; } = "";
    public string AdminB { get; private set; } = "";
    /// <summary>A user of store A who may sign in to the web and do nothing else.</summary>
    public string ViewerA { get; private set; } = "";
    /// <summary>A user of store A with the reports but not the staff reports.</summary>
    public string ReporterA { get; private set; } = "";
    public UserAccount CashierA { get; private set; } = null!;
    public UserAccount CashierB { get; private set; } = null!;

    public Item ItemA1 { get; private set; } = null!;
    public Item ItemA2 { get; private set; } = null!;
    public Item ItemB1 { get; private set; } = null!;
    public string GiftCardNumber { get; } = "GC" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    public Guid VoidedSaleId { get; private set; }
    public Guid SaleAtBId { get; private set; }
    public Guid TransferId { get; private set; }
    public Guid CountId { get; private set; }
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);

    public async Task InitializeAsync()
    {
        await World.InitializeAsync();
        if (!Available) return;
        StoreA = await World.AddStoreAsync("A" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant());
        StoreB = await World.AddStoreAsync("B" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant());

        AdminA = await UserAsync(Permissions.AdministratorRole, StoreA.Id);
        AdminB = await UserAsync(Permissions.AdministratorRole, StoreB.Id);
        ViewerA = await UserWithAsync("Web only", StoreA.Id, [Permissions.WebSignIn]);
        ReporterA = await UserWithAsync("Reports only", StoreA.Id, [Permissions.WebSignIn, Permissions.ReportsView]);
        CashierA = await AccountAsync(await UserAsync(Permissions.CashierRole, StoreA.Id));
        CashierB = await AccountAsync(await UserAsync(Permissions.CashierRole, StoreB.Id));

        A1 = await World.NewJoinedTillAsync(StoreA.Id);
        A2 = await World.NewJoinedTillAsync(StoreA.Id);
        B1 = await World.NewJoinedTillAsync(StoreB.Id);
        B2 = await World.NewJoinedTillAsync(StoreB.Id);

        // The items, made at one till of each store and sent to the other.
        var atA = await A1.SessionAsync();
        ItemA1 = await A1.Inventory.SaveAsync(new Item { LocationId = atA.LocationId, Plu = "A-SNACK", Name = "Trail mix", Price1 = 10m, AverageCost = 4m, LastCost = 4m, OnHand = 50 },
            "FOOD", "SNACKS", null);
        ItemA2 = await A1.Inventory.SaveAsync(new Item { LocationId = atA.LocationId, Plu = "A-LAMP", Name = "Desk lamp", Price1 = 25m, AverageCost = 10m, LastCost = 10m, OnHand = 20 },
            "HOME", null, null);
        var giftItem = await A1.Inventory.SaveAsync(new Item { LocationId = atA.LocationId, Plu = "A-GIFT", Name = "Gift card", ItemType = ItemType.GiftCard }, null, null, null);
        var atB = await B1.SessionAsync();
        ItemB1 = await B1.Inventory.SaveAsync(new Item { LocationId = atB.LocationId, Plu = "B-TEA", Name = "Green tea", Price1 = 7m, AverageCost = 3m, LastCost = 3m, OnHand = 30 },
            "FOOD", null, null);
        foreach (var till in new[] { A1, A2, B1, B2 }) await till.SyncAsync();

        // Sales on several days (the tills record the time they sell; earlier days are set before the sale is sent).
        await SellAsync(A1, CashierA, ItemA1, 2, daysAgo: 2);
        await SellAsync(A1, CashierA, ItemA2, 1, tender: "Visa", daysAgo: 1);
        await SellAsync(A1, CashierA, ItemA1, 3);
        await SellAsync(A1, CashierA, ItemA2, 1, daysAgo: 4);
        var card = new SaleDraftLine
        {
            ItemId = giftItem.Id, ItemType = ItemType.GiftCard, Plu = giftItem.Plu, Description = giftItem.Name, Quantity = 1, UnitPrice = 50m,
            GiftCardNumber = GiftCardNumber, GiftCardKind = GiftCardKind.Card,
        };
        await PostAsync(A1, CashierA, new SaleDraft { Lines = [card] }, "Cash", 0);
        await A2.SyncAsync();
        var voided = await SellAsync(A2, CashierA, (await A2.Inventory.GetAsync(ItemA1.Id))!, 1);
        await A2.Sales.VoidSaleAsync(voided.Id, await SessionAsync(A2, CashierA), "Wrong item");
        VoidedSaleId = voided.Id;
        SaleAtBId = (await SellAsync(B1, CashierB, ItemB1, 3)).Id;
        await SellAsync(B1, CashierB, ItemB1, 2, tender: "Debit", daysAgo: 1);

        // Time clock: two days at store A, one at store B.
        var yesterday = DateTime.Today.AddDays(-1);
        var staffA = new StaffService(A1.Data);
        await staffA.PunchInAsync(CashierA.Id, yesterday.AddDays(-1).AddHours(9));
        await staffA.PunchOutAsync(CashierA.Id, yesterday.AddDays(-1).AddHours(13));
        await staffA.PunchInAsync(CashierA.Id, yesterday.AddHours(9));
        await staffA.PunchOutAsync(CashierA.Id, yesterday.AddHours(17));
        var staffB = new StaffService(B1.Data);
        await staffB.PunchInAsync(CashierB.Id, yesterday.AddHours(10));
        await staffB.PunchOutAsync(CashierB.Id, yesterday.AddHours(16));

        // A transfer from A to B.
        var transfers = new TransferService(A1.Data);
        var transfer = await transfers.CreateAsync(atA.LocationId, atB.LocationId, "Restock B", CashierA.UserName);
        await transfers.AddItemAsync(transfer.Id, ItemA1.Plu, 4);
        await transfers.ShipAsync(transfer.Id, CashierA.UserName);
        TransferId = transfer.Id;

        foreach (var till in new[] { A1, A2, B1, B2 }) await till.SyncAsync();

        // Store B: the gift card from store A is spent there, the transfer arrives, and stock is counted.
        await PostAsync(B2, CashierB, Draft((await B2.Inventory.GetAsync(ItemB1.Id))!, 1), "Gift Card", 0, GiftCardNumber);
        await new TransferService(B1.Data).ReceiveAsync(TransferId, null, CashierB.UserName);
        var counts = new StockCountService(B1.Data);
        var count = await counts.StartAsync(atB.LocationId, "Shelf count", CashierB.UserName);
        await counts.CountCodeAsync(count.Id, ItemB1.Plu, 23);
        await counts.PostAsync(count.Id, zeroUncounted: false, CashierB.UserName);
        CountId = count.Id;

        foreach (var till in new[] { B2, B1, A1, A2 }) await till.SyncAsync();
    }

    public Task DisposeAsync() => World.DisposeAsync();

    // Users -----------------------------------------------------------------------------------------------------------

    /// <summary>A user on the server with a standard role and an optional home store; returns the user name.</summary>
    public async Task<string> UserAsync(string role, Guid? store)
    {
        var setup = new SetupService(World.ServerData);
        var roleId = (await setup.GetRolesAsync()).Single(r => r.Name == role).Id;
        var name = role.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N")[..6];
        await setup.SaveUserAsync(new UserAccount { UserName = name, RoleId = roleId, StoreId = store, IsActive = true }, Password);
        return name;
    }

    /// <summary>A user with a role of their own that has just these permissions.</summary>
    public async Task<string> UserWithAsync(string roleName, Guid? store, string[] permissions)
    {
        var setup = new SetupService(World.ServerData);
        var role = await setup.SaveRoleAsync(new Role { Name = roleName + " " + Guid.NewGuid().ToString("N")[..4], Level = 1 }, permissions);
        var name = "user-" + Guid.NewGuid().ToString("N")[..8];
        await setup.SaveUserAsync(new UserAccount { UserName = name, RoleId = role.Id, StoreId = store, IsActive = true }, Password);
        return name;
    }

    public async Task<UserAccount> AccountAsync(string name)
    {
        await using var db = World.ServerData.Create();
        return await db.UserAccounts.AsNoTracking().SingleAsync(u => u.UserName == name);
    }

    /// <summary>A client signed in with a bearer token (super administrator unless a user is given).</summary>
    public Task<HttpClient> SignInAsync(string? user = null) =>
        user is null ? World.SignInAsync() : World.SignInAsync(user, Password);

    // Selling at a till -----------------------------------------------------------------------------------------------

    public static SaleDraft Draft(Item item, decimal quantity) => new()
    {
        Lines =
        [
            new SaleDraftLine
            {
                ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = quantity, UnitPrice = item.Price1,
                UnitCost = item.AverageCost, Taxable1 = item.Taxable1, Taxable2 = item.Taxable2,
            },
        ],
    };

    private static async Task<SessionInfo> SessionAsync(Till till, UserAccount user)
    {
        var session = await till.SessionAsync();
        session.UserId = user.Id;
        session.UserName = user.UserName;
        return session;
    }

    private static async Task<Sale> SellAsync(Till till, UserAccount user, Item item, decimal quantity, string tender = "Cash", int daysAgo = 0) =>
        await PostAsync(till, user, Draft(item, quantity), tender, daysAgo);

    /// <summary>Posts a sale paid in full by one tender, dated some days back if asked (before the till sends it).</summary>
    private static async Task<Sale> PostAsync(Till till, UserAccount user, SaleDraft draft, string tender, int daysAgo, string? reference = null)
    {
        StoreSettings settings;
        Dictionary<string, TenderKind> kinds;
        await using (var db = till.Data.Create())
        {
            settings = await db.CurrentStoreSettings.AsNoTracking().SingleOrDefaultAsync() ?? new StoreSettings();
            kinds = await db.TenderTypes.AsNoTracking().ToDictionaryAsync(t => t.Name, t => t.Kind);
        }
        var kind = kinds[tender];
        decimal total = draft.Calculate(draft.EffectiveTax(settings)).Total;
        if (kind == TenderKind.Cash) total = Money.RoundToMinimumTender(total, settings.MinimumTender);
        draft.Staff = user.UserName;
        draft.StaffId = user.Id;
        var sale = await till.Sales.PostSaleAsync(draft, [new TenderEntry(tender, kind, total, total, Reference: reference)], await SessionAsync(till, user));
        if (daysAgo != 0)
        {
            await using var db = till.Data.Create();
            var soldAt = sale.SoldAt.AddDays(-daysAgo);
            await db.Sales.Where(s => s.Id == sale.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SoldAt, soldAt));
        }
        return sale;
    }

    // Reading answers -------------------------------------------------------------------------------------------------

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(SyncWorld.Json))!;
    }

    public static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        return await ReadAsync<T>(response);
    }
}
