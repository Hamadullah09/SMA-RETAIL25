using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Shoppers.Dtos;
using Retail25.Application.Shoppers.Services;
using Retail25.Domain.Common;
using Retail25.Domain.Shoppers;

namespace Retail25.Application.Shoppers.Commands;

/// <summary>
/// Exchanges a refresh token for a new pair. <b>This is the endpoint the fingerprint unlocks.</b>
/// <para>
/// The sensor is not consulted here and cannot be — Android will not release biometric data to an
/// application, let alone over a network. What the tap actually does is unlock the Keystore entry the
/// refresh token was sealed in, on the handset, so the app can read the token and call this. From the
/// server's point of view a biometric unlock and an ordinary silent refresh are the same request; the
/// difference is entirely in what it cost the person holding the phone to produce the token.
/// </para>
/// <para>
/// That is also why this endpoint carries the same weight as a password: whoever holds the token is
/// the shopper. It is single-use and rotated on every call, so a copy taken in transit is dead as
/// soon as the real phone refreshes again.
/// </para>
/// </summary>
public sealed record RefreshShopperSessionCommand(
    string? RefreshToken,
    string? DeviceName = null) : IRequest<Result<ShopperSessionDto>>;

public sealed class RefreshShopperSessionHandler
    : IRequestHandler<RefreshShopperSessionCommand, Result<ShopperSessionDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IShopperTokenIssuer _tokens;
    private readonly ShopperSessionFactory _sessions;
    private readonly IDateTime _clock;

    public RefreshShopperSessionHandler(
        IApplicationDbContext db,
        IShopperTokenIssuer tokens,
        ShopperSessionFactory sessions,
        IDateTime clock)
    {
        _db = db;
        _tokens = tokens;
        _sessions = sessions;
        _clock = clock;
    }

    public async Task<Result<ShopperSessionDto>> Handle(RefreshShopperSessionCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result.Failure<ShopperSessionDto>(ShopperDevice.TokenRejected);
        }

        var hash = _tokens.HashRefreshToken(request.RefreshToken);

        // Looked up by the hash alone, not by (device, hash). The token is the credential; asking the
        // caller to also name the device would let a caller who holds a valid token be refused for
        // getting the device id wrong, which protects nothing and strands a shopper at the till.
        var device = await _db.ShopperDevices
            .FirstOrDefaultAsync(d => d.RefreshTokenHash == hash, ct);

        // A token that matches nothing is either forged or already spent — rotation means a redeemed
        // token's hash is gone from the table. Both are the same answer to the caller.
        if (device is null || !device.IsUsable(_clock.Now))
        {
            return Result.Failure<ShopperSessionDto>(ShopperDevice.TokenRejected);
        }

        var shopper = await _db.Shoppers
            .FirstOrDefaultAsync(s => s.Id == device.ShopperId, ct);

        if (shopper is null)
        {
            return Result.Failure<ShopperSessionDto>(ShopperDevice.TokenRejected);
        }

        if (!shopper.IsActive)
        {
            return Result.Failure<ShopperSessionDto>(Shopper.Deactivated);
        }

        // Biometric preference passed as null: a refresh is not the shopper changing their mind about
        // the fingerprint, so whatever they chose on this handset stands.
        var session = await _sessions.StartAsync(
            shopper,
            device.DeviceId,
            request.DeviceName,
            biometricEnabled: null,
            ct);

        return Result.Success(session);
    }
}
