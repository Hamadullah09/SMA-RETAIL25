using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server.Tests;

/// <summary>Someone can always manage every store: on the first start, and after the Retail25 cutover.</summary>
public sealed class ServerStartupTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"rp-startup-{Guid.NewGuid():N}.db");
    private readonly DataContext _data;

    public ServerStartupTests() => _data = new DataContext(DatabaseConnection.SqliteFile(_file));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _file, _file + "-wal", _file + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }

    private Task StartAsync(string? adminUserName, string? adminPassword = null) =>
        new ServerStartup(_data, Options.Create(new BootstrapOptions { AdminUserName = adminUserName, AdminPassword = adminPassword }), NullLogger<ServerStartup>.Instance)
            .RunAsync();

    private async Task<UserAccount?> UserAsync(string name)
    {
        await using var db = _data.Create();
        return await db.UserAccounts.AsNoTracking().Include(u => u.Role).SingleOrDefaultAsync(u => u.UserName == name);
    }

    [Fact]
    public async Task The_first_start_makes_the_configured_super_administrator()
    {
        await StartAsync("boss", "Boss-pass-1");
        var boss = await UserAsync("boss");
        Assert.Equal(Permissions.SuperAdministratorRole, boss!.Role!.Name);
        Assert.True(SetupService.VerifyPassword("Boss-pass-1", boss.PasswordHash!));
        Assert.False(boss.MustChangePassword);
    }

    [Fact]
    public async Task After_the_cutover_only_the_named_user_is_made_super_administrator()
    {
        // A database as the cutover leaves it: users of one store, nobody who sees every store.
        await _data.MigrateAsync();
        await using (var db = _data.Create())
        {
            await SetupService.SeedRolesAsync(db);
            await db.SaveChangesAsync();
        }
        var setup = new SetupService(_data);
        var administrator = (await setup.GetRolesAsync()).Single(r => r.Name == Permissions.AdministratorRole);
        await setup.SaveUserAsync(new UserAccount { UserName = "owner", RoleId = administrator.Id, IsActive = true }, "Owner-pass-1");

        await StartAsync(null);
        Assert.Equal(Permissions.AdministratorRole, (await UserAsync("owner"))!.Role!.Name);
        Assert.Null(await UserAsync("admin"));

        await StartAsync("owner");
        var owner = await UserAsync("owner");
        Assert.Equal(Permissions.SuperAdministratorRole, owner!.Role!.Name);
        Assert.Null(owner.StoreId);
        Assert.True(SetupService.VerifyPassword("Owner-pass-1", owner.PasswordHash!));

        // Once someone sees every store, the setting changes nothing.
        await StartAsync("someone-else");
        Assert.Null(await UserAsync("someone-else"));
    }

    // The standard roles of a database in use --------------------------------------------------------------------------

    private async Task<List<RoleGrant>> GrantsOfAsync(string role)
    {
        await using var db = _data.Create();
        var id = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).SingleAsync();
        return await db.RoleGrants.AsNoTracking().Where(g => g.RoleId == id).ToListAsync();
    }

    private async Task<bool> HasAsync(string role, string key) => (await GrantsOfAsync(role)).Any(g => g.Permission == key);

    /// <summary>Takes the permission away as an administrator does, on the Roles page.</summary>
    private async Task TakeAwayAsync(string role, string key)
    {
        var setup = new SetupService(_data);
        var stored = (await setup.GetRolesAsync()).Single(r => r.Name == role);
        await setup.SaveRoleAsync(stored, stored.Permissions.Select(p => p.Permission).Where(p => p != key));
    }

    [Fact]
    public async Task A_database_in_use_gets_the_new_keys_of_its_standard_roles_once()
    {
        await StartAsync("boss", "Boss-pass-1");
        var manager = await GrantsOfAsync(Permissions.ManagerRole);
        Assert.All(manager, g => Assert.Equal(SetupService.StandardGrantId(g.RoleId, g.Permission), g.Id));

        // As a database made before managers signed in to the web and marketing and HR saw every store.
        await using (var db = _data.Create())
            foreach (var (role, key) in Permissions.KeysAddedToStandardRoles)
            {
                var roleId = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).SingleAsync();
                await db.RoleGrants.Where(g => g.RoleId == roleId && g.Permission == key).ExecuteDeleteAsync();
            }
        Assert.False(await HasAsync(Permissions.ManagerRole, Permissions.WebSignIn));

        await StartAsync(null);
        Assert.True(await HasAsync(Permissions.ManagerRole, Permissions.WebSignIn));
        Assert.True(await HasAsync(Permissions.MarketingRole, Permissions.StoresAll));
        Assert.True(await HasAsync(Permissions.HrOfficerRole, Permissions.StoresAll));
        Assert.False(await HasAsync(Permissions.ManagerRole, Permissions.StoresAll));
        Assert.False(await HasAsync(Permissions.ProductionPlannerRole, Permissions.StoresAll));

        // An administrator's choice wins: a key taken away stays away at the next starts.
        await TakeAwayAsync(Permissions.ManagerRole, Permissions.WebSignIn);
        await TakeAwayAsync(Permissions.MarketingRole, Permissions.StoresAll);
        await StartAsync(null);
        await StartAsync(null);
        Assert.False(await HasAsync(Permissions.ManagerRole, Permissions.WebSignIn));
        Assert.False(await HasAsync(Permissions.MarketingRole, Permissions.StoresAll));
        Assert.True(await HasAsync(Permissions.HrOfficerRole, Permissions.StoresAll));
    }

    [Fact]
    public async Task A_new_key_given_by_hand_before_is_kept_and_can_then_be_taken_away_for_good()
    {
        await StartAsync("boss", "Boss-pass-1");
        Guid handMade;
        await using (var db = _data.Create())
        {
            // Managers could not sign in to the web, so an administrator gave them the key by hand.
            var roleId = await db.Roles.Where(r => r.Name == Permissions.ManagerRole).Select(r => r.Id).SingleAsync();
            await db.RoleGrants.Where(g => g.RoleId == roleId && g.Permission == Permissions.WebSignIn).ExecuteDeleteAsync();
            var grant = new RoleGrant { RoleId = roleId, Permission = Permissions.WebSignIn };
            db.RoleGrants.Add(grant);
            await db.SaveChangesAsync();
            handMade = grant.Id;
        }

        await StartAsync(null);
        var signIn = Assert.Single(await GrantsOfAsync(Permissions.ManagerRole), g => g.Permission == Permissions.WebSignIn);
        Assert.NotEqual(handMade, signIn.Id);

        await TakeAwayAsync(Permissions.ManagerRole, Permissions.WebSignIn);
        await StartAsync(null);
        Assert.False(await HasAsync(Permissions.ManagerRole, Permissions.WebSignIn));
    }
}
