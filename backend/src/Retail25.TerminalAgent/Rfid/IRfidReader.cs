using Retail25.Contracts.Terminals;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// A tag source (decision Q3).
/// <para>
/// The port exists so the whole flow — ingest, debounce, cart, rejection reasons, UI — is developable
/// and testable with no hardware in the room. That is not a convenience: RFID readers are expensive,
/// slow to configure and impossible to put in CI, and a design that only works with one attached
/// would leave the most complex path in the system permanently untested.
/// </para>
/// </summary>
public interface IRfidReader : IAsyncDisposable
{
    /// <summary>Human-readable, for the status strip and the logs.</summary>
    string Description { get; }

    bool IsConnected { get; }

    Task ConnectAsync(ReaderProfileContract profile, CancellationToken ct);

    /// <summary>Begins inventorying. Idempotent — calling it twice must not start two sessions.</summary>
    Task StartAsync(CancellationToken ct);

    Task StopAsync(CancellationToken ct);

    /// <summary>Streams reads until the token is cancelled or the reader faults.</summary>
    IAsyncEnumerable<TagRead> ReadsAsync(CancellationToken ct);
}
