using System.Globalization;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Security;

namespace Retail25.Application.Roles;

/// <summary>A role with what it can do, for the role editor.</summary>
/// <param name="IsBuiltIn">
/// One of the five roles mapped to a legacy access level. They can be given and stripped of any
/// permission, but keep their names and cannot be deleted: the presets, the importer and the
/// startup catalogue sync all find them by level, and a shop's fifteen years of staff are mapped
/// onto them.
/// </param>
public sealed record RoleDto(
    long Id,
    string Name,
    string? Description,
    int? LegacyLevel,
    bool IsBuiltIn,
    int MemberCount,
    IReadOnlyList<string> Permissions);

/// <summary>One grantable permission, as the role editor lists it.</summary>
public sealed record PermissionDto(string Key, string Description, string Group, bool IsSensitive);

[RequiresPermission(PermissionKeys.System.UsersManage)]
public sealed record ListRolesQuery : IRequest<IReadOnlyList<RoleDto>>;

[RequiresPermission(PermissionKeys.System.UsersManage)]
public sealed record ListPermissionCatalogueQuery : IRequest<IReadOnlyList<PermissionDto>>;

/// <param name="CopyFromRoleId">
/// Start from another role's permissions. "Like Supervisor, but no voids" is how most new roles
/// are described, and it should not mean ticking thirty boxes to get there.
/// </param>
[RequiresPermission(PermissionKeys.System.UsersManage)]
public sealed record CreateRoleCommand(string Name, string? Description, long? CopyFromRoleId = null)
    : IRequest<Result<RoleDto>>;

[RequiresPermission(PermissionKeys.System.UsersManage)]
public sealed record UpdateRoleCommand(long RoleId, string Name, string? Description) : IRequest<Result<RoleDto>>;

/// <summary>
/// Replaces everything a role may do with exactly this list.
/// <para>
/// The whole list rather than add-one and remove-one, because that is the shape of the screen — a
/// page of ticks saved at once — and because a list that is applied in full cannot end up half
/// saved after a dropped connection.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.System.UsersManage)]
public sealed record SetRolePermissionsCommand(long RoleId, IReadOnlyList<string> Permissions) : IRequest<Result<RoleDto>>;

[RequiresPermission(PermissionKeys.System.UsersManage)]
public sealed record DeleteRoleCommand(long RoleId) : IRequest<Result>;

/// <summary>
/// Moves a member of staff into a role — which is what decides what they may do.
/// <para>
/// There was no way to do this. A colleague got a role when they were created and kept it; the
/// Users screen's "access level" changed a number on the staff profile and left the role, and
/// therefore every permission, exactly where it was.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.System.UsersManage)]
public sealed record AssignStaffRoleCommand(long StaffId, long RoleId) : IRequest<Result>;

public sealed class RoleHandlers
    : IRequestHandler<ListRolesQuery, IReadOnlyList<RoleDto>>,
      IRequestHandler<ListPermissionCatalogueQuery, IReadOnlyList<PermissionDto>>,
      IRequestHandler<CreateRoleCommand, Result<RoleDto>>,
      IRequestHandler<UpdateRoleCommand, Result<RoleDto>>,
      IRequestHandler<SetRolePermissionsCommand, Result<RoleDto>>,
      IRequestHandler<DeleteRoleCommand, Result>,
      IRequestHandler<AssignStaffRoleCommand, Result>
{
    public static readonly Error NotFound = new("role.not_found", "No such role.");
    public static readonly Error NameRequired = new("role.name_required", "A role needs a name.");
    public static readonly Error BuiltInName = new("role.builtin_name", "The built-in roles keep their names. Change what they can do instead.");
    public static readonly Error BuiltInDelete = new("role.builtin_delete", "The built-in roles cannot be deleted.");
    public static readonly Error HasMembers = new("role.has_members", "Move everyone out of this role before deleting it.");
    public static readonly Error UnknownPermission = new("role.unknown_permission", "That is not a permission this system has.");
    public static readonly Error StaffNotFound = new("staff.not_found", "No such member of staff.");
    public static readonly Error NoSignIn = new("staff.no_sign_in", "That member of staff has no sign-in to give a role to.");

    /// <summary>
    /// Removing the last way back in. Whoever can manage users is the only person who can fix a
    /// mistake made here, so the system refuses to leave nobody able to — it is the one change on
    /// this screen that cannot be undone from this screen.
    /// </summary>
    public static readonly Error LockOut = new(
        "role.lockout",
        "Nobody who can sign in would be left able to manage users and roles. Keep that permission on a role somebody active is in.");

    private readonly IApplicationDbContext _db;
    private readonly IRoleAdministration _roles;
    private readonly IPermissionResolver _permissions;
    private readonly IAuditWriter _audit;

    public RoleHandlers(
        IApplicationDbContext db,
        IRoleAdministration roles,
        IPermissionResolver permissions,
        IAuditWriter audit)
    {
        _db = db;
        _roles = roles;
        _permissions = permissions;
        _audit = audit;
    }

    public async Task<IReadOnlyList<RoleDto>> Handle(ListRolesQuery request, CancellationToken ct)
    {
        var roles = await _roles.ListAsync(ct);
        var grants = await GrantsByRoleAsync(roles.Select(r => r.Id).ToList(), ct);

        return roles.Select(r => ToDto(r, grants.GetValueOrDefault(r.Id) ?? [])).ToList();
    }

    public async Task<IReadOnlyList<PermissionDto>> Handle(ListPermissionCatalogueQuery request, CancellationToken ct)
        => await _db.Permissions.AsNoTracking()
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Key)
            .Select(p => new PermissionDto(p.Key, p.Description, p.Group, p.IsSensitive))
            .ToListAsync(ct);

    public async Task<Result<RoleDto>> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return Result.Failure<RoleDto>(NameRequired);
        }

        IReadOnlyList<string> copied = [];

        if (request.CopyFromRoleId is { } sourceId)
        {
            if (await _roles.FindAsync(sourceId, ct) is null)
            {
                return Result.Failure<RoleDto>(NotFound.With("roleId", sourceId));
            }

            copied = await _db.RolePermissions.AsNoTracking()
                .Where(rp => rp.RoleId == sourceId)
                .Select(rp => rp.PermissionKey)
                .ToListAsync(ct);
        }

        var created = await _roles.CreateAsync(name, Blank(request.Description), ct);
        if (created.IsFailure)
        {
            return Result.Failure<RoleDto>(created.Error);
        }

        foreach (var key in copied)
        {
            _db.RolePermissions.Add(RolePermission.Create(created.Value, key));
        }

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            AuditAction.Created,
            "Role",
            Id(created.Value),
            nameof(CreateRoleCommand),
            afterJson: Json(new { name, permissions = copied }),
            ct: ct);

        return await LoadAsync(created.Value, ct);
    }

    public async Task<Result<RoleDto>> Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        var role = await _roles.FindAsync(request.RoleId, ct);
        if (role is null)
        {
            return Result.Failure<RoleDto>(NotFound.With("roleId", request.RoleId));
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return Result.Failure<RoleDto>(NameRequired);
        }

        if (role.LegacyLevel is not null && !string.Equals(name, role.Name, StringComparison.Ordinal))
        {
            return Result.Failure<RoleDto>(BuiltInName);
        }

        var updated = await _roles.UpdateAsync(role.Id, name, Blank(request.Description), ct);
        if (updated.IsFailure)
        {
            return Result.Failure<RoleDto>(updated.Error);
        }

        await _audit.RecordAsync(
            AuditAction.Updated,
            "Role",
            Id(role.Id),
            nameof(UpdateRoleCommand),
            beforeJson: Json(new { name = role.Name, description = role.Description }),
            afterJson: Json(new { name, description = Blank(request.Description) }),
            ct: ct);

        return await LoadAsync(role.Id, ct);
    }

    public async Task<Result<RoleDto>> Handle(SetRolePermissionsCommand request, CancellationToken ct)
    {
        var role = await _roles.FindAsync(request.RoleId, ct);
        if (role is null)
        {
            return Result.Failure<RoleDto>(NotFound.With("roleId", request.RoleId));
        }

        var wanted = new HashSet<string>(
            (request.Permissions ?? []).Select(k => k.Trim()).Where(k => k.Length > 0),
            StringComparer.Ordinal);

        // Only what exists. A key that is not in the catalogue authorises nothing, and saving it
        // would show a role on this screen as able to do something it cannot.
        var known = await _db.Permissions.AsNoTracking()
            .Where(p => wanted.Contains(p.Key))
            .Select(p => p.Key)
            .ToListAsync(ct);

        var unknown = wanted.Except(known, StringComparer.Ordinal).FirstOrDefault();
        if (unknown is not null)
        {
            return Result.Failure<RoleDto>(UnknownPermission.With("permission", unknown));
        }

        var current = await _db.RolePermissions.Where(rp => rp.RoleId == role.Id).ToListAsync(ct);
        var before = current.Select(rp => rp.PermissionKey).OrderBy(k => k, StringComparer.Ordinal).ToList();

        if (await WouldLockOutAsync(role.Id, wanted.Contains(PermissionKeys.System.UsersManage), ct))
        {
            return Result.Failure<RoleDto>(LockOut);
        }

        foreach (var grant in current.Where(rp => !wanted.Contains(rp.PermissionKey)))
        {
            _db.RolePermissions.Remove(grant);
        }

        var held = new HashSet<string>(before, StringComparer.Ordinal);
        foreach (var key in wanted.Where(k => !held.Contains(k)))
        {
            _db.RolePermissions.Add(RolePermission.Create(role.Id, key));
        }

        await _db.SaveChangesAsync(ct);

        // Everyone in the role, at once. A revoked permission that stays live in a cache for minutes
        // is a revocation that did not work.
        await _permissions.InvalidateAllAsync(ct);

        await _audit.RecordAsync(
            AuditAction.Updated,
            "Role",
            Id(role.Id),
            nameof(SetRolePermissionsCommand),
            beforeJson: Json(new { name = role.Name, permissions = before }),
            afterJson: Json(new { name = role.Name, permissions = wanted.OrderBy(k => k, StringComparer.Ordinal) }),
            ct: ct);

        return await LoadAsync(role.Id, ct);
    }

    public async Task<Result> Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var role = await _roles.FindAsync(request.RoleId, ct);
        if (role is null)
        {
            return Result.Success();
        }

        if (role.LegacyLevel is not null)
        {
            return Result.Failure(BuiltInDelete);
        }

        if (role.MemberCount > 0)
        {
            return Result.Failure(HasMembers.With("members", role.MemberCount));
        }

        var grants = await _db.RolePermissions.Where(rp => rp.RoleId == role.Id).ToListAsync(ct);
        _db.RolePermissions.RemoveRange(grants);
        await _db.SaveChangesAsync(ct);

        var deleted = await _roles.DeleteAsync(role.Id, ct);
        if (deleted.IsFailure)
        {
            return deleted;
        }

        await _audit.RecordAsync(
            AuditAction.Deleted,
            "Role",
            Id(role.Id),
            nameof(DeleteRoleCommand),
            beforeJson: Json(new { name = role.Name, permissions = grants.Select(g => g.PermissionKey) }),
            ct: ct);

        return Result.Success();
    }

    public async Task<Result> Handle(AssignStaffRoleCommand request, CancellationToken ct)
    {
        var staff = await _db.StaffProfiles.FirstOrDefaultAsync(s => s.Id == request.StaffId, ct);
        if (staff is null)
        {
            return Result.Failure(StaffNotFound.With("staffId", request.StaffId));
        }

        var userId = staff.UserId;
        if (userId == 0)
        {
            return Result.Failure(NoSignIn);
        }

        var role = await _roles.FindAsync(request.RoleId, ct);
        if (role is null)
        {
            return Result.Failure(NotFound.With("roleId", request.RoleId));
        }

        var holders = await HoldersOfUserManagementAsync(ct);
        var current = await _roles.RolesOfAsync(userId, ct);

        var isHolderNow = current.Any(holders.Contains);
        var staysHolder = holders.Contains(role.Id);

        if (isHolderNow && !staysHolder
            && await _roles.CountEnabledMembersExceptAsync(holders, userId, ct) == 0)
        {
            return Result.Failure(LockOut);
        }

        var assigned = await _roles.AssignAsync(userId, role.Id, ct);
        if (assigned.IsFailure)
        {
            return assigned;
        }

        // A built-in role carries a legacy level, and the level still means something of its own:
        // level 0 is training mode, where nothing is committed. Moving somebody into Trainee without
        // moving their level would give them a trainee's permissions on a till that records their
        // sales for real.
        var previousLevel = staff.AccessLevel;
        if (role.LegacyLevel is { } level)
        {
            staff.AccessLevel = level;
            await _db.SaveChangesAsync(ct);
        }

        await _permissions.InvalidateAsync(userId, ct);

        await _audit.RecordAsync(
            AuditAction.Updated,
            "StaffProfile",
            Id(staff.Id),
            nameof(AssignStaffRoleCommand),
            beforeJson: Json(new { roles = current, accessLevel = previousLevel }),
            afterJson: Json(new { roles = new[] { role.Id }, role = role.Name, accessLevel = staff.AccessLevel }),
            ct: ct);

        return Result.Success();
    }

    /// <summary>
    /// Whether giving <paramref name="roleId"/> this set would take away the last way into user
    /// management.
    /// <para>
    /// Only a change that removes it from this role is ever refused. The first version asked
    /// "would anybody be left holding it" of every save, so on a database where nobody active held
    /// it already — a fresh install, a test bench — every edit to every role was refused, including
    /// the one that would have fixed it.
    /// </para>
    /// </summary>
    private async Task<bool> WouldLockOutAsync(long roleId, bool roleWillHold, CancellationToken ct)
    {
        var holders = await HoldersOfUserManagementAsync(ct);

        if (roleWillHold || !holders.Contains(roleId))
        {
            return false;
        }

        holders.Remove(roleId);
        return await _roles.CountEnabledMembersAsync(holders, ct) == 0;
    }

    private async Task<HashSet<long>> HoldersOfUserManagementAsync(CancellationToken ct)
    {
        var ids = await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.PermissionKey == PermissionKeys.System.UsersManage)
            .Select(rp => rp.RoleId)
            .ToListAsync(ct);

        return [.. ids];
    }

    private async Task<Dictionary<long, List<string>>> GrantsByRoleAsync(IReadOnlyList<long> roleIds, CancellationToken ct)
    {
        var rows = await _db.RolePermissions.AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => new { rp.RoleId, rp.PermissionKey })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.PermissionKey).OrderBy(k => k, StringComparer.Ordinal).ToList());
    }

    private async Task<Result<RoleDto>> LoadAsync(long roleId, CancellationToken ct)
    {
        var role = await _roles.FindAsync(roleId, ct);
        if (role is null)
        {
            return Result.Failure<RoleDto>(NotFound.With("roleId", roleId));
        }

        var grants = await GrantsByRoleAsync([roleId], ct);
        return Result.Success(ToDto(role, grants.GetValueOrDefault(roleId) ?? []));
    }

    private static RoleDto ToDto(RoleRecord role, IReadOnlyList<string> permissions)
        => new(role.Id, role.Name, role.Description, role.LegacyLevel, role.LegacyLevel is not null, role.MemberCount, permissions);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
