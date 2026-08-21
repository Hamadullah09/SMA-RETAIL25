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
    private readonly Rfid.DeviceConfigurationStore _devices;
    private readonly AgentOptions _options;
    private readonly ILogger<ProfileRefreshService> _logger;

    public ProfileRefreshService(
        IHttpClientFactory httpClientFactory,
        ProfileStore profiles,
        PeripheralCoordinator peripherals,
        Rfid.DeviceConfigurationStore devices,
        IOptions<AgentOptions> options,
        ILogger<ProfileRefreshService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _httpClientFactory = httpClientFactory;
        _profiles = profiles;
        _peripherals = peripherals;
        _devices = devices;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// How long to wait before trying again while the agent has no profile at all.
    /// <para>
    /// Five minutes is the right pace for noticing that a setting changed. It is the wrong pace for
    /// a till that has never had a profile, because until one arrives the agent runs the simulator —
    /// and a simulator reports itself online and reading while finding nothing, so the screen says
    /// the reader is working and no tag ever appears.
    /// </para>
    /// <para>
    /// That is not hypothetical. A till rebooted, the agent started before the network did, its
    /// first fetch failed on DNS, and it sat on the simulator for the next five minutes with a
    /// healthy-looking reader panel. Nine seconds after the failure the server was reachable again.
    /// </para>
    /// </summary>
    private static readonly TimeSpan RetryWhileUnconfigured = TimeSpan.FromSeconds(10);

    /// <summary>The status last logged for a refused configuration fetch, so it is said once.</summary>
    private int _deviceFetchFailed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await TryRefreshAsync(stoppingToken);
            await TryRefreshDeviceAsync(stoppingToken);

            // A till that has a profile can wait; one that has none cannot. Boot-order races are the
            // common case here and they clear in seconds, so this is a short wait rather than a
            // backoff — there is nothing to be gentle about when the alternative is a shop with a
            // reader that looks fine and reads nothing.
            var wait = _profiles.Current is null ? RetryWhileUnconfigured : PollInterval;

            await Task.Delay(wait, stoppingToken);
        }
    }

    /// <summary>
    /// Fetches what this machine should be driving: its readers, and what each antenna stands for.
    /// <para>
    /// Separate from the station profile above because they answer different questions and fail
    /// independently. A machine may be registered with readers while its station profile is missing,
    /// or the reverse, and collapsing them would make either failure look like both.
    /// </para>
    /// <para>
    /// A 404 is a real answer rather than an error: it means the server does not know this machine,
    /// so the configuration is cleared and the agent falls back to the per-station profile. Carrying
    /// on with a configuration that has been revoked would leave a machine driving readers it no
    /// longer owns.
    /// </para>
    /// </summary>
    private async Task TryRefreshDeviceAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("server");

            var url = $"api/v1/terminals/devices/{Uri.EscapeDataString(_options.ResolvedDeviceKey)}/configuration"
                + $"?locationId={_options.LocationId}";

            using var response = await client.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                if (_devices.Current is not null)
                {
                    _logger.LogInformation(
                        "The server no longer recognises this machine as {DeviceKey}; falling back to the station profile",
                        _options.ResolvedDeviceKey);
                }

                _devices.Clear();
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Said once, not every poll — but said. This returned in silence, and the silence is
                // why a live till spent days asking for its antenna map, being refused, and falling
                // back to one antenna with nothing anywhere recording that it had happened. The
                // rejection was 400 device.not_found; the branch above waits for a 404, so the case
                // that actually occurs had no handler and no log.
                if (_deviceFetchFailed != (int)response.StatusCode)
                {
                    _deviceFetchFailed = (int)response.StatusCode;

                    _logger.LogWarning(
                        "The server would not send this machine's reader configuration for {DeviceKey} "
                        + "({Status}); running from the station profile, which drives one reader",
                        _options.ResolvedDeviceKey,
                        (int)response.StatusCode);
                }

                return;
            }

            _deviceFetchFailed = 0;

            var configuration = await response.Content
                .ReadFromJsonAsync<DeviceConfigurationContract>(SerializerOptions, ct);

            if (configuration is null)
            {
                return;
            }

            var had = _devices.Current;
            _devices.Set(configuration);

            if (had?.Revision != configuration.Revision)
            {
                _logger.LogInformation(
                    "Device configuration for {DeviceKey}: {Readers} reader(s), {Antennas} antenna assignment(s)",
                    configuration.DeviceKey,
                    configuration.Readers.Count,
                    configuration.Readers.Sum(r => r.Antennas.Count));
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // Kept, not cleared. Unreachable is not the same as revoked, and dropping every reader
            // because a poll failed would stop a shop trading over a network blip.
            _logger.LogWarning("Could not refresh the device configuration ({Reason}); keeping the current one", ex.Message);
            _logger.LogDebug(ex, "Device configuration refresh failed");
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
            // Two different situations, and saying "keeping the current one" for both is what made
            // this hard to read: on a cold start there is no current one, and the sentence quietly
            // asserts a safety the agent does not have. The till is on the simulator at that point,
            // which is worth a louder line than a routine refresh failure.
            if (_profiles.Current is null)
            {
                _logger.LogWarning(
                    "Could not fetch the device profile ({Reason}) and this agent has none, so it is "
                    + "running the simulator and will read no real tags. Retrying every {Seconds}s.",
                    ex.Message,
                    RetryWhileUnconfigured.TotalSeconds);
            }
            else
            {
                _logger.LogWarning("Could not refresh the device profile ({Reason}); keeping the current one", ex.Message);
            }

            _logger.LogDebug(ex, "Profile refresh failed");
        }
    }
}
