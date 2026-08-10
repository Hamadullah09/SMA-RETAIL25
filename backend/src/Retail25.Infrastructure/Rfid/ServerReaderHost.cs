using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retail25.Application.Rfid.Commands;
using Retail25.Application.Terminals;
using Retail25.Contracts.Terminals;
using Retail25.Devices.Rfid;
using Retail25.Domain.Terminals;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Infrastructure.Rfid;

/// <summary>
/// Settings for holding reader connections in the API process.
/// </summary>
public sealed class ServerReaderOptions
{
    public const string Section = "Rfid:ServerReaders";

    /// <summary>
    /// Off unless the deployment says otherwise, and that default is deliberate.
    /// <para>
    /// This only works when the server can route to the reader — an API on the shop's own network.
    /// Hosted somewhere else, every reader is behind the shop's NAT and each connection attempt is a
    /// slow failure repeated forever. A shop in that position runs the terminal agent on the till
    /// instead, which is why the two are alternatives rather than layers.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>How long to wait before dialling a reader again after it drops.</summary>
    public int ReconnectSeconds { get; set; } = 10;

    /// <summary>
    /// How often to re-read the profile table, so a reader added or disabled in the settings screen
    /// is picked up without restarting the API.
    /// </summary>
    public int RefreshSeconds { get; set; } = 30;
}

/// <summary>
/// Holds the reader connections in the API, so a till needs nothing installed on it.
///
/// <para>
/// The terminal agent exists because a browser cannot open a TCP socket — there is no web API for
/// it, and a UHF reader on a serial-to-Ethernet bridge speaks raw TCP. That leaves exactly two
/// places the connection can be made from: a program on the till, or the server. When the server is
/// on the shop's own network it can reach the reader directly, and then the agent is a deployment
/// step that buys nothing.
/// </para>
/// <para>
/// One session per active profile, each reconnecting on its own. A reader that drops must not take
/// the others down with it: the failure this is written against is a shop with several lanes where
/// one antenna is unplugged, and the useful behaviour is the other lanes continuing to sell.
/// </para>
/// <para>
/// Reads go into <see cref="IngestTagReadsCommand"/> — the same command the agent posts to over
/// HTTP. Debounce, cart resolution, tag arbitration and the read feed are all downstream of that, so
/// this class adds a transport and changes no behaviour. A tag read here and a tag read from an
/// agent are indistinguishable by the time anything decides what to do with them.
/// </para>
/// </summary>
public sealed class ServerReaderHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ServerReaderHost> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ServerReaderOptions _options;

    /// <summary>Running sessions by reader profile id, so a refresh can tell new from known.</summary>
    private readonly Dictionary<long, ReaderSession> _sessions = [];

    /// <summary>Profiles already complained about, so a standing misconfiguration is said once.</summary>
    private readonly HashSet<long> _unattributable = [];

    public ServerReaderHost(
        IServiceScopeFactory scopes,
        ILogger<ServerReaderHost> logger,
        ILoggerFactory loggerFactory,
        IOptions<ServerReaderOptions> options)
    {
        _scopes = scopes;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _options = options?.Value ?? new ServerReaderOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        _logger.LogInformation(
            "Reader connections are held by this server. Tills need no agent installed; readers must be routable from here.");

        var refresh = TimeSpan.FromSeconds(Math.Max(5, _options.RefreshSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failure to read the profile table must not end the host. The database being
                // briefly unavailable is a reason to try again shortly, not a reason to stop reading
                // tags until somebody restarts the API.
                _logger.LogError(ex, "Could not reconcile reader profiles; retrying in {Seconds}s", refresh.TotalSeconds);
            }

            try
            {
                await Task.Delay(refresh, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await Task.WhenAll(_sessions.Values.Select(s => s.StopAsync()));
        _sessions.Clear();
    }

    /// <summary>
    /// Brings the running sessions in line with the profile table: start what is newly active, stop
    /// what has been disabled or deleted, and restart what has been edited.
    /// </summary>
    private async Task ReconcileAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profiles = await db.Set<ReaderProfile>()
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync(ct);

        // A tag read has to name a till before anything downstream can decide which cart it belongs
        // to. A profile that names its station answers that outright.
        //
        // A location-wide profile does not, and it is the shape most shops start with — one reader,
        // one counter, nobody has said which till because there is only one. So a location with a
        // single active station resolves to that station; a location with several is genuinely
        // ambiguous and is refused rather than guessed, because guessing wrong here rings an item
        // onto somebody else's sale.
        var stationsByLocation = await db.Set<Station>()
            .AsNoTracking()
            .Where(s => s.IsActive)
            .GroupBy(s => s.LocationId)
            .Select(g => new { LocationId = g.Key, Ids = g.Select(s => s.Id).ToList() })
            .ToDictionaryAsync(x => x.LocationId, x => x.Ids, ct);

        var resolved = new Dictionary<long, long>();

        foreach (var profile in profiles)
        {
            if (profile.StationId is { } bound)
            {
                resolved[profile.Id] = bound;
                continue;
            }

            var stations = stationsByLocation.GetValueOrDefault(profile.LocationId) ?? [];

            if (stations.Count == 1)
            {
                resolved[profile.Id] = stations[0];
            }
            else if (_unattributable.Add(profile.Id))
            {
                // Logged once per profile, not once per refresh: this is a standing configuration
                // problem, and repeating it every thirty seconds would make the log useless.
                _logger.LogWarning(
                    "Reader {Name} is not bound to a till and its location has {Count} active tills, so its "
                    + "reads could not be attributed. Set the station on the reader profile.",
                    profile.Name,
                    stations.Count);
            }
        }

        var wanted = profiles.Where(p => resolved.ContainsKey(p.Id)).ToDictionary(p => p.Id);

        foreach (var (id, session) in _sessions.ToList())
        {
            if (!wanted.TryGetValue(id, out var profile) || session.Revision != Revision(profile))
            {
                _logger.LogInformation("Stopping reader session {Reader} ({Reason})",
                    session.Description,
                    wanted.ContainsKey(id) ? "profile changed" : "profile disabled or removed");

                await session.StopAsync();
                _sessions.Remove(id);
            }
        }

        foreach (var profile in wanted.Values)
        {
            if (_sessions.ContainsKey(profile.Id))
            {
                continue;
            }

            var contract = ReaderProfileMapper.ToContract(profile);
            if (contract is null || !resolved.TryGetValue(profile.Id, out var stationId))
            {
                continue;
            }

            var session = new ReaderSession(
                contract,
                stationId,
                Revision(profile),
                _scopes,
                _loggerFactory,
                TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectSeconds)));

            _sessions[profile.Id] = session;
            session.Start();

            _logger.LogInformation(
                "Reader {Name} at {Host}:{Port} is now served by this process for station {Station}",
                profile.Name,
                profile.Host,
                profile.Port,
                stationId);
        }
    }

    /// <summary>
    /// What counts as "the profile changed" for a running session.
    /// <para>
    /// The row's concurrency token, not a field-by-field comparison: every edit bumps it, so a
    /// settings change of any kind restarts the session and the device is reconfigured on the next
    /// connect. Cheaper than deciding which fields the driver cares about, and it cannot fall behind
    /// when a field is added.
    /// </para>
    /// </summary>
    private static long Revision(ReaderProfile profile) => profile.RowVersion;
}
