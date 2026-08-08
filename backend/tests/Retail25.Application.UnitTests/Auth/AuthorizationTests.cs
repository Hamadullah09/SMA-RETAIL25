using FluentAssertions;
using MediatR;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Behaviors;
using Retail25.Application.Common;
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
    private sealed record StepUpCommand : IRequest<Result>;

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
    /// supervisor" instead of "you cannot do this", which is a materially different instruction.
    /// </summary>
    [Fact]
    public async Task A_step_up_able_command_says_so_when_it_refuses()
    {
        var behavior = BuildFor<StepUpCommand>([]);

        var act = async () => await behavior.Handle(new StepUpCommand(), Next(() => { }), default);

        var exception = await act.Should().ThrowAsync<PermissionDeniedException>();
        exception.Which.SupportsSupervisorApproval.Should().BeTrue();
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
        => new(CurrentUser(granted));

    private static AuthorizationBehavior<TRequest, Result> BuildFor<TRequest>(IReadOnlyList<string> granted)
        where TRequest : notnull
        => new(CurrentUser(granted));

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

    [Fact]
    public void An_approved_grant_can_be_spent_once()
    {
        var approval = Request();

        approval.Approve(Supervisor, Now).IsSuccess.Should().BeTrue();
        approval.Consume(nameof(VoidAction), Now).IsSuccess.Should().BeTrue();

        // The second attempt fails, so one approval cannot void a second sale.
        approval.Consume(nameof(VoidAction), Now).IsFailure.Should().BeTrue();
        approval.Status.Should().Be(ApprovalStatus.Consumed);
    }

    /// <summary>The point of the override is a second pair of eyes; approving your own is one pair.</summary>
    [Fact]
    public void A_supervisor_cannot_approve_their_own_request()
    {
        var approval = SupervisorApproval.Request(
            PermissionKeys.Pos.VoidSale, nameof(VoidAction), null, Supervisor, TestIds.Next(), TestIds.Next(), Now);

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

        var result = approval.Consume(nameof(VoidAction), Now.Add(SupervisorApproval.Lifetime).AddSeconds(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("approval.expired");
    }

    /// <summary>Scoped to one action: an approval for a discount must not unlock a void.</summary>
    [Fact]
    public void A_grant_for_one_action_does_not_unlock_another()
    {
        var approval = Request();
        approval.Approve(Supervisor, Now);

        var result = approval.Consume("SomeOtherCommand", Now);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("approval.wrong_action");
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
        => Request().Consume(nameof(VoidAction), Now).IsFailure.Should().BeTrue();

    [Fact]
    public void A_request_expires_two_minutes_after_it_is_raised()
    {
        var approval = Request();

        approval.ExpiresAt.Should().Be(Now.AddMinutes(2));
        approval.Status.Should().Be(ApprovalStatus.Pending);
    }

    private static SupervisorApproval Request() => SupervisorApproval.Request(
        PermissionKeys.Pos.VoidSale,
        nameof(VoidAction),
        "Void sale #1042",
        Cashier,
        TestIds.Next(),
        TestIds.Next(),
        Now);

    private sealed record VoidAction;
}
