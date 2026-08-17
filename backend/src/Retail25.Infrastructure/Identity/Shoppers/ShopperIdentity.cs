using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Retail25.Application.Abstractions;

namespace Retail25.Infrastructure.Identity.Shoppers;

/// <summary>
/// The shopper behind the current request, read from the bearer token the phone app presented.
/// <para>
/// Reads <c>HttpContext.User</c> live on every access rather than caching it in the constructor, for
/// the same reason <see cref="CurrentUser"/> does: authorization re-authenticates against the scheme
/// the policy names and replaces the principal after this object was built, so a snapshot taken in
/// the constructor is the anonymous one.
/// </para>
/// <para>
/// The token-use claim is checked, not just the subject. Both schemes put a numeric id in
/// <c>sub</c>, so without that check a staff token — which is a perfectly valid bearer token — would
/// resolve here to "shopper number 4", and staff id 4 would be handed shopper 4's basket.
/// </para>
/// </summary>
public sealed class CurrentShopper : ICurrentShopper
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentShopper(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    public long? ShopperId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;

            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            if (!user.HasClaim(
                    ShopperAuthentication.TokenUseClaim,
                    ShopperAuthentication.TokenUseValue))
            {
                return null;
            }

            var subject = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            return long.TryParse(subject, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;
        }
    }

    public bool IsAuthenticated => ShopperId is not null;
}

/// <summary>
/// Shopper passwords, hashed with the same Argon2id primitive as staff PINs.
/// <para>
/// Delegation rather than a second configuration of Argon2. Two independently tuned instances of the
/// same algorithm in one codebase is how one of them ends up with the weaker parameters and nobody
/// notices, since both produce hashes that verify perfectly well against themselves.
/// </para>
/// </summary>
public sealed class ShopperPasswordHasher : IShopperPasswordHasher
{
    private readonly IPinHasher _inner;

    public ShopperPasswordHasher(IPinHasher inner) => _inner = inner;

    public string Hash(string password) => _inner.Hash(password);

    public bool Verify(string password, string hash) => _inner.Verify(password, hash);
}
