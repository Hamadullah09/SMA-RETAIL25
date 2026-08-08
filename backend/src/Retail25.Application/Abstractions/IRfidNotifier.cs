namespace Retail25.Application.Abstractions;

/// <summary>
/// Pushes reader activity to whoever is watching — a till, a goods-in screen, a stock-count tablet.
/// <para>
/// Separate from <c>IPosNotifier</c> because the audiences differ. The POS feed is about a cart: a
/// line went on, a tag was rejected. This one is about the antenna field: what is in front of the
/// reader right now, whether the reader is alive, and how hard it is working. A stock count wants
/// the second and has no cart at all.
/// </para>
/// </summary>
public interface IRfidNotifier
{
    /// <summary>
    /// Distinct tags observed at a station, already through the debounce window.
    /// </summary>
    Task TagsObservedAsync(long locationId, long stationId, IReadOnlyList<ObservedTag> tags, CancellationToken ct = default);

    /// <summary>Reader health, so a screen can say "not reading" rather than showing a still list.</summary>
    Task ReaderStatusAsync(long locationId, long stationId, RfidReaderStatus status, CancellationToken ct = default);
}

/// <summary>
/// One tag, as the watching screen sees it.
/// </summary>
/// <param name="Epc">The tag's EPC, upper-case hex.</param>
/// <param name="Antenna">Which of the reader's antennas saw it. Four on a D2184; 0 when unknown.</param>
/// <param name="Rssi">Signal strength in dBm — how close it is, roughly. Null when the reader omits it.</param>
/// <param name="ReadCount">How many raw reads collapsed into this one observation inside the window.</param>
/// <param name="FirstSeenAt">When the window opened.</param>
/// <param name="LastSeenAt">The most recent raw read in the window.</param>
/// <param name="ProductId">Resolved item, when the EPC is one we know. Null for an unmapped tag.</param>
/// <param name="StockCode">Resolved stock code, for display without a second round trip.</param>
/// <param name="Name">Resolved description.</param>
public sealed record ObservedTag(
    string Epc,
    int Antenna,
    int? Rssi,
    int ReadCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    long? ProductId = null,
    string? StockCode = null,
    string? Name = null);

/// <summary>
/// What the reader is doing.
/// </summary>
/// <param name="Connected">Whether the agent currently holds a session with the reader.</param>
/// <param name="ReadsPerSecond">Raw reads off the antenna, before debounce — the number that tells you an antenna has died.</param>
/// <param name="DistinctTagsInField">How many different tags the window currently holds.</param>
/// <param name="Detail">Free text for a fault, shown to the operator as-is.</param>
public sealed record RfidReaderStatus(
    bool Connected,
    int ReadsPerSecond,
    int DistinctTagsInField,
    string? Detail = null);
