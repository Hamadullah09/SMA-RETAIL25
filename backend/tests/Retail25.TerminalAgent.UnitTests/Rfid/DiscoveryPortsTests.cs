using FluentAssertions;
using Retail25.TerminalAgent.Rfid;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests.Rfid;

/// <summary>
/// Which ports a sweep knocks on, and in what order.
///
/// <para>
/// This is the difference between discovery working on the installation it exists for and finding
/// nothing there. A till that has been told about its readers knows the answer; a till in a shop
/// nobody has configured yet knows only what it shipped with, and that placeholder is not a port any
/// reader in this estate answers on.
/// </para>
/// </summary>
public sealed class DiscoveryPortsTests
{
    private static readonly int[] Configured = [4001, 5084];

    [Fact]
    public void The_ports_the_shops_own_readers_use_are_tried_first()
    {
        var ports = ReaderDiscoveryService.PortsToSweep([4001], profilePort: 5084, Configured);

        ports.Should().StartWith(4001, "a reader answering on 4001 is evidence, and the rest are guesses");
    }

    [Fact]
    public void A_till_that_has_been_told_nothing_still_has_somewhere_to_look()
    {
        // The shipped fallback profile, before any server has answered.
        var ports = ReaderDiscoveryService.PortsToSweep([], profilePort: 5084, Configured);

        ports.Should().Contain(4001, "otherwise the first sweep in a new shop searches only the placeholder");
    }

    [Fact]
    public void No_port_is_swept_twice()
    {
        var ports = ReaderDiscoveryService.PortsToSweep([4001, 4001, 5084], profilePort: 4001, Configured);

        ports.Should().Equal(4001, 5084);
    }

    [Fact]
    public void A_port_nobody_has_set_is_not_swept()
    {
        // Zero is the "nobody has said" placeholder rather than a port.
        var ports = ReaderDiscoveryService.PortsToSweep([0], profilePort: 0, [0, -1, 70000, 4001]);

        ports.Should().Equal(4001);
    }

    [Fact]
    public void Every_port_the_estate_uses_is_swept_when_readers_differ()
    {
        var ports = ReaderDiscoveryService.PortsToSweep([4001, 4002, 4003], profilePort: 4001, Configured);

        ports.Should().Equal(4001, 4002, 4003, 5084);
    }
}
