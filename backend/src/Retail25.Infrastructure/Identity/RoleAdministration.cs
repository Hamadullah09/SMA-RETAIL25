using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Common;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Infrastructure.Identity;

/// <summary>Identity's half of the role editor. See <see cref="IRoleAdministration"/>.</summary>
public sealed class RoleAdministration : IRoleAdministration
{
    public static readonly Error NameTaken = new("role.name_taken", "Another role already has that name.");
    public static readonly Error NotFound = new("role.not_found", "No such role.");
    public static readonly Error UserNotFound = new("staff.no_sign_in", "That member of staff has no sign-in to give a role to.");

    private readonly RoleManager<ApplicationRole> _roles;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ApplicationDbContext _db;

    public RoleAdministration(RoleManager<ApplicationRole> roles, UserManager<ApplicationUser> users, ApplicationDbContext db)
    {
        _roles = roles;
        _users = users;
        _db = db;
    }

    public async Task<IReadOnlyList<RoleRecord>> ListAsync(CancellationToken ct)
    {
        // Membership counted in one grouped query rather than a GetUsersInRoleAsync per role.
        var members = await _db.UserRoles
            .AsNoTracking()
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct);

        var roles = await _roles.Roles
            .AsNoTracking()
            .OrderBy(r => r.LegacyLevel ?? int.MaxValue)
            .ThenBy(r => r.Name)
            .ToListAsync(ct);

        return roles
            .Select(r => new RoleRecord(r.Id, r.Name ?? string.Empty, r.Description, r.LegacyLevel, members.GetValueOrDefault(r.Id)))
            .ToList();
    }

    public async Task<RoleRecord?> FindAsync(long roleId, CancellationToken ct)
    {
        var role = await _roles.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId, ct);
        if (role is null)
        {
            return null;
        }

        var members = await _db.UserRoles.CountAsync(ur => ur.RoleId == roleId, ct);
        return new RoleRecord(role.Id, role.Name ?? string.Empty, role.Description, role.LegacyLevel, members);
    }

    public async Task<Result<long>> CreateAsync(string name, string? description, CancellationToken ct)
    {
        var role = new ApplicationRole { Name = name, Description = description };
        var created = await _roles.CreateAsync(role);

        if (!created.Succeeded)
        {
            return Result.Failure<long>(Translate(created));
        }

        return Result.Success(role.Id);
    }

    public async Task<Result> UpdateAsync(long roleId, string name, string? description, CancellationToken ct)
    {
        var role = await _roles.FindByIdAsync(Id(roleId));
        if (role is null)
        {
            return Result.Failure(NotFound.With("roleId", roleId));
        }

        if (!string.Equals(role.Name, name, StringComparison.Ordinal))
        {
            var renamed = await _roles.SetRoleNameAsync(role, name);
            if (!renamed.Succeeded)
            {
                return Result.Failure(Translate(renamed));
            }
        }

        role.Description = description;
        var updated = await _roles.UpdateAsync(role);

        return updated.Succeeded ? Result.Success() : Result.Failure(Translate(updated));
    }

    public async Task<Result> DeleteAsync(long roleId, CancellationToken ct)
    {
        var role = await _roles.FindByIdAsync(Id(roleId));
        if (role is null)
        {
            // Gone already is the outcome the caller wanted.
            return Result.Success();
        }

        var deleted = await _roles.DeleteAsync(role);
        return deleted.Succeeded ? Result.Success() : Result.Failure(Translate(deleted));
    }

    public async Task<IReadOnlyList<long>> RolesOfAsync(long userId, CancellationToken ct)
        => await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

    public async Task<Result> AssignAsync(long userId, long roleId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(Id(userId));
        if (user is null)
        {
            return Result.Failure(UserNotFound.With("userId", userId));
        }

        var role = await _roles.FindByIdAsync(Id(roleId));
        if (role?.Name is null)
        {
            return Result.Failure(NotFound.With("roleId", roleId));
        }

        var current = await _users.GetRolesAsync(user);
        var leaving = current.Where(r => !string.Equals(r, role.Name, StringComparison.Ordinal)).ToList();

        if (leaving.Count > 0)
        {
            var removed = await _users.RemoveFromRolesAsync(user, leaving);
            if (!removed.Succeeded)
            {
                return Result.Failure(Translate(removed));
            }
        }

        if (!current.Contains(role.Name, StringComparer.Ordinal))
        {
            var added = await _users.AddToRoleAsync(user, role.Name);
            if (!added.Succeeded)
            {
                return Result.Failure(Translate(added));
            }
        }

        return Result.Success();
    }

    public Task<int> CountEnabledMembersAsync(IReadOnlyCollection<long> roleIds, CancellationToken ct)
        => EnabledMembers(roleIds).CountAsync(ct);

    public Task<int> CountEnabledMembersExceptAsync(IReadOnlyCollection<long> roleIds, long userId, CancellationToken ct)
        => EnabledMembers(roleIds).Where(id => id != userId).CountAsync(ct);

    private IQueryable<long> EnabledMembers(IReadOnlyCollection<long> roleIds)
    {
        var ids = roleIds.ToArray();

        return (from userRole in _db.UserRoles
                join user in _db.Users on userRole.UserId equals user.Id
                where ids.Contains(userRole.RoleId) && user.IsEnabled
                select user.Id).Distinct();
    }

    private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Identity's validator codes, turned into this application's stable ones.</summary>
    private static Error Translate(IdentityResult result)
    {
        var first = result.Errors.FirstOrDefault();

        return first?.Code switch
        {
            nameof(IdentityErrorDescriber.DuplicateRoleName) => NameTaken,
            nameof(IdentityErrorDescriber.InvalidRoleName) => new Error("role.name_invalid", "A role needs a name."),
            _ => new Error("role.identity_failed", first?.Description ?? "The role could not be saved."),
        };
    }
}
