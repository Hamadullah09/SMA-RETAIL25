using FluentAssertions;
using MediatR;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Auth;
using Retail25.Application.Behaviors;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Common;
using Retail25.Application.UnitTests.Carts;
using Retail25.Domain.Common;
using Retail25.Domain.Security;
using Xunit;

namespace Retail25.Application.UnitTests.Auth;

/// <summary>
/// The authorisation behaviour (doc 07 §Authorization model).
/// <para>
/// It runs on the MediatR request, not on the endpoint, so the same rule applies whether a command
/// arrived over HTTP, over a hub or from a background job. A check written into a controller only
/// protects the controller.
/// </para>
/// </summary>
public sealed class AuthorizationBehaviorTests
{
    [RequiresPermission(PermissionKeys.Pos.VoidSale)]
    private sealed record GuardedCommand : IRequest<Result>;

    [RequiresPermission(PermissionKeys.Pos.VoidSale)]
    [SupportsSupervisorApproval]
    private sealed record StepUpCommand(long? ApprovalId = null) : IRequest<Result>, ISupervisorApprovable;

    private sealed record OpenCommand : IRequest<Result>;

    [Fact]
    public async Task A_request_with_no_declared_permission_passes_straight_through()
    {
        var behavior = Build(granted: []);
        var ran = false;

        await behavior.Handle(new OpenCommand(), Next(() => ran = true), default);

        ran.Should().BeTrue();
    }

    [Fact]
    public async Task A_holder_of_the_permission_is_allowed_through()
    {
        var behavior = BuildFor<GuardedCommand>([PermissionKeys.Pos.VoidSale]);
        var ran = false;

        await behavior.Handle(new GuardedCommand(), Next(() => ran = true), default);

        ran.Should().BeTrue();
    }

    /// <summary>The handler must never run — refusal happens before the request is even inspected.</summary>
    [Fact]
    public async Task Someone_without_the_permission_is_refused_before_the_handler_runs()
    {
        var behavior = BuildFor<GuardedCommand>([PermissionKeys.Pos.Sell]);
        var ran = false;

        var act = async () => await behavior.Handle(new GuardedCommand(), Next(() => ran = true), default);

        var exception = await act.Should().ThrowAsync<PermissionDeniedException>();
        exception.Which.Permission.Should().Be(PermissionKeys.Pos.VoidSale);
        ran.Should().BeFalse();
    }

    /// <summary>
    /// A step-up-able refusal is marked so the API can answer 428 rather than 403 — "fetch a
    /// supervisor" instead of "you cannot do this", which is a materially different instruction. It
    /// also names the command, so the till raises the approval for exactly this without keeping its
    /// own copy of every command's name.
    /// </summary>
    [Fact]
    public async Task A_step_up_able_command_says_so_and_names_itself_when_it_refuses()
    {
        var behavior = BuildFor<StepUpCommand>([]);

        var act = async () => await behavior.Handle(new StepUpCommand(), Next(() => { }), default);

        var exception = await act.Should().ThrowAsync<PermissionDeniedException>();
        exception.Which.SupportsSupervisorApproval.Should().BeTrue();
        exception.Which.Action.Should().Be(nameof(StepUpCommand));
    }

    /// <summary>
    /// The retry carrying a supervisor's grant reaches the handler.
    /// <para>
    /// It never used to. This behaviour refused anyone without the permission, on the first attempt
    /// and on the retry alike, and the grant was left for the handler to spend — the handler that the
    /// refusal had just stopped from running. A cashier could be approved and still never void.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_retry_carrying_a_grant_runs_and_records_who_approved_it()
    {
        var supervisor = TestIds.Next();
        var grants = Substitute.For<IStepUpGrants>();
        grants.SpendAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(supervisor));

        var stepUp = new StepUpContext();
        var behavior = BuildFor<StepUpCommand>([PermissionKeys.Pos.Sell], grants, stepUp);
        var ran = false;

        await behavior.Handle(new StepUpCommand(ApprovalId: 42), Next(() => ran = true), default);

        ran.Should().BeTrue();
        stepUp.ApprovedByStaffId.Should().Be(supervisor);
    }

    /// <summary>
    /// Spent against what was actually refused — this command, and the permission it needed — never
    /// against whatever the till claimed when it raised the request.
    /// </summary>
    [Fact]
    public async Task A_grant_is_spent_against_the_command_and_permission_being_refused()
    {
        var grants = Substitute.For<IStepUpGrants>();
        grants.SpendAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(TestIds.Next()));

        var behavior = BuildFor<StepUpCommand>([], grants);

        await behavior.Handle(new StepUpCommand(ApprovalId: 7), Next(() => { }), default);

        await grants.Received(1).SpendAsync(7, nameof(StepUpCommand), PermissionKeys.Pos.VoidSale, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A grant that cannot be used is refused in its own words, and still as a step-up: an expired
    /// approval needs a fresh one, not a different person at the till.
    /// </summary>
    [Fact]
    public async Task A_grant_that_cannot_be_spent_is_refused_with_its_reason()
    {
        var grants = Substitute.For<IStepUpGrants>();
        grants.SpendAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<long>(SupervisorApproval.Expired));

        var behavior = BuildFor<StepUpCommand>([], grants);
        var ran = false;

        var act = async () => await behavior.Handle(new StepUpCommand(ApprovalId: 9), Next(() => ran = true), default);

        var exception = await act.Should().ThrowAsync<PermissionDeniedException>();
        exception.Which.Reason!.Code.Should().Be("approval.expired");
        exception.Which.SupportsSupervisorApproval.Should().BeTrue();
        ran.Should().BeFalse();
    }

    /// <summary>Somebody who holds the permission spends nobody's approval, even if one is attached.</summary>
    [Fact]
    public async Task A_holder_of_the_permission_never_spends_a_grant()
    {
        var grants = Substitute.For<IStepUpGrants>();
        var behavior = BuildFor<StepUpCommand>([PermissionKeys.Pos.VoidSale], grants);

        await behavior.Handle(new StepUpCommand(ApprovalId: 3), Next(() => { }), default);

        await grants.DidNotReceiveWithAnyArgs().SpendAsync(default, default!, default!, default);
    }

    /// <summary>A grant on a command that does not support step-up is ignored, not honoured.</summary>
    [Fact]
    public async Task A_command_that_does_not_support_step_up_cannot_be_unlocked_by_one()
    {
        var grants = Substitute.For<IStepUpGrants>();
        var behavior = BuildFor<GuardedCommand>([], grants);

        var act = async () => await behavior.Handle(new GuardedCommand(), Next(() => { }), default);

        await act.Should().ThrowAsync<PermissionDeniedException>();
        await grants.DidNotReceiveWithAnyArgs().SpendAsync(default, default!, default!, default);
    }

    /// <summary>
    /// Clearing a sale is a manager's call: a clerk is answered "fetch a manager", not "no".
    /// </summary>
    [Fact]
    public async Task Clearing_a_sale_asks_a_clerk_for_a_manager()
    {
        var clerk = PermissionKeys.LegacyLevelPresets[2];
        var behavior = BuildFor<ClearCartCommand>(clerk);

        var act = async () => await behavior.Handle(new ClearCartCommand(CartId: 1), Next(() => { }), default);

        var exception = await act.Should().ThrowAsync<PermissionDeniedException>();
        exception.Which.Permission.Should().Be(PermissionKeys.Pos.ClearSale);
        exception.Which.SupportsSupervisorApproval.Should().BeTrue();
        exception.Which.Action.Should().Be(nameof(ClearCartCommand));
    }

    [Fact]
    public void Supervisors_and_administrators_clear_sales_and_nobody_below_does()
    {
        PermissionKeys.LegacyLevelPresets[3].Should().Contain(PermissionKeys.Pos.ClearSale);
        PermissionKeys.LegacyLevelPresets[4].Should().Contain(PermissionKeys.Pos.ClearSale);

        foreach (var level in new[] { 0, 1, 2 })
        {
            PermissionKeys.LegacyLevelPresets[level].Should().NotContain(PermissionKeys.Pos.ClearSale);
        }
    }

    /// <summary>
    /// Every permission a request declares must exist in the catalogue, or the grant that would
    /// satisfy it can never be seeded and the command is permanently unreachable.
    /// </summary>
    [Fact]
    public void The_legacy_level_presets_only_grant_permissions_that_exist()
    {
        foreach (var (level, permissions) in PermissionKeys.LegacyLevelPresets)
        {
            permissions.Should().BeSubsetOf(PermissionKeys.All, $"level {level} must not grant unknown permissions");
        }
    }

    /// <summary>
    /// Level 0 is the legacy training mode (guide p.82): reachable, but nothing is committed. It must
    /// never hold a permission that moves money or stock.
    /// </summary>
    [Fact]
    public void The_trainee_preset_cannot_move_money_or_stock()
    {
        var trainee = PermissionKeys.LegacyLevelPresets[0];

        trainee.Should().NotContain(PermissionKeys.Pos.VoidSale);
        trainee.Should().NotContain(PermissionKeys.Pos.Discount);
        trainee.Should().NotContain(PermissionKeys.Inventory.Adjust);
        trainee.Should().NotContain(PermissionKeys.Drawer.Close);
    }

    /// <summary>Each level is a superset of the one below, which is what "level" means to a manager.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    public void Each_legacy_level_includes_everything_the_one_below_it_can_do(int lower, int higher)
    {
        var below = PermissionKeys.LegacyLevelPresets[lower];
        var above = PermissionKeys.LegacyLevelPresets[higher];

        above.Should().Contain(below);
    }

    [Fact]
    public void The_administrator_preset_holds_everything()
        => PermissionKeys.LegacyLevelPresets[4].Should().BeEquivalentTo(PermissionKeys.All);

    private static AuthorizationBehavior<OpenCommand, Result> Build(IReadOnlyList<string> granted)
        => new(CurrentUser(granted), Substitute.For<IStepUpGrants>(), new StepUpContext());

    private static AuthorizationBehavior<TRequest, TResponse> BuildFor<TRequest, TResponse>(
        IReadOnlyList<string> granted,
        IStepUpGrants? grants = null,
        StepUpContext? stepUp = null)
        where TRequest : notnull
        => new(CurrentUser(granted), grants ?? Substitute.For<IStepUpGrants>(), stepUp ?? new StepUpContext());

    private static AuthorizationBehavior<TRequest, Result> BuildFor<TRequest>(
        IReadOnlyList<string> granted,
        IStepUpGrants? grants = null,
        StepUpContext? stepUp = null)
        where TRequest : notnull
        => BuildFor<TRequest, Result>(granted, grants, stepUp);

    private static ICurrentUser CurrentUser(IReadOnlyList<string> granted)
    {
        var user = Substitute.For<ICurrentUser>();
        var set = new HashSet<string>(granted, StringComparer.Ordinal);

        user.Permissions.Returns(set);
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(TestIds.Next());

        // The default interface method delegates to Permissions, but a substitute intercepts it, so
        // the behaviour has to be stated explicitly here.
        user.HasPermission(Arg.Any<string>()).Returns(call => set.Contains(call.Arg<string>()));

        return user;
    }

    private static RequestHandlerDelegate<Result> Next(Action onRun)
        => () =>
        {
            onRun();
            return Task.FromResult(Result.Success());
        };
}

/// <summary>
/// The supervisor override (doc 07 §Step-up). Single-use, short-lived and scoped to one action —
/// each of which is load-bearing.
/// </summary>
public sealed class SupervisorApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    private static readonly long Cashier = TestIds.Next();
    private static readonly long Supervisor = TestIds.Next();

    private const string Void = PermissionKeys.Pos.VoidSale;

    [Fact]
    public void An_approved_grant_can_be_spent_once()
    {
        var approval = Request();

        approval.Approve(Supervisor, Now).IsSuccess.Should().BeTrue();
        approval.Consume(nameof(VoidAction), Void, Now).IsSuccess.Should().BeTrue();

        // The second attempt fails, so one approval cannot void a second sale.
        approval.Consume(nameof(VoidAction), Void, Now).IsFailure.Should().BeTrue();
        approval.Status.Should().Be(ApprovalStatus.Consumed);
    }

    /// <summary>The point of the override is a second pair of eyes; approving your own is one pair.</summary>
    [Fact]
    public void A_supervisor_cannot_approve_their_own_request()
    {
        var approval = SupervisorApproval.Request(
            Void, nameof(VoidAction), null, Supervisor, TestIds.Next(), TestIds.Next(), Now);

        var result = approval.Approve(Supervisor, Now);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("approval.self_approval");
    }

    /// <summary>A grant that outlived its window cannot be banked and spent later.</summary>
    [Fact]
    public void An_expired_grant_cannot_be_spent()
    {
        var approval = Request();
        approval.Approve(Supervisor, Now);

        var result = approval.Consume(nameof(VoidAction), Void, Now.Add(SupervisorApproval.Lifetime).AddSeconds(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("approval.expired");
    }

    /// <summary>Scoped to one action: an approval for a discount must not unlock a void.</summary>
    [Fact]
    public void A_grant_for_one_action_does_not_unlock_another()
    {
        var approval = Request();
        approval.Approve(Supervisor, Now);

        var result = approval.Consume("SomeOtherCommand", Void, Now);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("approval.wrong_action");
    }

    /// <summary>
    /// The cheap-grant attack. The till chooses both the permission and the action when it raises a
    /// request, and the approver is only asked whether they hold that permission. Raised as
    /// "pos.sell, for a void", any cashier could approve it — every cashier holds pos.sell — and the
    /// grant would then unlock the void. It must not.
    /// </summary>
    [Fact]
    public void A_grant_approved_for_a_lesser_permission_does_not_unlock_a_greater_one()
    {
        var approval = SupervisorApproval.Request(
            PermissionKeys.Pos.Sell, nameof(VoidAction), "Void sale #1042", Cashier, TestIds.Next(), TestIds.Next(), Now);
        approval.Approve(TestIds.Next(), Now);

        var result = approval.Consume(nameof(VoidAction), Void, Now);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("approval.wrong_permission");
    }

    [Fact]
    public void A_denied_request_cannot_then_be_approved()
    {
        var approval = Request();
        approval.Deny(Supervisor, "Not authorised", Now);

        approval.Approve(Supervisor, Now).IsFailure.Should().BeTrue();
        approval.Status.Should().Be(ApprovalStatus.Denied);
    }

    [Fact]
    public void An_unapproved_request_cannot_be_spent()
        => Request().Consume(nameof(VoidAction), Void, Now).IsFailure.Should().BeTrue();

    [Fact]
    public void A_request_expires_two_minutes_after_it_is_raised()
    {
        var approval = Request();

        approval.ExpiresAt.Should().Be(Now.AddMinutes(2));
        approval.Status.Should().Be(ApprovalStatus.Pending);
    }

    private static SupervisorApproval Request() => SupervisorApproval.Request(
        Void,
        nameof(VoidAction),
        "Void sale #1042",
        Cashier,
        TestIds.Next(),
        TestIds.Next(),
        Now);

    private sealed record VoidAction;
}

/// <summary>Spending a grant against the database: who may, and what is left behind.</summary>
public sealed class StepUpGrantTests
{
    [Fact]
    public async Task The_cashier_who_asked_can_spend_it_and_learns_who_approved()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var supervisor = TestIds.Next();
        var approval = await ApprovedAsync(harness, requestedBy: harness.CurrentUser.StaffId!.Value, supervisor);

        var spent = await Grants(harness).SpendAsync(approval.Id, "ClearCartCommand", PermissionKeys.Pos.ClearSale, default);

        spent.IsSuccess.Should().BeTrue();
        spent.Value.Should().Be(supervisor);
        (await harness.Db.SupervisorApprovals.FindAsync(approval.Id))!.Status.Should().Be(ApprovalStatus.Consumed);
    }

    /// <summary>
    /// A grant belongs to whoever asked for it. Otherwise an approved request is a token any colleague
    /// at any till can pick up and spend in its two minutes.
    /// </summary>
    [Fact]
    public async Task Somebody_else_cannot_spend_a_grant_they_did_not_ask_for()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var approval = await ApprovedAsync(harness, requestedBy: TestIds.Next(), supervisor: TestIds.Next());

        var spent = await Grants(harness).SpendAsync(approval.Id, "ClearCartCommand", PermissionKeys.Pos.ClearSale, default);

        spent.IsFailure.Should().BeTrue();
        spent.Error.Code.Should().Be("approval.not_yours");
        (await harness.Db.SupervisorApprovals.FindAsync(approval.Id))!.Status.Should().Be(ApprovalStatus.Approved, "an attempt by the wrong person must not use it up");
    }

    private static StepUpGrants Grants(PosTestHarness harness)
        => new(harness.Db, harness.CurrentUser, Substitute.For<IAuditWriter>(), harness.Clock);

    private static async Task<SupervisorApproval> ApprovedAsync(PosTestHarness harness, long requestedBy, long supervisor)
    {
        var approval = SupervisorApproval.Request(
            PermissionKeys.Pos.ClearSale, "ClearCartCommand", "Clear sale", requestedBy, harness.Station.Id, harness.Location.Id, harness.Clock.Now);

        approval.Approve(supervisor, harness.Clock.Now).IsSuccess.Should().BeTrue();

        harness.Db.SupervisorApprovals.Add(approval);
        await harness.Db.SaveChangesAsync();
        return approval;
    }
}
