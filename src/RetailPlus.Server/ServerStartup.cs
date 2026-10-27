using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

/// <summary>
/// What the server does before it takes requests: bring the database up to date, give it the standard roles and tender
/// types every store shares, and on the very first start make a super administrator.
/// </summary>
public sealed class ServerStartup(DataContext data, IOptions<BootstrapOptions> bootstrap, ILogger<ServerStartup> log)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await data.MigrateAsync(cancellationToken);
        await using var db = data.Create();
        await SetupService.SeedRolesAsync(db, cancellationToken);
        if (!await db.TenderTypes.AnyAsync(cancellationToken))
        {
            int order = 0;
            foreach (var (tender, kind) in SetupService.DefaultTenders)
                db.TenderTypes.Add(new TenderType { Name = tender, Kind = kind, SortOrder = ++order, OpensDrawer = kind is TenderKind.Cash or TenderKind.Cheque });
        }
        await db.SaveChangesAsync(cancellationToken);
        if (await db.UserAccounts.AnyAsync(cancellationToken)) return;

        var role = await db.Roles.SingleAsync(r => r.Name == Permissions.SuperAdministratorRole, cancellationToken);
        var name = string.IsNullOrWhiteSpace(bootstrap.Value.AdminUserName) ? "admin" : bootstrap.Value.AdminUserName.Trim();
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
