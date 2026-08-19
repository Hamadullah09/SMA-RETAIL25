using System.Globalization;
using System.ComponentModel.DataAnnotations;

namespace Retail25.TerminalAgent;

/// <summary>
/// The only things the agent is told locally: which till it is, where the server lives, and a
/// bootstrap secret (doc 06 §7).
/// <para>
/// Everything else — reader endpoint, antenna zoning, thresholds, escape codes, port settings — is
/// pulled from the server, because those are the settings that change when a store swaps hardware,
/// and a site visit to edit a file on each till is exactly the failure mode the legacy system had.
/// </para>
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>The station this machine is. Provisioned once at install.</summary>
    [Required]
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// Which machine this is, for the device registry.
    /// <para>
    /// Defaults to the computer's own name so an agent installed with no configuration still has a
    /// stable identity — one that survives a DHCP lease, a new switch port and a move between
    /// subnets, which is the whole reason identity is not an address.
    /// </para>
    /// <para>
    /// Set it explicitly where machine names are not unique across a chain. Two shops each with a
    /// "TILL-01" would otherwise be one device as far as the registry is concerned, and the second to
    /// check in would take the first one's readers.
    /// </para>
    /// </summary>
    public string DeviceKey { get; set; } = string.Empty;

    /// <summary>The shop this machine belongs to. Needed to resolve the device key, which is unique per location.</summary>
    public long LocationId { get; set; } = 1;

    public string ResolvedDeviceKey =>
        string.IsNullOrWhiteSpace(DeviceKey) ? Environment.MachineName.ToUpperInvariant() : DeviceKey.Trim().ToUpperInvariant();

    [Required]
    public string ApiUrl { get; set; } = "http://localhost:5000";

    /// <summary>Presented when registering. Exchanged for a session; never logged.</summary>
    public string? BootstrapSecret { get; set; }

    /// <summary>Loopback only. The browser calls this for actions with no server-side meaning.</summary>
    public string LocalApiUrl { get; set; } = "http://127.0.0.1:8477";

    /// <summary>
    /// Overrides the server's reader protocol. Set to <c>Simulator</c> on a bench or in a demo so the
    /// whole flow runs with no hardware in the room.
    /// </summary>
    public string? ForceReaderProtocol { get; set; }

    /// <summary>Where the offline spool lives. Relative paths resolve under the agent's data directory.</summary>
    public string SpoolPath { get; set; } = "spool/tags.db";

    /// <summary>Bounded at 24 hours: reads older than that describe a basket that left long ago.</summary>
    public int SpoolRetentionHours { get; set; } = 24;

    public int SpoolMaxBatches { get; set; } = 5000;

    /// <summary>How often the agent tells the server its hardware is alive (doc 06 §3).</summary>
    public int HeartbeatSeconds { get; set; } = 5;

    /// <summary>Seconds between reconnection attempts after the reader faults, before backoff.</summary>
    public int ReaderRetrySeconds { get; set; } = 5;

    /// <summary>
    /// Disables every serial and parallel device. Set on a developer machine, where opening COM1
    /// either fails or — worse — talks to something that is not a till printer.
    /// </summary>
    public bool DisablePeripherals { get; set; }

    /// <summary>
    /// The configured station id as a number, or 0 when it is missing or not a number.
    /// <para>
    /// Zero rather than throwing: a till whose configuration file has a typo should log that it is
    /// unregistered and keep running its local endpoints, not fail to start — the person fixing it
    /// needs the status page the agent serves.
    /// </para>
    /// </summary>
    public long StationKey =>
        long.TryParse(StationId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0L;

    public string ResolveSpoolPath()
    {
        if (Path.IsPathRooted(SpoolPath))
        {
            return SpoolPath;
        }

        // Beside the logs, and for the same reason: under a service account's profile this is a path
        // that needs elevation to inspect, and the spool is the thing holding tag reads that have not
        // reached the server yet.
        return Path.Combine(AgentPaths.DataDirectory, SpoolPath);
    }
}
