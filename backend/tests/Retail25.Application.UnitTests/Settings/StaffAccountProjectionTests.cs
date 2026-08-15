using System.Reflection;
using FluentAssertions;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Settings;
using Retail25.Application.UnitTests.Masters;
using Xunit;

namespace Retail25.Application.UnitTests.Settings;

/// <summary>
/// What the Users screen is allowed to know about somebody.
/// <para>
/// An administrator needs enough to actually administer an account — which address it signs in
/// with, what it may do, whether it can get in at all — and nothing that would let them, or anybody
/// reading over their shoulder, sign in as that person. The line between those two sets is the
/// whole point of this file.
/// </para>
/// </summary>
public sealed class StaffAccountProjectionTests
{
    [Fact]
    public async Task The_screen_can_say_what_somebody_signs_in_with()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("BM", "Bea", "Mills");

        harness.Accounts.AccountsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, UserAccountInfo>
            {
                [staff.UserId] = new(staff.UserId, "bea@shop.com", true, ["Cashier"], true, null),
            });

        var result = await harness.Settings.Handle(
            new GetSettingsQuery(harness.Location.Id), CancellationToken.None);

        var row = result.Value.Staff.Single(s => s.Id == staff.Id);

        row.Email.Should().Be("bea@shop.com");
        row.Roles.Should().ContainSingle().Which.Should().Be("Cashier");
        row.CanSignIn.Should().BeTrue();
    }

    /// <summary>
    /// Disabled and locked out both mean "cannot get in", and the screen needs to tell them apart:
    /// one is a decision somebody made, the other is five bad passwords and clears itself.
    /// </summary>
    [Fact]
    public async Task A_lockout_is_reported_with_the_time_it_lifts()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("BM", "Bea", "Mills");
        var until = DateTimeOffset.UtcNow.AddMinutes(15);

        harness.Accounts.AccountsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, UserAccountInfo>
            {
                [staff.UserId] = new(staff.UserId, "bea@shop.com", true, ["Cashier"], false, until),
            });

        var result = await harness.Settings.Handle(
            new GetSettingsQuery(harness.Location.Id), CancellationToken.None);

        var row = result.Value.Staff.Single(s => s.Id == staff.Id);

        row.CanSignIn.Should().BeFalse();
        row.LockedOutUntil.Should().BeCloseTo(until, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// A staff record can outlive its sign-in. That has to render as "no account", not as a page
    /// that fails to load, because the person it describes still has sales attributed to them.
    /// </summary>
    [Fact]
    public async Task Somebody_with_no_sign_in_still_appears()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("BM", "Bea", "Mills");

        var result = await harness.Settings.Handle(
            new GetSettingsQuery(harness.Location.Id), CancellationToken.None);

        var row = result.Value.Staff.Single(s => s.Id == staff.Id);

        row.Email.Should().BeNull();
        row.CanSignIn.Should().BeFalse();
        row.FullNameIsPresent();
    }

    /// <summary>
    /// The guarantee, made structural.
    /// <para>
    /// Every other test here checks what the DTO says. This one checks what it cannot say: there is
    /// no member on it that a password, hash, PIN or token could be assigned to. Reviewing that by
    /// eye works right up until somebody adds a field in a hurry, which is precisely the change
    /// nobody reviews carefully. A name-shaped check fails at that commit instead of at a breach.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("password")]
    [InlineData("hash")]
    [InlineData("pin")]
    [InlineData("secret")]
    [InlineData("token")]
    [InlineData("stamp")]
    [InlineData("salt")]
    public void The_projection_has_nowhere_to_put_a_secret(string forbidden)
    {
        var offenders = typeof(StaffSettingsDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            // HasPin and PinLocked are state, not the PIN: booleans that say whether one is set and
            // whether it is currently refusing. Neither can be typed into a keypad.
            .Where(name => name is not ("HasPin" or "PinLocked" or "PinLockedUntil"))
            .Where(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            .ToList();

        offenders.Should().BeEmpty(
            "the Users screen must not be able to display a {0}, and the way to guarantee that is for its data never to carry one",
            forbidden);
    }
}

file static class StaffRowAssertions
{
    /// <summary>Reads as a sentence at the call site; the name is what is being asserted about.</summary>
    public static void FullNameIsPresent(this StaffSettingsDto row)
        => row.FirstName.Should().NotBeNullOrWhiteSpace();
}
