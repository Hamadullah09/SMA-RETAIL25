using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retail25.Contracts.Terminals;
using Retail25.Devices.Rfid;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// Sweeps this machine's network for readers and tells the server what it found.
///
/// <para>
/// This is the half of discovery that only a till can do. The readers are on the shop's own LAN and
/// the server may be in a data centre; a server scanning <c>192.168.x.x</c> would be scanning its
/// own neighbours. So the machine that can see them looks, and the machine that holds the
/// configuration records it. An administrator then assigns antennas to stations from a browser that
/// is nowhere near the shop.
/// </para>
/// <para>
/// It runs on a slow clock on purpose. Readers are screwed to walls — the set changes when somebody
/// installs one, not continuously — and a sweep touches a few hundred addresses. Running it often
/// would put a recurring scan on a shop's network for no benefit and would look, correctly, like
/// something worth investigating. The first sweep happens shortly after start, when a machine is
/// most likely to have just had hardware attached to it.
/// </para>
/// <para>
/// It never interferes with reading. Addresses that a running session already holds are excluded
/// before the sweep begins, because this reader family accepts exactly one client and probing a
/// reader mid-sale would take it away from the till.
/// </para>
/// </summary>
public sealed class ReaderDiscoveryService : BackgroundService
{
    /// <summary>
    /// Long enough that this is not a recurring scan of somebody's network, short enough that a
    /// reader installed this morning is found before anybody thinks to ask why it is not listed.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Long enough for the agent to have enrolled and brought up the readers it already knows about,
    /// so the first sweep excludes them rather than fighting them for the socket.
    /// </summary>
    private static readonly TimeSpan FirstSweepDelay = TimeSpan.FromSeconds(45);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ReaderDiscovery _discovery;
    private readonly RfidReaderService _readers;
    private readonly ProfileStore _profiles;
    private readonly DeviceConfigurationStore _devices;
    private readonly AgentOptions _options;
    private readonly ILogger<ReaderDiscoveryService> _logger;

    public ReaderDiscoveryService(
        IHttpClientFactory httpClientFactory,
        ReaderDiscovery discovery,
        RfidReaderService readers,
        ProfileStore profiles,
        DeviceConfigurationStore devices,
        IOptions<AgentOptions> options,
        ILogger<ReaderDiscoveryService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _httpClientFactory = httpClientFactory;
        _discovery = discovery;
        _readers = readers;
        _profiles = profiles;
        _devices = devices;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.DiscoverReaders)
        {
            _logger.LogInformation(
                "Reader discovery is switched off for this machine; readers must be registered by hand.");

            return;
        }

        try
        {
            await Task.Delay(FirstSweepDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed sweep is not a reason to stop sweeping. The network may be down, the
                // server unreachable, or a reader may have misbehaved; none of those get better by
                // this service ending.
                _logger.LogWarning(ex, "A reader sweep failed; trying again in {Minutes} minutes", Interval.TotalMinutes);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// One sweep, and the report that follows it. Public so the loopback API can offer an
    /// administrator a "scan now" button rather than making them wait for the timer.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredReaderContract>> SweepAsync(CancellationToken ct)
    {
        // Excluded, not probed and discarded. These are the readers that are working; asking them to
        // prove themselves would mean taking the socket from a session that is mid-sale.
        var inUse = _readers.ReaderCheckIns()
            .Where(r => r.Connected)
            .Select(r => r.Host)
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var found = new List<ReaderIdentity>();

        foreach (var port in PortsToSweep())
        {
            found.AddRange(await _discovery.DiscoverAsync(port, inUse, ct).ConfigureAwait(false));
        }

        var readers = found
            // One reader answering on two ports is one reader. It happens on bridges that expose the
            // same serial line twice, and reporting it twice would put two rows in front of an
            // administrator for one box on the wall.
            .GroupBy(r => r.SerialNumber ?? $"@{r.Host}:{r.Port}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(r => new DiscoveredReaderContract(
                r.Host,
                r.Port,
                r.Protocol.ToString(),
                r.SerialNumber,
                r.FirmwareVersion,
                r.AntennaCount,
                r.AntennaCountReported))
            .ToList();

        if (readers.Count > 0)
        {
            await ReportAsync(readers, ct).ConfigureAwait(false);
        }

        return readers;
    }

    /// <summary>
    /// Which ports this machine should knock on, most-likely first.
    ///
    /// <para>
    /// The ports the shop's own readers already answer on come first, because on a commissioned till
    /// they are the answer and everything after them is a guess. The station's own reader profile
    /// follows. Only then the configured starting guesses, which exist for the machine that has been
    /// told nothing yet.
    /// </para>
    /// <para>
    /// The order is load-bearing rather than cosmetic: a reader found on the first port is not looked
    /// for again on the rest, so a commissioned shop does one sweep and a new one does as many as it
    /// has guesses. Taking the port from the profile alone — as this did — meant a fresh agent swept
    /// the placeholder 5084 it ships with and reported nothing, on precisely the installation
    /// discovery exists to serve.
    /// </para>
    /// </summary>
    private IReadOnlyList<int> PortsToSweep() => PortsToSweep(
        (_devices.Current?.Readers ?? []).Select(r => r.Port),
        _profiles.Reader.Port,
        _options.DiscoveryPorts);

    /// <summary>Internal so the ordering can be pinned without putting a sweep on a test machine's network.</summary>
    internal static IReadOnlyList<int> PortsToSweep(
        IEnumerable<int> knownReaderPorts,
        int profilePort,
        IEnumerable<int> configured)
    {
        var ports = new List<int>();

        void Add(int port)
        {
            // Out-of-range values are dropped rather than clamped. A zero is the "nobody has said"
            // placeholder and a negative is a typo; sweeping a guessed port because a setting was
            // wrong would search the wrong thing and report the result as fact.
            if (port is > 0 and <= 65535 && !ports.Contains(port))
            {
                ports.Add(port);
            }
        }

        foreach (var port in knownReaderPorts)
        {
            Add(port);
        }

        Add(profilePort);

        foreach (var port in configured)
        {
            Add(port);
        }

        return ports;
    }

    private async Task ReportAsync(IReadOnlyList<DiscoveredReaderContract> readers, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("server");

        var payload = new
        {
            LocationId = _options.LocationId,
            DeviceKey = _options.ResolvedDeviceKey,
            Readers = readers,
        };

        using var response = await client
            .PostAsJsonAsync("api/v1/rfid-topology/discovered", payload, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Found {Count} reader(s) but the server answered {Status}; they will be reported again on the next sweep",
                readers.Count,
                (int)response.StatusCode);

            return;
        }

        _logger.LogInformation("Reported {Count} discovered reader(s) to the server", readers.Count);
    }
}
