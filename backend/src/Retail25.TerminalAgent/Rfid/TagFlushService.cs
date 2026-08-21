using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Retail25.Contracts.Terminals;
using Retail25.TerminalAgent.Server;
using Retail25.TerminalAgent.Spooling;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// Drains the coalescing buffer on a timer and publishes what it finds (doc 06 §2).
/// <para>
/// A batch that cannot be delivered goes to the local spool rather than being dropped, and the spool
/// is drained ahead of live traffic once the server returns so tags are replayed in the order they
/// were read. The server's Redis debounce is what makes replay safe: a tag that was already applied
/// before the connection dropped is rejected as a duplicate rather than sold twice.
/// </para>
/// </summary>
public sealed class TagFlushService : BackgroundService
{
    private readonly TagBuffer _buffer;
    private readonly IServerConnection _server;
    private readonly ITagSpool _spool;
    private readonly ProfileStore _profiles;
    private readonly ILogger<TagFlushService> _logger;

    private bool _warnedOffline;

    public TagFlushService(
        TagBuffer buffer,
        IServerConnection server,
        ITagSpool spool,
        ProfileStore profiles,
        ILogger<TagFlushService> logger)
    {
        _buffer = buffer;
        _server = server;
        _spool = spool;
        _profiles = profiles;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var profile = _profiles.Reader;

            try
            {
                await DrainSpoolAsync(stoppingToken);
                await FlushAsync(profile.MaxBatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Flushing tags failed");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(50, profile.FlushIntervalMs)), stoppingToken);
        }

        // On shutdown, whatever is left is spooled rather than lost: the till may be being restarted
        // mid-basket, and those tags are still on the counter.
        //
        // By reader, like the live path. Draining them into one heap would spool a restart's worth of
        // reads with no reader identity, and they would come back after the restart addressed to this
        // machine's own till — which for a reader serving two checkouts is the wrong one for half of
        // them, on the first basket after every restart.
        foreach (var batch in _buffer.DrainByReader(int.MaxValue))
        {
            await _spool.EnqueueAsync(batch.ReaderId, batch.Tags, CancellationToken.None);
        }
    }

    private async Task FlushAsync(int maxBatchSize, CancellationToken ct)
    {
        // Split by the reader that saw each tag. A machine driving three readers sends three
        // batches, each addressed to its own reader, because the station is resolved on the server
        // from reader and antenna and a merged batch could not say which reader saw what.
        var batches = _buffer.DrainByReader(maxBatchSize);

        if (batches.Count == 0)
        {
            return;
        }

        var delivered = true;

        foreach (var batch in batches)
        {
            // Reader 0 is an agent still running the per-station profile: it has no reader identity
            // to address a batch to, so it goes out by station exactly as it always did. That is what
            // lets an estate be upgraded one till at a time rather than in a single evening.
            var sent = batch.ReaderId == 0
                ? await _server.PublishTagsAsync(batch.Tags, ct)
                : await _server.PublishReaderTagsAsync(batch.ReaderId, batch.Tags, ct);

            if (sent)
            {
                continue;
            }

            // Spooled per batch, keeping its own reads together. One reader's batch failing must not
            // cost another's: they are separate observations of separate places, and merging them
            // into one spool entry would replay them as though one reader had seen everything.
            delivered = false;
            await _spool.EnqueueAsync(batch.ReaderId, batch.Tags, ct);
        }

        if (!delivered)
        {
            if (!_warnedOffline)
            {
                _logger.LogWarning("Server unreachable; spooling tag reads locally");
                _warnedOffline = true;
            }

            return;
        }

        if (_warnedOffline)
        {
            _logger.LogInformation("Server reachable again; tag publishing resumed");
            _warnedOffline = false;
        }
    }

    /// <summary>
    /// Replays spooled batches oldest-first, stopping at the first failure so ordering is preserved.
    /// Acknowledging only what was actually delivered is what makes the spool safe to interrupt.
    /// </summary>
    private async Task DrainSpoolAsync(CancellationToken ct)
    {
        if (!_server.IsConnected)
        {
            return;
        }

        var batches = await _spool.PeekAsync(20, ct);
        if (batches.Count == 0)
        {
            return;
        }

        var delivered = new List<long>(batches.Count);

        foreach (var batch in batches)
        {
            // Replayed the way it was taken. A batch from a managed reader goes back addressed to
            // that reader, so the server resolves the station from the antenna exactly as it would
            // have live; only a batch with no reader identity — an older spool file, or an agent on
            // the per-station profile — goes out by station.
            var sent = batch.ReaderId == 0
                ? await _server.PublishTagsAsync(batch.Tags, ct)
                : await _server.PublishReaderTagsAsync(batch.ReaderId, batch.Tags, ct);

            if (!sent)
            {
                break;
            }

            delivered.Add(batch.Id);
        }

        if (delivered.Count > 0)
        {
            await _spool.AcknowledgeAsync(delivered, ct);
            _logger.LogInformation("Replayed {Count} spooled tag batches", delivered.Count);
        }
    }
}

/// <summary>
/// Tells the server this till's hardware is alive (doc 06 §3).
/// <para>
/// Without it, a reader that has silently stopped reporting is indistinguishable from a reader with
/// nothing in front of it — and a cashier who cannot tell those apart will keep waving a basket at a
/// dead antenna. The heartbeat is what turns the strip red.
/// </para>
/// </summary>
public sealed class HeartbeatService : BackgroundService
{
    private readonly IServerConnection _server;
    private readonly RfidReaderService _reader;
    private readonly Peripherals.PeripheralCoordinator _peripherals;
    private readonly TagBuffer _buffer;
    private readonly Server.DeviceCheckIn _checkIn;
    private readonly AgentOptions _options;
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(
        IServerConnection server,
        RfidReaderService reader,
        Peripherals.PeripheralCoordinator peripherals,
        TagBuffer buffer,
        Server.DeviceCheckIn checkIn,
        Microsoft.Extensions.Options.IOptions<AgentOptions> options,
        ILogger<HeartbeatService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _server = server;
        _reader = reader;
        _peripherals = peripherals;
        _buffer = buffer;
        _checkIn = checkIn;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Reads since the last beat, expressed per second, so the feed can show a rate
                // rather than a meaningless running total.
                var reads = _buffer.ResetRate();
                var rate = (int)Math.Round(reads / Math.Max(1, interval.TotalSeconds));

                await _server.ReportStatusAsync(
                    new AgentStatusReport(
                        _options.StationId,
                        AgentVersion.Current,
                        _reader.ReaderOnline,
                        _peripherals.PrinterOnline,
                        _peripherals.ScaleOnline,
                        _peripherals.DrawerOnline,
                        _peripherals.PoleDisplayOnline,
                        rate),
                    stoppingToken);

                // The station heartbeat above says "this till is alive". This says "this machine
                // exists, and here is what it is driving" — which is what the server needs before it
                // can answer with an antenna map. Same beat, because the server marks a machine
                // offline after three of them.
                await _checkIn.CheckInAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Heartbeat failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
