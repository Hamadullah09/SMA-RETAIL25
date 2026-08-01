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
/// <param name="Rssi">
/// Signal strength in dBm. Negative; closer to zero is stronger. <see cref="TagRead.UnknownRssi"/>
/// when the reader did not measure it.
/// </param>
/// <param name="ReadCount">Times the tag was seen inside the coalescing window.</param>
public sealed record TagRead(
    string Epc,
    int Antenna,
    int Rssi,
    int ReadCount,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen)
{
    /// <summary>
    /// The reader saw the tag but reported no signal strength.
    /// <para>
    /// This is not a hypothetical. An R2000-family reader running <em>real-time</em> inventory sends
    /// tag frames with the RSSI byte unpopulated — the vendor's own demo displays every tag at
    /// −128 dBm while showing a genuine −89/−46 range in its summary. Only the caching and
    /// fast-4-antenna modes fill the per-tag field in.
    /// </para>
    /// <para>
    /// It has to be a distinct value rather than a very negative number, because the proximity gate
    /// compares against a threshold. Treating "not measured" as "infinitely weak" would silently
    /// discard <em>every</em> read on that reader, and it would look exactly like a dead antenna.
    /// </para>
    /// </summary>
    public const int UnknownRssi = int.MinValue;

    /// <summary>Whether <see cref="Rssi"/> carries a real measurement.</summary>
    public bool HasRssi => Rssi != UnknownRssi;
}

/// <summary>A batch as it is published. Batching is what makes a thirty-item basket one round trip.</summary>
public sealed record TagBatch(string StationId, IReadOnlyList<TagRead> Tags);
