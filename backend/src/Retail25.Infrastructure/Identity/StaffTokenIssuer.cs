using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;

namespace Retail25.Infrastructure.Identity;

/// <summary>Names shared by the issuing and validating halves, so they cannot drift apart.</summary>
public static class StaffAuthentication
{
    /// <summary>The default scheme. A bare <c>[Authorize]</c> means a signed-in member of staff.</summary>
    public const string Scheme = "StaffJwt";

    /// <summary>Separates an access token from a refresh token signed by the same key.</summary>
    public const string TokenUseClaim = "token_use";

    public const string AccessTokenUse = "access";
    public const string RefreshTokenUse = "refresh";

    /// <summary>
    /// Identity's security stamp, carried in the refresh token.
    /// <para>
    /// This is what makes a self-contained token revocable without a table to revoke it in. Identity
    /// already rotates the stamp whenever a password is reset or an account is disabled, so a
    /// refresh presented after either of those no longer matches the user's current stamp and is
    /// refused. Without it, a JWT keeps working until it expires no matter what an administrator
    /// does — which on a system holding takings is the wrong answer.
    /// </para>
    /// </summary>
    public const string SecurityStampClaim = "security_stamp";
}

/// <summary>Configuration for <see cref="StaffTokenIssuer"/>, bound from <c>Auth:Jwt</c>.</summary>
public sealed class StaffTokenOptions
{
    public const string Section = "Auth:Jwt";

    /// <summary>
    /// The HMAC signing key, at least 32 bytes. There is no default and there must not be one: a key
    /// shipped in source is a key every deployment shares, and anyone holding it can mint a token for
    /// any member of staff, with any permission. Startup fails loudly rather than falling back.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "retail25";

    public string Audience { get; set; } = "retail25.api";

    /// <summary>
    /// Short, and this is the whole cost of moving to self-contained tokens.
    /// <para>
    /// The permissions travel inside the token, so the API can authorise without a database round
    /// trip — which is what makes it fast, and also what stops a revoked permission taking effect
    /// immediately. Fifteen minutes is the window in which somebody keeps a grant an administrator
    /// has just taken away. Reference tokens closed that window instantly and cost a lookup on every
    /// request; this is the trade, and it is bounded rather than open-ended.
    /// </para>
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// Long enough that a shift does not end with an unexpected sign-in prompt at the till.
    /// Revoked the moment the security stamp changes.
    /// </summary>
    public int RefreshTokenDays { get; set; } = 14;
}

/// <summary>The pair handed back to the caller.</summary>
public sealed record StaffTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpires,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpires);

/// <summary>
/// Mints a member of staff's credentials: a signed, self-contained access token carrying everything
/// the API needs to authorise a request, and a refresh token that is checked against the account's
/// current security stamp rather than against a table.
/// </summary>
public sealed class StaffTokenIssuer
{
    private readonly StaffTokenOptions _options;
    private readonly IDateTime _clock;
    private readonly SymmetricSecurityKey _key;

    public StaffTokenIssuer(IOptions<StaffTokenOptions> options, IDateTime clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _clock = clock;

        var secret = _options.SigningKey ?? string.Empty;

        if (Encoding.UTF8.GetByteCount(secret) < 32)
        {
            throw new InvalidOperationException(
                "Auth:Jwt:SigningKey must be set to at least 32 bytes before the API can issue tokens. "
                + "Generate one per environment; a shared or hardcoded key lets anyone holding it sign "
                + "in as any member of staff, with any permission.");
        }

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public SymmetricSecurityKey SigningKey => _key;

    public string Issuer => _options.Issuer;

    public string Audience => _options.Audience;

    /// <summary>
    /// The access token.
    /// <para>
    /// <paramref name="claims"/> is the identity the caller has already built — staff id, location,
    /// role and one entry per permission. It is passed in rather than assembled here because the
    /// authority on what somebody may do is Identity plus the permission tables, and this class
    /// should not become a second opinion on that.
    /// </para>
    /// </summary>
    public StaffTokens Issue(long userId, string securityStamp, IEnumerable<Claim> claims)
    {
        var now = _clock.Now;
        var accessExpires = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpires = now.AddDays(_options.RefreshTokenDays);

        var incoming = claims.ToList();

        // Identity's own claims are filtered before the subject is added.
        //
        // The handler maps ClaimTypes.NameIdentifier to "sub" when it writes the token, so passing
        // Identity's principal through untouched and then adding a "sub" of our own produced two of
        // them. A repeated claim serialises as a JSON array, and the reader — which expects a string
        // — refuses the whole token with a base64 decoding error that names neither the claim nor
        // the cause. Every request came back 401 while the token itself looked perfect.
        var identity = new ClaimsIdentity(incoming.Where(claim =>
            claim.Type is not (ClaimTypes.NameIdentifier or JwtRegisteredClaimNames.Sub)
            && claim.Type != AuthConstants.PermissionClaim));

        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, Id(userId)));

        // Sixty-one permissions as sixty-one claims is about 2.1KB of token, and the token is only
        // part of what has to fit: the front end seals it into a session cookie, and browsers drop a
        // cookie over roughly 4KB silently. Signing in returned 200, the cookie was discarded, and
        // every subsequent request was anonymous — with no error anywhere to say why.
        //
        // One space-delimited claim carries the same set for around half the bytes, because what
        // was expensive was never the names but sixty-one repetitions of the JSON around them.
        var permissions = incoming
            .Where(claim => claim.Type == AuthConstants.PermissionClaim)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (permissions.Length > 0)
        {
            identity.AddClaim(new Claim(AuthConstants.PackedPermissionsClaim, string.Join(' ', permissions)));
        }
        identity.AddClaim(new Claim(StaffAuthentication.TokenUseClaim, StaffAuthentication.AccessTokenUse));
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")));

        var access = Write(identity, accessExpires, now);

        // The refresh token carries nothing but who it is for and which version of that account it
        // was issued against. It cannot be replayed as an access token because the validating side
        // requires token_use=access, and it cannot outlive a password reset because the stamp moves.
        var refreshIdentity = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, Id(userId)),
            new Claim(StaffAuthentication.TokenUseClaim, StaffAuthentication.RefreshTokenUse),
            new Claim(StaffAuthentication.SecurityStampClaim, securityStamp ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        ]);

        var refresh = Write(refreshIdentity, refreshExpires, now);

        return new StaffTokens(access, accessExpires, refresh, refreshExpires);
    }

    private string Write(ClaimsIdentity identity, DateTimeOffset expires, DateTimeOffset now)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
            Subject = identity,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string Id(long value)
        => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
