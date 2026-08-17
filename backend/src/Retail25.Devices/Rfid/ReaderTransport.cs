using System.Net.Sockets;

namespace Retail25.Devices.Rfid;

/// <summary>
/// An open reader: the stream to talk over, and the thing that owns it.
/// <para>
/// The R2000 family speaks one protocol over either a socket or a serial lead. A unit with an
/// Ethernet port, or one behind a serial-to-Ethernet bridge, is reached at a host and port; the same
/// unit plugged into the till by USB appears as a COM port and speaks identical frames. Only the way
/// the bytes arrive differs, so only that is abstracted — the codec never learns which it got.
/// </para>
/// </summary>
public sealed class ReaderConnection : IDisposable
{
    private readonly IDisposable _owner;
    private readonly Func<bool> _isOpen;

    public ReaderConnection(Stream stream, IDisposable owner, string description, Func<bool> isOpen)
    {
        Stream = stream;
        _owner = owner;
        Description = description;
        _isOpen = isOpen;
    }

    public Stream Stream { get; }

    /// <summary>How to describe this connection to somebody reading a log.</summary>
    public string Description { get; }

    public bool IsOpen => _isOpen();

    public void Dispose()
    {
        // The stream first: a serial port disposed underneath its own BaseStream throws on Windows.
        try
        {
            Stream.Dispose();
        }
        catch (IOException)
        {
            // A lead pulled mid-close. Nothing left to salvage and nothing worth reporting.
        }

        _owner.Dispose();
    }
}

/// <summary>
/// Opens the wire to a reader.
/// <para>
/// A delegate rather than a fixed implementation because the two hosts can reach different kinds of
/// reader. A serial port is physically attached to one machine, so only the agent running <em>on</em>
/// the till can open one — an API, especially one in a datacentre, never can. The agent therefore
/// supplies an opener that understands COM ports; the API keeps the network-only default, and this
/// project stays free of a serial-port dependency it would only be carrying for somebody else.
/// </para>
/// </summary>
public delegate Task<ReaderConnection> ReaderConnectionOpener(
    string address,
    int port,
    int baudRate,
    CancellationToken ct);

/// <summary>Reaching a reader over the network, which is all this project can do on its own.</summary>
public static class NetworkReaderTransport
{
    public static async Task<ReaderConnection> OpenAsync(string address, int port, int baudRate, CancellationToken ct)
    {
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(address, port, ct);

        return new ReaderConnection(client.GetStream(), client, $"{address}:{port}", () => client.Connected);
    }
}
