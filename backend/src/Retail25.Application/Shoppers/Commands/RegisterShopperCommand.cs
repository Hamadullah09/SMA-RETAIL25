using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Shoppers.Dtos;
using Retail25.Application.Shoppers.Services;
using Retail25.Domain.Common;
using Retail25.Domain.Shoppers;

namespace Retail25.Application.Shoppers.Commands;

/// <summary>
/// Creates a shopper account from the phone app's sign-up form and signs them straight in.
/// <para>
/// No <c>[RequiresPermission]</c>, because there is nobody to hold a permission yet — this is the
/// anonymous entry point to the whole customer surface. It is rate limited at the controller instead.
/// </para>
/// </summary>
public sealed record RegisterShopperCommand(
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Email,
    string? Password,
    string? DeviceId = null,
    string? DeviceName = null,
    bool EnableBiometric = false) : IRequest<Result<ShopperSessionDto>>;

public sealed class RegisterShopperHandler : IRequestHandler<RegisterShopperCommand, Result<ShopperSessionDto>>
{
    /// <summary>
    /// Eight characters, and no composition rules.
    /// <para>
    /// Length is the only requirement that reliably buys entropy. Mandating a digit and a symbol
    /// mostly produces "Password1!" and a shopper who cannot remember it, which is why current NIST
    /// guidance drops those rules. The real defence is that the hash is Argon2id and the endpoint is
    /// rate limited.
    /// </para>
    /// </summary>
    public const int MinimumPasswordLength = 8;

    public static readonly Error PasswordTooShort = new(
        "shopper.password_too_short",
        $"A password must be at least {MinimumPasswordLength} characters.");

    private readonly IApplicationDbContext _db;
    private readonly IShopperPasswordHasher _passwords;
    private readonly ShopperSessionFactory _sessions;

    public RegisterShopperHandler(
        IApplicationDbContext db,
        IShopperPasswordHasher passwords,
        ShopperSessionFactory sessions)
    {
        _db = db;
        _passwords = passwords;
        _sessions = sessions;
    }

    public async Task<Result<ShopperSessionDto>> Handle(RegisterShopperCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var password = request.Password ?? string.Empty;

        if (password.Length < MinimumPasswordLength)
        {
            return Result.Failure<ShopperSessionDto>(PasswordTooShort);
        }

        // Hashed before the duplicate check rather than after, so that a request for an address that
        // already exists costs the same wall-clock time as one that does not. Skipping the Argon2 work
        // on the "taken" path turns this endpoint into a fast oracle for which of your customers'
        // email addresses are registered.
        var hash = _passwords.Hash(password);

        var creation = Shopper.Create(
            request.FirstName,
            request.LastName,
            request.Phone,
            request.Email,
            hash);

        if (creation.IsFailure)
        {
            return Result.Failure<ShopperSessionDto>(creation.Error);
        }

        var shopper = creation.Value;

        var taken = await _db.Shoppers
            .AnyAsync(s => s.NormalizedEmail == shopper.NormalizedEmail, ct);

        if (taken)
        {
            return Result.Failure<ShopperSessionDto>(Shopper.EmailTaken);
        }

        // Saved on its own first: the device row points at the shopper by id, and the id does not
        // exist until the insert has happened (see Entity's remarks on integer identity).
        _db.Shoppers.Add(shopper);
        await _db.SaveChangesAsync(ct);

        var session = await _sessions.StartAsync(
            shopper,
            request.DeviceId,
            request.DeviceName,
            request.EnableBiometric,
            ct);

        return Result.Success(session);
    }
}
