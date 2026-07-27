namespace Retail25.Application.Abstractions;

/// <summary>
/// Abstraction over SignalR POS hub notifications. Application never references SignalR directly;
/// this port is implemented by the infrastructure layer.
/// </summary>
public interface IPosNotifier
{
    Task CartUpdatedAsync(Guid locationId, Guid cartId, object cartDto, int revision, CancellationToken ct = default);

    Task CartLinesAddedAsync(Guid locationId, Guid cartId, object[] lineDtos, CancellationToken ct = default);

    Task CartLineRejectedAsync(Guid locationId, string epc, string reason, CancellationToken ct = default);

    Task TotalsChangedAsync(Guid locationId, Guid cartId, object totalsDto, CancellationToken ct = default);

    Task StockLevelChangedAsync(Guid locationId, Guid productId, decimal newOnHand, CancellationToken ct = default);

    Task ProductChangedAsync(Guid locationId, Guid productId, CancellationToken ct = default);

    Task ProductDeletedAsync(Guid locationId, Guid productId, CancellationToken ct = default);

    Task TagStreamStatusAsync(Guid stationId, bool readerOnline, int readRate, CancellationToken ct = default);

    Task PeripheralStatusAsync(Guid stationId, object statusDto, CancellationToken ct = default);
}
