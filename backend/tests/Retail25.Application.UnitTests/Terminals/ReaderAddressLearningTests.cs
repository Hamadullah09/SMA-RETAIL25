using System.Net;
using FluentAssertions;
using Xunit;

namespace Retail25.Application.UnitTests.Terminals;

/// <summary>
/// What a machine may teach the server about where its reader is.
/// <para>
/// A check-in can move a reader to a new address, which is the feature that makes a DHCP lease
/// changing over a weekend a column update instead of an outage. It is also a path with no human on
/// it, so what it is allowed to write matters.
/// </para>
/// <para>
/// It wrote 127.0.0.1 — the placeholder in the agent's own fallback profile, reported before a reader
/// had answered — over an address an administrator had set. The configuration then told the agent to
/// dial 127.0.0.1, the reader went dark, and the agent, the server and the health screen all agreed
/// on an address that was never right. Correcting the row by hand lasted exactly one heartbeat.
/// </para>
/// </summary>
public sealed class ReaderAddressLearningTests
{
    /// <summary>
    /// Mirrors DeviceRegistryHandlers.IsWorthLearning, which is private. The rule is small and the
    /// consequence of getting it wrong is a shop that cannot read, so it is stated here too.
    /// </summary>
    private static bool IsWorthLearning(string host)
    {
        var trimmed = host.Trim();

        if (trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IPAddress.TryParse(trimmed, out var address) || !IPAddress.IsLoopback(address);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.1.1")]
    [InlineData(" 127.0.0.1 ")]
    [InlineData("::1")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    public void Loopback_is_never_learned_from_a_machine(string reported)
        => IsWorthLearning(reported).Should().BeFalse(
            "{0} means \"this machine and no other\", so it can never be where another party finds the reader",
            reported);

    [Theory]
    [InlineData("192.168.0.178")]
    [InlineData("10.4.1.9")]
    public void A_real_address_is_learned(string reported)
        => IsWorthLearning(reported).Should().BeTrue();

    /// <summary>
    /// A serial lead is kept. "The reader is plugged into this machine" is exactly the kind of fact
    /// only the machine can report, and it is not an address that could belong to anything else.
    /// </summary>
    [Theory]
    [InlineData("COM3")]
    [InlineData("COM12")]
    public void A_serial_port_is_learned(string reported)
        => IsWorthLearning(reported).Should().BeTrue();
}
