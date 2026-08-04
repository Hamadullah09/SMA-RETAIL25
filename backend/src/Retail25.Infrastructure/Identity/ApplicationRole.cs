using Microsoft.AspNetCore.Identity;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// Identity role mapped from legacy access levels 0–4 (guide p.82).
/// Authorization is by permission, not role; roles are presets for migration.
/// </summary>
public class ApplicationRole : IdentityRole<long>
{
    /// <summary>Legacy access level for migration mapping.</summary>
    public int? LegacyLevel { get; set; }

    public string? Description { get; set; }
}
