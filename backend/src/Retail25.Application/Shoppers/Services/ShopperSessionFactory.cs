using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Shoppers.Dtos;
using Retail25.Domain.Shoppers;

namespace Retail25.Application.Shoppers.Services;

/// <summary>
/// Turns "this shopper is who they say they are" into a signed-in session on a particular phone.
/// <para>
/// Registering, signing in and refreshing all end the same way — mint a pair, write the hash against
/// the device, hand the plaintext back once — and that sequence is easy to get subtly wrong in a way
/// nothing fails on immediately. Forget to rotate and the old refresh token stays valid for ever;
/// forget to clear <c>RevokedAt</c> and the shopper is signed out on their next cold start. One
/// implementation, three callers.
/// </para>
/// </summary>
public sealed class ShopperSessionFactory
{
    private readonly IApplicationDbContext _db;
    private readonly IShopperTokenIssuer _tokens;
    private readonly IDateTime _clock;

    public ShopperSessionFactory(IApplicationDbContext db, IShopperTokenIssuer tokens, IDateTime clock)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
    }

    /// <summary>
    /// Issues a fresh credential pair and binds it to <paramref name="deviceId"/>, creating the device
    /// row on first sight and rotating it on every subsequent call.
    /// </summary>
    /// <param name="biometricEnabled">
    /// Null leaves an existing device's preference alone. A refresh must not silently turn the
    /// fingerprint off just because the refresh request had no opinion about it.
    /// </param>
    public async Task<ShopperSessionDto> StartAsync(
        Shopper shopper,
        string? deviceId,
        string? deviceName,
        bool? biometricEnabled,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        var now = _clock.Now;
        var issued = _tokens.Issue(shopper.Id);
        var hash = _tokens.HashRefreshToken(issued.RefreshToken);

        var device = await _db.ShopperDevices
            .FirstOrDefaultAsync(d => d.ShopperId == shopper.Id && d.DeviceId == deviceId, ct);

        if (device is null)
        {
            var registration = ShopperDevice.Register(
                shopper.Id,
                deviceId,
                deviceName,
                hash,
                issued.RefreshExpiresAt,
                biometricEnabled ?? false);

            // A device id the phone failed to supply is not worth refusing a sign-in over — the
            // shopper simply does not get the fingerprint shortcut on this handset. Falling back to a
            // per-session identifier keeps the access token working and lets them shop.
            device = registration.IsSuccess
                ? registration.Value
                : ShopperDevice.Register(
                    shopper.Id,
                    Guid.NewGuid().ToString("N"),
                    deviceName,
                    hash,
                    issued.RefreshExpiresAt,
                    biometricEnabled: false).Value;

            _db.ShopperDevices.Add(device);
        }
        else
        {
            device.IssueToken(hash, issued.RefreshExpiresAt, now);
            device.Rename(deviceName);

            if (biometricEnabled is { } wanted)
            {
                device.EnableBiometric(wanted);
            }
        }

        shopper.RecordSignIn(now);

        await _db.SaveChangesAsync(ct);

        return new ShopperSessionDto(
            Describe(shopper),
            issued.AccessToken,
            issued.ExpiresAt,
            issued.RefreshToken,
            issued.RefreshExpiresAt,
            device.BiometricEnabled);
    }

    public static ShopperDto Describe(Shopper shopper)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        return new ShopperDto(
            shopper.Id,
            shopper.FirstName,
            shopper.LastName,
            shopper.Email,
            shopper.Phone,
            shopper.EmailConfirmed);
    }
}
