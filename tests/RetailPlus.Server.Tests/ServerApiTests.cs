using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Server;
using RetailPlus.Sync;
using RetailPlus.Sync.Tests;

namespace RetailPlus.Server.Tests;

/// <summary>The server's HTTP API through WebApplicationFactory, on a throwaway LocalDB database.</summary>
public sealed class ServerApiTests(SyncWorld world) : IClassFixture<SyncWorld>
{
    private void Require() => Skip.IfNot(world.Available, world.UnavailableReason);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>(SyncWorld.Json))!;

    /// <summary>A user on the server with a standard (or given) role and an optional home store.</summary>
    private async Task<string> UserAsync(string role, Guid? store, string? name = null)
    {
        var setup = new SetupService(world.ServerData);
        var roleId = (await setup.GetRolesAsync()).Single(r => r.Name == role).Id;
        name ??= role.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N")[..6];
        await setup.SaveUserAsync(new UserAccount { UserName = name, RoleId = roleId, StoreId = store, IsActive = true }, "Password-1");
        return name;
    }

    [SkippableFact]
    public async Task Health_and_the_API_description_need_no_sign_in()
    {
        Require();
        using var client = world.Server.CreateClient();
        using var health = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("\"ok\"", await health.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var openApi = await client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/sync/upload", openApi, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/stores")).StatusCode);
    }

    [SkippableFact]
    public async Task Signing_in_gives_a_token_with_the_roles_permissions()
    {
        Require();
        using var client = world.Server.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(SyncWorld.AdminUser, SyncWorld.AdminPassword));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var signedIn = await ReadAsync<SignInResponse>(response);
        Assert.False(string.IsNullOrEmpty(signedIn.Token));
        Assert.Equal(Permissions.SuperAdministratorRole, signedIn.User.Role);
        Assert.Contains(Permissions.StoresAll, signedIn.User.Permissions);
        Assert.Null(signedIn.User.StoreId);

        using var admin = await world.SignInAsync();
        var me = await ReadAsync<UserInfo>(await admin.GetAsync("/api/auth/me"));
        Assert.Equal(SyncWorld.AdminUser, me.UserName);

        using var wrong = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(SyncWorld.AdminUser, "not-the-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        // A cashier works at the tills only.
        var cashier = await UserAsync(Permissions.CashierRole, null);
        using var refused = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(cashier, "Password-1"));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [SkippableFact]
    public async Task A_user_changes_their_password()
    {
        Require();
        var store = await world.AddStoreAsync();
        var name = await UserAsync(Permissions.AdministratorRole, store.Id);
        using var client = await world.SignInAsync(name, "Password-1");
        using var bad = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("wrong", "Another-pass-2"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using var good = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Password-1", "Another-pass-2"));
        Assert.Equal(HttpStatusCode.NoContent, good.StatusCode);
        using var again = await world.SignInAsync(name, "Another-pass-2");
        using var old = await world.Server.CreateClient().PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(name, "Password-1"));
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);

        // The change is in the audit log under the user's store, without the password.
        await using var db = world.ServerData.Create();
        var user = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.UserName == name);
        var line = await db.AuditEntries.AsNoTracking().SingleAsync(a => a.EntityId == user.Id && a.Action == "Changed" && a.UserId == user.Id);
        Assert.Equal(store.Id, line.StoreId);
        Assert.DoesNotContain("Another-pass-2", line.After ?? "", StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task A_store_administrator_sees_and_changes_only_their_own_store()
    {
        Require();
        var mine = await world.AddStoreAsync();
        var other = await world.AddStoreAsync();
        var otherTill = await world.AddStationAsync(other.Id);
        using var admin = await world.SignInAsync(await UserAsync(Permissions.AdministratorRole, mine.Id), "Password-1");

        var stores = await ReadAsync<List<StoreInfo>>(await admin.GetAsync("/api/stores"));
        Assert.Equal([mine.Id], stores.Select(s => s.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/stores/{other.Id}/stations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/stores/{other.Id}/stations", new NewStationRequest(null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/stations/{otherTill.Station.Id}/retire", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/stores/{other.Id}", new StoreRequest(other.Code, "Taken over", null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/sync/batches?storeId={other.Id}")).StatusCode);
        // Only a super administrator adds stores.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/stores", new StoreRequest("NEWONE", "New", null))).StatusCode);

        using var created = await admin.PostAsJsonAsync($"/api/stores/{mine.Id}/stations", new NewStationRequest(null, "Front till"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var stations = await ReadAsync<List<StationInfo>>(await admin.GetAsync($"/api/stores/{mine.Id}/stations"));
        Assert.Equal("Front till", Assert.Single(stations).Name);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/stores/{mine.Id}", new StoreRequest(mine.Code, "Renamed", null))).StatusCode);

        // The till of the other store sends a package; this administrator does not see it.
        var till = await world.JoinAsync(await world.NewTillAsync(), other.Id);
        await till.SyncAsync();
        var batches = await ReadAsync<List<BatchInfo>>(await admin.GetAsync("/api/sync/batches"));
        Assert.DoesNotContain(batches, b => b.StoreId == other.Id);
    }

    [SkippableFact]
    public async Task Each_endpoint_needs_its_permission()
    {
        Require();
        var store = await world.AddStoreAsync();
        await new SetupService(world.ServerData).SaveRoleAsync(new Role { Name = "Viewer " + Guid.NewGuid().ToString("N")[..4], Level = 1 }, [Permissions.WebSignIn]);
        var role = (await new SetupService(world.ServerData).GetRolesAsync()).First(r => r.Name.StartsWith("Viewer", StringComparison.Ordinal));
        var name = "viewer-" + Guid.NewGuid().ToString("N")[..6];
        await new SetupService(world.ServerData).SaveUserAsync(new UserAccount { UserName = name, RoleId = role.Id, StoreId = store.Id, IsActive = true }, "Password-1");
        using var viewer = await world.SignInAsync(name, "Password-1");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/stores")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/stores/{store.Id}/stations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/stores/{store.Id}/stations", new NewStationRequest(null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/stores/{store.Id}", new StoreRequest(store.Code, "X", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/sync/upload-file", new ByteArrayContent([1, 2, 3]))).StatusCode);

        // Taking web.signin away from the role turns the user away at the next request, whatever the token says.
        await new SetupService(world.ServerData).SaveRoleAsync(role, [Permissions.ReportsView]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.GetAsync("/api/stores")).StatusCode);
    }

    [SkippableFact]
    public async Task A_till_joins_once_with_its_code_sends_and_receives_and_is_shown_with_its_status()
    {
        Require();
        var store = await world.AddStoreAsync();
        var code = await world.AddStationAsync(store.Id);
        using var anonymous = world.Server.CreateClient();
        using (var unknown = await anonymous.PostAsJsonAsync("/api/sync/enroll", new EnrollRequest("ZZZZ-ZZZZ", "test", null)))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // A code is used once.
        using (var first = await anonymous.PostAsJsonAsync("/api/sync/enroll", new EnrollRequest(code.Code, "test", null)))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var joined = await ReadAsync<EnrollResponse>(first);
            Assert.Equal((store.Id, code.Station.Number), (joined.StoreId, joined.StationNumber));
            Assert.NotEmpty(joined.Package);
        }
        using (var twice = await anonymous.PostAsJsonAsync("/api/sync/enroll", new EnrollRequest(code.Code, "test", null)))
            Assert.Equal(HttpStatusCode.NotFound, twice.StatusCode);

        var till = await world.NewTillAsync();
        await world.JoinAsync(till, store.Id);

        var session = await till.SessionAsync();
        await till.Inventory.SaveAsync(new Item { LocationId = session.LocationId, Plu = "API1", Name = "Through the API", Price1 = 5, OnHand = 2 }, null, null, null);
        var run = await till.SyncAsync();
        Assert.Equal(1, run.Sent);

        // The upload and download endpoints refuse requests that are not signed by a joined till.
        using (var unsigned = await anonymous.PostAsync("/api/sync/upload", new ByteArrayContent([1, 2, 3])))
            Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/sync/download?cursor=0")).StatusCode);

        using var admin = await world.SignInAsync();
        var stations = await ReadAsync<List<StationInfo>>(await admin.GetAsync($"/api/stores/{store.Id}/stations"));
        var line = stations.Single(s => s.Number == till.Data.Station!.StationNumber);
        Assert.True(line.Enrolled);
        Assert.NotNull(line.LastUploadAtUtc);
        Assert.NotNull(line.LastDownloadAtUtc);
        Assert.Equal("test", line.AppVersion);
        Assert.Equal(SyncBatchStatus.Applied, line.LastBatch!.Status);
        Assert.Equal(0, line.FailedBatches);
        Assert.False(line.Behind);
        var batches = await ReadAsync<List<BatchInfo>>(await admin.GetAsync($"/api/sync/batches?storeId={store.Id}&station={line.Number}&status=Applied"));
        Assert.Contains(batches, b => b.Direction == SyncDirection.Upload && b.Rows > 0);

        // Retired: the till's key no longer works.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/stations/{line.Id}/retire", null)).StatusCode);
        var after = await till.Sync.RunAsync();
        Assert.False(after.Ok);
        Assert.Contains("retired", after.Error, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task A_package_from_a_usb_stick_is_uploaded_by_an_administrator_of_its_store_only()
    {
        Require();
        var store = await world.AddStoreAsync();
        var other = await world.AddStoreAsync();
        var till = await world.NewJoinedTillAsync(store.Id);
        var session = await till.SessionAsync();
        var item = await till.Inventory.SaveAsync(new Item { LocationId = session.LocationId, Plu = "STICK", Name = "On a stick", Price1 = 5, OnHand = 9 }, null, null, null);
        var file = await till.Sync.SavePackageToFileAsync(Path.Combine(world.Folder, "stick-" + Guid.NewGuid().ToString("N")[..6]), includeDatabase: false);
        using var content = new MultipartFormDataContent { { new ByteArrayContent(await File.ReadAllBytesAsync(file)), "file", Path.GetFileName(file) } };

        using var stranger = await world.SignInAsync(await UserAsync(Permissions.AdministratorRole, other.Id), "Password-1");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync("/api/sync/upload-file", content)).StatusCode);

        using var admin = await world.SignInAsync(await UserAsync(Permissions.AdministratorRole, store.Id), "Password-1");
        using var response = await admin.PostAsync("/api/sync/upload-file", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsync<UploadResponse>(response);
        Assert.Equal(nameof(SyncBatchStatus.Applied), result.Status);
        await using (var db = world.ServerData.Create()) Assert.Equal(9m, (await db.Items.SingleAsync(i => i.Id == item.Id)).OnHand);

        // The same file again is a duplicate; a file that is not a package is refused.
        Assert.Equal(nameof(SyncBatchStatus.Duplicate), (await ReadAsync<UploadResponse>(await admin.PostAsync("/api/sync/upload-file", content))).Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsync("/api/sync/upload-file", new ByteArrayContent([1, 2, 3]))).StatusCode);

        // The server's package for the till, for the way back.
        using var download = await admin.GetAsync($"/api/sync/download-file?station={till.Data.Station!.StationNumber}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.EndsWith(SyncPackage.Extension, download.Content.Headers.ContentDisposition!.FileName!.Trim('"'), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/sync/download-file?station={till.Data.Station.StationNumber}")).StatusCode);
    }
}
