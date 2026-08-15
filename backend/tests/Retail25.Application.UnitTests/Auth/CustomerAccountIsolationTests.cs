using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Retail25.Application.Common;
using Retail25.Infrastructure.Identity;
using Xunit;

namespace Retail25.Application.UnitTests.Auth;

/// <summary>
/// A shopper's own account cannot reach the shop.
/// <para>
/// Counterless checkout means somebody who does not work here signs in, on their own phone, and is
/// handed a session against a real till. Every permission check in the application then stands
/// between that session and the back office, and they all read from one place — so that place is
/// what these tests hold still.
/// </para>
/// <para>
/// The tests below are not about the normal case. A customer account with no staff claims resolves
/// to nothing anyway, and always did. They are about the accounts that go wrong: one seeded with an
/// access level, one that ends up in a staff role, one carrying an explicit grant. Each of those is
/// a plausible mistake — a shared registration path, a copied seeder, an administrator clicking the
/// wrong row — and each one, without the refusal being explicit, silently hands a stranger a till.
/// </para>
/// </summary>
public sealed class CustomerAccountIsolationTests
{
    [Fact]
    public void A_customer_has_no_permissions_at_all()
    {
        var customer = CurrentUserFor(Customer());

        customer.Permissions.Should().BeEmpty();
    }

    /// <summary>
    /// The access-level ladder is the likeliest way this goes wrong. Level 0 sounds like "no access"
    /// and is not — it is the trainee preset, which can ring a sale.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void An_access_level_on_a_customer_account_grants_nothing(int level)
    {
        var customer = CurrentUserFor(Customer(
            new Claim("access_level", level.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        customer.Permissions.Should().BeEmpty(
            "an access level is a fact about a member of staff, and a customer holding one is a mistake, not a promotion");
    }

    /// <summary>
    /// The worst case, and the reason the refusal comes before the ladder rather than after it:
    /// Administrator resolves to every permission there is.
    /// </summary>
    [Fact]
    public void A_customer_who_is_somehow_an_administrator_still_gets_nothing()
    {
        var customer = CurrentUserFor(Customer(
            new Claim(ClaimTypes.Role, "Administrator")));

        customer.Permissions.Should().BeEmpty();
    }

    /// <summary>An explicit grant is still refused: being a customer outranks being given something.</summary>
    [Fact]
    public void An_explicit_grant_on_a_customer_account_is_refused()
    {
        var customer = CurrentUserFor(Customer(
            new Claim("permission", PermissionKeys.Pos.Sell),
            new Claim("permission", PermissionKeys.Settings.Write)));

        customer.Permissions.Should().BeEmpty();
    }

    /// <summary>
    /// The counterweight. If this fails the refusal has been written too broadly and has started
    /// taking permissions away from the people who need them.
    /// </summary>
    [Fact]
    public void Staff_are_unaffected()
    {
        var cashier = CurrentUserFor(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("permission", PermissionKeys.Pos.Sell)],
            authenticationType: "Test")));

        cashier.Permissions.Should().Contain(PermissionKeys.Pos.Sell);
    }

    private static ClaimsPrincipal Customer(params Claim[] extra)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, CurrentUser.CustomerRole),
            new(ClaimTypes.NameIdentifier, "5001"),
        };

        claims.AddRange(extra);

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static CurrentUser CurrentUserFor(ClaimsPrincipal principal)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal },
        };

        return new CurrentUser(accessor);
    }
}
