using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retail25.Contracts.Terminals;
using Retail25.Devices.Rfid;
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
        0L,
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
    private readonly ReaderDiscovery _discovery;
    private readonly ILogger<RfidReaderService> _logger;

    private IRfidReader? _reader;

    /// <summary>
    /// The address last found by searching, kept so a reader that moved is not hunted for again on
    /// every reconnect. Cleared implicitly by being overwritten when the search runs afresh.
    /// </summary>
    private string? _discovered;

    /// <summary>Rotates the serial port tried when the network search finds nothing.</summary>
    private int _serialAttempt;

    public RfidReaderService(
        IServiceProvider services,
        ProfileStore profiles,
        TagBuffer buffer,
        IOptions<AgentOptions> options,
        ReaderDiscovery discovery,
        ILogger<RfidReaderService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _services = services;
        _profiles = profiles;
        _buffer = buffer;
        _options = options.Value;
        _discovery = discovery;
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
                var profile = await LocateAsync(_profiles.Reader, session.Token);
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
    /// <summary>
    /// What the attached reader reports about itself. Answers even with no reader connected, so a
    /// settings screen on a misconfigured till says so rather than hanging.
    /// </summary>
    public Task<ReaderDiagnostics> ReadDiagnosticsAsync(CancellationToken ct)
        => _reader is null
            ? Task.FromResult(new ReaderDiagnostics { Unavailable = ["no reader is connected to this station"] })
            : _reader.ReadDiagnosticsAsync(ct);

    /// <summary>
    /// Pushes the current profile into the device again, returning what it would not take.
    /// <para>
    /// The profile comes from <see cref="ProfileStore"/> rather than from the caller: the settings
    /// are the server's to decide, and an endpoint that accepted them from the browser would be a way
    /// to set a till's transmit power without passing through any permission check.
    /// </para>
    /// </summary>
    public Task<IReadOnlyList<string>> ApplySettingsAsync(CancellationToken ct)
        => _reader is null
            ? Task.FromResult<IReadOnlyList<string>>(["no reader is connected to this station"])
            : _reader.ApplySettingsAsync(_profiles.Reader, ct);

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

    /// <summary>
    /// Returns the profile with its host replaced by wherever the reader actually is.
    ///
    /// <para>
    /// Only network protocols are searched for. A simulator has nothing to find, and moving its
    /// loopback address to some other machine on the shop's network would be actively wrong.
    /// </para>
    /// <para>
    /// The address the search last settled on is preferred over the configured one, because after a
    /// DHCP change the configured one is the stale value and re-probing it first costs a timeout on
    /// every reconnect. The configured value is still tried if the remembered address stops
    /// answering — a reader put back on its old lease is found without a restart.
    /// </para>
    /// </summary>
    private async Task<ReaderProfileContract> LocateAsync(ReaderProfileContract profile, CancellationToken ct)
    {
        if (EffectiveProtocol(profile) is not (ReaderProtocol.UhfSerial or ReaderProtocol.Llrp))
        {
            return profile;
        }

        var preferred = _discovered ?? profile.Host;

        // A lead plugged into this machine wins, and is not searched for over the network.
        //
        // Asked first because it is the cheaper and the more certain answer: a COM port either
        // exists on this machine or it does not, while a network sweep takes seconds and can find
        // somebody else's reader on a shared shop network.
        if (SerialReaderTransport.IsSerialPort(preferred))
        {
            return profile with { Host = preferred };
        }

        var host = await _discovery.FindAsync(preferred, profile.Port, ct);

        if (host is not null)
        {
            _discovered = host;

            if (!string.Equals(host, profile.Host, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Reader {Name} answered on {Found} rather than the configured {Configured}; using {Found}",
                    profile.Name,
                    host,
                    profile.Host,
                    host);
            }

            return profile with { Host = host };
        }

        // Nothing on the network. Before giving up, look at what is plugged in — a reader on a USB
        // lead is the ordinary case for a single till, and it is invisible to an address sweep.
        //
        // The ports are re-read every time rather than remembered, because the whole point is to
        // notice a reader somebody has just connected. The session loop retries on a backoff, so a
        // lead plugged in while the till is open is picked up within the minute, with nobody
        // restarting anything.
        var ports = SerialReaderTransport.AvailablePorts();

        if (ports.Count > 0)
        {
            // A different port each attempt, so a machine with a modem on COM1 and the reader on
            // COM7 reaches the reader on the second try rather than failing on the first for ever.
            var chosen = ports[_serialAttempt++ % ports.Count];

            _logger.LogInformation(
                "No reader answered on the network; trying the serial port {Port} ({Count} available)",
                chosen,
                ports.Count);

            // Deliberately not cached: caching would pin the choice to whichever port happened to
            // be first, and a lead moved to another socket would never be found again.
            return profile with { Host = chosen };
        }

        // Nothing anywhere. Hand back what was configured so the connection attempt — and the error
        // it raises — names the address the operator set, which is the one they can act on.
        return profile;
    }

    /// <summary>
    /// The protocol actually used, which is the profile's unless this machine overrides it.
    /// <para>
    /// Shared with <see cref="LocateAsync"/> so the override cannot mean one thing when deciding
    /// whether to search for the reader and another when deciding how to talk to it — forcing
    /// UhfSerial on a bench would otherwise skip the search, because the profile still said
    /// Simulator.
    /// </para>
    /// </summary>
    private ReaderProtocol EffectiveProtocol(ReaderProfileContract profile) =>
        // A bench or a demo forces the simulator regardless of what the store's profile says.
        Enum.TryParse<ReaderProtocol>(_options.ForceReaderProtocol, ignoreCase: true, out var forced)
            ? forced
            : profile.Protocol;

    private IRfidReader CreateReader(ReaderProfileContract profile)
    {
        return EffectiveProtocol(profile) switch
        {
            ReaderProtocol.Llrp => ActivatorUtilities.CreateInstance<LlrpRfidReader>(_services),

            // Given the agent's own opener, which understands a COM port as well as a socket. The
            // driver is shared with the API, and the API can never open a lead plugged into a till.
            ReaderProtocol.UhfSerial => ActivatorUtilities.CreateInstance<UhfSerialRfidReader>(
                _services,
                (ReaderConnectionOpener)SerialReaderTransport.OpenAsync),
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
