using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Retail25.Application.Abstractions;

namespace Retail25.Infrastructure.Identity.Shoppers;

/// <summary>Names shared by the issuing and validating halves, so they cannot drift apart.</summary>
public static class ShopperAuthentication
{
    /// <summary>
    /// The authentication scheme shopper endpoints name explicitly.
    /// <para>
    /// It has to be named explicitly, and that is the point. The default authorization policy in
    /// <see cref="IdentityRegistration"/> requires the OpenIddict scheme, so a bare <c>[Authorize]</c>
    /// anywhere in this application means "a signed-in member of staff". A shopper token cannot
    /// satisfy it. The only way to reach a shopper-authenticated action is for that action to say so
    /// in its attribute, which makes the customer-facing surface enumerable by grep.
    /// </para>
    /// </summary>
    public const string Scheme = "Shopper";

    /// <summary>Marks a token as belonging to the phone app rather than to staff.</summary>
    public const string TokenUseClaim = "token_use";

    public const string TokenUseValue = "shopper";
}

/// <summary>Configuration for <see cref="ShopperTokenIssuer"/>, bound from the <c>Shoppers</c> section.</summary>
public sealed class ShopperTokenOptions
{
    public const string Section = "Shoppers";

    /// <summary>
    /// The HMAC signing key, at least 32 bytes. There is no default and there must not be one: a
    /// key shipped in source is a key every deployment shares, and anyone holding it can mint a token
    /// for any shopper. Startup fails loudly rather than falling back.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "retail25";

    public string Audience { get; set; } = "retail25.shopper";

    /// <summary>
    /// Short, because this token is not revocable — it is checked by signature alone, with no database
    /// round trip, which is what keeps the cart responsive. Fifteen minutes bounds how long a
    /// deactivated account keeps working.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// Long, because the whole point of the fingerprint shortcut is not typing a password on a phone
    /// in a shop. Revocable at any moment by clearing the device row, unlike the access token.
    /// </summary>
    public int RefreshTokenDays { get; set; } = 60;
}

/// <summary>
/// Mints the phone app's credentials: a signed, self-contained access token and an opaque refresh
/// token whose plaintext this process forgets the moment it is returned.
/// </summary>
public sealed class ShopperTokenIssuer : IShopperTokenIssuer
{
    private readonly ShopperTokenOptions _options;
    private readonly IDateTime _clock;
    private readonly SymmetricSecurityKey _key;

    public ShopperTokenIssuer(IOptions<ShopperTokenOptions> options, IDateTime clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _clock = clock;

        var secret = _options.SigningKey ?? string.Empty;

        if (Encoding.UTF8.GetByteCount(secret) < 32)
        {
            throw new InvalidOperationException(
                "Shoppers:SigningKey must be set to at least 32 bytes before the shopper API can issue "
                + "tokens. Generate one per environment; a shared or hardcoded key lets anyone holding "
                + "it sign in as any customer.");
        }

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public ShopperTokens Issue(long shopperId)
    {
        var now = _clock.Now;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),

            // The entire claim set. No roles, no access level, no permission claims — a shopper is
            // authorised by owning a trolley session, and a token that cannot express a grant is a
            // token that cannot be tricked into carrying one.
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, shopperId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(ShopperAuthentication.TokenUseClaim, ShopperAuthentication.TokenUseValue),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
        };

        var accessToken = new JsonWebTokenHandler().CreateToken(descriptor);

        return new ShopperTokens(
            accessToken,
            expires,
            CreateRefreshToken(),
            now.AddDays(_options.RefreshTokenDays));
    }

    /// <summary>
    /// SHA-256, deliberately fast, unlike the password hash.
    /// <para>
    /// A slow KDF protects a secret a human chose, because that secret is guessable. A refresh token
    /// is 256 bits from a cryptographic RNG — there is no dictionary to run against it, so iterating
    /// the hash would buy nothing and cost a round trip on every cold start of the app. What the hash
    /// is for is making a stolen copy of the table useless, and one pass of SHA-256 does that.
    /// </para>
    /// </summary>
    public string HashRefreshToken(string refreshToken)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken ?? string.Empty)));

    private static string CreateRefreshToken()
        => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
}
