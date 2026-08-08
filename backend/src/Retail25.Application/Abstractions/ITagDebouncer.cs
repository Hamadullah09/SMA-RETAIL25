namespace Retail25.Application.Abstractions;

/// <summary>
/// Cross-station arbitration for RFID tags, backed by Redis <c>SET key value NX PX</c> (doc 06 §2).
/// <para>
/// The agent already coalesces the 20-reads-a-second noise locally. This second layer exists for a
/// different reason: two tills within reading distance of the same basket must not both claim the
/// same tag, and a claim has to survive an agent reconnect. A distributed lock with an expiry is the
/// only thing that gives both properties.
/// </para>
/// </summary>
public interface ITagDebouncer
{
    /// <summary>
    /// Attempts to claim an EPC for a station. Returns false when another station already holds it.
    /// Re-claiming for the same station inside the window succeeds and is idempotent.
    /// </summary>
    Task<bool> TryClaimAsync(string epc, long stationId, TimeSpan window, CancellationToken ct = default);

    /// <summary>Releases a claim early — when a line is removed, or a batch is rejected.</summary>
    Task ReleaseAsync(string epc, long stationId, CancellationToken ct = default);

    /// <summary>Which station currently holds the tag, if any. Drives the "claimed elsewhere" message.</summary>
    Task<long?> GetHolderAsync(string epc, CancellationToken ct = default);
}
