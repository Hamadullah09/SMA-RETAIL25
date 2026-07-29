using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retail25.Contracts.Terminals;
using Retail25.TerminalAgent.Peripherals;
using Retail25.TerminalAgent.Rfid;

namespace Retail25.TerminalAgent.Server;

/// <summary>
/// Pulls this till's device profile from the server on start, then keeps it fresh (doc 06 §7).
/// <para>
/// The server also pushes changes over the hub, so this poll is the safety net rather than the
/// mechanism: an agent that was offline when an administrator changed a printer would otherwise keep
/// the old settings until someone restarted it, which is the site visit the design exists to avoid.
/// </para>
/// </summary>
public sealed class ProfileRefreshService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProfileStore _profiles;
    private readonly PeripheralCoordinator _peripherals;
    private readonly AgentOptions _options;
    private readonly ILogger<ProfileRefreshService> _logger;

    public ProfileRefreshService(
        IHttpClientFactory httpClientFactory,
        ProfileStore profiles,
        PeripheralCoordinator peripherals,
        IOptions<AgentOptions> options,
        ILogger<ProfileRefreshService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _httpClientFactory = httpClientFactory;
        _profiles = profiles;
        _peripherals = peripherals;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await TryRefreshAsync(stoppingToken);
            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task TryRefreshAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("server");
            var url = $"api/v1/terminals/{_options.StationId}/profile";

            var profile = await client.GetFromJsonAsync<TerminalProfileContract>(url, SerializerOptions, ct);
            if (profile is null)
            {
                return;
            }

            var current = _profiles.Current;
            if (current is not null && current == profile)
            {
                return;
            }

            _profiles.Set(profile);
            await _peripherals.ApplyProfileAsync(profile, ct);

            _logger.LogInformation("Device profile refreshed for station {StationCode}", profile.StationCode);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // The agent keeps whatever profile it already had; unreachable is not the same as changed.
            //
            // An unreachable server is an expected, recurring condition for a till, so it gets one
            // line. The stack trace goes to Debug: a shop that has been offline overnight would
            // otherwise wake up to a log file made entirely of identical socket exceptions, and the
            // one genuinely different error in it would be invisible.
            _logger.LogWarning("Could not refresh the device profile ({Reason}); keeping the current one", ex.Message);
            _logger.LogDebug(ex, "Profile refresh failed");
        }
    }
}
