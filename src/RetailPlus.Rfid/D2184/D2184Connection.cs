using System.IO.Ports;
using System.Net.Sockets;
using RetailPlus.Core.Model;

namespace RetailPlus.Rfid.D2184;

/// <summary>
/// The byte link to one reader (TCP, or a serial port for RS-232/RS-485) with request/response on top. The protocol
/// is one command at a time: a request waits for the reply carrying the same command code; inventory commands also
/// deliver any number of intermediate frames (the tags) before their final frame.
/// </summary>
internal sealed class D2184Connection(RfidReader settings) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly FrameDecoder _decoder = new();
    private TcpClient? _tcp;
    private SerialPort? _serial;
    private Stream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;
    private volatile Action<ReaderFrame>? _handler;
    private volatile bool _connected;

    public byte Address { get; } = (byte)Math.Clamp(settings.Address, 0, 255);
    public bool IsConnected => _connected;
    public static TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await CloseAsync();
        if (settings.Transport == RfidTransport.Serial)
        {
            if (string.IsNullOrWhiteSpace(settings.SerialPort)) throw new RfidException("No serial port is set for this reader.");
            _serial = new SerialPort(settings.SerialPort, settings.BaudRate, Parity.None, 8, StopBits.One);
            _serial.Open();
            _stream = _serial.BaseStream;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(settings.Host)) throw new RfidException("No IP address is set for this reader.");
            _tcp = new TcpClient { NoDelay = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            try
            {
                await _tcp.ConnectAsync(settings.Host, settings.Port, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _tcp.Dispose();
                _tcp = null;
                throw new RfidException($"No answer from {settings.Host}:{settings.Port} within {ConnectTimeout.TotalSeconds:0} seconds.");
            }
            _tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            _stream = _tcp.GetStream();
        }
        _receiveCts = new CancellationTokenSource();
        _connected = true;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_stream, _receiveCts.Token), CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buffer, cancellationToken);
                if (n == 0) break;
                foreach (var frame in _decoder.Feed(buffer.AsSpan(0, n)))
                    _handler?.Invoke(frame);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _connected = false;
        }
    }

    /// <summary>
    /// Sends a command and waits for its final reply. <paramref name="isFinal"/> decides which reply ends the request
    /// (by default the first one with the same command code); the others go to <paramref name="onIntermediate"/>.
    /// </summary>
    public async Task<ReaderFrame> RequestAsync(byte command, byte[] data, TimeSpan timeout, CancellationToken cancellationToken,
        Func<ReaderFrame, bool>? isFinal = null, Action<ReaderFrame>? onIntermediate = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stream = _stream;
            if (stream is null || !_connected) throw new RfidException("The reader is not connected.");
            var reply = new TaskCompletionSource<ReaderFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            _handler = frame =>
            {
                if (frame.Command != command) return;
                if (isFinal is null || isFinal(frame)) reply.TrySetResult(frame);
                else onIntermediate?.Invoke(frame);
            };
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timer.CancelAfter(timeout);
            using var registration = timer.Token.Register(() =>
            {
                if (cancellationToken.IsCancellationRequested) reply.TrySetCanceled(cancellationToken);
                else reply.TrySetException(new RfidException($"The reader did not answer command 0x{command:X2} in time."));
            });
            try
            {
                await stream.WriteAsync(ReaderFrame.Encode(Address, command, data), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                _connected = false;
                throw new RfidException("The connection to the reader was lost.", ex);
            }
            return await reply.Task;
        }
        finally
        {
            _handler = null;
            _gate.Release();
        }
    }

    /// <summary>Sends a command that has no reply on success (reset).</summary>
    public async Task SendAsync(byte command, byte[] data, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stream = _stream ?? throw new RfidException("The reader is not connected.");
            await stream.WriteAsync(ReaderFrame.Encode(Address, command, data), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseAsync()
    {
        _connected = false;
        if (_receiveCts is not null) await _receiveCts.CancelAsync();
        _stream?.Dispose();
        _tcp?.Dispose();
        _serial?.Dispose();
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop.ConfigureAwait(false); } catch (Exception) { }
        }
        _receiveCts?.Dispose();
        _stream = null;
        _tcp = null;
        _serial = null;
        _receiveLoop = null;
        _receiveCts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _gate.Dispose();
    }
}
