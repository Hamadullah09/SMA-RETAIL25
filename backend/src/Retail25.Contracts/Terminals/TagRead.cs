namespace Retail25.Contracts.Terminals;

/// <summary>
/// One tag as the agent observed it, after its own local pre-filter (doc 06 §2).
/// <para>
/// <see cref="ReadCount"/> and the two timestamps survive the trip because the server re-checks the
/// thresholds itself. The agent's filter is an optimisation — it stops noise costing a round trip —
/// not a trust boundary, and a compromised or misconfigured agent must not be able to put a tag on a
/// cart that the reader profile would have rejected.
/// </para>
/// </summary>
/// <param name="Epc">24–96 uppercase hex characters.</param>
/// <param name="Antenna">Antenna number, resolved to a zone by the reader profile.</param>
/// <param name="Rssi">Signal strength in dBm. Negative; closer to zero is stronger.</param>
/// <param name="ReadCount">Times the tag was seen inside the coalescing window.</param>
public sealed record TagRead(
    string Epc,
    int Antenna,
    int Rssi,
    int ReadCount,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen);

/// <summary>A batch as it is published. Batching is what makes a thirty-item basket one round trip.</summary>
public sealed record TagBatch(string StationId, IReadOnlyList<TagRead> Tags);
