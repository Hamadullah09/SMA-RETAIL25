using Retail25.Domain.Common;

namespace Retail25.Domain.Shoppers;

/// <summary>
/// One phone that a shopper has signed in on, and the long-lived credential that lets them back in
/// with a fingerprint instead of a password.
/// <para>
/// <b>No biometric data is sent to or stored by this system, and none can be.</b> The fingerprint or
/// face never leaves the handset's secure element — Android will not hand it to an application. What
/// actually happens is: at sign-in the server mints a refresh token and returns it once; the app puts
/// it in the Android Keystore behind a key that requires user authentication to unlock; a later tap
/// of the sensor unlocks the key, the app reads the token back out and exchanges it here for a fresh
/// access token. The sensor gates <em>local</em> access to a secret this table already trusts.
/// </para>
/// <para>
/// Only <see cref="RefreshTokenHash"/> is stored, for the same reason passwords are hashed: a leak of
/// this table must not be a leak of working credentials for every shopper's phone.
/// </para>
/// <para>
/// Rotation is on every use. A refresh token is single-use — redeeming it issues a new one and
/// supersedes this row — so a token captured in transit stops working the moment the real phone next
/// refreshes, and a replay of an already-redeemed token is a signal that something is wrong rather
/// than a silent success.
/// </para>
/// </summary>
public sealed class ShopperDevice : Entity, IAuditable
{
    public static readonly Error NotRecognised =
        new("shopper_device.not_recognised", "This device is not registered for that account.");

    public static readonly Error TokenRejected =
        new("shopper_device.token_rejected", "Sign in again on this device.");

    private ShopperDevice()
    {
    }

    public long ShopperId { get; private set; }

    /// <summary>
    /// A stable per-installation identifier the app generates once and keeps. Not a hardware id:
    /// Android stopped handing those out, and a value the user can reset by reinstalling is the
    /// correct granularity for "forget this phone" anyway.
    /// </summary>
    public string DeviceId { get; private set; } = string.Empty;

    /// <summary>What the shopper sees in a device list — "Pixel 8", "Chainway C72".</summary>
    public string DeviceName { get; private set; } = string.Empty;

    public string RefreshTokenHash { get; private set; } = string.Empty;

    public DateTimeOffset RefreshTokenExpiresAt { get; private set; }

    /// <summary>
    /// Whether the shopper opted this phone in to unlocking with a fingerprint or face.
    /// <para>
    /// Recorded so the app can be told, before it shows anything, whether to offer the sensor. It
    /// grants nothing on its own — the token is what authenticates, and it is issued the same way
    /// either way.
    /// </para>
    /// </summary>
    public bool BiometricEnabled { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && RefreshTokenExpiresAt > now;

    public static Result<ShopperDevice> Register(
        long shopperId,
        string? deviceId,
        string? deviceName,
        string refreshTokenHash,
        DateTimeOffset expiresAt,
        bool biometricEnabled)
    {
        var id = (deviceId ?? string.Empty).Trim();

        if (id.Length is 0 or > 128)
        {
            return Result.Failure<ShopperDevice>(NotRecognised.With("deviceId", deviceId));
        }

        var name = (deviceName ?? string.Empty).Trim();

        return Result.Success(new ShopperDevice
        {
            ShopperId = shopperId,
            DeviceId = id,
            DeviceName = name.Length == 0 ? "Phone" : name[..Math.Min(name.Length, 100)],
            RefreshTokenHash = refreshTokenHash,
            RefreshTokenExpiresAt = expiresAt,
            BiometricEnabled = biometricEnabled,
        });
    }

    /// <summary>Replaces the stored credential. Called on every refresh, not only at sign-in.</summary>
    public void IssueToken(string refreshTokenHash, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        RefreshTokenHash = refreshTokenHash;
        RefreshTokenExpiresAt = expiresAt;
        RevokedAt = null;
        LastUsedAt = now;
    }

    public void EnableBiometric(bool enabled) => BiometricEnabled = enabled;

    public void Rename(string? deviceName)
    {
        var name = (deviceName ?? string.Empty).Trim();

        if (name.Length > 0)
        {
            DeviceName = name[..Math.Min(name.Length, 100)];
        }
    }

    public void Revoke(DateTimeOffset now) => RevokedAt = now;
}
