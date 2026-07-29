using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Auth;
using Retail25.Application.Common;
using Retail25.Application.UnitTests.Carts;
using Retail25.Domain.Security;
using Retail25.Domain.Staff;
using Retail25.Infrastructure.Identity;
using Xunit;

namespace Retail25.Application.UnitTests.Auth;

/// <summary>
/// POS fast user switching (guide p.13, doc 07). A PIN re-attributes a sale inside a session the
/// station already holds — it is never a login on its own, which is what makes a four-digit secret
/// acceptable at all.
/// </summary>
public sealed class StaffPinTests
{
    private const string Pin = "4821";

    [Fact]
    public async Task A_correct_pin_returns_the_staff_member_and_their_permissions()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handlers = await BuildAsync(harness, "SK", Pin, [PermissionKeys.Pos.Sell, PermissionKeys.Pos.Discount]);

        var result = await handlers.Handle(new VerifyStaffPinCommand("SK", Pin, harness.Station.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.StaffCode.Should().Be("SK");
        result.Value.Permissions.Should().Contain(PermissionKeys.Pos.Discount);
    }

    [Fact]
    public async Task The_staff_code_is_matched_case_insensitively()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handlers = await BuildAsync(harness, "SK", Pin, [PermissionKeys.Pos.Sell]);

        var result = await handlers.Handle(new VerifyStaffPinCommand("sk", Pin, harness.Station.Id), default);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// The same error for a wrong PIN and an unknown code. Distinguishing them would let anyone with
    /// a keypad enumerate who works here, which is the first half of a targeted attempt.
    /// </summary>
    [Theory]
    [InlineData("SK", "0000")]
    [InlineData("NOBODY", "4821")]
    public async Task A_wrong_pin_and_an_unknown_code_are_indistinguishable(string code, string pin)
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handlers = await BuildAsync(harness, "SK", Pin, [PermissionKeys.Pos.Sell]);

        var result = await handlers.Handle(new VerifyStaffPinCommand(code, pin, harness.Station.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("staff.pin_invalid");
    }

    /// <summary>
    /// A four-digit PIN has ten thousand combinations. Without a lockout, a machine left alone in a
    /// shop is an afternoon's work.
    /// </summary>
    [Fact]
    public async Task Five_wrong_attempts_lock_the_profile()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handlers = await BuildAsync(harness, "SK", Pin, [PermissionKeys.Pos.Sell]);

        for (var attempt = 0; attempt < StaffProfile.MaxPinAttempts; attempt++)
        {
            await handlers.Handle(new VerifyStaffPinCommand("SK", "0000", harness.Station.Id), default);
        }

        // Even the correct PIN is refused while the lockout stands.
        var result = await handlers.Handle(new VerifyStaffPinCommand("SK", Pin, harness.Station.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("staff.pin_locked");
    }

    [Fact]
    public async Task A_correct_pin_clears_the_failure_count()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handlers = await BuildAsync(harness, "SK", Pin, [PermissionKeys.Pos.Sell]);

        await handlers.Handle(new VerifyStaffPinCommand("SK", "0000", harness.Station.Id), default);
        await handlers.Handle(new VerifyStaffPinCommand("SK", Pin, harness.Station.Id), default);

        var staff = await harness.Db.StaffProfiles.SingleAsync(s => s.StaffCode == "SK");
        staff.FailedPinAttempts.Should().Be(0);
    }

    [Fact]
    public async Task An_inactive_staff_member_cannot_switch_in()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handlers = await BuildAsync(harness, "SK", Pin, [PermissionKeys.Pos.Sell]);

        var staff = await harness.Db.StaffProfiles.SingleAsync(s => s.StaffCode == "SK");
        staff.SetActive(false);
        await harness.Db.SaveChangesAsync();

        var result = await handlers.Handle(new VerifyStaffPinCommand("SK", Pin, harness.Station.Id), default);

        result.IsFailure.Should().BeTrue();
    }

    /// <summary>Every attempt is recorded — a failed one especially, because that is the interesting one.</summary>
    [Fact]
    public async Task Both_success_and_failure_are_audited()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var audit = Substitute.For<IAuditWriter>();
        var handlers = await BuildAsync(harness, "SK", Pin, [PermissionKeys.Pos.Sell], audit);

        await handlers.Handle(new VerifyStaffPinCommand("SK", "0000", harness.Station.Id), default);
        await handlers.Handle(new VerifyStaffPinCommand("SK", Pin, harness.Station.Id), default);

        await audit.Received().RecordAsync(
            AuditAction.SignInFailed,
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());

        await audit.Received().RecordAsync(
            AuditAction.SignedIn,
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The plaintext PIN is never stored, so a database read cannot recover it.</summary>
    [Fact]
    public async Task The_pin_is_stored_only_as_a_hash()
    {
        using var harness = await PosTestHarness.CreateAsync();
        await BuildAsync(harness, "SK", Pin, [PermissionKeys.Pos.Sell]);

        var staff = await harness.Db.StaffProfiles.SingleAsync(s => s.StaffCode == "SK");

        staff.PinHash.Should().NotBeNull();
        staff.PinHash.Should().NotContain(Pin);
        staff.PinHash.Should().StartWith("argon2id$");
    }

    [Fact]
    public async Task A_short_pin_is_refused()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handlers = await BuildAsync(harness, "SK", Pin, [PermissionKeys.Pos.Sell]);

        var staff = await harness.Db.StaffProfiles.SingleAsync(s => s.StaffCode == "SK");

        var result = await handlers.Handle(new SetStaffPinCommand(staff.Id, "12"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("staff.pin_too_short");
    }

    [Fact]
    public async Task A_supervisor_can_clear_a_lockout()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handlers = await BuildAsync(harness, "SK", Pin, [PermissionKeys.Pos.Sell]);

        for (var attempt = 0; attempt < StaffProfile.MaxPinAttempts; attempt++)
        {
            await handlers.Handle(new VerifyStaffPinCommand("SK", "0000", harness.Station.Id), default);
        }

        var staff = await harness.Db.StaffProfiles.SingleAsync(s => s.StaffCode == "SK");
        await handlers.Handle(new UnlockStaffPinCommand(staff.Id), default);

        var result = await handlers.Handle(new VerifyStaffPinCommand("SK", Pin, harness.Station.Id), default);
        result.IsSuccess.Should().BeTrue();
    }

    private static async Task<StaffPinHandlers> BuildAsync(
        PosTestHarness harness,
        string staffCode,
        string pin,
        IReadOnlyList<string> permissions,
        IAuditWriter? audit = null)
    {
        var hasher = new Argon2PinHasher();
        var userId = Guid.NewGuid();

        var staff = StaffProfile.Create(userId, staffCode, "Sarah", "Kaur", accessLevel: 3);
        staff.SetPin(hasher.Hash(pin));

        harness.Db.StaffProfiles.Add(staff);
        await harness.Db.SaveChangesAsync();

        var resolver = Substitute.For<IPermissionResolver>();
        resolver.ResolveForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(permissions, StringComparer.Ordinal));

        return new StaffPinHandlers(
            harness.Db,
            hasher,
            resolver,
            audit ?? Substitute.For<IAuditWriter>(),
            harness.Clock);
    }
}

/// <summary>
/// Argon2id hashing (doc 07). A PIN is short by necessity, so the hash carries the cost the secret
/// cannot.
/// </summary>
public sealed class PinHasherTests
{
    private readonly Argon2PinHasher _hasher = new();

    [Fact]
    public void A_pin_verifies_against_its_own_hash()
    {
        var hash = _hasher.Hash("4821");

        _hasher.Verify("4821", hash).Should().BeTrue();
        _hasher.Verify("4822", hash).Should().BeFalse();
    }

    /// <summary>
    /// A per-PIN salt means two staff who chose the same PIN do not share a hash — otherwise one
    /// cracked hash would unlock everyone who picked 1234.
    /// </summary>
    [Fact]
    public void The_same_pin_hashes_differently_every_time()
    {
        var first = _hasher.Hash("4821");
        var second = _hasher.Hash("4821");

        first.Should().NotBe(second);
        _hasher.Verify("4821", first).Should().BeTrue();
        _hasher.Verify("4821", second).Should().BeTrue();
    }

    [Fact]
    public void The_parameters_travel_with_the_hash_so_they_can_be_raised_later()
    {
        var hash = _hasher.Hash("4821");

        // argon2id$iterations$memory$parallelism$salt$hash
        hash.Split('$').Should().HaveCount(6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("argon2id$bad")]
    [InlineData("argon2id$4$65536$2$notbase64$alsonot")]
    public void A_malformed_hash_verifies_as_false_rather_than_throwing(string hash)
        => _hasher.Verify("4821", hash).Should().BeFalse();
}
