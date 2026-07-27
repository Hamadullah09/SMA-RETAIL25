using Retail25.Domain.Common;

namespace Retail25.Domain.Staff;

/// <summary>
/// Staff profile linked to an ASP.NET Core Identity user. Contains the legacy access level
/// (0–4), PIN hash for fast-switch at POS, and commission defaults.
/// </summary>
public sealed class StaffProfile : Entity, IAuditable
{
    private StaffProfile()
    {
    }

    public Guid UserId { get; set; }

    /// <summary>Short code for display (e.g. "SK" for Sarah K.).</summary>
    public string StaffCode { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>Argon2id hash of the PIN for fast user switching at POS.</summary>
    public string? PinHash { get; set; }

    /// <summary>
    /// Legacy access level 0–4 mapped to roles (guide p.82). Kept for migration mapping;
    /// authorization is by permission, not level.
    /// </summary>
    public int AccessLevel { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public static StaffProfile Create(Guid userId, string staffCode, string firstName, string lastName, int accessLevel)
    {
        return new StaffProfile
        {
            UserId = userId,
            StaffCode = staffCode.Trim().ToUpperInvariant(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            AccessLevel = accessLevel,
        };
    }
}
