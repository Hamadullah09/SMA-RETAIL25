namespace Retail25.Application.Shoppers.Dtos;

/// <summary>What the phone app knows about the person signed in on it.</summary>
public sealed record ShopperDto(
    long Id,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    bool EmailConfirmed);

/// <summary>
/// A completed sign-in: who you are, and the two credentials that keep you signed in.
/// <para>
/// <see cref="RefreshToken"/> appears in exactly one response and is never readable again — the
/// server holds only its hash. The app must store it before it acts on anything else in this
/// payload, because there is no way to ask for it a second time.
/// </para>
/// </summary>
public sealed record ShopperSessionDto(
    ShopperDto Shopper,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt,
    bool BiometricEnabled);
