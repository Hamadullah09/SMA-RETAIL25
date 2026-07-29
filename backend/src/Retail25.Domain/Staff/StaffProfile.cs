using Retail25.Domain.Common;

namespace Retail25.Domain.Staff;

/// <summary>
/// A person, as the shop floor knows them. Linked to an Identity user but distinct from it: the user
/// is who signs in, the staff profile is who a sale is attributed to and who appears on a commission
/// report.
/// </summary>
public sealed class StaffProfile : Entity, IAuditable
{
    public static readonly Error PinLocked = new("staff.pin_locked", "Too many incorrect PIN attempts. Ask a supervisor to unlock.");
    public static readonly Error PinNotSet = new("staff.pin_not_set", "This staff member has no PIN set.");
    public static readonly Error PinTooShort = new("staff.pin_too_short", "A PIN must be at least four digits.");
    public static readonly Error Inactive = new("staff.inactive", "That staff member is no longer active.");

    /// <summary>
    /// Five attempts, then a lockout. A four-digit PIN has ten thousand combinations, so an unlimited
    /// prompt is guessable in an afternoon by anyone left alone with a till.
    /// </summary>
    public const int MaxPinAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public StaffProfile()
    {
    }

    public Guid UserId { get; set; }

    /// <summary>Short code shown on screen and printed on receipts, e.g. <c>SK</c>.</summary>
    public string StaffCode { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>Argon2id hash with a per-user salt. The PIN itself is never stored or logged.</summary>
    public string? PinHash { get; set; }

    public int FailedPinAttempts { get; set; }

    public DateTimeOffset? PinLockedUntil { get; set; }

    /// <summary>
    /// Legacy access level 0–4 (guide p.82), kept so migrated staff land on a sensible role.
    /// Authorisation is by permission; this only chooses the preset.
    /// </summary>
    public int AccessLevel { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public bool HasPin => !string.IsNullOrEmpty(PinHash);

    public bool IsPinLocked(DateTimeOffset now) => PinLockedUntil is { } until && now < until;

    public static StaffProfile Create(Guid userId, string staffCode, string firstName, string lastName, int accessLevel)
        => new()
        {
            UserId = userId,
            StaffCode = staffCode.Trim().ToUpperInvariant(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            AccessLevel = accessLevel,
        };

    /// <summary>
    /// Sets the PIN. The caller hashes it — the domain never sees the plaintext, so it cannot end up
    /// in a log, an exception message or a debugger watch window by accident.
    /// </summary>
    public Result SetPin(string pinHash)
    {
        if (string.IsNullOrWhiteSpace(pinHash))
        {
            return Result.Failure(PinNotSet);
        }

        PinHash = pinHash;
        FailedPinAttempts = 0;
        PinLockedUntil = null;
        return Result.Success();
    }

    /// <summary>Records a correct PIN: the counter resets and any lockout clears.</summary>
    public void RecordPinSuccess()
    {
        FailedPinAttempts = 0;
        PinLockedUntil = null;
    }

    /// <summary>Records an incorrect PIN, locking the profile once the limit is reached.</summary>
    public void RecordPinFailure(DateTimeOffset now)
    {
        FailedPinAttempts++;

        if (FailedPinAttempts >= MaxPinAttempts)
        {
            PinLockedUntil = now.Add(LockoutDuration);
        }
    }

    public void UnlockPin()
    {
        FailedPinAttempts = 0;
        PinLockedUntil = null;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
