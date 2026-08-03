using System.Net.Sockets;
using Retail25.Contracts.Terminals;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// A short-lived request/response connection to a UHF reader, for settings and diagnostics.
/// <para>
/// Separate from the inventory connection on purpose. The inventory loop owns a frame channel it
/// drains continuously, and a query issued on that connection would race it — sometimes the loop
/// consumes the reply, sometimes the query consumes a tag. Neither failure is reproducible, and the
/// one that loses a tag loses a sale.
/// </para>
/// <para>
/// A second connection sidesteps it entirely: reading the temperature cannot disturb a basket being
/// rung up. It costs a TCP handshake per operation, which is nothing against how often anybody opens
/// a settings screen. Verified against a D2184B, which accepts a second connection while its own
/// vendor demo holds the first.
/// </para>
/// </summary>
internal sealed class UhfSerialControlChannel : IAsyncDisposable
{
    /// <summary>Generous: a reader measuring return loss physically transmits before it answers.</summary>
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(3);

    private readonly TcpClient _client = new() { NoDelay = true };
    private readonly UhfSerialCodec.FrameReassembler _reassembler = new();
    private readonly byte _address;

    private NetworkStream? _stream;

    private UhfSerialControlChannel(byte address) => _address = address;

    public static async Task<UhfSerialControlChannel> ConnectAsync(
        string host, int port, byte address, CancellationToken ct)
    {
        var channel = new UhfSerialControlChannel(address);

        await channel._client.ConnectAsync(host, port, ct);
        channel._stream = channel._client.GetStream();

        return channel;
    }

    /// <summary>
    /// Sends a command and returns the data of the reply that carries the same opcode.
    /// <para>
    /// Matched on opcode rather than taken as the next frame back: a reader mid-inventory when the
    /// connection opened can push tag frames that have nothing to do with the question asked.
    /// </para>
    /// <para>
    /// Returns null on timeout rather than throwing. Not every reader implements every command, and a
    /// diagnostics screen should say "unknown" for the one field its hardware will not answer rather
    /// than fail whole.
    /// </para>
    /// </summary>
    public async Task<byte[]?> QueryAsync(byte cmd, byte[] data, CancellationToken ct)
    {
        if (_stream is null)
        {
            return null;
        }

        var frame = UhfSerialCodec.Encode(_address, cmd, data);
        await _stream.WriteAsync(frame, ct);
        await _stream.FlushAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ReplyTimeout);

        var buffer = new byte[512];

        try
        {
            while (true)
            {
                var received = await _stream.ReadAsync(buffer, timeout.Token);
                if (received == 0)
                {
                    return null;
                }

                foreach (var reply in _reassembler.Push(buffer.AsSpan(0, received)))
                {
                    if (reply.Cmd == cmd)
                    {
                        return reply.Data;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sends a setting and reports whether the reader accepted it.
    /// <para>
    /// A write's reply is a single status byte: <c>0x10</c> for success, anything else a reason. Not
    /// checking it is how a settings screen comes to say "saved" about a value the reader refused —
    /// which is worse than an error, because it stops anyone looking.
    /// </para>
    /// </summary>
    public async Task<bool> CommandAsync(byte cmd, byte[] data, CancellationToken ct)
    {
        var reply = await QueryAsync(cmd, data, ct);
        return reply is { Length: > 0 } && reply[0] == UhfSerialStatus.Success;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        _client.Dispose();
    }
}

/// <summary>Status byte a write command answers with.</summary>
internal static class UhfSerialStatus
{
    public const byte Success = 0x10;

    /// <summary>Words for the codes worth telling a user apart. Anything else is reported as its number.</summary>
    public static string Describe(byte code) => code switch
    {
        0x10 => "accepted",
        0x11 => "the reader refused the command",
        0x20 => "the reader's radio module did not reset",
        0x22 => "no antenna is connected to that port",
        0x23 => "the reader could not save the setting",
        0x25 => "that transmit power is out of range for this reader",
        0x26 => "that frequency is outside the selected region",
        0x28 => "that antenna port does not exist on this reader",
        _ => $"the reader answered with code 0x{code:X2}",
    };
}
