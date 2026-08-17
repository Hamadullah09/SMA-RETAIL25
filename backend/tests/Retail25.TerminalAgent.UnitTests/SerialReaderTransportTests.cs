using FluentAssertions;
using Retail25.TerminalAgent.Rfid;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests;

/// <summary>
/// Telling a lead from an address.
/// <para>
/// A UHF reader plugged into the till by USB appears as a COM port and speaks the same frames as
/// one on the network. Which wire to open is decided from the configured address, so this is the
/// question the whole feature turns on: a reader that is called a hostname when it is a port fails
/// with a DNS error nobody can act on.
/// </para>
/// </summary>
public sealed class SerialReaderTransportTests
{
    [Theory]
    [InlineData("COM1")]
    [InlineData("COM3")]
    [InlineData("COM12")]
    [InlineData("com7")]          // however the operator typed it
    [InlineData("  COM4  ")]      // pasted out of Device Manager
    [InlineData("/dev/ttyUSB0")]
    [InlineData("/dev/ttyS1")]
    public void A_port_is_recognised_as_a_lead(string address)
        => SerialReaderTransport.IsSerialPort(address).Should().BeTrue();

    [Theory]
    [InlineData("192.168.0.178")]
    [InlineData("reader.shop.local")]
    [InlineData("localhost")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("COM")]           // no number: not a port
    [InlineData("COMPUTER")]      // the trap — starts with COM and is a hostname
    [InlineData("COM1A")]         // trailing rubbish
    public void An_address_is_not_mistaken_for_one(string? address)
        => SerialReaderTransport.IsSerialPort(address).Should().BeFalse();

    /// <summary>
    /// Enumerating ports must never throw. It runs inside the reconnect loop, on machines that may
    /// have no serial subsystem at all, and an exception there would stop the agent looking for a
    /// reader on the network too.
    /// </summary>
    [Fact]
    public void Listing_the_ports_is_safe_on_any_machine()
    {
        var act = () => SerialReaderTransport.AvailablePorts();

        act.Should().NotThrow();
        SerialReaderTransport.AvailablePorts().Should().NotBeNull();
    }

    /// <summary>
    /// Highest first, because on a till the low COM numbers are motherboard headers and Bluetooth
    /// pairings and the reader somebody just plugged in is the newest one.
    /// </summary>
    [Fact]
    public void The_ports_come_back_newest_looking_first()
    {
        var ports = SerialReaderTransport.AvailablePorts();

        ports.Should().BeInDescendingOrder(StringComparer.OrdinalIgnoreCase);
        ports.Should().OnlyHaveUniqueItems("the same port offered twice would waste an attempt");
    }
}
