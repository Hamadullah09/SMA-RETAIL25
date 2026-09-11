using System.Net.Sockets;
using Retail25.Contracts.Terminals;

namespace Retail25.Devices.Rfid;

/// <summary>
/// What a candidate turned out to be, once asked.
/// </summary>
/// <param name="Host">Where it answered. Current address, never identity — see <paramref name="SerialNumber"/>.</param>
/// <param name="SerialNumber">
/// The reader's own identifier, as it reports it. Null where the device answered the firmware query
/// but not the identifier one, which some units in this family do.
/// </param>
/// <param name="AntennaCount">
/// Ports the reader actually reports, or the family default where it will not say. See
/// <see cref="ReaderIdentityProbe"/> for why this is a floor rather than a fact.
/// </param>
/// <param name="AntennaCountReported">
/// Whether <paramref name="AntennaCount"/> came from the device or from the fallback. Carried so a
/// screen can distinguish "this reader has four ports" from "we assumed four", instead of presenting
/// a guess as a measurement.
/// </param>
public sealed record ReaderIdentity(
    string Host,
    int Port,
    ReaderProtocol Protocol,
    string? SerialNumber,
    string? FirmwareVersion,
    int AntennaCount,
    bool AntennaCountReported);

/// <summary>
/// Asks an address whether it is a reader.
/// <para>
/// An interface so a discovery sweep can be tested without hardware. The tests that matter here —
/// a non-reader is rejected, two readers are both found, the same serial at a new address is the
/// same reader — are about the sweep's logic, and none of them should need a switch and seven boxes
/// on a desk to run.
/// </para>
/// </summary>
public interface IReaderIdentityProbe
{
    Task<ReaderIdentity?> ProbeAsync(string host, int port, CancellationToken ct);
}

/// <summary>
/// Asks a candidate address whether it is a reader, and if so, which one.
///
/// <para>
/// This is the step that turns "something is listening on 4001" into "reader 0A1B2C with firmware
/// 8.2 and four antenna ports". Discovery without it can only offer an address, and an address is
/// not an identity: two shops on 192.168.0.178 are two different readers, and one reader that moved
/// from .178 to .150 overnight is still the same one. Every assignment an administrator makes hangs
/// off that distinction.
/// </para>
/// <para>
/// A TCP connect is deliberately not treated as proof. A printer, a scale, a camera or a
/// serial-to-Ethernet bridge with nothing behind it all accept a connection on a port somebody chose
/// arbitrarily. The only honest test is to speak the protocol and see whether the reply is a
/// well-formed frame for the command that was sent — which is what
/// <see cref="UhfSerialRfidReader"/> already does on connect, and what this repeats before a session
/// is ever opened.
/// </para>
/// <para>
/// Nothing is left open. The probe connects, asks at most three questions, and closes. A discovery
/// sweep that leaves a socket per candidate would exhaust the reader's single client slot on this
/// hardware family and lock out the session that actually wants to read tags.
/// </para>
/// </summary>
public sealed class ReaderIdentityProbe : IReaderIdentityProbe
{
    /// <summary>
    /// The family maximum, used only when the reader will not report its own port count.
    /// <para>
    /// Not a belief about the hardware — a floor that keeps a discovered reader usable. Four is the
    /// D2184B's actual count and this family's maximum, so assuming it never invents ports that a
    /// four-port unit lacks. A larger portal reader under-reports until an administrator corrects
    /// the count, which is the safe direction to be wrong in: an antenna nobody assigned reads
    /// nothing, whereas an antenna that does not exist is a station that silently never works.
    /// </para>
    /// </summary>
    public const int DefaultAntennaCount = 4;

    /// <summary>
    /// How long one question may take. A reader on the same switch answers in single-digit
    /// milliseconds; this is generous enough for a congested switch and short enough that a sweep of
    /// a /24 does not outlast a cashier's patience.
    /// </summary>
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(1200);

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Returns what is at <paramref name="host"/>, or null if it is not a reader.
    /// <para>
    /// Null covers every way of not being one — refused, unreachable, silent, or answering something
    /// that is not a valid frame. The caller does not need to tell those apart: none of them is a
    /// reader, and a discovery sweep that reported the difference would be reporting the state of a
    /// shop's network rather than its hardware.
    /// </para>
    /// </summary>
    public async Task<ReaderIdentity?> ProbeAsync(string host, int port, CancellationToken ct)
    {
        using var client = new TcpClient { NoDelay = true };

        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connect.CancelAfter(ConnectTimeout);

            await client.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }

        try
        {
            await using var stream = client.GetStream();
            var reassembler = new UhfSerialCodec.FrameReassembler();

            // The firmware query is the proof. A reader answers it with a two-byte version; nothing
            // else on a shop network answers this opcode with a checksum-valid frame carrying it
            // back. Without an answer here, whatever is listening is not this.
            var firmware = await AskAsync(stream, reassembler, UhfSerialCommand.GetFirmwareVersion, ct)
                .ConfigureAwait(false);

            if (firmware is not { Length: >= 2 })
            {
                return null;
            }

            // Identity second, and optional. Where the unit answers, this is the value that survives
            // a DHCP lease change and keeps an administrator's antenna assignments attached to the
            // right physical box.
            var identifier = await AskAsync(stream, reassembler, UhfSerialCommand.GetReaderIdentifier, ct)
                .ConfigureAwait(false);

            var power = await AskAsync(stream, reassembler, UhfSerialCommand.GetOutputPower, ct)
                .ConfigureAwait(false);

            var reported = CountPorts(power);

            return new ReaderIdentity(
                Host: host,
                Port: port,
                Protocol: ReaderProtocol.UhfSerial,
                SerialNumber: FormatIdentifier(identifier),
                FirmwareVersion: $"{firmware[0]}.{firmware[1]}",
                AntennaCount: reported ?? DefaultAntennaCount,
                AntennaCountReported: reported is not null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Answered the connect and then failed the conversation. Not a reader, as far as anything
            // downstream is concerned.
            return null;
        }
    }

    /// <summary>
    /// How many antenna ports the reply implies, or null when it does not imply one.
    /// <para>
    /// The protocol allows both shapes: one byte meaning every port shares a setting, or one byte per
    /// port where they differ. Only the second carries a count. Returning null for the first is the
    /// point — it is the difference between knowing and assuming, and the caller records which.
    /// </para>
    /// </summary>
    private static int? CountPorts(byte[]? power)
        => power is { Length: > 1 } ? power.Length : null;

    /// <summary>
    /// The identifier as hex, which is what is printed on these units and what an administrator will
    /// compare against. Trailing zero padding is dropped: the field is fixed width on the wire and
    /// shorter identifiers are zero-filled, so keeping them would make one reader look like two if a
    /// firmware revision ever changed the padding.
    /// </summary>
    private static string? FormatIdentifier(byte[]? identifier)
    {
        if (identifier is not { Length: > 0 })
        {
            return null;
        }

        var end = identifier.Length;

        while (end > 0 && identifier[end - 1] == 0)
        {
            end--;
        }

        return end == 0 ? null : Convert.ToHexString(identifier.AsSpan(0, end));
    }

    /// <summary>
    /// Sends one command and waits for the frame that answers it, ignoring anything else the reader
    /// happens to be saying. Returns null on timeout rather than throwing, because a device that does
    /// not answer a question is an ordinary outcome here rather than a fault.
    /// </summary>
    private static async Task<byte[]?> AskAsync(
        NetworkStream stream,
        UhfSerialCodec.FrameReassembler reassembler,
        byte command,
        CancellationToken ct)
    {
        var frame = UhfSerialCodec.Encode(UhfSerialCodec.PublicAddress, command, []);

        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ReplyTimeout);

        var buffer = new byte[512];

        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);

                if (read <= 0)
                {
                    return null;
                }

                foreach (var decoded in reassembler.Push(buffer.AsSpan(0, read)))
                {
                    if (decoded.Cmd == command)
                    {
                        return decoded.Data;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
