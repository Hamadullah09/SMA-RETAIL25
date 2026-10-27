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
}
