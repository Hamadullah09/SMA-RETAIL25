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
