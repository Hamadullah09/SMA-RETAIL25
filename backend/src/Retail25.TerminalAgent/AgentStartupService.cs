using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retail25.TerminalAgent.Peripherals;
using Retail25.TerminalAgent.Rfid;
using Retail25.TerminalAgent.Server;

namespace Retail25.TerminalAgent;

/// <summary>Where the agent keeps its spool and its logs.</summary>
public static class AgentPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Retail25",
        "TerminalAgent");
}

/// <summary>
/// Wires the pieces together once everything is constructed: connects to the server, applies whatever
/// profile is available, and routes the server's commands to the right service.
/// <para>
/// It runs as a hosted service rather than at composition time because connecting to a server that
/// may be down must not stop the agent starting. A till whose server is unreachable still needs its
/// local API for the scale, its reader running, and its spool collecting.
/// </para>
/// </summary>
public sealed class AgentStartupService : IHostedService
{
    private readonly IServerConnection _server;
    private readonly PeripheralCoordinator _peripherals;
    private readonly RfidReaderService _reader;
    private readonly ProfileStore _profiles;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentStartupService> _logger;

    public AgentStartupService(
        IServerConnection server,
        PeripheralCoordinator peripherals,
        RfidReaderService reader,
        ProfileStore profiles,
        IOptions<AgentOptions> options,
        ILogger<AgentStartupService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _server = server;
        _peripherals = peripherals;
        _reader = reader;
        _profiles = profiles;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Retail25 terminal agent {Version} starting for station {StationId}",
            AgentVersion.Current,
            _options.StationId);

        _peripherals.OnWeightRead((value, unit, stable, ct) => _server.ReportWeightAsync(value, unit, stable, ct));
        _peripherals.OnReaderModeChanged((mode, ct) => _reader.ApplyModeAsync(mode, ct));
        _peripherals.OnProfileChanged((profile, _) =>
        {
            _profiles.Set(profile);
            return Task.CompletedTask;
        });

        await _server.StartAsync(_peripherals, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _server.StopAsync(cancellationToken);
        _logger.LogInformation("Retail25 terminal agent stopped");
    }
}
