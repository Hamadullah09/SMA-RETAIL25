using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

/// <summary>
/// What the server does before it takes requests: bring the database up to date, give it the standard roles and tender
/// types every store shares, and make sure someone can manage every store: on the very first start a new super
/// administrator, and on a database that has users but nobody with <see cref="Permissions.StoresAll"/> (one filled by
/// the Retail25 cutover) the user named in <c>Bootstrap:AdminUserName</c>.
/// </summary>
public sealed class ServerStartup(DataContext data, IOptions<BootstrapOptions> bootstrap, ILogger<ServerStartup> log)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await data.MigrateAsync(cancellationToken);
        await using var db = data.Create();
        await SetupService.SeedRolesAsync(db, cancellationToken);
        await new LedgerService(data).SeedAsync(cancellationToken);
        await new FinanceSetupService(data).SeedAsync(cancellationToken);
        if (!await db.TenderTypes.AnyAsync(cancellationToken))
        {
            int order = 0;
            foreach (var (tender, kind) in SetupService.DefaultTenders)
                db.TenderTypes.Add(new TenderType { Name = tender, Kind = kind, SortOrder = ++order, OpensDrawer = kind is TenderKind.Cash or TenderKind.Cheque });
        }
        await db.SaveChangesAsync(cancellationToken);

        var role = await db.Roles.SingleAsync(r => r.Name == Permissions.SuperAdministratorRole, cancellationToken);
        var configured = bootstrap.Value.AdminUserName?.Trim();
        if (!await db.UserAccounts.AnyAsync(cancellationToken))
        {
            await AddAdministratorAsync(db, role, string.IsNullOrEmpty(configured) ? "admin" : configured, cancellationToken);
            return;
        }
        if (await AnyoneSeesEveryStoreAsync(db, cancellationToken)) return;

        // Users came in without anyone who sees every store: only the configured user is made super administrator, so
        // nobody gets every store by accident.
        if (string.IsNullOrEmpty(configured))
        {
            ServerLog.NoSuperAdministrator(log);
            return;
        }
        var user = await db.UserAccounts.SingleOrDefaultAsync(u => u.UserName == configured, cancellationToken);
        if (user is null)
        {
            await AddAdministratorAsync(db, role, configured, cancellationToken);
            return;
        }
        user.RoleId = role.Id;
        user.AccessLevel = role.Level;
        user.StoreId = null;
        user.IsActive = true;
        user.LockedUntilUtc = null;
        user.FailedSignIns = 0;
        await db.SaveChangesAsync(cancellationToken);
        ServerLog.AdministratorPromoted(log, user.UserName);
    }

    private static Task<bool> AnyoneSeesEveryStoreAsync(RetailPlusDbContext db, CancellationToken cancellationToken) =>
        db.UserAccounts.AnyAsync(u => u.IsActive && u.RoleId != null
            && db.RoleGrants.Any(g => g.RoleId == u.RoleId && g.Permission == Permissions.StoresAll), cancellationToken);

    private async Task AddAdministratorAsync(RetailPlusDbContext db, Role role, string name, CancellationToken cancellationToken)
    {
        var password = bootstrap.Value.AdminPassword;
        bool made = string.IsNullOrEmpty(password);
        if (made) password = MakePassword();
        db.UserAccounts.Add(new UserAccount
        {
            UserName = name, FullName = "Super administrator", RoleId = role.Id, AccessLevel = role.Level, PasswordHash = SetupService.HashPassword(password!),
            IsActive = true, MustChangePassword = made,
        });
        await db.SaveChangesAsync(cancellationToken);
        if (made) ServerLog.AdminMade(log, name, password!);
        else ServerLog.AdminConfigured(log, name);
    }

    private static string MakePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 16).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }
}
