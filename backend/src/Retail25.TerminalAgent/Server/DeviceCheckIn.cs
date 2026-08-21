using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retail25.TerminalAgent.Rfid;

namespace Retail25.TerminalAgent.Server;

/// <summary>
/// Tells the server this machine exists, and what it is driving.
/// <para>
/// This is the call the whole per-antenna model rests on and it was never made. The server learns a
/// machine from its first check-in; until then it has no <c>Device</c> row, so the configuration
/// endpoint answers <c>device.not_found</c> — which is exactly what a live till was doing every five
/// minutes for days. The agent read the rejection as "no configuration", fell back to the single
/// station profile, and inventoried one antenna. Everything downstream looked healthy: the reader
/// connected, the strip was green, tags read. They simply all arrived from antenna one.
/// </para>
/// <para>
/// Sent on the heartbeat rather than on the five-minute profile poll, because
/// <c>ReportDeviceStatusCommand.OfflineAfter</c> is fifteen seconds — three beats. Registering on the
/// slow poll would leave every machine in the estate reading as offline between polls, which is the
/// dashboard lying in the other direction.
/// </para>
/// </summary>
public sealed class DeviceCheckIn
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RfidReaderService _readers;
    private readonly AgentOptions _options;
    private readonly ILogger<DeviceCheckIn> _logger;

    /// <summary>
    /// Whether the last check-in succeeded, so the failure is reported once rather than every beat.
    /// <para>
    /// Twelve times a minute, forever, is how a log stops being read. The transition is the news.
    /// </para>
    /// </summary>
    private bool _reported;

    public DeviceCheckIn(
        IHttpClientFactory httpClientFactory,
        RfidReaderService readers,
        IOptions<AgentOptions> options,
        ILogger<DeviceCheckIn> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _httpClientFactory = httpClientFactory;
        _readers = readers;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> CheckInAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("server");

            var payload = new DeviceStatusPayload(
                _options.LocationId,
                _options.ResolvedDeviceKey,
                Environment.MachineName,
                LocalAddresses(),
                Environment.OSVersion.VersionString,
                AgentVersion.Current,
                [.. _readers.ReaderCheckIns().Select(r =>
                    new ReaderStatusPayload(r.ReaderKey, null, r.Connected, r.Host, r.Port))]);

            using var response = await client.PostAsJsonAsync(
                "api/v1/terminals/devices/status", payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                if (_reported)
                {
                    _logger.LogWarning(
                        "Could not check this machine in as {DeviceKey}: the server answered {Status}. "
                        + "Until it does, the server cannot send this machine its antenna assignments.",
                        _options.ResolvedDeviceKey,
                        (int)response.StatusCode);

                    _reported = false;
                }

                return false;
            }

            if (!_reported)
            {
                _logger.LogInformation(
                    "Checked in as {DeviceKey} with {Readers} reader(s)",
                    _options.ResolvedDeviceKey,
                    _readers.ReaderCheckIns().Count);

                _reported = true;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_reported)
            {
                _logger.LogWarning(ex, "Could not check this machine in");
                _reported = false;
            }

            return false;
        }
    }

    /// <summary>
    /// The addresses an installer would recognise this machine by, so a row on the health screen can
    /// be matched to a box on a counter without walking to it.
    /// </summary>
    private static string LocalAddresses()
    {
        try
        {
            var addresses = System.Net.NetworkInformation.NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                            && !System.Net.IPAddress.IsLoopback(a))
                .Select(a => a.ToString())
                .Distinct();

            return string.Join(", ", addresses);
        }
        catch (Exception)
        {
            // A machine that cannot enumerate its own interfaces still needs to register.
            return string.Empty;
        }
    }

    private sealed record DeviceStatusPayload(
        long LocationId,
        string DeviceKey,
        string? Hostname,
        string? LocalIpAddresses,
        string? OperatingSystem,
        string? AgentVersion,
        IReadOnlyList<ReaderStatusPayload> Readers);

    private sealed record ReaderStatusPayload(
        string ReaderKey,
        string? SerialNumber,
        bool Connected,
        string? Host,
        int? Port);
}
