using Microsoft.AspNetCore.Identity;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// Who signs in. Deliberately thin: everything the shop floor cares about — staff code, access
/// level, PIN, commission — lives on <c>StaffProfile</c>, so an identity concern and a payroll
/// concern never end up in the same table.
/// </summary>
public class ApplicationUser : IdentityUser<long>
{
    /// <summary>Full display name, for audit rows and the header.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Which shop this user works at. Carried on the token so a back-office query does not have to
    /// ask, and so a multi-location business does not default someone into the wrong store's data.
    /// </summary>
    public long? DefaultLocationId { get; set; }

    public DateTimeOffset? LastSignedInAt { get; set; }
}
