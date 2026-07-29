using Retail25.Domain.Common;

namespace Retail25.Domain.Security;

/// <summary>
/// One thing a user may be allowed to do.
/// <para>
/// The catalogue is rows, not an enum, so an administrator can see what exists and grant it. The
/// constants in <c>PermissionKeys</c> are the compile-time half — a typo there is a build error
/// rather than a silent authorisation hole — and these rows are the administrable half.
/// </para>
/// </summary>
public sealed class Permission : Entity
{
    public Permission()
    {
    }

    /// <summary>Stable key, e.g. <c>pos.void_sale</c>. Matches a constant in <c>PermissionKeys</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Shown in the role editor, so an administrator is not granting opaque strings.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Grouping for the settings UI — "Point of sale", "Drawer", "Purchasing".</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// True for permissions that should force a confirmation or a supervisor before they are granted
    /// — voiding sales, managing users, running a migration.
    /// </summary>
    public bool IsSensitive { get; set; }

    public static Permission Create(string key, string description, string group, bool isSensitive = false)
        => new()
        {
            Key = key,
            Description = description,
            Group = group,
            IsSensitive = isSensitive,
        };
}

/// <summary>
/// A grant: this role holds this permission.
/// <para>
/// Roles exist because the legacy system had five access levels and fifteen years of staff are
/// mapped onto them (guide p.82). Authorisation is still by permission — the role is only how a
/// sensible default set gets assigned, and an administrator can reshape any of it without a release.
/// </para>
/// </summary>
public sealed class RolePermission : Entity
{
    public RolePermission()
    {
    }

    /// <summary>The ASP.NET Core Identity role id. Kept as a plain Guid so Domain stays free of Identity.</summary>
    public Guid RoleId { get; set; }

    public string PermissionKey { get; set; } = string.Empty;

    public static RolePermission Create(Guid roleId, string permissionKey)
        => new() { RoleId = roleId, PermissionKey = permissionKey };
}
