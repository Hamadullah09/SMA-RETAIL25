namespace Retail25.Application.Abstractions;

/// <summary>What the phone is handed after a successful sign-in.</summary>
/// <param name="AccessToken">Short-lived bearer token. Carries the shopper id and nothing else.</param>
/// <param name="ExpiresAt">When <paramref name="AccessToken"/> stops being accepted.</param>
/// <param name="RefreshToken">
/// Opaque, single-use, and the <b>only</b> time its plaintext exists outside the handset — the server
/// keeps a hash. This is the value the app seals in the Android Keystore behind the fingerprint.
/// </param>
/// <param name="RefreshExpiresAt">When the shopper has to type their password again.</param>
public sealed record ShopperTokens(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt);

/// <summary>
/// Mints and verifies the phone app's credentials.
/// <para>
/// Implemented in Infrastructure because signing keys and JWT libraries are infrastructure concerns;
/// declared here because the sign-in and refresh handlers are application logic and must not
/// reference either.
/// </para>
/// </summary>
public interface IShopperTokenIssuer
{
    /// <summary>Issues a fresh pair. The refresh token's plaintext is returned exactly once.</summary>
    ShopperTokens Issue(long shopperId);

    /// <summary>
    /// The one-way transform applied before a refresh token is stored or looked up.
    /// <para>
    /// Exposed rather than kept private because both the issuing path (which stores the hash) and the
    /// refresh path (which searches by it) must apply the identical transform, and two independent
    /// implementations of "hash it" is how a token stops matching itself.
    /// </para>
    /// </summary>
    string HashRefreshToken(string refreshToken);
}
