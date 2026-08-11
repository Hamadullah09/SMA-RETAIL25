using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Retail25.TerminalAgent.Rfid;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests.Rfid;

/// <summary>
/// Finding the reader wherever the shop's DHCP put it.
/// <para>
/// These run against a real socket on this machine's own network rather than a mock, because the
/// thing under test is precisely the behaviour a mock would have to assume: which interfaces are
/// searched, and whether a listener on one of them is actually reached. A test that stubbed the
/// probe would pass on a machine where the real thing finds nothing.
/// </para>
/// </summary>
public sealed class ReaderDiscoveryTests
{
    private static ReaderDiscovery Discovery() => new(NullLogger<ReaderDiscovery>.Instance);

    /// <summary>
    /// Binds to every interface and returns the port, so the listener is reachable both on loopback
    /// and on whatever address this machine has on the shop network.
    /// </summary>
    private static TcpListener Listening(out int port)
    {
        var listener = new TcpListener(IPAddress.Any, 0);

        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;

        return listener;
    }

    [Fact]
    public async Task The_configured_address_is_used_when_something_answers_there()
    {
        var listener = Listening(out var port);

        try
        {
            var found = await Discovery().FindAsync("127.0.0.1", port, CancellationToken.None);

            // Returned as given, and — the point of the test — without a sweep having to happen. A
            // shop whose reader is where it says it is must not pay for a search on every reconnect.
            found.Should().Be("127.0.0.1");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task A_listener_on_this_machine_is_found_when_the_configured_address_is_wrong()
    {
        var listener = Listening(out var port);

        try
        {
            // 192.0.2.0/24 is TEST-NET-1 (RFC 5737): reserved for documentation and guaranteed not to
            // be routable, so this stands in for a stale DHCP lease without depending on the address
            // being free on whatever network the test happens to run on.
            var found = await Discovery().FindAsync("192.0.2.1", port, CancellationToken.None);

            var searchable = LocalAddresses()
                .Where(address => address != "127.0.0.1" && !address.StartsWith("169.254", StringComparison.Ordinal))
                .ToList();

            // Asserted both ways rather than skipped, so the test says something on a build agent
            // with only a loopback interface instead of quietly proving nothing there.
            if (searchable.Count == 0)
            {
                found.Should().BeNull("there is no non-loopback network on this machine to find anything on");
                return;
            }

            found.Should().NotBe("192.0.2.1", "the unreachable configured address must not be handed back as if it answered");
            searchable.Should().Contain(found!, "the only listener on this port is the one this test started");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Nothing_is_returned_when_no_reader_is_anywhere()
    {
        // A port nothing is listening on, on an address that cannot be reached. The sweep is bounded
        // to this machine's own /24s, so this completes rather than hanging.
        var found = await Discovery().FindAsync("192.0.2.1", 9, CancellationToken.None);

        // Port 9 is discard: occasionally enabled, so a hit is possible and is not a failure of the
        // logic under test. What must never happen is the unreachable configured address coming back.
        found.Should().NotBe("192.0.2.1");
    }

    private static List<string> LocalAddresses() =>
        System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address.ToString())
            .ToList();
}
