using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retail25.Contracts.Terminals;
using Retail25.TerminalAgent.Server;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// The current device profile, shared by the services that need it.
/// <para>
/// The server owns these values and pushes changes at any time, so nothing caches its own copy: the
/// reader service, the flush service and the peripheral coordinator all read from here, and a
/// profile change therefore takes effect everywhere at once rather than in whichever service happens
/// to restart first.
/// </para>
/// </summary>
public sealed class ProfileStore
{
    private TerminalProfileContract? _profile;

    public TerminalProfileContract? Current => Volatile.Read(ref _profile);

    public ReaderProfileContract Reader => Current?.Reader ?? DefaultReader;

    public ReaderMode Mode { get; private set; } = ReaderMode.OnDemand;

    /// <summary>Used until the server answers, so the agent starts sanely rather than not at all.</summary>
    public static ReaderProfileContract DefaultReader { get; } = new(
        Guid.Empty,
        "Default",
        "127.0.0.1",
        5084,
        ReaderProtocol.Simulator,
        "1=Checkout",
        RssiThresholdDbm: -70,
        MinimumReadCount: 2,
        DebounceMs: 3000,
        CoalesceMs: 250,
        FlushIntervalMs: 200,
        MaxBatchSize: 50,
        AutoAcceptBatches: false,
        ContinuousMode: false);

    /// <summary>
    /// Raised when the server sends new hardware settings.
    /// <para>
    /// A service that is blocked reading from a socket cannot notice a changed field by polling it —
    /// it is not running any code to poll with. So the change has to arrive as a signal it is already
    /// waiting on. This is what makes "change the reader's address in Setup and it takes effect"
    /// true rather than "…and it takes effect after someone restarts the till".
    /// </para>
    /// </summary>
    public event Action? Changed;

    public void Set(TerminalProfileContract profile)
    {
        var previous = Volatile.Read(ref _profile);

        Volatile.Write(ref _profile, profile);
        Mode = profile.ReaderMode;

        // Only when the reader half actually differs. The server re-sends the whole profile on every
        // reconnect, and tearing down a working reader session because the printer's name changed
        // would drop tags for no reason.
        if (previous?.Reader != profile.Reader)
        {
            Changed?.Invoke();
        }
    }

    public void SetMode(ReaderMode mode) => Mode = mode;
}

/// <summary>
/// Keeps a reader connected and streaming into the coalescing buffer (doc 06 §3).
/// <para>
/// A reader that faults is reconnected with backoff, forever. That is deliberate: the most common
/// real failure is a network switch rebooting or a reader power-cycling overnight, and the correct
/// behaviour in both cases is to come back on its own rather than to need someone to restart a
/// service before the shop can open.
/// </para>
/// </summary>
public sealed class RfidReaderService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ProfileStore _profiles;
    private readonly TagBuffer _buffer;
    private readonly AgentOptions _options;
    private readonly ILogger<RfidReaderService> _logger;

    private IRfidReader? _reader;

    public RfidReaderService(
        IServiceProvider services,
        ProfileStore profiles,
        TagBuffer buffer,
        IOptions<AgentOptions> options,
        ILogger<RfidReaderService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _services = services;
        _profiles = profiles;
        _buffer = buffer;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>True while a reader is connected. Drives the red strip on the live feed.</summary>
    public bool ReaderOnline => _reader?.IsConnected == true;

    public string ReaderDescription => _reader?.Description ?? "none";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            // Cancelled either by shutdown or by the server sending new hardware settings. The second
            // is the one that matters here: the agent starts before the server has answered, so its
            // first session always runs on the built-in Simulator default. Without a way to interrupt
            // that session, the simulator stayed for the life of the process — the real reader was
            // configured, reachable, and never opened, and nothing in the logs said so, because from
            // the agent's point of view every step had succeeded.
            using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            void OnProfileChanged()
            {
                _logger.LogInformation("The device profile changed; restarting the reader session");

                // Already disposed if the session ended for its own reasons first.
                try
                {
                    session.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            _profiles.Changed += OnProfileChanged;

            try
            {
                var profile = _profiles.Reader;
                _reader = CreateReader(profile);

                await _reader.ConnectAsync(profile, session.Token);
                attempt = 0;

                if (_profiles.Mode != ReaderMode.Off)
                {
                    await _reader.StartAsync(session.Token);
                }

                await foreach (var read in _reader.ReadsAsync(session.Token))
                {
                    // Local pre-filter (doc 06 §2). The server re-checks it, so this is purely about
                    // not paying for a round trip on a tag that could never be accepted.
                    //
                    // Only applied when the reader actually measured. A reader that reports no signal
                    // strength — which R2000-family units do in real-time inventory mode — would
                    // otherwise have every one of its reads discarded here, and the symptom at the
                    // till would be a reader that connects, reports healthy, and never sees a tag.
                    if (read.HasRssi && read.Rssi < profile.RssiThresholdDbm)
                    {
                        continue;
                    }

                    _buffer.Offer(read);
                }

            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // The profile changed. Not a fault, so the backoff counter is left alone — the next
                // session should start immediately rather than after a penalty this did not earn.
                attempt = 0;
            }
            catch (Exception ex)
            {
                attempt++;
                _logger.LogWarning(ex, "Reader session ended (attempt {Attempt}); reconnecting", attempt);
            }
            finally
            {
                _profiles.Changed -= OnProfileChanged;
                await SafeStopAsync();
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // Backoff caps at a minute: past that, retrying faster does not help and the logs become
            // noise that hides the actual fault.
            var delay = TimeSpan.FromSeconds(Math.Min(60, _options.ReaderRetrySeconds * Math.Max(1, attempt)));
            await Task.Delay(delay, stoppingToken);
        }

        await SafeStopAsync();
    }

    /// <summary>Applies a mode change from the server without dropping the session.</summary>
    public async Task ApplyModeAsync(ReaderMode mode, CancellationToken ct)
    {
        _profiles.SetMode(mode);

        if (_reader is null)
        {
            return;
        }

        if (mode == ReaderMode.Off)
        {
            await _reader.StopAsync(ct);
            _buffer.Clear();
            return;
        }

        await _reader.StartAsync(ct);
    }

    private IRfidReader CreateReader(ReaderProfileContract profile)
    {
        var protocol = profile.Protocol;

        // A bench or a demo forces the simulator regardless of what the store's profile says.
        if (Enum.TryParse<ReaderProtocol>(_options.ForceReaderProtocol, ignoreCase: true, out var forced))
        {
            protocol = forced;
        }

        return protocol switch
        {
            ReaderProtocol.Llrp => ActivatorUtilities.CreateInstance<LlrpRfidReader>(_services),
            ReaderProtocol.UhfSerial => ActivatorUtilities.CreateInstance<UhfSerialRfidReader>(_services),
            _ => ActivatorUtilities.CreateInstance<SimulatedRfidReader>(_services),
        };
    }

    private async Task SafeStopAsync()
    {
        if (_reader is null)
        {
            return;
        }

        try
        {
            await _reader.StopAsync(CancellationToken.None);
            await _reader.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignoring an error while closing the reader");
        }
        finally
        {
            _reader = null;
        }
    }
}
