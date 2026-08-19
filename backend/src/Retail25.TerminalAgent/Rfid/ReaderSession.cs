using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Retail25.Contracts.Terminals;
using Retail25.Devices.Rfid;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// One reader, connected and kept connected.
/// <para>
/// Extracted from the service that used to hold a single reader in a field, because that field was
/// the last thing making one machine mean one reader. A session owns its own connection, its own
/// discovery state and its own backoff, so a machine can run several and one dropping cannot take
/// the others with it — which is the property the whole multi-reader arrangement rests on.
/// </para>
/// <para>
/// The reader it drives is identified by <see cref="ReaderId"/>, which travels with every read into
/// the buffer. Zero means a reader the server has not registered: an agent still running the
/// per-station profile, whose reads go out addressed by station as they always did.
/// </para>
/// </summary>
public sealed class ReaderSession : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly TagBuffer _buffer;
    private readonly ReaderDiscovery _discovery;
    private readonly AgentOptions _options;
    private readonly ILogger _logger;
    private readonly Func<ReaderProfileContract> _profile;
    private readonly Func<ReaderMode> _mode;

    private IRfidReader? _reader;

    /// <summary>Where the reader was last found, so a moved address is not re-swept every attempt.</summary>
    private string? _discovered;

    private int _serialAttempt;

    public ReaderSession(
        long readerId,
        Func<ReaderProfileContract> profile,
        Func<ReaderMode> mode,
        IServiceProvider services,
        TagBuffer buffer,
        ReaderDiscovery discovery,
        AgentOptions options,
        ILogger logger)
    {
        ReaderId = readerId;
        _profile = profile;
        _mode = mode;
        _services = services;
        _buffer = buffer;
        _discovery = discovery;
        _options = options;
        _logger = logger;
    }

    /// <summary>The server's id for this reader. Zero for an agent on the per-station profile.</summary>
    public long ReaderId { get; }

    public bool IsConnected => _reader?.IsConnected == true;

    public string Description => _reader?.Description ?? "none";

    /// <summary>
    /// Connects, reads, and reconnects until cancelled.
    /// <para>
    /// Every failure is contained here. A reader that will not answer costs this session its backoff
    /// and nothing else — the sibling sessions on the same machine keep reading, which is exactly
    /// what one shared loop could not do.
    /// </para>
    /// </summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var profile = await LocateAsync(_profile(), stoppingToken);
                _reader = CreateReader(profile);

                await _reader.ConnectAsync(profile, stoppingToken);
                attempt = 0;

                if (_mode() != ReaderMode.Off)
                {
                    await _reader.StartAsync(stoppingToken);
                }

                await foreach (var read in _reader.ReadsAsync(stoppingToken))
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

                    _buffer.Offer(ReaderId, read);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // The configuration changed under this session. Not a fault, so the backoff counter
                // is left alone — the next attempt should start immediately rather than after a
                // penalty it did not earn.
                attempt = 0;
            }
            catch (Exception ex)
            {
                attempt++;

                _logger.LogWarning(
                    ex,
                    "Reader {ReaderId} session ended (attempt {Attempt}); reconnecting",
                    ReaderId,
                    attempt);
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

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await SafeStopAsync();
    }

    /// <summary>
    /// What the attached reader reports about itself. Answers even with nothing connected, so a
    /// settings screen on a misconfigured till says so rather than hanging.
    /// </summary>
    public Task<ReaderDiagnostics> ReadDiagnosticsAsync(CancellationToken ct)
        => _reader is null
            ? Task.FromResult(new ReaderDiagnostics { Unavailable = ["no reader is connected to this station"] })
            : _reader.ReadDiagnosticsAsync(ct);

    public Task<IReadOnlyList<string>> ApplySettingsAsync(CancellationToken ct)
        => _reader is null
            ? Task.FromResult<IReadOnlyList<string>>(["no reader is connected to this station"])
            : _reader.ApplySettingsAsync(_profile(), ct);

    public async Task ApplyModeAsync(ReaderMode mode, CancellationToken ct)
    {
        if (_reader is null)
        {
            return;
        }

        if (mode == ReaderMode.Off)
        {
            await _reader.StopAsync(ct);
            return;
        }

        await _reader.StartAsync(ct);
    }

    public async ValueTask DisposeAsync() => await SafeStopAsync();

    /// <summary>
    /// Returns the profile with its host replaced by wherever the reader actually is.
    /// <para>
    /// Only network protocols are searched for. A simulator has nothing to find, and sweeping for
    /// one would cost seconds on every attempt for no possible answer.
    /// </para>
    /// </summary>
    private async Task<ReaderProfileContract> LocateAsync(ReaderProfileContract profile, CancellationToken ct)
    {
        if (EffectiveProtocol(profile) == ReaderProtocol.Simulator)
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
        // notice a reader somebody has just connected.
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
    /// whether to search for the reader and another when deciding how to talk to it.
    /// </para>
    /// </summary>
    private ReaderProtocol EffectiveProtocol(ReaderProfileContract profile) =>
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
            _logger.LogDebug(ex, "Ignoring an error while closing reader {ReaderId}", ReaderId);
        }
        finally
        {
            _reader = null;
        }
    }
}
