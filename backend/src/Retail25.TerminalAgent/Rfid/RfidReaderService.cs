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

    public void Set(TerminalProfileContract profile)
    {
        Volatile.Write(ref _profile, profile);
        Mode = profile.ReaderMode;
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
            try
            {
                var profile = _profiles.Reader;
                _reader = CreateReader(profile);

                await _reader.ConnectAsync(profile, stoppingToken);
                attempt = 0;

                if (_profiles.Mode != ReaderMode.Off)
                {
                    await _reader.StartAsync(stoppingToken);
                }

                await foreach (var read in _reader.ReadsAsync(stoppingToken))
                {
                    // Local pre-filter (doc 06 §2). The server re-checks it, so this is purely about
                    // not paying for a round trip on a tag that could never be accepted.
                    if (read.Rssi < profile.RssiThresholdDbm)
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
            catch (Exception ex)
            {
                attempt++;
                _logger.LogWarning(ex, "Reader session ended (attempt {Attempt}); reconnecting", attempt);
            }
            finally
            {
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
