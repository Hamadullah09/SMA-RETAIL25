using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Retail25.Contracts.Terminals;

namespace Retail25.Devices.Rfid;

/// <summary>
/// A reader speaking the R2000-family "UHF RFID Reader Serial Interface Protocol" (v3.1) â€” the
/// protocol a D2184B and its relatives use â€” over TCP.
/// <para>
/// This protocol has no push-forever inventory mode: <c>cmd_real_time_inventory</c> (<c>0x89</c>)
/// streams each tag as it is found for one round, then ends with a summary or error frame and goes
/// quiet (Â§2.2.8). Continuous reads are therefore this class's own doing â€” it re-issues the command,
/// round after round, for as long as <see cref="StartAsync"/> is in effect, cycling through the
/// profile's checkout antennas with <c>cmd_set_work_antenna</c> (<c>0x74</c>) between rounds when more
/// than one is configured. Repeat is fixed at <c>0xFF</c>, the manual's own documented technique for
/// making each round as short as possible (Â§1.6.2 Method 1) â€” the point of running rounds back to back
/// rather than asking for a single long one.
/// </para>
/// <para>
/// TCP only: either the reader's own network interface, or a serial-to-Ethernet bridge (an IPort
/// module, or equivalent) in front of a unit wired via RS-232 â€” the same shape as
/// <c>LlrpRfidReader</c>, and the reason <see cref="ReaderProfileContract"/> needs no new fields to
/// carry this protocol.
/// </para>
/// </summary>
public sealed class UhfSerialRfidReader : IRfidReader
{
    /// <summary>Shortest-round technique from Â§1.6.2 Method 1 â€” this class supplies "continuous" by looping it.</summary>
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

    private ReaderConnection? _connection;
    private Stream? _stream;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _pump;
    private Task? _inventoryLoop;
    private IReadOnlyList<byte> _antennas = [0];
    private volatile bool _running;

    /// <summary>Kept so settings and diagnostics can open their own connection to the same reader.</summary>
    private ReaderProfileContract? _profile;

    /// <summary>
    /// One control operation at a time. Two settings screens open at once would otherwise each open a
    /// connection and interleave writes, and the reader would end up with a mixture of both.
    /// </summary>
    private readonly SemaphoreSlim _controlGate = new(1, 1);

    /// <summary>
    /// Four is the family's maximum and the D2184B's actual count. Only used to decide how many ports
    /// to interrogate; a reader with fewer simply does not answer for the ones it lacks.
    /// </summary>
    private const int AntennaPorts = 4;

    private readonly ReaderConnectionOpener _open;

    /// <param name="open">
    /// How to reach the reader. Defaults to the network, which is the only wire this project can
    /// open on its own; the terminal agent passes one that also understands a COM port, because a
    /// serial lead is attached to the till and only something running there can use it.
    /// </param>
    public UhfSerialRfidReader(ILogger<UhfSerialRfidReader> logger, ReaderConnectionOpener? open = null)
    {
        _logger = logger;
        _open = open ?? NetworkReaderTransport.OpenAsync;
    }

    public string Description { get; private set; } = "UHF Serial";

    public bool IsConnected => _connection?.IsOpen == true;

    public async Task ConnectAsync(ReaderProfileContract profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _profile = profile;

        // A socket or a COM port, decided by what the profile's Host looks like. The same reader
        // speaks the same frames either way; only the lead differs, which is why everything below
        // this line is unchanged.
        _connection = await _open(profile.Host, profile.Port, profile.BaudRate, ct);
        _stream = _connection.Stream;

        Description = $"UHF Serial {_connection.Description}";

        // Antenna ids on the wire are 0-based (per-port); the reader profile's zoning is 1-based, so a
        // configured "1" is physical port 0. No zoning configured falls back to port 0 â€” matches the
        // simulator's own fallback for an unzoned profile.
        var checkoutAntennas = AntennaZoneMap.CheckoutAntennas(profile.AntennaZones);
        _antennas = checkoutAntennas.Count == 0
            ? [0]
            : checkoutAntennas.Select(a => (byte)Math.Max(0, a - 1)).ToArray();

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pump = Task.Run(() => PumpAsync(_lifetimeCts.Token), CancellationToken.None);

        await ProveItIsAReaderAsync(ct);

        _logger.LogInformation(
            "Connected to {Reader}, inventorying antennas {Antennas}",
            Description,
            AntennaZoneMap.Describe(checkoutAntennas));

        // The device is configured from the profile on every connect, not only when the profile
        // changes. A reader that has been swapped for a spare, factory-reset, or reconfigured by
        // somebody with the vendor's demo open is otherwise silently running settings nobody chose.
        var refused = await ApplySettingsAsync(profile, ct);

        if (refused.Count > 0)
        {
            _logger.LogWarning(
                "{Reader} would not accept: {Refused}. It is running its own settings for those.",
                Description,
                string.Join(", ", refused));
        }
    }

    public async Task<ReaderDiagnostics> ReadDiagnosticsAsync(CancellationToken ct)
    {
        if (_profile is null)
        {
            return new ReaderDiagnostics { Unavailable = ["the reader is not configured"] };
        }

        await _controlGate.WaitAsync(ct);

        try
        {
            return await UhfSerialSettings.ReadAsync(this, AntennaPorts, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read diagnostics from {Reader}", Description);
            return new ReaderDiagnostics { Unavailable = ["the reader did not answer"] };
        }
        finally
        {
            _controlGate.Release();
        }
    }

    /// <summary>
    /// Makes the device prove it is a reader before we report one.
    /// <para>
    /// Opening is not evidence. A TCP connect proves something is listening on a port; opening a COM
    /// port proves almost nothing at all, because Windows opens a serial port successfully whether or
    /// not anything is on the other end of it. <see cref="IsConnected"/> was built on exactly that,
    /// so any openable port counted as a working reader.
    /// </para>
    /// <para>
    /// A till in a shop found the hole. The serial fallback picked the highest COM port, which on that
    /// machine was <c>Intel(R) Active Management Technology - SOL (COM3)</c> — a motherboard virtual
    /// port with no reader behind it. It opened, so the agent announced a reader, the status strip lit
    /// green, and the real reader on the shop LAN was left alone. A cashier held a tag against a
    /// reader the screen called healthy and nothing happened, which is worse than an outage: an
    /// outage at least tells you to go and look.
    /// </para>
    /// <para>
    /// One firmware query is enough, and it is the cheapest frame the protocol has. A reader answers
    /// it; a virtual COM port, a printer, or a scale on the wrong lead does not. Failing here throws,
    /// which puts the reconnect loop back to searching instead of settling on a device that will never
    /// read a tag.
    /// </para>
    /// </summary>
    private async Task ProveItIsAReaderAsync(CancellationToken ct)
    {
        byte[]? firmware;

        await _controlGate.WaitAsync(ct);

        try
        {
            firmware = await ControlQueryAsync(UhfSerialCommand.GetFirmwareVersion, [], ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            firmware = null;
        }
        finally
        {
            _controlGate.Release();
        }

        if (firmware is not null)
        {
            return;
        }

        var refused = Description;

        // Torn down here rather than left to the caller: this is a COM port on the failing path, and
        // a handle held by a dead session is one the next attempt cannot open.
        await TearDownAsync();

        throw new IOException(
            $"{refused} opened but did not answer a firmware query, so it is not a reader.");
    }

    /// <summary>
    /// Closes the wire and stops the pump, leaving the object reusable for the next attempt.
    /// <para>
    /// The connection is disposed <em>before</em> the pump is waited on, and the wait is bounded. Both
    /// matter, and the first version of this had neither: a serial port's stream does not honour a
    /// cancellation token on a pending read — the token is accepted and then ignored — so cancelling
    /// and awaiting the pump waits for a read that will never be cancelled. Disposing the stream is
    /// what actually unblocks it. The timeout is the belt to that braces: a teardown on the failing
    /// path must never be able to wedge the reconnect loop, because the whole point of reaching here
    /// is to go and try something else.
    /// </para>
    /// </summary>
    private async Task TearDownAsync()
    {
        if (_lifetimeCts is not null)
        {
            await _lifetimeCts.CancelAsync();
        }

        _connection?.Dispose();

        if (_pump is not null)
        {
            try
            {
                await _pump.WaitAsync(TearDownTimeout);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or SocketException)
            {
                // Expected: this is the shutdown path, and an abandoned pump on a disposed stream
                // ends itself. Waiting longer would achieve nothing a caller can use.
            }
        }

        _connection = null;
        _stream = null;
        _pump = null;
        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
    }

    private static readonly TimeSpan TearDownTimeout = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<string>> ApplySettingsAsync(ReaderProfileContract profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profile = profile;

        await _controlGate.WaitAsync(ct);

        try
        {
            return await UhfSerialSettings.ApplyAsync(this, profile, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not apply settings to {Reader}", Description);
            return ["the reader did not answer"];
        }
        finally
        {
            _controlGate.Release();
        }
    }

    /// <summary>
    /// Sends a control command on the reader's own connection and returns the reply's data.
    /// <para>
    /// On the reader's connection, not a second one, and this was not the first design. A separate
    /// socket is tidier â€” it cannot possibly disturb an inventory round â€” and against a D2184B it
    /// silently returns nothing for every query. These readers are a single serial line behind a TCP
    /// bridge: a second client is accepted and then starved, because there is only one UART and the
    /// bridge is already servicing the first. The tidier design was answering "unknown" for every
    /// field on hardware that answers perfectly well.
    /// </para>
    /// <para>
    /// So control shares the wire, and <see cref="_controlGate"/> keeps it out of the middle of an
    /// inventory round. Callers must already hold that gate.
    /// </para>
    /// </summary>
    internal async Task<byte[]?> ControlQueryAsync(byte cmd, byte[] data, CancellationToken ct)
    {
        if (_stream is null)
        {
            return null;
        }

        // Anything already queued belongs to whatever happened before this command. Draining first
        // means a stale frame cannot be mistaken for this command's answer.
        while (_frames.Reader.TryRead(out _))
        {
        }

        await SendAsync(cmd, data, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ControlReplyTimeout);

        try
        {
            while (true)
            {
                var frame = await _frames.Reader.ReadAsync(timeout.Token);

                if (frame.Cmd == cmd)
                {
                    return frame.Data;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("{Reader} did not answer command 0x{Cmd:X2}", Description, cmd);
            return null;
        }
    }

    /// <summary>True when the reader accepted the setting. Anything but 0x10 is a refusal.</summary>
    internal async Task<bool> ControlCommandAsync(byte cmd, byte[] data, CancellationToken ct)
    {
        var reply = await ControlQueryAsync(cmd, data, ct);
        return reply is { Length: > 0 } && reply[0] == UhfSerialStatus.Success;
    }

    /// <summary>Generous: measuring return loss makes the reader physically transmit before it answers.</summary>
    private static readonly TimeSpan ControlReplyTimeout = TimeSpan.FromSeconds(2);

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

                // One round at a time on the wire, so a settings read cannot land in the middle of
                // one. Taken per round rather than around the whole loop: a control operation should
                // wait for the current round, not for the till to stop selling.
                await _controlGate.WaitAsync(ct);

                try
                {
                    await SendAsync(UhfSerialCommand.SetWorkAntenna, [antenna], ct);
                    await TryAwaitFrameAsync(UhfSerialCommand.SetWorkAntenna, AntennaAckTimeout, ct);

                    await SendAsync(UhfSerialCommand.RealTimeInventory, [RepeatFastest], ct);
                    await RunRoundAsync(ct);
                }
                finally
                {
                    _controlGate.Release();
                }
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

        // The connection owns the stream and closes it in the right order for its kind.
        _connection?.Dispose();
        _reads.Writer.TryComplete();
        _frames.Writer.TryComplete();
    }
}
