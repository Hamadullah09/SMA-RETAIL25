using System.Reflection;
using FluentAssertions;
using Retail25.Application.Common;
using Retail25.Application.Terminals;
using Xunit;

namespace Retail25.Application.UnitTests.Terminals;

/// <summary>
/// The permissions a terminal agent needs to do its job, pinned against the narrow set it is granted.
///
/// <para>
/// A mismatch here does not fail loudly. The agent carries on, the server answers 403, and the only
/// evidence is a warning in a log on a till nobody reads — which is exactly how a live shop swept its
/// network correctly every fifteen minutes for a day and registered nothing.
/// </para>
/// </summary>
public sealed class AgentPermissionTests
{
    /// <summary>
    /// Mirrors <c>CurrentUser.TerminalAgentPermissions</c>. Duplicated rather than referenced because
    /// that set lives in Infrastructure behind an identity type this project has no business
    /// constructing — and because a copy that must be updated deliberately is the point: widening the
    /// agent's grant should require changing this line and reading why.
    /// </summary>
    private static readonly string[] GrantedToAgent =
    [
        PermissionKeys.Terminals.Read,
        PermissionKeys.Terminals.Operate,
        PermissionKeys.Pos.Sell,
    ];

    private static string RequiredBy<T>()
        => typeof(T).GetCustomAttribute<RequiresPermissionAttribute>()?.Permission
           ?? throw new InvalidOperationException($"{typeof(T).Name} declares no permission.");

    [Fact]
    public void An_agent_can_report_the_readers_it_found()
    {
        RequiredBy<RecordReaderDiscoveryCommand>().Should().BeOneOf(
            GrantedToAgent,
            "only a till can see the shop's LAN, so if the agent cannot report a sweep nobody can");
    }

    [Fact]
    public void An_agent_can_check_its_machine_in()
    {
        RequiredBy<ReportDeviceStatusCommand>().Should().BeOneOf(GrantedToAgent);
    }

    /// <summary>
    /// The other half of the argument: reporting a sweep must not require the permission that guards
    /// editing reader profiles, because granting that to every till would let each one rewrite the
    /// others' hardware settings.
    /// </summary>
    [Fact]
    public void Reporting_a_sweep_does_not_need_the_permission_that_edits_reader_profiles()
    {
        RequiredBy<RecordReaderDiscoveryCommand>().Should().NotBe(PermissionKeys.Terminals.Register);
        GrantedToAgent.Should().NotContain(PermissionKeys.Terminals.Register);
    }
}
