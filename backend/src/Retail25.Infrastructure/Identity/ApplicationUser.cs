using Microsoft.AspNetCore.Identity;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity user. Minimal properties; extended by StaffProfile in the Domain.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Full display name for audit logs and UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Current refresh token family for reuse detection.</summary>
    public string? RefreshTokenFamily { get; set; }
}
