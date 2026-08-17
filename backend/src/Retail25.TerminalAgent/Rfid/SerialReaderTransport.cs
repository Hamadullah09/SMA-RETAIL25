using System.Globalization;
using System.IO.Ports;
using Retail25.Devices.Rfid;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// Reaching a reader that is plugged into this machine.
/// <para>
/// A UHF reader on a USB lead presents as a virtual COM port and speaks exactly the frames the
/// network driver already speaks. Before this, such a reader could not be used at all: the driver
/// opened a socket or nothing, so a shop that plugged a reader into the till saw "RFID offline"
/// with no way to act on it.
/// </para>
/// <para>
/// Only the agent has this. A serial port belongs to one machine, so the API — which on a hosted
/// deployment is in another country — could never open the till's lead however it was configured.
/// </para>
/// </summary>
public static class SerialReaderTransport
{
    /// <summary>
    /// Whether an address names a serial port rather than a host.
    /// <para>
    /// <c>COM3</c> on Windows, <c>/dev/ttyUSB0</c> or <c>/dev/ttyS1</c> elsewhere. Decided by shape
    /// rather than by asking the operating system, so the answer is the same when the reader is
    /// unplugged — otherwise a configured port would quietly become a hostname the moment somebody
    /// pulled the lead, and the error would talk about DNS.
    /// </para>
    /// </summary>
    public static bool IsSerialPort(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var trimmed = address.Trim();

        if (trimmed.StartsWith("/dev/tty", StringComparison.Ordinal))
        {
            return true;
        }

        return trimmed.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            && trimmed.Length > 3
            && int.TryParse(trimmed.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>Every serial port this machine currently has, newest-looking first.</summary>
    /// <remarks>
    /// Reversed because a USB reader is almost always the highest-numbered port on a machine whose
    /// low COM numbers are motherboard headers and Bluetooth pairings. It is a search order, not an
    /// assumption: every port is still tried.
    /// </remarks>
    public static IReadOnlyList<string> AvailablePorts()
    {
        try
        {
            return SerialPort.GetPortNames()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or PlatformNotSupportedException)
        {
            // A machine with no serial subsystem at all. Not an error — it simply has no reader on a
            // lead, and the network search is the one that matters there.
            return [];
        }
    }

    /// <summary>Opens either kind of wire, choosing by what the address looks like.</summary>
    public static async Task<ReaderConnection> OpenAsync(string address, int port, int baudRate, CancellationToken ct)
    {
        if (!IsSerialPort(address))
        {
            return await NetworkReaderTransport.OpenAsync(address, port, baudRate, ct);
        }

        var name = address.Trim();

        // Opening a COM port is synchronous and can block on a wedged driver; pushed off the
        // caller's thread so one bad lead cannot stall the agent's reconnect loop.
        var serial = await Task.Run(
            () =>
            {
                var opened = new SerialPort(name, baudRate, Parity.None, 8, StopBits.One)
                {
                    // The pump reads continuously and the codec frames the bytes itself, so a read
                    // that returns nothing is normal rather than something to time out on.
                    ReadTimeout = SerialPort.InfiniteTimeout,
                    WriteTimeout = 5_000,

                    // Some USB bridges hold the reader in reset until these are asserted, and until
                    // they are the port opens cleanly and then says nothing at all.
                    DtrEnable = true,
                    RtsEnable = true,
                };

                opened.Open();
                return opened;
            },
            ct);

        return new ReaderConnection(
            serial.BaseStream,
            serial,
            $"{name} @ {baudRate.ToString(CultureInfo.InvariantCulture)}",
            () => serial.IsOpen);
    }
}
