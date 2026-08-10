using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Retail25.Application.Rfid.Commands;
using Retail25.Contracts.Terminals;
using Retail25.Devices.Rfid;

namespace Retail25.Infrastructure.Rfid;

/// <summary>
/// One reader, one connection, one loop.
/// <para>
/// Separate from <see cref="ServerReaderHost"/> because the interesting failure is per reader. Ten
/// lanes share a host but not a fate: an unplugged antenna on lane three must cost lane three and
/// nothing else, and that is only true if each reader owns its own connection, its own retry clock
/// and its own cancellation.
/// </para>
/// </summary>
internal sealed class ReaderSession : IDisposable
{
    private readonly ReaderProfileContract _profile;
    private readonly long _stationId;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly TimeSpan _reconnectDelay;
    private readonly CancellationTokenSource _cts = new();

    private Task? _loop;

    public ReaderSession(
        ReaderProfileContract profile,
        long stationId,
        long revision,
        IServiceScopeFactory scopes,
        ILoggerFactory loggerFactory,
        TimeSpan reconnectDelay)
    {
        _profile = profile;
        _stationId = stationId;
        Revision = revision;
        _scopes = scopes;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger($"Retail25.Infrastructure.Rfid.Reader.{profile.Name}");
        _reconnectDelay = reconnectDelay;
    }

    /// <summary>The profile version this session was built from. See ServerReaderHost.Revision.</summary>
    public long Revision { get; }

    public string Description => $"{_profile.Name} ({_profile.Host}:{_profile.Port})";

    public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);

    public async Task StopAsync()
    {
        await _cts.CancelAsync();

        if (_loop is not null)
        {
            // Faults are already logged inside the loop; this await is only to let the reader close
            // its socket before the process moves on.
            try
            {
                await _loop;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Reader session ended with a fault while stopping");
            }
        }

        _cts.Dispose();
    }

    /// <summary>
    /// Idempotent, and the reason this type is disposable at all: the token source outlives the loop
    /// it cancels. <see cref="StopAsync"/> is the ordinary path and disposes on its way out; this
    /// exists so a session abandoned without being stopped still releases it.
    /// </summary>
    public void Dispose() => _cts.Dispose();

    private async Task RunAsync(CancellationToken ct)
    {
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            IRfidReader? reader = null;

            try
            {
                reader = Create();

                await reader.ConnectAsync(_profile, ct);
                await reader.StartAsync(ct);

                attempt = 0;
                _logger.LogInformation("Reading tags from {Reader} for station {Station}", Description, _stationId);

                await foreach (var read in reader.ReadsAsync(ct))
                {
                    await IngestAsync(read, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                attempt++;

                // Logged as a warning, not an error, and only with the reader's name. A reader being
                // unreachable is an ordinary condition in a shop — somebody unplugged it, or the
                // bridge rebooted — and an error-level line per retry would bury the faults that do
                // need somebody's attention.
                _logger.LogWarning(
                    "Reader {Reader} is not answering (attempt {Attempt}): {Message}",
                    Description,
                    attempt,
                    ex.Message);
            }
            finally
            {
                if (reader is not null)
                {
                    await reader.DisposeAsync();
                }
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(_reconnectDelay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Hands one read to the same command the agent posts to.
    /// <para>
    /// A scope per read rather than a long-lived one: the handler resolves a DbContext, and holding
    /// one open for the life of a reader session would mean a connection pinned per lane for as long
    /// as the shop is open, and a change-tracker that grows all day.
    /// </para>
    /// </summary>
    private async Task IngestAsync(TagRead read, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            await sender.Send(new IngestTagReadsCommand(_stationId, [read]), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallowed on purpose. A tag that cannot be ingested must not break the read loop: the
            // next tag is a fresh chance, and a reader that stops reading because one EPC upset the
            // handler is a till that stops working for reasons nobody at the counter can see.
            _logger.LogError(ex, "Could not ingest a tag read from {Reader}", Description);
        }
    }

    private IRfidReader Create() => _profile.Protocol switch
    {
        ReaderProtocol.UhfSerial => new UhfSerialRfidReader(_loggerFactory.CreateLogger<UhfSerialRfidReader>()),
        ReaderProtocol.Llrp => new LlrpRfidReader(_loggerFactory.CreateLogger<LlrpRfidReader>()),
        _ => new SimulatedRfidReader(_loggerFactory.CreateLogger<SimulatedRfidReader>()),
    };
}
