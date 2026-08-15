using FluentAssertions;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Staff;
using Retail25.Application.UnitTests.Carts;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Common;
using Xunit;

namespace Retail25.Application.UnitTests.Staff;

/// <summary>
/// Taking somebody's access away.
/// <para>
/// Deactivation rather than deletion, because a staff row is what a sale is attributed to, what a
/// commission is owed against and what an audit entry points at. Removing one would either break
/// those references or quietly rewrite who did what — the single thing an audit trail exists to
/// prevent.
/// </para>
/// <para>
/// The two guards below are the reason this is not a one-line handler. Both failure modes end with
/// nobody able to sign in, and both are a single click away from an administrator doing something
/// entirely reasonable.
/// </para>
/// </summary>
public sealed class DeactivateStaffTests
{
    [Fact]
    public async Task Deactivating_stops_the_sign_in_and_hides_the_person()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("BM", "Bea", "Mills");
        var provisioner = Provisioner();

        var result = await Handlers(harness, provisioner).Handle(
            new DeactivateStaffCommand(staff.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        harness.Db.StaffProfiles.Single(s => s.Id == staff.Id).IsActive.Should().BeFalse();
        await provisioner.Received(1).SetEnabledAsync(staff.UserId, false, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The record survives. This is the whole point of deactivating rather than deleting: every sale
    /// they rang still says who rang it.
    /// </summary>
    [Fact]
    public async Task The_staff_record_itself_is_kept()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("BM", "Bea", "Mills");

        await Handlers(harness, Provisioner()).Handle(
            new DeactivateStaffCommand(staff.Id), CancellationToken.None);

        harness.Db.StaffProfiles.Should().ContainSingle(s => s.Id == staff.Id);
        harness.Db.StaffProfiles.Single(s => s.Id == staff.Id).FullName.Should().Be("Bea Mills");
    }

    /// <summary>
    /// The likeliest mistake, and the one with no way back: an administrator removing their own
    /// access and then having no account able to restore it.
    /// </summary>
    [Fact]
    public async Task You_cannot_remove_your_own_access()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("ME", "Self", "Same");
        var me = new TestCurrentUser { StaffId = staff.Id };

        var result = await Handlers(harness, Provisioner(), me).Handle(
            new DeactivateStaffCommand(staff.Id), CancellationToken.None);

        result.Error.Should().Be(StaffProvisioningHandlers.CannotDeactivateSelf);
        harness.Db.StaffProfiles.Single(s => s.Id == staff.Id).IsActive.Should().BeTrue();
    }

    /// <summary>
    /// The same failure one step further out. Deactivating the last administrator leaves a shop with
    /// nobody who can create one, and no route back that does not involve editing the database.
    /// </summary>
    [Fact]
    public async Task The_last_administrator_cannot_be_removed()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("AD", "Only", "Admin");

        var provisioner = Provisioner();
        provisioner.IsInRoleAsync(staff.UserId, "Administrator", Arg.Any<CancellationToken>()).Returns(true);
        provisioner.CountEnabledInRoleAsync("Administrator", Arg.Any<CancellationToken>()).Returns(1);

        var result = await Handlers(harness, provisioner).Handle(
            new DeactivateStaffCommand(staff.Id), CancellationToken.None);

        result.Error.Should().Be(StaffProvisioningHandlers.LastAdministrator);
        harness.Db.StaffProfiles.Single(s => s.Id == staff.Id).IsActive.Should().BeTrue();
    }

    /// <summary>The guard is about the last one, not about administrators generally.</summary>
    [Fact]
    public async Task One_of_several_administrators_can_be_removed()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("AD", "One", "Admin");

        var provisioner = Provisioner();
        provisioner.IsInRoleAsync(staff.UserId, "Administrator", Arg.Any<CancellationToken>()).Returns(true);
        provisioner.CountEnabledInRoleAsync("Administrator", Arg.Any<CancellationToken>()).Returns(3);

        var result = await Handlers(harness, provisioner).Handle(
            new DeactivateStaffCommand(staff.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Deactivating_somebody_who_does_not_exist_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness, Provisioner()).Handle(
            new DeactivateStaffCommand(9999), CancellationToken.None);

        result.Error.Should().Be(StaffProvisioningHandlers.StaffNotFound);
    }

    /// <summary>
    /// A shop that walks somebody out on Friday and rehires them in March should not need a database
    /// restore, so the operation is reversible.
    /// </summary>
    [Fact]
    public async Task Reactivating_puts_the_access_back()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("BM", "Bea", "Mills");
        var provisioner = Provisioner();
        var handlers = Handlers(harness, provisioner);

        await handlers.Handle(new DeactivateStaffCommand(staff.Id), CancellationToken.None);
        var result = await handlers.Handle(new ReactivateStaffCommand(staff.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        harness.Db.StaffProfiles.Single(s => s.Id == staff.Id).IsActive.Should().BeTrue();
        await provisioner.Received(1).SetEnabledAsync(staff.UserId, true, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// If Identity refuses to disable the sign-in, the profile must not be marked inactive either —
    /// that pairing is what would leave somebody hidden from the list and still able to log in.
    /// </summary>
    [Fact]
    public async Task A_failure_to_disable_the_sign_in_leaves_the_profile_alone()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("BM", "Bea", "Mills");

        var provisioner = Provisioner();
        provisioner.SetEnabledAsync(staff.UserId, false, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new Error("identity.locked", "Could not disable.")));

        var result = await Handlers(harness, provisioner).Handle(
            new DeactivateStaffCommand(staff.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        harness.Db.StaffProfiles.Single(s => s.Id == staff.Id).IsActive
            .Should().BeTrue("a half-applied deactivation is worse than none");
    }

    private static IUserProvisioner Provisioner()
    {
        var provisioner = Substitute.For<IUserProvisioner>();

        provisioner.SetEnabledAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        provisioner.IsInRoleAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        provisioner.CountEnabledInRoleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(5);

        return provisioner;
    }

    private static StaffProvisioningHandlers Handlers(
        MastersTestHarness harness,
        IUserProvisioner provisioner,
        ICurrentUser? currentUser = null)
        => new(harness.Db, provisioner, Substitute.For<IPinHasher>(), currentUser ?? new TestCurrentUser());
}
