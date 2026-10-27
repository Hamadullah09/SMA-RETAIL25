using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

public sealed record PermissionRow(string Key, string Group, string Description);
public sealed record RoleRow(Guid Id, string Version, string Name, string? Description, int Level, bool IsSystem, IReadOnlyList<string> Permissions, int UserCount);
public sealed record RoleBody(string? Version, string Name, string? Description, int Level, IReadOnlyList<string>? Permissions);
public sealed record UserRow(Guid Id, string Version, string UserName, string? FullName, Guid? RoleId, string? Role, Guid? StoreId, string? StoreCode,
    string? StaffCode, bool IsActive, bool Locked, bool MustChangePassword, DateTime? LastSignInAtUtc);
public sealed record UserBody(string? Version, string UserName, string? FullName, Guid? RoleId, Guid? StoreId, bool IsActive, string? Password);
public sealed record TemporaryPasswordInfo(string TemporaryPassword);
public sealed record PinBody(string? StaffCode, string? Pin);

/// <summary>
/// Permissions, roles and users. Roles are shared by every store; a user belongs to one store or to every store. An
/// administrator without <see cref="Permissions.StoresAll"/> manages only their own store's users, and can neither give
/// anyone the reach over every store nor make a user of every store.
/// </summary>
public static partial class Endpoints
{
    private static void MapUsers(RouteGroupBuilder api)
    {
        api.MapGet("/permissions", () => Results.Ok(Permissions.All.Select(p => new PermissionRow(p.Key, p.Group, p.Description))))
            .RequireAuthorization(Need(Permissions.SetupUsers));
        api.MapGet("/roles", RolesAsync).RequireAuthorization(Need(Permissions.SetupUsers));
        api.MapPost("/roles", (RoleBody body, ApiRequest api) => SaveRoleAsync(null, body, api)).RequireAuthorization(Need(Permissions.SetupUsers));
        api.MapPut("/roles/{id:guid}", (Guid id, RoleBody body, ApiRequest api) => SaveRoleAsync(id, body, api)).RequireAuthorization(Need(Permissions.SetupUsers));
        api.MapDelete("/roles/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            await using (var db = api.Root.Create())
                if (!await db.Roles.AnyAsync(r => r.Id == id)) throw ApiException.NotFound("That role");
            await new SetupService(await api.ForChangeAsync()).DeleteRoleAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.SetupUsers));

        api.MapGet("/users", UsersAsync).RequireAuthorization(Need(Permissions.SetupUsers));
        api.MapPost("/users", (UserBody body, ApiRequest api) => SaveUserAsync(null, body, api)).RequireAuthorization(Need(Permissions.SetupUsers));
        api.MapPut("/users/{id:guid}", (Guid id, UserBody body, ApiRequest api) => SaveUserAsync(id, body, api)).RequireAuthorization(Need(Permissions.SetupUsers));
        api.MapDelete("/users/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var user = await ManagedUserAsync(api, id);
            await new SetupService(await api.ForChangeAsync(user.StoreId)).DeleteUserAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.SetupUsers));
        api.MapPost("/users/{id:guid}/reset-password", async (Guid id, ApiRequest api) =>
        {
            var user = await ManagedUserAsync(api, id);
            var password = MakeTemporaryPassword();
            await new SetupService(await api.ForChangeAsync(user.StoreId)).ResetPasswordAsync(id, password);
            return Results.Ok(new TemporaryPasswordInfo(password));

        }).RequireAuthorization(Need(Permissions.SetupUsers));
        api.MapPost("/users/{id:guid}/pin", async (Guid id, PinBody body, ApiRequest api) =>
        {
            var user = await ManagedUserAsync(api, id);
            await new SetupService(await api.ForChangeAsync(user.StoreId)).SetPinAsync(id, body.StaffCode, body.Pin);
            return Results.NoContent();
        }).RequireAuthorization(Need(Permissions.SetupUsers));
    }

    // Roles -----------------------------------------------------------------------------------------------------------

    private static async Task<IResult> RolesAsync(ApiRequest api)
    {
        var setup = new SetupService(api.Root);
        var usage = await setup.GetRoleUsageAsync();
        await using var db = api.Root.Create();
        var times = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.UpdatedAtUtc);
        return Results.Ok((await setup.GetRolesAsync()).Select(r => ToRow(r, times.GetValueOrDefault(r.Id), usage.GetValueOrDefault(r.Id))));
    }

    private static RoleRow ToRow(Role r, DateTime updated, int users) => new(r.Id, ApiRequest.Version(RoleVersion(r, updated)), r.Name, r.Description, r.Level, r.IsSystem,
        [.. r.Permissions.Select(p => p.Permission).Order(StringComparer.Ordinal)], users);

    /// <summary>A role's version: the latest change of the role or of its permissions.</summary>
    private static DateTime RoleVersion(Role role, DateTime updated) =>
        role.Permissions.Select(p => p.UpdatedAtUtc).Append(updated).Max();

    private static async Task<IResult> SaveRoleAsync(Guid? id, RoleBody body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        var keys = (body.Permissions ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Contains(Permissions.StoresAll) && !api.User.AllStores) throw ApiException.Forbidden("Only a user who sees every store can give that reach.");
        var role = new Role { Name = body.Name ?? "", Description = body.Description, Level = body.Level };
        if (id is { } existing)
        {
            await using var db = api.Root.Create();
            var stored = await db.Roles.AsNoTracking().Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == existing) ?? throw ApiException.NotFound("That role");
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version(RoleVersion(stored, stored.UpdatedAtUtc)));
            if (!api.User.AllStores && stored.Permissions.Any(p => p.Permission == Permissions.StoresAll))
                throw ApiException.Forbidden("Only a user who sees every store can change that role.");
            role.Id = existing;
        }
        var saved = await new SetupService(await api.ForChangeAsync()).SaveRoleAsync(role, keys);
        var setup = new SetupService(api.Root);
        var reloaded = (await setup.GetRolesAsync()).Single(r => r.Id == saved.Id);
        await using var check = api.Root.Create();
        var row = ToRow(reloaded, await check.Roles.Where(r => r.Id == saved.Id).Select(r => r.UpdatedAtUtc).SingleAsync(), (await setup.GetRoleUsageAsync()).GetValueOrDefault(saved.Id));
        return id is null ? Results.Created($"/api/roles/{saved.Id}", row) : Results.Ok(row);
    }

    // Users -----------------------------------------------------------------------------------------------------------

    private static async Task<IResult> UsersAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var users = db.UserAccounts.AsNoTracking().Include(u => u.Role).AsQueryable();
        if (store is { } s) users = users.Where(u => u.StoreId == s);
        var list = await users.OrderBy(u => u.UserName).ToListAsync();
        var codes = await StoreCodesAsync(db);
        var ids = list.Select(u => (Guid?)u.Id).ToList();
        var lastSignIn = await db.AuditEntries.AsNoTracking().Where(a => a.Action == AuditActions.SignIn && ids.Contains(a.UserId))
            .GroupBy(a => a.UserId!.Value).Select(g => new { g.Key, Last = g.Max(a => a.AtUtc) }).ToDictionaryAsync(x => x.Key, x => x.Last);
        var edited = await EditedAsync(db, ids.Select(i => i!.Value).ToList());
        var now = DateTime.UtcNow;
        return Results.Ok(list.Select(u => new UserRow(u.Id, ApiRequest.Version(edited.GetValueOrDefault(u.Id)), u.UserName, u.FullName, u.RoleId, u.Role?.Name, u.StoreId,
            u.StoreId is { } us ? codes.GetValueOrDefault(us) : null, u.StaffCode, u.IsActive, u.LockedUntilUtc > now, u.MustChangePassword,
            lastSignIn.TryGetValue(u.Id, out var at) ? ApiRequest.Utc(at) : null)));
    }

    /// <summary>A user the signed-in administrator may manage: 404 for a user of a store they do not see or of every store.</summary>
    private static async Task<UserAccount> ManagedUserAsync(ApiRequest api, Guid id)
    {
        await using var db = api.Root.Create();
        var user = await db.UserAccounts.AsNoTracking().Include(u => u.Role).ThenInclude(r => r!.Permissions).SingleOrDefaultAsync(u => u.Id == id)
                   ?? throw ApiException.NotFound("That user");
        if (!api.User.AllStores && (user.StoreId is null || user.StoreId != api.User.StoreId)) throw ApiException.NotFound("That user");
        return user;
    }

    private static async Task<IResult> SaveUserAsync(Guid? id, UserBody body, ApiRequest api)
    {
        UserAccount? stored = null;
        if (id is { } existing)
        {
            stored = await ManagedUserAsync(api, existing);
            await using var db = api.Root.Create();
            ApiRequest.CheckVersion(body.Version, ApiRequest.Version((await EditedAsync(db, [existing])).GetValueOrDefault(existing)));
        }
        await using (var db = api.Root.Create())
        {
            if (body.StoreId is { } store && !await db.Stores.AnyAsync(s => s.Id == store)) throw ApiException.Bad("That store does not exist.");
            if (body.RoleId is { } roleId)
            {
                var grants = await db.RoleGrants.AsNoTracking().Where(g => g.RoleId == roleId).Select(g => g.Permission).ToListAsync();
                if (!await db.Roles.AnyAsync(r => r.Id == roleId)) throw ApiException.Bad("That role does not exist.");
                if (grants.Contains(Permissions.StoresAll) && !api.User.AllStores) throw ApiException.Forbidden("Only a user who sees every store can give that role.");
            }
        }
        if (!api.User.AllStores)
        {
            if (body.StoreId is null) throw ApiException.Forbidden("Only a user who sees every store can make a user of every store.");
            if (body.StoreId != api.User.StoreId) throw ApiException.Forbidden("You manage the users of your own store only.");
        }
        var user = new UserAccount
        {
            Id = id ?? Guid.Empty, UserName = body.UserName ?? "", FullName = body.FullName, RoleId = body.RoleId, StoreId = body.StoreId, IsActive = body.IsActive,
            AccessLevel = 1, MustChangePassword = stored?.MustChangePassword ?? false,
        };
        var saved = await new SetupService(await api.ForChangeAsync(body.StoreId)).SaveUserAsync(user, string.IsNullOrEmpty(body.Password) ? null : body.Password);
        await using var check = api.Root.Create();
        var u = await check.UserAccounts.AsNoTracking().Include(x => x.Role).SingleAsync(x => x.Id == saved.Id);
        var codes = await StoreCodesAsync(check);
        var row = new UserRow(u.Id, ApiRequest.Version((await EditedAsync(check, [u.Id])).GetValueOrDefault(u.Id)), u.UserName, u.FullName, u.RoleId, u.Role?.Name, u.StoreId,
            u.StoreId is { } us ? codes.GetValueOrDefault(us) : null, u.StaffCode, u.IsActive, u.LockedUntilUtc > DateTime.UtcNow, u.MustChangePassword, null);
        return id is null ? Results.Created($"/api/users/{u.Id}", row) : Results.Ok(row);
    }

    /// <summary>
    /// When users were last edited: a user's version. (Their change time also moves when they sign in, which is not an
    /// edit and must not make an administrator's change fail.)
    /// </summary>
    private static Task<Dictionary<Guid, DateTime>> EditedAsync(RetailPlusDbContext db, List<Guid> ids) =>
        db.UserAccounts.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, Edited = EF.Property<DateTime>(u, SyncColumns.EditedAtUtc) }).ToDictionaryAsync(x => x.Id, x => x.Edited);

    private static string MakeTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 12).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }
}
