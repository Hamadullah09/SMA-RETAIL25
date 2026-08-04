namespace Retail25.Application.Abstractions;

/// <summary>
/// Server-to-agent commands over <c>TerminalHub</c> (doc 06 §5).
/// <para>
/// Printing and drawer pops for real sales go this way rather than through the browser's loopback
/// call, because they are authoritative and auditable: the server decides that a receipt is owed,
/// and the record of that decision does not depend on a page staying open.
/// </para>
/// </summary>
public interface ITerminalNotifier
{
    Task PrintReceiptAsync(long stationId, object receiptPayload, int copies, CancellationToken ct = default);

    Task OpenDrawerAsync(long stationId, CancellationToken ct = default);

    Task DisplayPoleAsync(long stationId, string line1, string line2, CancellationToken ct = default);

    Task RequestWeightAsync(long stationId, CancellationToken ct = default);

    Task ZeroScaleAsync(long stationId, CancellationToken ct = default);

    Task SetReaderModeAsync(long stationId, string mode, CancellationToken ct = default);

    /// <summary>Pushes a fresh device profile so a peripheral swap is a settings edit, not a site visit.</summary>
    Task UpdateProfileAsync(long stationId, object profile, CancellationToken ct = default);
}
