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
        var remaining = _buffer.Drain(int.MaxValue);
        if (remaining.Count > 0)
        {
            await _spool.EnqueueAsync(remaining, CancellationToken.None);
        }
    }

    private async Task FlushAsync(int maxBatchSize, CancellationToken ct)
    {
        var batch = _buffer.Drain(maxBatchSize);
        if (batch.Count == 0)
        {
            return;
        }

        if (await _server.PublishTagsAsync(batch, ct))
        {
            if (_warnedOffline)
            {
                _logger.LogInformation("Server reachable again; tag publishing resumed");
                _warnedOffline = false;
            }

            return;
        }

        if (!_warnedOffline)
        {
            _logger.LogWarning("Server unreachable; spooling tag reads locally");
            _warnedOffline = true;
        }

        await _spool.EnqueueAsync(batch, ct);
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
            if (!await _server.PublishTagsAsync(batch.Tags, ct))
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
    private readonly AgentOptions _options;
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(
        IServerConnection server,
        RfidReaderService reader,
        Peripherals.PeripheralCoordinator peripherals,
        TagBuffer buffer,
        Microsoft.Extensions.Options.IOptions<AgentOptions> options,
        ILogger<HeartbeatService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _server = server;
        _reader = reader;
        _peripherals = peripherals;
        _buffer = buffer;
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
