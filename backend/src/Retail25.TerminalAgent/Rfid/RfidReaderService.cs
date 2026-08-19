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
/// Supervises this machine's reader sessions (doc 06 §3).
/// <para>
/// It used to hold one reader in a field and one loop in <c>ExecuteAsync</c>, which is what made a
/// machine mean a reader. It now owns a set of <see cref="ReaderSession"/>s and does nothing itself
/// except decide which should exist: each session connects, reads and reconnects on its own backoff,
/// so one reader failing costs that reader and no other.
/// </para>
/// <para>
/// A reader that faults is reconnected forever. That is deliberate: the most common real failure is
/// a switch rebooting or a reader power-cycling overnight, and the correct behaviour in both cases is
/// to come back unaided rather than to need somebody to restart a service before the shop can open.
/// </para>
/// </summary>
public sealed class RfidReaderService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ProfileStore _profiles;
    private readonly TagBuffer _buffer;
    private readonly AgentOptions _options;
    private readonly ReaderDiscovery _discovery;
    private readonly DeviceConfigurationStore _devices;
    private readonly ILogger<RfidReaderService> _logger;

    private readonly Dictionary<long, RunningSession> _sessions = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RfidReaderService(
        IServiceProvider services,
        ProfileStore profiles,
        TagBuffer buffer,
        IOptions<AgentOptions> options,
        ReaderDiscovery discovery,
        DeviceConfigurationStore devices,
        ILogger<RfidReaderService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _services = services;
        _profiles = profiles;
        _buffer = buffer;
        _options = options.Value;
        _discovery = discovery;
        _devices = devices;
        _logger = logger;
    }

    /// <summary>
    /// True while any reader is connected. Drives the red strip on the live feed.
    /// <para>
    /// Any rather than all, because the strip answers "can this till read a tag" and one working
    /// reader means yes. Which readers are down is a question for the dashboard, where there is room
    /// to say so per reader.
    /// </para>
    /// </summary>
    public bool ReaderOnline => _sessions.Values.Any(s => s.Session.IsConnected);

    public string ReaderDescription
    {
        get
        {
            var connected = _sessions.Values
                .Where(s => s.Session.IsConnected)
                .Select(s => s.Session.Description)
                .ToList();

            return connected.Count switch
            {
                0 => "none",
                1 => connected[0],
                _ => $"{connected.Count} readers: {string.Join(", ", connected)}",
            };
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fire-and-forget, because the event is raised from whichever thread received the profile and
        // must not block it — but never unobserved. An exception thrown into a discarded task is a
        // reconciliation that silently did not happen, which presents as a reader that never comes
        // back after a settings change and leaves nothing behind to explain why.
        void OnProfileChanged() => _ = ReconcileSafelyAsync(stoppingToken);

        _profiles.Changed += OnProfileChanged;

        // The device configuration is the other thing that can change which readers exist, and it
        // changes far more often than the station profile once an estate is being commissioned.
        _devices.Changed += OnProfileChanged;

        try
        {
            await ReconcileAsync(stoppingToken);

            // The supervisor itself does nothing while the shop trades. Every reader is a session
            // running on its own task, and the only reason to wake up here is a configuration change,
            // which arrives as an event rather than as a poll.
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            _profiles.Changed -= OnProfileChanged;
            _devices.Changed -= OnProfileChanged;
            await StopAllAsync();
        }
    }

    /// <summary>
    /// Starts, stops and leaves alone, so that a change to one reader does not disturb the others.
    /// <para>
    /// The reconciliation is the point. Restarting every session whenever anything changed would mean
    /// re-pointing antenna 2 of one reader dropped tags on the other three readers of the same
    /// machine — which is precisely the coupling this design exists to remove.
    /// </para>
    /// </summary>
    private async Task ReconcileSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ReconcileAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down mid-reconcile.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not apply the new reader configuration; the previous one is still running");
        }
    }

    private async Task ReconcileAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        await _gate.WaitAsync(stoppingToken);

        try
        {
            var wanted = DesiredReaders();

            foreach (var readerId in _sessions.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
            {
                _logger.LogInformation("Reader {ReaderId} is no longer assigned to this machine; stopping it", readerId);
                await StopAsync(readerId);
            }

            foreach (var (readerId, profile) in wanted)
            {
                if (_sessions.TryGetValue(readerId, out var running))
                {
                    // Already running. Restarted only if its own settings moved: a session whose
                    // reader is unchanged keeps its connection, and keeps reading.
                    if (running.Profile == profile)
                    {
                        continue;
                    }

                    _logger.LogInformation("Reader {ReaderId} settings changed; restarting its session", readerId);
                    await StopAsync(readerId);
                }

                Start(readerId, profile, stoppingToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Which readers this machine should be driving.
    /// <para>
    /// The device configuration when the server has sent one, and the per-station profile when it has
    /// not. This is the only method that decides how many readers exist, which is why one PC driving
    /// twelve of them is this method returning twelve entries and no other change anywhere.
    /// </para>
    /// <para>
    /// The fallback is not a nicety. An agent the server has not registered still has a till to serve,
    /// and a machine that read nothing until somebody had created a device row would make the upgrade
    /// an outage.
    /// </para>
    /// </summary>
    private Dictionary<long, ReaderProfileContract> DesiredReaders()
    {
        var configuration = _devices.Current;

        if (configuration is null || configuration.Readers.Count == 0)
        {
            return new Dictionary<long, ReaderProfileContract> { [0] = _profiles.Reader };
        }

        return configuration.Readers.ToDictionary(r => r.ReaderId, ToProfile);
    }

    /// <summary>
    /// A managed reader as the drivers expect to receive it.
    /// <para>
    /// The reader's own tuning is carried through when the server sent it, because power, region and
    /// debounce are the reader's settings and a machine driving three readers may well have three
    /// different ones. Where it is absent the station profile supplies the thresholds — its address
    /// and protocol are still overridden, since those belong to the reader rather than the till.
    /// </para>
    /// </summary>
    private ReaderProfileContract ToProfile(ManagedReaderContract reader)
    {
        var basis = reader.Settings ?? _profiles.Reader;

        return basis with
        {
            Id = reader.ReaderId,
            Name = reader.ReaderKey,
            Host = reader.Host,
            Port = reader.Port,
            Protocol = Enum.TryParse<ReaderProtocol>(reader.Protocol, ignoreCase: true, out var parsed)
                ? parsed
                : basis.Protocol,
        };
    }

    private void Start(long readerId, ReaderProfileContract profile, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        // Its own profile, captured for this session. Handing every session the shared station
        // profile would point all of a machine.s readers at one address -- the single-reader
        // assumption reappearing one layer down.
        var session = new ReaderSession(
            readerId,
            () => profile,
            () => _profiles.Mode,
            _services,
            _buffer,
            _discovery,
            _options,
            _logger);

        // Not awaited: each session runs for the life of the agent, and awaiting one would mean the
        // second reader never started.
        var runner = Task.Run(() => session.RunAsync(cts.Token), CancellationToken.None);

        _sessions[readerId] = new RunningSession(session, cts, runner, profile);
    }

    private async Task StopAsync(long readerId)
    {
        if (!_sessions.Remove(readerId, out var running))
        {
            return;
        }

        await running.StopAsync(_logger);
    }

    private async Task StopAllAsync()
    {
        foreach (var readerId in _sessions.Keys.ToList())
        {
            await StopAsync(readerId);
        }
    }

    /// <summary>The reader this control targets, or the only one when a caller did not say.</summary>
    private ReaderSession? SessionFor(long? readerId = null)
        => readerId is { } id
            ? _sessions.TryGetValue(id, out var found) ? found.Session : null
            : _sessions.Values.FirstOrDefault()?.Session;

    /// <summary>
    /// What the attached reader reports about itself. Answers even with no reader connected, so a
    /// settings screen on a misconfigured till says so rather than hanging.
    /// </summary>
    public Task<ReaderDiagnostics> ReadDiagnosticsAsync(CancellationToken ct)
        => SessionFor() is { } session
            ? session.ReadDiagnosticsAsync(ct)
            : Task.FromResult(new ReaderDiagnostics { Unavailable = ["no reader is connected to this station"] });

    /// <summary>
    /// Pushes the current profile into the device again, returning what it would not take.
    /// <para>
    /// The profile comes from <see cref="ProfileStore"/> rather than from the caller: the settings
    /// are the server's to decide, and an endpoint that accepted them from the browser would be a way
    /// to set a till's transmit power without passing through any permission check.
    /// </para>
    /// </summary>
    public Task<IReadOnlyList<string>> ApplySettingsAsync(CancellationToken ct)
        => SessionFor() is { } session
            ? session.ApplySettingsAsync(ct)
            : Task.FromResult<IReadOnlyList<string>>(["no reader is connected to this station"]);

    /// <summary>Applies a mode change to every reader this machine drives.</summary>
    public async Task ApplyModeAsync(ReaderMode mode, CancellationToken ct)
    {
        _profiles.SetMode(mode);

        if (mode == ReaderMode.Off)
        {
            _buffer.Clear();
        }

        foreach (var running in _sessions.Values.ToList())
        {
            await running.Session.ApplyModeAsync(mode, ct);
        }
    }

    private sealed record RunningSession(
        ReaderSession Session,
        CancellationTokenSource Cts,
        Task Runner,
        ReaderProfileContract Profile)
    {
        public async Task StopAsync(ILogger logger)
        {
            try
            {
                await Cts.CancelAsync();

                // Bounded: a driver blocked in a synchronous read cannot be interrupted, and waiting
                // on it for ever would stop the agent shutting down. The session disposes its reader
                // in its own finally either way.
                await Runner.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            catch (TimeoutException)
            {
                logger.LogWarning("Reader {ReaderId} did not stop within five seconds", Session.ReaderId);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Ignoring an error while stopping reader {ReaderId}", Session.ReaderId);
            }
            finally
            {
                await Session.DisposeAsync();
                Cts.Dispose();
            }
        }
    }
}
