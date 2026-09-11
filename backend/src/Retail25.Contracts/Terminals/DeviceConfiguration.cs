namespace Retail25.Contracts.Terminals;

/// <summary>
/// One antenna of a reader, and the station it stands for.
/// <para>
/// The agent needs the station id so it can label reads before sending them — but it is told the
/// mapping rather than deciding it. That distinction is the architecture: configuration flows down,
/// observations flow up, and nothing on a till invents which station an antenna belongs to.
/// </para>
/// </summary>
public sealed record AntennaAssignmentContract(
    int AntennaNumber,
    long StationId,
    string StationCode,
    bool Enabled);

/// <summary>One reader an agent is responsible for, with its antenna map.</summary>
public sealed record ManagedReaderContract(
    long ReaderId,
    string ReaderKey,
    string? SerialNumber,
    string Host,
    int Port,
    string Protocol,
    int AntennaCount,
    IReadOnlyList<AntennaAssignmentContract> Antennas,

    /// <summary>
    /// The reader's own tuning — power, region, debounce — carried through unchanged from the
    /// existing profile so the drivers keep the settings they already honour.
    /// </summary>
    ReaderProfileContract? Settings = null);

/// <summary>
/// One reader an agent found on its own network, as the hardware describes itself.
///
/// <para>
/// The counterpart to <see cref="ManagedReaderContract"/> and deliberately a different shape.
/// A managed reader is what the server has decided a machine should drive; a discovered one is what
/// the machine can actually see. Conflating them would let a till invent its own configuration,
/// which is exactly the direction this architecture does not allow: observations flow up,
/// configuration flows down, and an administrator decides which discovered reader becomes a managed
/// one.
/// </para>
/// <para>
/// <see cref="SerialNumber"/> is the reader's own identifier where the protocol exposes one, and it
/// is what makes a DHCP lease change a move rather than a new reader. Null where the unit will not
/// report one — those are matched on address instead, which is weaker and is why the field exists
/// separately rather than being folded into a single key.
/// </para>
/// </summary>
public sealed record DiscoveredReaderContract(
    string Host,
    int Port,
    string Protocol,
    string? SerialNumber,
    string? FirmwareVersion,
    int AntennaCount,

    /// <summary>
    /// Whether the reader reported its antenna count or the agent fell back to the family default.
    /// Carried so a screen can show a measured four differently from an assumed four, rather than
    /// presenting a guess with the same confidence as a fact.
    /// </summary>
    bool AntennaCountReported);

/// <summary>
/// What one machine found when it last looked.
/// <para>
/// Sent after a sweep. The server records it so an administrator can see what is on the shop's
/// network from a browser that is nowhere near that network — which is the whole reason discovery
/// runs on the till rather than on the server.
/// </para>
/// </summary>
public sealed record ReaderDiscoveryReport(
    string DeviceKey,
    DateTimeOffset CompletedAt,
    IReadOnlyList<DiscoveredReaderContract> Readers);

/// <summary>
/// Everything one machine needs to do its job.
/// <para>
/// Replaces the assumption behind the per-station profile, which could only ever describe one till
/// and therefore one reader. A machine driving three readers across twelve stations has no single
/// station to ask about, so it asks about itself.
/// </para>
/// <para>
/// Versioned so an older agent meeting a newer server can tell that it does not understand what it
/// has been sent, rather than quietly running on the half of it that still parses.
/// </para>
/// </summary>
public sealed record DeviceConfigurationContract(
    long DeviceId,
    string DeviceKey,
    long LocationId,
    IReadOnlyList<ManagedReaderContract> Readers,
    int Version = 1)
{
    /// <summary>
    /// A fingerprint of the configuration, so an agent can tell "unchanged" from "changed" without
    /// comparing the whole tree — and so a reassignment is applied within one poll rather than at the
    /// next restart.
    /// </summary>
    public string Revision => string.Join(
        '|',
        Readers
            .OrderBy(r => r.ReaderId)
            .Select(r => $"{r.ReaderId}:{r.Host}:{r.Port}:" + string.Join(
                ',',
                r.Antennas.OrderBy(a => a.AntennaNumber)
                    .Select(a => $"{a.AntennaNumber}>{a.StationId}{(a.Enabled ? string.Empty : "-off")}"))));
}
