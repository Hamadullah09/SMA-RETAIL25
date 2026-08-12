using FluentAssertions;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Staff;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Common;
using Retail25.Domain.Staff;
using Xunit;

namespace Retail25.Application.UnitTests.Staff;

/// <summary>
/// Onboarding a colleague. The system shipped with no way to do this at all — the only account was
/// the seeded administrator — so these cover the whole path, not just the happy one.
/// </summary>
public sealed class CreateStaffTests
{
    private const string GoodPassword = "Str0ngEnough!";

    [Fact]
    public async Task A_new_colleague_gets_a_sign_in_and_a_staff_record()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var provisioner = Provisioner();
        var handlers = Handlers(harness, provisioner);

        var result = await handlers.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StaffCode.Should().Be("SK");
        result.Value.FullName.Should().Be("Sam Kerr");
        result.Value.IsActive.Should().BeTrue();

        harness.Db.StaffProfiles.Should().ContainSingle(s => s.StaffCode == "SK" && s.UserId == 42);

        await provisioner.Received(1).CreateAsync(
            "sam@shop.test", "Sam Kerr", GoodPassword, "Cashier", 1, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The staff code is what appears on a receipt, so it is upper-cased on the way in rather than
    /// left to whoever typed it — otherwise "sk" and "SK" are two people to the database and one
    /// person to everybody else.
    /// </summary>
    [Fact]
    public async Task A_staff_code_is_stored_upper_cased()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var handlers = Handlers(harness, Provisioner());

        var result = await handlers.Handle(Command() with { StaffCode = "sk" }, CancellationToken.None);

        result.Value.StaffCode.Should().Be("SK");
    }

    [Fact]
    public async Task An_email_already_in_use_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var provisioner = Provisioner();
        provisioner.EmailTakenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handlers(harness, provisioner).Handle(Command(), CancellationToken.None);

        result.Error.Should().Be(StaffProvisioningHandlers.EmailTaken);
    }

    /// <summary>
    /// Staff codes are unique in the schema. Catching the clash here turns a constraint violation at
    /// SaveChanges — a 500 — into a message the person filling in the form can act on.
    /// </summary>
    [Fact]
    public async Task A_staff_code_already_in_use_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddStaffAsync("SK", "Sam", "Kerr");
        var handlers = Handlers(harness, Provisioner());

        var result = await handlers.Handle(Command(), CancellationToken.None);

        result.Error.Code.Should().Be(StaffProvisioningHandlers.StaffCodeTaken.Code);
    }

    /// <summary>The clash must be caught whatever case it was typed in.</summary>
    [Fact]
    public async Task A_staff_code_clash_is_caught_regardless_of_case()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddStaffAsync("SK", "Sam", "Kerr");
        var handlers = Handlers(harness, Provisioner());

        var result = await handlers.Handle(Command() with { StaffCode = "sk" }, CancellationToken.None);

        result.Error.Code.Should().Be(StaffProvisioningHandlers.StaffCodeTaken.Code);
    }

    [Fact]
    public async Task An_unknown_role_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var provisioner = Provisioner();
        provisioner.RoleExistsAsync("Wizard", Arg.Any<CancellationToken>()).Returns(false);

        var result = await Handlers(harness, provisioner)
            .Handle(Command() with { Role = "Wizard" }, CancellationToken.None);

        result.Error.Code.Should().Be(StaffProvisioningHandlers.UnknownRole.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_email_is_refused(string email)
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness, Provisioner())
            .Handle(Command() with { Email = email }, CancellationToken.None);

        result.Error.Should().Be(StaffProvisioningHandlers.EmailRequired);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("no@domain")]
    [InlineData("@shop.test")]
    [InlineData("two@at@shop.test")]
    [InlineData("sam@")]
    [InlineData("sam @shop.test")]
    public async Task A_malformed_email_is_refused(string email)
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness, Provisioner())
            .Handle(Command() with { Email = email }, CancellationToken.None);

        result.Error.Should().Be(StaffProvisioningHandlers.EmailMalformed);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public async Task An_access_level_outside_zero_to_four_is_refused(int level)
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness, Provisioner())
            .Handle(Command() with { AccessLevel = level }, CancellationToken.None);

        result.Error.Should().Be(StaffProvisioningHandlers.AccessLevelOutOfRange);
    }

    [Fact]
    public async Task A_missing_name_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness, Provisioner())
            .Handle(Command() with { LastName = "  " }, CancellationToken.None);

        result.Error.Should().Be(StaffProvisioningHandlers.NameRequired);
    }

    [Fact]
    public async Task A_non_numeric_pin_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness, Provisioner())
            .Handle(Command() with { Pin = "12a4" }, CancellationToken.None);

        result.Error.Should().Be(StaffProvisioningHandlers.PinNotNumeric);
    }

    [Fact]
    public async Task A_pin_shorter_than_four_digits_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness, Provisioner())
            .Handle(Command() with { Pin = "123" }, CancellationToken.None);

        result.Error.Should().Be(StaffProfile.PinTooShort);
    }

    [Fact]
    public async Task A_supplied_pin_is_hashed_and_never_stored_in_the_clear()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var hasher = Substitute.For<IPinHasher>();
        hasher.Hash("4821").Returns("hashed-pin");

        var handlers = new StaffProvisioningHandlers(harness.Db, Provisioner(), hasher);

        await handlers.Handle(Command() with { Pin = "4821" }, CancellationToken.None);

        var staff = harness.Db.StaffProfiles.Single(s => s.StaffCode == "SK");
        staff.PinHash.Should().Be("hashed-pin");
        staff.PinHash.Should().NotContain("4821");
    }

    [Fact]
    public async Task No_pin_is_allowed_and_leaves_the_profile_without_one()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness, Provisioner()).Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        harness.Db.StaffProfiles.Single(s => s.StaffCode == "SK").HasPin.Should().BeFalse();
    }

    /// <summary>
    /// The password rule lives in Identity's options, not here. When it rejects a password the
    /// handler must pass that verdict through rather than substitute its own wording.
    /// </summary>
    [Fact]
    public async Task A_password_the_identity_validator_rejects_comes_back_with_its_own_code()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var provisioner = Provisioner();
        provisioner
            .CreateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<long>(new Error("identity.password_too_short", "Passwords must be at least 8 characters.")));

        var result = await Handlers(harness, provisioner)
            .Handle(Command() with { Password = "abc" }, CancellationToken.None);

        result.Error.Code.Should().Be("identity.password_too_short");
    }

    /// <summary>
    /// The staff profile must not survive a failed sign-in creation. A profile with no user is a
    /// row that shows up on a commission report for someone who cannot log in.
    /// </summary>
    [Fact]
    public async Task No_staff_profile_is_written_when_the_sign_in_could_not_be_created()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var provisioner = Provisioner();
        provisioner
            .CreateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<long>(new Error("identity.duplicate_user_name", "Taken.")));

        await Handlers(harness, provisioner).Handle(Command(), CancellationToken.None);

        harness.Db.StaffProfiles.Should().NotContain(s => s.StaffCode == "SK");
    }

    [Fact]
    public async Task Resetting_the_password_of_a_missing_staff_member_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness, Provisioner())
            .Handle(new ResetStaffPasswordCommand(9999, GoodPassword), CancellationToken.None);

        result.Error.Should().Be(StaffProvisioningHandlers.StaffNotFound);
    }

    [Fact]
    public async Task Resetting_a_password_goes_through_to_identity_for_that_users_sign_in()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("BM", "Bea", "Mills");
        var provisioner = Provisioner();
        provisioner.ResetPasswordAsync(staff.UserId, GoodPassword, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await Handlers(harness, provisioner)
            .Handle(new ResetStaffPasswordCommand(staff.Id, GoodPassword), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await provisioner.Received(1).ResetPasswordAsync(staff.UserId, GoodPassword, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_role_picker_lists_what_the_deployment_actually_has()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var provisioner = Provisioner();
        provisioner.RolesAsync(Arg.Any<CancellationToken>()).Returns(new List<RoleInfo>
        {
            new("Trainee", 0, "Learning the till"),
            new("Cashier", 1, null),
        });

        var roles = await Handlers(harness, provisioner)
            .Handle(new ListAssignableRolesQuery(), CancellationToken.None);

        roles.Should().HaveCount(2);
        roles[0].Name.Should().Be("Trainee");
        roles[0].LegacyLevel.Should().Be(0);
        roles[0].Description.Should().Be("Learning the till");
    }

    private static CreateStaffCommand Command() => new(
        Email: "sam@shop.test",
        FirstName: "Sam",
        LastName: "Kerr",
        StaffCode: "SK",
        Password: GoodPassword,
        Role: "Cashier",
        AccessLevel: 1,
        LocationId: 1);

    /// <summary>A provisioner that accepts everything, so each test only sets up its own refusal.</summary>
    private static IUserProvisioner Provisioner()
    {
        var provisioner = Substitute.For<IUserProvisioner>();

        provisioner.RoleExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        provisioner.EmailTakenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        provisioner
            .CreateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(42L));

        return provisioner;
    }

    private static StaffProvisioningHandlers Handlers(MastersTestHarness harness, IUserProvisioner provisioner)
        => new(harness.Db, provisioner, Substitute.For<IPinHasher>());
}
