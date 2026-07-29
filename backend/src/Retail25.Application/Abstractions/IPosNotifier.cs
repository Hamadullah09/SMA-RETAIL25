namespace Retail25.Application.Abstractions;

/// <summary>
/// The POS hub, seen from the application layer (doc 05 §SignalR). Handlers speak this; only
/// Infrastructure knows SignalR exists.
/// <para>
/// Every cart message carries the cart's revision. A client that spots a gap in the sequence calls
/// <c>RequestCartResync</c> rather than quietly rendering stale money.
/// </para>
/// </summary>
public interface IPosNotifier
{
    /// <summary>Authoritative full state after any mutation.</summary>
    Task CartUpdatedAsync(Guid locationId, Guid cartId, object cartDto, int revision, CancellationToken ct = default);

    /// <summary>Fast path for a bulk RFID read: only the new lines, not the whole cart.</summary>
    Task CartLinesAddedAsync(Guid locationId, Guid cartId, object[] lineDtos, int revision, CancellationToken ct = default);

    /// <summary>A tag that will not be sold, and the plain-language reason why (doc 06 §2).</summary>
    Task CartLineRejectedAsync(Guid stationId, string epc, string reason, string message, CancellationToken ct = default);

    Task TotalsChangedAsync(Guid locationId, Guid cartId, object totalsDto, int revision, CancellationToken ct = default);

    Task CartSuspendedAsync(Guid locationId, object suspendedDto, CancellationToken ct = default);

    Task CartRecalledAsync(Guid locationId, Guid cartId, Guid stationId, CancellationToken ct = default);

    Task DrawerStateChangedAsync(Guid stationId, object drawerDto, CancellationToken ct = default);

    Task StockLevelChangedAsync(Guid locationId, Guid productId, decimal newOnHand, CancellationToken ct = default);

    Task ProductChangedAsync(Guid locationId, Guid productId, CancellationToken ct = default);

    Task ProductDeletedAsync(Guid locationId, Guid productId, CancellationToken ct = default);

    /// <summary>
    /// A row in a browse grid was created or edited (doc 05 §SignalR). This is the direct answer to
    /// the legacy complaint that browse windows go stale over a network (guide p.100–101): the grid
    /// patches the one row it was sent instead of refetching the page, so a second workstation sees
    /// the change without losing its scroll position or its selection.
    /// </summary>
    /// <param name="entity">Grid key: <c>product</c>, <c>customer</c>, <c>supplier</c>, …</param>
    Task RowChangedAsync(Guid locationId, string entity, Guid id, object row, CancellationToken ct = default);

    /// <summary>A row left the grid — deleted, or edited out of the current filter.</summary>
    Task RowRemovedAsync(Guid locationId, string entity, Guid id, CancellationToken ct = default);

    /// <summary>A settings section was saved; open settings screens reload it rather than overwrite.</summary>
    Task SettingsChangedAsync(Guid locationId, string section, CancellationToken ct = default);

    /// <summary>Drives the red strip on the live feed when a reader stops answering (doc 06 §3).</summary>
    Task TagStreamStatusAsync(Guid stationId, bool readerOnline, int readRate, CancellationToken ct = default);

    Task PeripheralStatusAsync(Guid stationId, object statusDto, CancellationToken ct = default);

    /// <summary>The legacy per-item prompt shown when an item is scanned (guide p.43).</summary>
    Task PosMessageAsync(Guid stationId, Guid productId, string message, CancellationToken ct = default);

    /// <summary>A step-up request any supervisor at the location can approve (doc 05).</summary>
    Task SupervisorApprovalRequestedAsync(Guid locationId, object requestDto, CancellationToken ct = default);

    Task WeightReportedAsync(Guid stationId, decimal value, string unit, bool stable, CancellationToken ct = default);
}
