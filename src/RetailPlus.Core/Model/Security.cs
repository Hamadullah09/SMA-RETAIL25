namespace RetailPlus.Core.Model;

/// <summary>
/// A set of permissions given to users: the desktop roles (cashier, supervisor, manager) and the web roles (admin, super
/// admin). <see cref="Level"/> is the Retail Plus access level (0–4) a user of the role has where the program still
/// asks for one.
/// </summary>
public class Role : Entity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int Level { get; set; }
    /// <summary>One of the roles every database starts with; it can be changed but not deleted.</summary>
    public bool IsSystem { get; set; }
    public List<RoleGrant> Permissions { get; set; } = [];
}

/// <summary>One permission of a role, by its key from <see cref="Rules.Permissions"/>.</summary>
public class RoleGrant : Entity
{
    public Guid RoleId { get; set; }
    public string Permission { get; set; } = "";
}
