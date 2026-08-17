using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Shoppers.Dtos;
using Retail25.Application.Shoppers.Services;
using Retail25.Domain.Common;
using Retail25.Domain.Shoppers;

namespace Retail25.Application.Shoppers.Commands;

/// <summary>Email and password, from the phone app's sign-in screen.</summary>
public sealed record SignInShopperCommand(
    string? Email,
    string? Password,
    string? DeviceId = null,
    string? DeviceName = null,
    bool EnableBiometric = false) : IRequest<Result<ShopperSessionDto>>;

public sealed class SignInShopperHandler : IRequestHandler<SignInShopperCommand, Result<ShopperSessionDto>>
{
    /// <summary>
    /// Verified against when no account matches, purely to spend the same time as a real check.
    /// <para>
    /// Without it, "no such email" returns in microseconds and "wrong password" returns in the tens
    /// of milliseconds Argon2id costs, and the difference is measurable over the network. That gap is
    /// a membership oracle: it tells an attacker which addresses have accounts here, which is exactly
    /// what the uniform error message exists to withhold.
    /// </para>
    /// </summary>
    private const string DecoyHash =
        "$argon2id$v=19$m=65536,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly IApplicationDbContext _db;
    private readonly IShopperPasswordHasher _passwords;
    private readonly ShopperSessionFactory _sessions;

    public SignInShopperHandler(
        IApplicationDbContext db,
        IShopperPasswordHasher passwords,
        ShopperSessionFactory sessions)
    {
        _db = db;
        _passwords = passwords;
        _sessions = sessions;
    }

    public async Task<Result<ShopperSessionDto>> Handle(SignInShopperCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalized = Shopper.Normalize(request.Email);
        var password = request.Password ?? string.Empty;

        var shopper = await _db.Shoppers
            .FirstOrDefaultAsync(s => s.NormalizedEmail == normalized, ct);

        // Always verify something, then decide. See DecoyHash.
        var verified = _passwords.Verify(password, shopper?.PasswordHash ?? DecoyHash);

        if (shopper is null || !verified)
        {
            // One error for both causes. "No account with that email" is a free list of who shops
            // here, and the shopper who genuinely mistyped is no better served by knowing which half
            // they got wrong.
            return Result.Failure<ShopperSessionDto>(Shopper.CredentialsInvalid);
        }

        if (!shopper.IsActive)
        {
            return Result.Failure<ShopperSessionDto>(Shopper.Deactivated);
        }

        var session = await _sessions.StartAsync(
            shopper,
            request.DeviceId,
            request.DeviceName,
            request.EnableBiometric,
            ct);

        return Result.Success(session);
    }
}
