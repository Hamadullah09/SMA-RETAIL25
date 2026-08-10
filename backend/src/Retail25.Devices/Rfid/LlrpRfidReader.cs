using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Retail25.Contracts.Terminals;

namespace Retail25.Devices.Rfid;

/// <summary>
/// An LLRP client, speaking the binary protocol over TCP (doc 06 Â§3).
/// <para>
/// The reader pushes RO_ACCESS_REPORT messages continuously once an ROSpec is running, so the socket
/// is drained on its own task into an unbounded channel and <see cref="ReadsAsync"/> simply consumes
/// it. Reading synchronously from the consumer instead would mean a slow batch flush applies
/// backpressure to the TCP socket, and a reader that cannot deliver reports starts dropping them.
/// </para>
/// <para>
/// Keepalives are answered immediately. Three missed ones is what the supervising service treats as
/// a dead reader â€” the point being that a silent socket and a reader with nothing in front of it look
/// identical, and a till must be able to tell them apart.
/// </para>
/// </summary>
public sealed class LlrpRfidReader : IRfidReader
{
    private const int KeepaliveMs = 5000;

    private readonly ILogger<LlrpRfidReader> _logger;
    private readonly Channel<TagRead> _reads = Channel.CreateUnbounded<TagRead>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = true,
    });

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _pumpCts;
    private Task? _pump;
    private uint _messageId;
    private bool _running;

    public LlrpRfidReader(ILogger<LlrpRfidReader> logger) => _logger = logger;

    public string Description { get; private set; } = "LLRP";

    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(ReaderProfileContract profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Description = $"LLRP {profile.Host}:{profile.Port}";

        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(profile.Host, profile.Port, ct);
        _stream = _client.GetStream();

        _pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pump = Task.Run(() => PumpAsync(_pumpCts.Token), CancellationToken.None);

        // A reader may still be running a previous session's ROSpec. Reset, then install ours.
        await SendAsync(LlrpMessageType.SetReaderConfig, LlrpBuilder.SetReaderConfig(KeepaliveMs), ct);
        await SendAsync(LlrpMessageType.DeleteRoSpec, LlrpBuilder.RoSpecId(LlrpBuilder.InventoryRoSpecId), ct);

        var antennas = AntennaZoneMap.CheckoutAntennas(profile.AntennaZones);
        await SendAsync(LlrpMessageType.AddRoSpec, LlrpBuilder.AddRoSpec(antennas), ct);
        await SendAsync(LlrpMessageType.EnableRoSpec, LlrpBuilder.RoSpecId(LlrpBuilder.InventoryRoSpecId), ct);

        _logger.LogInformation(
            "Connected to {Reader}, inventorying antennas {Antennas}",
            Description,
            AntennaZoneMap.Describe(antennas));
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_running)
        {
            return;
        }

        await SendAsync(LlrpMessageType.StartRoSpec, LlrpBuilder.RoSpecId(LlrpBuilder.InventoryRoSpecId), ct);
        _running = true;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!_running)
        {
            return;
        }

        _running = false;

        // Best effort: a reader that has already gone away must not stop the agent from shutting down.
        try
        {
            await SendAsync(LlrpMessageType.StopRoSpec, LlrpBuilder.RoSpecId(LlrpBuilder.InventoryRoSpecId), ct);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "Reader was already gone when stopping the ROSpec");
        }
    }

    public IAsyncEnumerable<TagRead> ReadsAsync(CancellationToken ct) => _reads.Reader.ReadAllAsync(ct);

    /// <summary>
    /// LLRP carries its own configuration model, and this client implements the reading half only.
    /// Rather than half-answer, it says which fields it cannot supply â€” the settings screen then
    /// shows "unknown" instead of an invented figure.
    /// </summary>
    public Task<ReaderDiagnostics> ReadDiagnosticsAsync(CancellationToken ct)
        => Task.FromResult(new ReaderDiagnostics
        {
            Unavailable = ["this reader speaks LLRP, whose settings are not managed from here"],
        });

    /// <summary>
    /// Nothing is pushed. Returned as a refusal rather than silent success, so nobody is told a
    /// setting was applied to a reader that never received it.
    /// </summary>
    public Task<IReadOnlyList<string>> ApplySettingsAsync(ReaderProfileContract profile, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>(
            ["every hardware setting: this reader speaks LLRP, which is configured on the device itself"]);

    private async Task PumpAsync(CancellationToken ct)
    {
        var header = new byte[LlrpCodec.HeaderLength];

        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                await ReadExactlyAsync(_stream, header, ct);
                var (type, length, messageId) = LlrpCodec.DecodeHeader(header);

                var payloadLength = (int)length - LlrpCodec.HeaderLength;
                if (payloadLength < 0 || payloadLength > 4 * 1024 * 1024)
                {
                    throw new InvalidDataException($"LLRP frame declared an implausible length of {length} bytes.");
                }

                var payload = new byte[payloadLength];
                if (payloadLength > 0)
                {
                    await ReadExactlyAsync(_stream, payload, ct);
                }

                await DispatchAsync(type, messageId, payload, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLRP session to {Reader} faulted", Description);
            _reads.Writer.TryComplete(ex);
        }
    }

    private async Task DispatchAsync(ushort type, uint messageId, byte[] payload, CancellationToken ct)
    {
        switch (type)
        {
            case LlrpMessageType.RoAccessReport:
                foreach (var read in LlrpReportParser.Parse(payload, DateTimeOffset.UtcNow))
                {
                    await _reads.Writer.WriteAsync(read, ct);
                }

                break;

            case LlrpMessageType.Keepalive:
                // Unanswered keepalives make the reader close the connection on us.
                await SendAsync(LlrpMessageType.KeepaliveAck, [], ct, messageId);
                break;

            case LlrpMessageType.ReaderEventNotification:
                _logger.LogDebug("Reader event notification from {Reader} ({Bytes} bytes)", Description, payload.Length);
                break;

            default:
                _logger.LogTrace("Ignoring LLRP message type {Type} from {Reader}", type, Description);
                break;
        }
    }

    private async Task SendAsync(ushort type, byte[] payload, CancellationToken ct, uint? messageId = null)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("The reader is not connected.");
        }

        var id = messageId ?? Interlocked.Increment(ref _messageId);
        var frame = LlrpCodec.Encode(type, id, payload);

        await _stream.WriteAsync(frame, ct);
        await _stream.FlushAsync(ct);
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;

        while (read < buffer.Length)
        {
            var received = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (received == 0)
            {
                throw new IOException("The reader closed the connection.");
            }

            read += received;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_pumpCts is not null)
        {
            await _pumpCts.CancelAsync();
        }

        if (_pump is not null)
        {
            try
            {
                await _pump;
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        _pumpCts?.Dispose();
        _stream?.Dispose();
        _client?.Dispose();
        _reads.Writer.TryComplete();
    }
}
