using Retail25.Domain.Common;

namespace Retail25.Domain.Terminals;

/// <summary>
/// A one-time credential that lets a machine claim its identity and collect its real one.
/// <para>
/// Every agent currently authenticates with the same shared secret, which on a single-till shop is
/// merely untidy and on an estate of 252 is one secret sitting on 252 machines: it cannot be rotated
/// for one till, and a single compromised PC is every till compromised. This is the first half of
/// undoing that.
/// </para>
/// <para>
/// The token is generated for one device, expires, and can be redeemed once. What the installer
/// carries to the machine is therefore worth nothing after enrolment and worth nothing to anybody
/// else even before it — which is what makes it safe to put in a file, email, or a support ticket.
/// The durable credential is handed back over TLS at redemption and never appears in that file.
/// </para>
/// <para>
/// Only the hash is stored. A registry that holds live enrolment tokens is a list of keys to the
/// estate, and the point of a bearer credential is that the issuer does not need to keep it.
/// </para>
/// </summary>
public sealed class DeviceEnrolment : AggregateRoot, IAuditable
{
    public static readonly Error NotFound = new("enrolment.not_found", "That enrolment code was not recognised.");

    public static readonly Error Expired = new(
        "enrolment.expired",
        "That enrolment code has expired. Generate a new one from the RFID settings screen.");

    public static readonly Error AlreadyRedeemed = new(
        "enrolment.already_redeemed",
        "That enrolment code has already been used. Generate a new one to enrol another machine.");

    public DeviceEnrolment()
    {
    }

    public long DeviceId { get; private set; }

    public long LocationId { get; private set; }

    /// <summary>SHA-256 of the token. The token itself is shown once and never stored.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RedeemedAt { get; private set; }

    /// <summary>Which machine actually used it, for the audit trail an estate needs.</summary>
    public string? RedeemedByHostname { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static DeviceEnrolment Issue(long deviceId, long locationId, string tokenHash, DateTimeOffset expiresAt)
        => new()
        {
            DeviceId = deviceId,
            LocationId = locationId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
        };

    /// <summary>
    /// Marks the token used, refusing a second attempt.
    /// <para>
    /// Expiry is checked before redemption rather than after, so a token that timed out reports that
    /// rather than reporting success on a race. Redeemed-twice is reported distinctly from expired
    /// because they mean opposite things to whoever is stood at the machine: one is "you waited too
    /// long", the other is "somebody already used this".
    /// </para>
    /// </summary>
    public Result Redeem(DateTimeOffset now, string? hostname)
    {
        if (RedeemedAt is not null)
        {
            return Result.Failure(AlreadyRedeemed);
        }

        if (now >= ExpiresAt)
        {
            return Result.Failure(Expired);
        }

        RedeemedAt = now;
        RedeemedByHostname = hostname?.Trim();

        return Result.Success();
    }
}
