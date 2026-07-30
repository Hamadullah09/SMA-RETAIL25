using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Retail25.Contracts.Terminals;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// A reader speaking the R2000-family "UHF RFID Reader Serial Interface Protocol" (v3.1) — the
/// protocol a D2184B and its relatives use — over TCP.
/// <para>
/// This protocol has no push-forever inventory mode: <c>cmd_real_time_inventory</c> (<c>0x89</c>)
/// streams each tag as it is found for one round, then ends with a summary or error frame and goes
/// quiet (§2.2.8). Continuous reads are therefore this class's own doing — it re-issues the command,
/// round after round, for as long as <see cref="StartAsync"/> is in effect, cycling through the
/// profile's checkout antennas with <c>cmd_set_work_antenna</c> (<c>0x74</c>) between rounds when more
/// than one is configured. Repeat is fixed at <c>0xFF</c>, the manual's own documented technique for
/// making each round as short as possible (§1.6.2 Method 1) — the point of running rounds back to back
/// rather than asking for a single long one.
/// </para>
/// <para>
/// TCP only: either the reader's own network interface, or a serial-to-Ethernet bridge (an IPort
/// module, or equivalent) in front of a unit wired via RS-232 — the same shape as
/// <c>LlrpRfidReader</c>, and the reason <see cref="ReaderProfileContract"/> needs no new fields to
/// carry this protocol.
/// </para>
/// </summary>
public sealed class UhfSerialRfidReader : IRfidReader
{
    /// <summary>Shortest-round technique from §1.6.2 Method 1 — this class supplies "continuous" by looping it.</summary>
    private const byte RepeatFastest = 0xFF;

    /// <summary>How long to wait for a <c>SetWorkAntenna</c> acknowledgement before proceeding anyway.</summary>
    private static readonly TimeSpan AntennaAckTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger<UhfSerialRfidReader> _logger;

    private readonly Channel<TagRead> _reads = Channel.CreateUnbounded<TagRead>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = true,
    });

    private readonly Channel<UhfSerialFrame> _frames = Channel.CreateUnbounded<UhfSerialFrame>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = true,
    });

    private readonly UhfSerialCodec.FrameReassembler _reassembler = new();

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _pump;
    private Task? _inventoryLoop;
    private IReadOnlyList<byte> _antennas = [0];
    private volatile bool _running;

    public UhfSerialRfidReader(ILogger<UhfSerialRfidReader> logger) => _logger = logger;

    public string Description { get; private set; } = "UHF Serial";

    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(ReaderProfileContract profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Description = $"UHF Serial {profile.Host}:{profile.Port}";

        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(profile.Host, profile.Port, ct);
        _stream = _client.GetStream();

        // Antenna ids on the wire are 0-based (per-port); the reader profile's zoning is 1-based, so a
        // configured "1" is physical port 0. No zoning configured falls back to port 0 — matches the
        // simulator's own fallback for an unzoned profile.
        var checkoutAntennas = AntennaZoneMap.CheckoutAntennas(profile.AntennaZones);
        _antennas = checkoutAntennas.Count == 0
            ? [0]
            : checkoutAntennas.Select(a => (byte)Math.Max(0, a - 1)).ToArray();

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pump = Task.Run(() => PumpAsync(_lifetimeCts.Token), CancellationToken.None);

        _logger.LogInformation(
            "Connected to {Reader}, inventorying antennas {Antennas}",
            Description,
            AntennaZoneMap.Describe(checkoutAntennas));
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (_running)
        {
            return Task.CompletedTask;
        }

        _running = true;
        _inventoryLoop = Task.Run(() => InventoryLoopAsync(_lifetimeCts!.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!_running)
        {
            return;
        }

        _running = false;

        if (_inventoryLoop is not null)
        {
            try
            {
                await _inventoryLoop;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    public IAsyncEnumerable<TagRead> ReadsAsync(CancellationToken ct) => _reads.Reader.ReadAllAsync(ct);

    /// <summary>
    /// Cycles the configured antennas forever: point at one, run a fast inventory round, hand every
    /// tag seen to the output channel, then move on once the round's summary or error frame arrives.
    /// </summary>
    private async Task InventoryLoopAsync(CancellationToken ct)
    {
        try
        {
            var antennaIndex = 0;

            while (_running && !ct.IsCancellationRequested)
            {
                var antenna = _antennas[antennaIndex % _antennas.Count];
                antennaIndex++;

                await SendAsync(UhfSerialCommand.SetWorkAntenna, [antenna], ct);
                await TryAwaitFrameAsync(UhfSerialCommand.SetWorkAntenna, AntennaAckTimeout, ct);

                await SendAsync(UhfSerialCommand.RealTimeInventory, [RepeatFastest], ct);
                await RunRoundAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UHF serial inventory loop on {Reader} faulted", Description);
            _reads.Writer.TryComplete(ex);
        }
    }

    /// <summary>Consumes frames for one inventory round until the summary or an error frame ends it.</summary>
    private async Task RunRoundAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        while (_running && !ct.IsCancellationRequested)
        {
            var frame = await _frames.Reader.ReadAsync(ct);

            if (frame.Cmd != UhfSerialCommand.RealTimeInventory)
            {
                continue;
            }

            switch (InventoryFrameParser.Classify(frame))
            {
                case InventoryFrameKind.Tag:
                    var tag = InventoryFrameParser.ParseTag(frame);
                    await _reads.Writer.WriteAsync(
                        new TagRead(tag.Epc, tag.RawAntenna + 1, tag.RssiDbm, 1, now, now),
                        ct);
                    break;

                case InventoryFrameKind.RoundComplete:
                    return;

                case InventoryFrameKind.Error:
                    _logger.LogDebug(
                        "UHF serial reader {Reader} reported inventory error 0x{Code:X2}",
                        Description,
                        InventoryFrameParser.ReadErrorCode(frame));
                    return;
            }
        }
    }

    /// <summary>Best-effort wait for a response to a setup command; a timeout just proceeds regardless.</summary>
    private async Task TryAwaitFrameAsync(byte cmd, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (true)
            {
                var frame = await _frames.Reader.ReadAsync(timeoutCts.Token);
                if (frame.Cmd == cmd)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("No acknowledgement for command 0x{Cmd:X2} from {Reader} within {Timeout}", cmd, Description, timeout);
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                var received = await _stream.ReadAsync(buffer, ct);
                if (received == 0)
                {
                    throw new IOException("The reader closed the connection.");
                }

                foreach (var frame in _reassembler.Push(buffer.AsSpan(0, received)))
                {
                    await _frames.Writer.WriteAsync(frame, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UHF serial connection to {Reader} faulted", Description);
            _reads.Writer.TryComplete(ex);
            _frames.Writer.TryComplete(ex);
        }
    }

    private async Task SendAsync(byte cmd, byte[] data, CancellationToken ct)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("The reader is not connected.");
        }

        var frame = UhfSerialCodec.Encode(UhfSerialCodec.PublicAddress, cmd, data);
        await _stream.WriteAsync(frame, ct);
        await _stream.FlushAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        _running = false;

        if (_lifetimeCts is not null)
        {
            await _lifetimeCts.CancelAsync();
        }

        foreach (var task in new[] { _inventoryLoop, _pump })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        _lifetimeCts?.Dispose();
        _stream?.Dispose();
        _client?.Dispose();
        _reads.Writer.TryComplete();
        _frames.Writer.TryComplete();
    }
}
