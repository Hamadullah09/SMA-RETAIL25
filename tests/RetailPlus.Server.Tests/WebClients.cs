using System.Net;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Sync.Tests;

namespace RetailPlus.Server.Tests;

/// <summary>Signed-in web clients for the tests that build their own stores on a <see cref="SyncWorld"/>.</summary>
public static class WebClients
{
    /// <summary>The password of the users made here (the same as <see cref="BackOffice.Password"/>).</summary>
    public const string UserPassword = BackOffice.Password;

    /// <summary>A new administrator of one store (every permission but <see cref="Permissions.StoresAll"/>), signed in, with the CSRF header.</summary>
    public static async Task<HttpClient> AdminOfAsync(this SyncWorld world, Guid store)
    {
        var setup = new SetupService(world.ServerData);
        var role = (await setup.GetRolesAsync()).Single(r => r.Name == Permissions.AdministratorRole).Id;
        var name = "admin-" + Guid.NewGuid().ToString("N")[..8];
        await setup.SaveUserAsync(new UserAccount { UserName = name, RoleId = role, StoreId = store, IsActive = true }, UserPassword);
        var client = await world.SignInAsync(name, UserPassword);
        client.DefaultRequestHeaders.Add(CsrfGuard.Header, CsrfGuard.Value);
        return client;
    }

    /// <summary>The super administrator, signed in, with the CSRF header.</summary>
    public static async Task<HttpClient> SuperAsync(this SyncWorld world)
    {
        var client = await world.SignInAsync();
        client.DefaultRequestHeaders.Add(CsrfGuard.Header, CsrfGuard.Value);
        return client;
    }

    public static async Task<HttpStatusCode> StatusAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        return response.StatusCode;
    }
}
