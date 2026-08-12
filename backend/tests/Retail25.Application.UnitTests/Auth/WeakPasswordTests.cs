using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Retail25.Infrastructure.Identity;
using Xunit;

namespace Retail25.Application.UnitTests.Auth;

/// <summary>
/// The passwords a shop must not be allowed to choose.
/// <para>
/// The policy was eight characters and a digit, which "password1" satisfies. Adding the rules that
/// would reject it — a capital, a symbol — gets "Password1!", which satisfies those too and is no
/// harder to guess. Composition rules do not measure the thing they appear to measure, which is
/// why NIST SP 800-63B stopped recommending them and recommends a banned-password check instead.
/// </para>
/// </summary>
public sealed class WeakPasswordTests
{
    private static readonly UserManager<ApplicationUser> AnyManager = null!;

    private static ApplicationUser User(string email = "ayesha.khan@sma.rms.com", string display = "Ayesha Khan")
        => new() { Email = email, UserName = email, DisplayName = display };

    private static async Task<IdentityResult> Check(string password, ApplicationUser? user = null)
        => await new WeakPasswordValidator().ValidateAsync(AnyManager, user ?? User(), password);

    [Theory]
    [InlineData("password")]
    [InlineData("Password1")]
    [InlineData("P@ssw0rd")]
    [InlineData("P@$$w0rd!")]
    [InlineData("password2026")]
    [InlineData("welcome123")]
    [InlineData("letmein")]
    [InlineData("qwertyuiop")]
    [InlineData("1qaz2wsx")]
    [InlineData("changeme")]
    [InlineData("administrator")]
    public async Task A_guessable_password_is_refused(string password)
    {
        var result = await Check(password);

        result.Succeeded.Should().BeFalse($"\"{password}\" is among the first an attacker tries");
        result.Errors.Should().Contain(e => e.Code == WeakPasswordValidator.TooCommonCode);
    }

    /// <summary>
    /// The shapes a shop invents for itself. A till whose password is the product's name is not
    /// meaningfully protected.
    /// </summary>
    [Theory]
    [InlineData("retail25")]
    [InlineData("SmaRetail")]
    [InlineData("cashier1")]
    [InlineData("Karachi123")]
    public async Task A_password_made_from_the_shop_is_refused(string password)
    {
        (await Check(password)).Succeeded.Should().BeFalse();
    }

    /// <summary>
    /// A password built from the account it protects is public knowledge — the address is on every
    /// email the person has ever sent.
    /// </summary>
    [Theory]
    [InlineData("ayesha-till-2026")]
    [InlineData("KhanKhanKhan99")]
    [InlineData("ayesha.khan.2026")]
    public async Task A_password_containing_the_users_own_name_is_refused(string password)
    {
        var result = await Check(password);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == WeakPasswordValidator.ContainsIdentityCode);
    }

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("Str0ng-Till-2026!")]
    [InlineData("thequickbrownfoxjumps")]
    [InlineData("SmaRetail2026-Kx7924")]
    public async Task A_password_that_is_actually_unpredictable_is_accepted(string password)
    {
        (await Check(password)).Succeeded.Should().BeTrue();
    }

    /// <summary>
    /// Dressing a common password in punctuation does not make it a different guess, and a
    /// validator that compares literally would be fooled by exactly the substitutions people reach
    /// for first.
    /// </summary>
    [Fact]
    public async Task Punctuation_and_leetspeak_do_not_disguise_a_common_password()
    {
        (await Check("p-a-s-s-w-o-r-d")).Succeeded.Should().BeFalse();
        (await Check("l3tm31n")).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task An_empty_password_is_left_to_the_length_rule()
    {
        // Not this validator's job to complain about length — Identity's own rule reports that, and
        // two validators saying the same thing gives the user two errors for one mistake.
        (await Check("")).Succeeded.Should().BeTrue();
    }

    /// <summary>
    /// A short name must not swallow every password containing it. "Ada" is three characters and
    /// appears inside a great many perfectly good passphrases.
    /// </summary>
    [Fact]
    public async Task A_very_short_name_is_not_used_as_a_substring_rule()
    {
        var user = User("ada@shop.test", "Ada Lovelace");

        (await Check("a-cadaverous-fandango", user)).Succeeded.Should().BeTrue();
    }
}
