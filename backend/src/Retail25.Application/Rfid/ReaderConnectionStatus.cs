namespace Retail25.Application.Rfid;

/// <summary>One reader as the server currently sees it.</summary>
/// <param name="Endpoint">Host and port, so a shop can tell two identical readers apart.</param>
public sealed record ReaderConnectionState(
    long ProfileId,
    string Name,
    string Endpoint,
    long StationId,
    bool Connected);

/// <summary>
/// What the server can say about the readers it holds.
/// </summary>
/// <param name="ServerHosted">
/// Whether this deployment holds reader connections at all. False is not a fault: it means the tills
/// run terminal agents, and the till should ask its own agent rather than the server. The till needs
/// to tell those two apart, because "no reader here" and "not my job to know" look identical
/// otherwise — and one of them is a broken shop.
/// </param>
public sealed record ReaderConnectionSnapshot(bool ServerHosted, IReadOnlyList<ReaderConnectionState> Readers);

/// <summary>
/// Live reader connection state, for the status strip.
/// <para>
/// Deliberately not a query over the profile table. The table says which readers are configured; this
/// says which are answering right now, which is the only question a cashier looking at a red chip
/// actually has.
/// </para>
/// </summary>
public interface IReaderConnectionStatus
{
    ReaderConnectionSnapshot Current { get; }
}
