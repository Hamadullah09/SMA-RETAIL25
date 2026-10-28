using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Server;
using RetailPlus.Sync.Tests;

namespace RetailPlus.Rfid.Tests;

/// <summary>
/// An RFID hub that reads its store's readers from the server: readers set up at a till and sent to the server reach a
/// hub on another machine, which has a hub key and no database. The server runs in-process on a throwaway LocalDB
/// database, as in the sync tests.
/// </summary>
public sealed class HubFromServerTests(SyncWorld world) : IClassFixture<SyncWorld>, IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RetailPlus_RfidHub_" + Guid.NewGuid().ToString("N")[..12]);

    private void Require() => Skip.IfNot(world.Available, world.UnavailableReason);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static RfidReader SimulatedReader(string name, int station, string epc, string hub = "main") => new()
    {
        Name = name, HubKey = hub, Host = "sim", Port = 4001, Notes = "1:" + epc, DedupSeconds = 60,
        Antennas = [new RfidAntenna { Port = 1, Name = name + " mat", IsEnabled = true, StationNumber = station }],
    };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>(SyncWorld.Json))!;

    private static async Task<NewRfidHubResponse> NewHubKeyAsync(HttpClient user, Guid storeId, string name = "Back office PC")
    {
        using var response = await user.PostAsJsonAsync($"/api/stores/{storeId}/rfid-hubs", new NewRfidHubRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<NewRfidHubResponse>(response);
    }

    /// <summary>A client that sends a hub key, as the hub does (none when <paramref name="key"/> is null).</summary>
    private HttpClient HubClient(string? key)
    {
        var client = world.Server.CreateClient();
        if (key is not null) client.DefaultRequestHeaders.Add(RfidServerReaders.KeyHeader, key);
        return client;
    }

    /// <summary>A user of the server with a role and a home store, signed in.</summary>
    private async Task<HttpClient> UserAsync(string role, Guid store)
    {
        var setup = new SetupService(world.ServerData);
        var roleId = (await setup.GetRolesAsync()).Single(r => r.Name == role).Id;
        var name = "rfid-" + Guid.NewGuid().ToString("N")[..8];
        await setup.SaveUserAsync(new UserAccount { UserName = name, RoleId = roleId, StoreId = store, IsActive = true }, "Password-1");
        return await world.SignInAsync(name, "Password-1");
    }

    /// <summary>The first tag a station hears, waiting up to 30 seconds.</summary>
    private static async Task<RfidTagEvent> FirstTagAsync(RfidHub hub, int station)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await foreach (var e in hub.ListenAsync(station, deadline.Token)) return e;
        }
        catch (OperationCanceledException)
        {
        }
        throw new TimeoutException($"Station {station} heard no tag within 30 seconds.");
    }

    [SkippableFact]
    public async Task A_hub_key_reads_its_own_stores_readers_and_nothing_else()
    {
        Require();
        var storeA = await world.AddStoreAsync();
        var storeB = await world.AddStoreAsync();
        var tillA = await world.NewJoinedTillAsync(storeA.Id);
        var tillB = await world.NewJoinedTillAsync(storeB.Id);
        await new RfidService(tillA.Data).SaveReaderAsync(SimulatedReader("Counter A", tillA.Data.Station!.StationNumber, "AAAA0001"));
        await new RfidService(tillA.Data).SaveReaderAsync(SimulatedReader("Back room", tillA.Data.Station!.StationNumber, "AAAA0002", hub: "back"));
        await new RfidService(tillB.Data).SaveReaderAsync(SimulatedReader("Counter B", tillB.Data.Station!.StationNumber, "BBBB0001"));
        await tillA.SyncAsync();
        await tillB.SyncAsync();

        using var admin = await world.SignInAsync();
        var made = await NewHubKeyAsync(admin, storeA.Id);
        Assert.StartsWith("rfh_", made.Key, StringComparison.Ordinal);
        Assert.Equal(47, made.Key.Length);
        Assert.StartsWith(made.Hub.KeyHint, made.Key, StringComparison.Ordinal);
        Assert.Equal(storeA.Id, made.Hub.StoreId);

        // The key is shown once: the list has its first characters, and the server keeps only a hash.
        var listText = await admin.GetStringAsync($"/api/stores/{storeA.Id}/rfid-hubs");
        Assert.DoesNotContain(made.Key, listText, StringComparison.Ordinal);
        await using (var db = world.ServerData.Create())
        {
            var row = await db.RfidHubLinks.AsNoTracking().SingleAsync(h => h.Id == made.Hub.Id);
            Assert.Equal(RfidHubKeys.Hash(made.Key), row.KeyHash);
            Assert.DoesNotContain(made.Key, row.KeyHash, StringComparison.Ordinal);
        }

        // The store's readers of the hub asked for, with their antennas and settings; never another store's.
        using var hubA = HubClient(made.Key);
        var main = await ReadAsync<RfidServerReaderList>(await hubA.GetAsync("/api/rfid/readers?hub=main"));
        Assert.Equal((storeA.Id, storeA.Code), (main.StoreId, main.StoreCode));
        var counter = Assert.Single(main.Readers);
        Assert.Equal("Counter A", counter.Name);
        Assert.Equal("1:AAAA0001", counter.Notes);
        Assert.Equal(tillA.Data.Station!.StationNumber, counter.Antennas.Single(a => a.Port == 1).StationNumber);
        Assert.Equal(4, counter.Antennas.Count);
        Assert.Equal(["Back room"], (await ReadAsync<RfidServerReaderList>(await hubA.GetAsync("/api/rfid/readers?hub=back"))).Readers.Select(r => r.Name));
        Assert.Equal(["Back room", "Counter A"], (await ReadAsync<RfidServerReaderList>(await hubA.GetAsync("/api/rfid/readers"))).Readers.Select(r => r.Name));
        var listed = Assert.Single(await ReadAsync<List<RfidHubInfo>>(await admin.GetAsync($"/api/stores/{storeA.Id}/rfid-hubs")));
        Assert.NotNull(listed.LastSeenAtUtc);
        Assert.Empty(await ReadAsync<List<RfidHubInfo>>(await admin.GetAsync($"/api/stores/{storeB.Id}/rfid-hubs")));

        // Store B's key reads store B's readers.
        using var hubB = HubClient((await NewHubKeyAsync(admin, storeB.Id)).Key);
        Assert.Equal(["Counter B"], (await ReadAsync<RfidServerReaderList>(await hubB.GetAsync("/api/rfid/readers?hub=main"))).Readers.Select(r => r.Name));

        // No key, a wrong key, a signed-in user instead of a key, and a revoked key are refused.
        using (var none = HubClient(null))
            Assert.Equal(HttpStatusCode.Unauthorized, (await none.GetAsync("/api/rfid/readers")).StatusCode);
        using (var wrong = HubClient(RfidHubKeys.New()))
            Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync("/api/rfid/readers")).StatusCode);
        using (var almost = HubClient(made.Key[..^1] + (made.Key[^1] == 'A' ? 'B' : 'A')))
            Assert.Equal(HttpStatusCode.Unauthorized, (await almost.GetAsync("/api/rfid/readers")).StatusCode);
        using (var user = HubClient(null))
        {
            user.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;
            Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/rfid/readers")).StatusCode);
        }
        using var revoke = await admin.PostAsync($"/api/rfid-hubs/{made.Hub.Id}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.True((await ReadAsync<RfidHubInfo>(revoke)).Revoked);
        Assert.Equal(HttpStatusCode.Unauthorized, (await hubA.GetAsync("/api/rfid/readers")).StatusCode);

        // Making and revoking a key are in the store's audit log.
        await using (var db = world.ServerData.Create())
        {
            var actions = await db.AuditEntries.AsNoTracking().Where(a => a.StoreId == storeA.Id && a.EntityId == made.Hub.Id).Select(a => a.Action).ToListAsync();
            Assert.Equal(["RFID hub key made", "RFID hub key revoked"], actions.Order(StringComparer.Ordinal));
        }
    }

    [SkippableFact]
    public async Task Hub_keys_are_made_and_revoked_by_an_administrator_of_the_store()
    {
        Require();
        var storeA = await world.AddStoreAsync();
        var storeB = await world.AddStoreAsync();
        using var super = await world.SignInAsync();
        var keyOfA = await NewHubKeyAsync(super, storeA.Id);

        // A store administrator manages their own store's hub keys, and sees nothing of another store's.
        using var adminB = await UserAsync("Administrator", storeB.Id);
        var keyOfB = await NewHubKeyAsync(adminB, storeB.Id);
        Assert.Equal(keyOfB.Hub.Id, Assert.Single(await ReadAsync<List<RfidHubInfo>>(await adminB.GetAsync($"/api/stores/{storeB.Id}/rfid-hubs"))).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync($"/api/stores/{storeA.Id}/rfid-hubs")).StatusCode);
        using (var other = await adminB.PostAsJsonAsync($"/api/stores/{storeA.Id}/rfid-hubs", new NewRfidHubRequest("Mine now")))
            Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        using (var other = await adminB.PostAsync($"/api/rfid-hubs/{keyOfA.Hub.Id}/revoke", null))
            Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        using (var hubA = HubClient(keyOfA.Key))
            Assert.Equal(HttpStatusCode.OK, (await hubA.GetAsync("/api/rfid/readers")).StatusCode);

        // A role without "Add stores and tills" may see the list but not make a key.
        var viewer = "Web viewer " + Guid.NewGuid().ToString("N")[..6];
        await new SetupService(world.ServerData).SaveRoleAsync(new Role { Name = viewer, Level = 1 }, [Permissions.WebSignIn]);
        using var signedIn = await UserAsync(viewer, storeB.Id);
        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync($"/api/stores/{storeB.Id}/rfid-hubs")).StatusCode);
        using (var refused = await signedIn.PostAsJsonAsync($"/api/stores/{storeB.Id}/rfid-hubs", new NewRfidHubRequest("No")))
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using (var refused = await signedIn.PostAsync($"/api/rfid-hubs/{keyOfB.Hub.Id}/revoke", null))
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // A name longer than the list shows is refused.
        using (var tooLong = await adminB.PostAsJsonAsync($"/api/stores/{storeB.Id}/rfid-hubs", new NewRfidHubRequest(new string('x', 41))))
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminB.PostAsync($"/api/rfid-hubs/{keyOfB.Hub.Id}/revoke", null)).StatusCode);
    }

    [SkippableFact]
    public async Task The_hub_picks_up_reader_changes_from_the_server_by_itself()
    {
        Require();
        var store = await world.AddStoreAsync();
        var till = await world.NewJoinedTillAsync(store.Id);
        int station = till.Data.Station!.StationNumber;
        var rfid = new RfidService(till.Data);
        var counter = await rfid.SaveReaderAsync(SimulatedReader("Counter", station, "E2000000000000000000A001"));
        await till.SyncAsync();
        using var admin = await world.SignInAsync();
        var key = await NewHubKeyAsync(admin, store.Id);

        // The hub's machine has no copy of the till's database: the reader list comes from the server, read again every
        // 200 ms here (every minute by default).
        using var readers = new RfidServerReaders(SyncWorld.ServerUrl, key.Key, "main", Path.Combine(_folder, "readers.json"), world.Server.Server.CreateHandler());
        await using var hub = new RfidHub(readers.LoadAsync);
        await hub.StartAsync();
        using var stop = new CancellationTokenSource();
        var keepCurrent = hub.KeepCurrentAsync(TimeSpan.FromMilliseconds(200), null, stop.Token);
        Assert.Equal("E2000000000000000000A001", (await FirstTagAsync(hub, station)).Epc);
        Assert.Equal(store.Name, readers.StoreName);
        var device = hub.DeviceOf(counter.Id);

        // At the till the counter's antenna goes to another station and a gate reader is added; the till sends its changes.
        var changed = (await rfid.GetReaderAsync(counter.Id))!;
        changed.Antennas.Single(a => a.Port == 1).StationNumber = 990;
        await rfid.SaveReaderAsync(changed);
        var gate = await rfid.SaveReaderAsync(SimulatedReader("Gate", 991, "E2000000000000000000B002"));
        await till.SyncAsync();

        // The hub has both by itself, and the moved antenna kept its reader's connection.
        Assert.Equal("E2000000000000000000A001", (await FirstTagAsync(hub, 990)).Epc);
        Assert.Equal("E2000000000000000000B002", (await FirstTagAsync(hub, 991)).Epc);
        Assert.Same(device, hub.DeviceOf(counter.Id));

        // A reader deleted at the till goes from the hub too.
        await rfid.DeleteReaderAsync(gate.Id);
        await till.SyncAsync();
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until && hub.DeviceOf(gate.Id) is not null) await Task.Delay(100);
        Assert.Null(hub.DeviceOf(gate.Id));
        Assert.Equal(["Counter"], (await hub.GetStatusAsync()).Select(r => r.Name));

        await stop.CancelAsync();
        await keepCurrent;
    }
}
