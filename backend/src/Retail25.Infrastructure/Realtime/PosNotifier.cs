using Microsoft.AspNetCore.SignalR;
using Retail25.Application.Abstractions;

namespace Retail25.Infrastructure.Realtime;

/// <summary>
/// Publishes to <see cref="PosHub"/> and <see cref="InventoryHub"/> on behalf of the application
/// layer, which never sees SignalR.
/// <para>
/// Cart messages go to the cart group and the station group. Both, deliberately: a supervisor
/// watching a till from the back office subscribes to the station without knowing the cart id, and a
/// till that has just been handed a recalled cart is in the cart group before it has re-joined
/// anything else.
/// </para>
/// </summary>
public sealed class PosNotifier : IPosNotifier
{
    private readonly IHubContext<PosHub> _pos;
    private readonly IHubContext<InventoryHub> _inventory;

    public PosNotifier(IHubContext<PosHub> pos, IHubContext<InventoryHub> inventory)
    {
        _pos = pos;
        _inventory = inventory;
    }

    public Task CartUpdatedAsync(long locationId, long cartId, object cartDto, int revision, CancellationToken ct = default)
        => _pos.Clients.Group(PosGroups.Cart(cartId)).SendAsync("CartUpdated", cartDto, revision, ct);

    public Task CartLinesAddedAsync(long locationId, long cartId, object[] lineDtos, int revision, CancellationToken ct = default)
        => _pos.Clients.Group(PosGroups.Cart(cartId)).SendAsync("CartLinesAdded", lineDtos, revision, ct);

    public Task CartLineRejectedAsync(long stationId, string epc, string reason, string message, CancellationToken ct = default)
        => _pos.Clients.Group(PosGroups.Station(stationId))
            .SendAsync("CartLineRejected", new { epc, reason, message }, ct);

    public Task TotalsChangedAsync(long locationId, long cartId, object totalsDto, int revision, CancellationToken ct = default)
        => _pos.Clients.Group(PosGroups.Cart(cartId)).SendAsync("TotalsChanged", totalsDto, revision, ct);

    public Task CartSuspendedAsync(long locationId, object suspendedDto, CancellationToken ct = default)
        => _pos.Clients.Group(PosGroups.Location(locationId)).SendAsync("CartSuspended", suspendedDto, ct);

    public Task CartRecalledAsync(long locationId, long cartId, long stationId, CancellationToken ct = default)
        => _pos.Clients.Group(PosGroups.Location(locationId))
            .SendAsync("CartRecalled", new { cartId, stationId }, ct);

    public Task DrawerStateChangedAsync(long stationId, object drawerDto, CancellationToken ct = default)
        => _pos.Clients.Group(PosGroups.Station(stationId)).SendAsync("DrawerStateChanged", drawerDto, ct);

    public async Task StockLevelChangedAsync(long locationId, long productId, decimal newOnHand, CancellationToken ct = default)
    {
        var payload = new { productId, onHand = newOnHand };

        // Both hubs: the till shows stock on the line detail, the back-office grid patches its row.
        await _inventory.Clients.Group(PosGroups.Location(locationId)).SendAsync("StockLevelChanged", payload, ct);
        await _pos.Clients.Group(PosGroups.Location(locationId)).SendAsync("StockLevelChanged", payload, ct);
    }

    public Task ProductChangedAsync(long locationId, long productId, CancellationToken ct = default)
        => _inventory.Clients.Group(PosGroups.Location(locationId)).SendAsync("ProductChanged", new { productId }, ct);

    public Task ProductDeletedAsync(long locationId, long productId, CancellationToken ct = default)
        => _inventory.Clients.Group(PosGroups.Location(locationId)).SendAsync("ProductDeleted", new { productId }, ct);

    public Task RowChangedAsync(long locationId, string entity, long id, object row, CancellationToken ct = default)
        => _inventory.Clients.Group(PosGroups.Location(locationId))
            .SendAsync("RowChanged", new { entity, id, row }, ct);

    public Task RowRemovedAsync(long locationId, string entity, long id, CancellationToken ct = default)
        => _inventory.Clients.Group(PosGroups.Location(locationId))
            .SendAsync("RowRemoved", new { entity, id }, ct);

    public Task SettingsChangedAsync(long locationId, string section, CancellationToken ct = default)
        => _inventory.Clients.Group(PosGroups.Location(locationId))
            .SendAsync("SettingsChanged", new { section }, ct);

    public Task TagStreamStatusAsync(long stationId, bool readerOnline, int readRate, CancellationToken ct = default)
        => _pos.Clients.Group(PosGroups.Station(stationId))
            .SendAsync("TagStreamStatus", new { readerOnline, readRate }, ct);

    public Task PeripheralStatusAsync(long stationId, object statusDto, CancellationToken ct = default)
        => _pos.Clients.Group(PosGroups.Station(stationId)).SendAsync("PeripheralStatus", statusDto, ct);

    public Task PosMessageAsync(long stationId, long productId, string message, CancellationToken ct = default)
        => _pos.Clients.Group(PosGroups.Station(stationId))
            .SendAsync("PosMessage", new { productId, message }, ct);

    public Task SupervisorApprovalRequestedAsync(long locationId, object requestDto, CancellationToken ct = default)
        => _pos.Clients.Group(PosGroups.Location(locationId)).SendAsync("SupervisorApprovalRequested", requestDto, ct);

    public Task WeightReportedAsync(long stationId, decimal value, string unit, bool stable, CancellationToken ct = default)
        => _pos.Clients.Group(PosGroups.Station(stationId))
            .SendAsync("WeightReported", new { value, unit, stable }, ct);
}

/// <summary>
/// Server-to-agent commands over <see cref="TerminalHub"/>. Sending to the station group rather than
/// a connection id means an agent that reconnected mid-sale still receives its receipt.
/// </summary>
public sealed class TerminalNotifier : ITerminalNotifier
{
    private readonly IHubContext<TerminalHub> _hub;

    public TerminalNotifier(IHubContext<TerminalHub> hub) => _hub = hub;

    public Task PrintReceiptAsync(long stationId, object receiptPayload, int copies, CancellationToken ct = default)
        => _hub.Clients.Group(PosGroups.Station(stationId)).SendAsync("PrintReceipt", receiptPayload, copies, ct);

    public Task OpenDrawerAsync(long stationId, CancellationToken ct = default)
        => _hub.Clients.Group(PosGroups.Station(stationId)).SendAsync("OpenDrawer", ct);

    public Task DisplayPoleAsync(long stationId, string line1, string line2, CancellationToken ct = default)
        => _hub.Clients.Group(PosGroups.Station(stationId)).SendAsync("DisplayPole", line1, line2, ct);

    public Task RequestWeightAsync(long stationId, CancellationToken ct = default)
        => _hub.Clients.Group(PosGroups.Station(stationId)).SendAsync("RequestWeight", ct);

    public Task ZeroScaleAsync(long stationId, CancellationToken ct = default)
        => _hub.Clients.Group(PosGroups.Station(stationId)).SendAsync("ZeroScale", ct);

    public Task SetReaderModeAsync(long stationId, string mode, CancellationToken ct = default)
        => _hub.Clients.Group(PosGroups.Station(stationId)).SendAsync("SetReaderMode", mode, ct);

    public Task UpdateProfileAsync(long stationId, object profile, CancellationToken ct = default)
        => _hub.Clients.Group(PosGroups.Station(stationId)).SendAsync("UpdateProfile", profile, ct);
}
