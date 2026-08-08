using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retail25.Contracts.Terminals;

namespace Retail25.TerminalAgent.Server;

/// <summary>What the server can ask this till to do. Implemented by the peripheral coordinator.</summary>
public interface ITerminalCommandHandler
{
    Task PrintReceiptAsync(ReceiptDocument document, int copies, CancellationToken ct);

    Task OpenDrawerAsync(CancellationToken ct);

    Task DisplayPoleAsync(string line1, string line2, CancellationToken ct);

    Task RequestWeightAsync(CancellationToken ct);

    Task ZeroScaleAsync(CancellationToken ct);

    Task SetReaderModeAsync(ReaderMode mode, CancellationToken ct);

    Task UpdateProfileAsync(TerminalProfileContract profile, CancellationToken ct);
}

/// <summary>The agent's half of the link to the server.</summary>
public interface IServerConnection
{
    bool IsConnected { get; }

    Task StartAsync(ITerminalCommandHandler handler, CancellationToken ct);

    /// <summary>Publishes a batch. Returns false when the server could not be reached, so the caller spools.</summary>
    Task<bool> PublishTagsAsync(IReadOnlyList<TagRead> tags, CancellationToken ct);

    Task<bool> ReportStatusAsync(AgentStatusReport status, CancellationToken ct);

    Task<bool> ReportWeightAsync(decimal value, string unit, bool stable, CancellationToken ct);

    Task<bool> ReportPrintResultAsync(long transactionId, bool succeeded, string? error, CancellationToken ct);

    Task StopAsync(CancellationToken ct);
}

/// <summary>
/// SignalR client with backoff and jitter (doc 06 §6).
/// <para>
/// Every publish returns a boolean rather than throwing, because the caller's response to "the server
/// is not there" is to spool and carry on, not to unwind. A till whose reader stops working because
/// the network blipped is worse than one that quietly queues.
/// </para>
/// </summary>
public sealed class SignalRServerConnection : IServerConnection, IAsyncDisposable
{
    private readonly AgentOptions _options;
    private readonly AgentTokenProvider _tokens;
    private readonly ILogger<SignalRServerConnection> _logger;

    private HubConnection? _connection;
    private ITerminalCommandHandler? _handler;

    public SignalRServerConnection(
        IOptions<AgentOptions> options,
        AgentTokenProvider tokens,
        ILogger<SignalRServerConnection> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _tokens = tokens;
        _logger = logger;
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task StartAsync(ITerminalCommandHandler handler, CancellationToken ct)
    {
        _handler = handler;

        var connection = new HubConnectionBuilder()
            .WithUrl($"{_options.ApiUrl.TrimEnd('/')}/hubs/terminal", options =>
            {
                // Called again on every reconnect, which is exactly what is wanted: a till that has
                // been offline overnight gets a fresh token rather than replaying a dead one.
                options.AccessTokenProvider = () => _tokens.GetAsync();
            })
            .WithAutomaticReconnect(new JitteredRetryPolicy())
            .Build();

        Bind(connection);

        connection.Reconnected += async _ =>
        {
            _logger.LogInformation("Reconnected to the server");
            await RegisterAsync(connection, CancellationToken.None);
        };

        connection.Closed += error =>
        {
            _logger.LogWarning("Server connection closed{Reason}", error is null ? string.Empty : $": {error.Message}");
            _logger.LogDebug(error, "Hub connection closed");
            return Task.CompletedTask;
        };

        _connection = connection;

        // A till must start even when the server is down: it keeps its local API alive, keeps the
        // reader running and spools. The reconnect loop takes over from here.
        try
        {
            await connection.StartAsync(ct);
            await RegisterAsync(connection, ct);
            _logger.LogInformation("Connected to {ApiUrl} as station {StationId}", _options.ApiUrl, _options.StationId);
        }
        catch (Exception ex)
        {
            // One line, not a stack trace: a till starting before the server is up is routine, and
            // the reconnect loop is about to make this a non-event.
            _logger.LogWarning("Could not reach {ApiUrl} at startup ({Reason}); will keep retrying", _options.ApiUrl, ex.Message);
            _logger.LogDebug(ex, "Initial hub connection failed");
        }
    }

    private void Bind(HubConnection connection)
    {
        connection.On<ReceiptDocument, int>(TerminalHubMethods.ToAgent.PrintReceipt, async (document, copies) =>
        {
            if (_handler is null)
            {
                return;
            }

            try
            {
                await _handler.PrintReceiptAsync(document, copies, CancellationToken.None);
                await ReportPrintResultAsync(document.TransactionId, true, null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The sale is already saved; a print failure is reported and stays reprintable.
                _logger.LogError(ex, "Printing transaction {TransactionId} failed", document.TransactionId);
                await ReportPrintResultAsync(document.TransactionId, false, ex.Message, CancellationToken.None);
            }
        });

        connection.On(TerminalHubMethods.ToAgent.OpenDrawer, async () =>
            await Guard(() => _handler!.OpenDrawerAsync(CancellationToken.None), "opening the drawer"));

        connection.On<string, string>(TerminalHubMethods.ToAgent.DisplayPole, async (line1, line2) =>
            await Guard(() => _handler!.DisplayPoleAsync(line1, line2, CancellationToken.None), "updating the pole display"));

        connection.On(TerminalHubMethods.ToAgent.RequestWeight, async () =>
            await Guard(() => _handler!.RequestWeightAsync(CancellationToken.None), "reading the scale"));

        connection.On(TerminalHubMethods.ToAgent.ZeroScale, async () =>
            await Guard(() => _handler!.ZeroScaleAsync(CancellationToken.None), "zeroing the scale"));

        connection.On<string>(TerminalHubMethods.ToAgent.SetReaderMode, async mode =>
        {
            if (Enum.TryParse<ReaderMode>(mode, ignoreCase: true, out var parsed))
            {
                await Guard(() => _handler!.SetReaderModeAsync(parsed, CancellationToken.None), "changing reader mode");
            }
        });

        connection.On<TerminalProfileContract>(TerminalHubMethods.ToAgent.UpdateProfile, async profile =>
            await Guard(() => _handler!.UpdateProfileAsync(profile, CancellationToken.None), "applying a new profile"));
    }

    private Task RegisterAsync(HubConnection connection, CancellationToken ct)
        => connection.InvokeAsync(TerminalHubMethods.ToServer.RegisterStation, _options.StationId, AgentVersion.Current, ct);

    public Task<bool> PublishTagsAsync(IReadOnlyList<TagRead> tags, CancellationToken ct)
        => TryInvokeAsync(TerminalHubMethods.ToServer.PublishTags, [_options.StationId, tags], ct);

    public Task<bool> ReportStatusAsync(AgentStatusReport status, CancellationToken ct)
        => TryInvokeAsync(
            TerminalHubMethods.ToServer.ReportStatus,
            [
                status.StationId,
                status.AgentVersion,
                status.ReaderOnline,
                status.PrinterOnline,
                status.ScaleOnline,
                status.DrawerOnline,
                status.PoleDisplayOnline,
                status.ReadRate,
            ],
            ct);

    public Task<bool> ReportWeightAsync(decimal value, string unit, bool stable, CancellationToken ct)
        => TryInvokeAsync(TerminalHubMethods.ToServer.ReportWeight, [_options.StationId, value, unit, stable], ct);

    public Task<bool> ReportPrintResultAsync(long transactionId, bool succeeded, string? error, CancellationToken ct)
        => TryInvokeAsync(
            TerminalHubMethods.ToServer.ReportPrintResult,
            [_options.StationId, transactionId, succeeded, error],
            ct);

    private async Task<bool> TryInvokeAsync(string method, object?[] args, CancellationToken ct)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            return false;
        }

        try
        {
            await _connection.InvokeCoreAsync(method, args, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Server call {Method} failed; the caller will spool or retry", method);
            return false;
        }
    }

    private async Task Guard(Func<Task> action, string what)
    {
        if (_handler is null)
        {
            return;
        }

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The server asked this till for {What} and it failed", what);
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_connection is not null)
        {
            await _connection.StopAsync(ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    /// <summary>
    /// Backoff with jitter. Without the jitter, a shop full of tills that all lost the server at the
    /// same instant reconnect in lockstep and hammer it at the exact moment it is coming back up.
    /// </summary>
    private sealed class JitteredRetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Schedule =
        [
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
        ];

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            ArgumentNullException.ThrowIfNull(retryContext);

            var attempt = (long)retryContext.PreviousRetryCount;

            var baseDelay = attempt >= Schedule.Length
                ? TimeSpan.FromSeconds(20)
                : Schedule[(int)attempt];

            return baseDelay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
        }
    }
}

/// <summary>The agent's own version, reported on every heartbeat and used by the auto-update check.</summary>
public static class AgentVersion
{
    public static string Current { get; } =
        typeof(AgentVersion).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
