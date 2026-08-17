namespace Retail25.Application.Shoppers.Dtos;

/// <summary>
/// One past visit, as a customer's phone shows it.
/// <para>
/// A summary and not a receipt. Line detail is deliberately absent: the list screen never needs it,
/// and sending every line of every past shop to a handset is a lot of somebody's shopping history to
/// put on the network for a screen that shows a date and a total.
/// </para>
/// </summary>
/// <param name="TransactionNumber">
/// What staff will ask for at the customer service desk. The database id is not that number, and
/// quoting the wrong one is how a refund conversation goes wrong.
/// </param>
/// <param name="CounterCode">Which self-checkout counter it was rung up on.</param>
public sealed record ShopperSaleDto(
    long SaleId,
    long TransactionNumber,
    DateTimeOffset CompletedAt,
    decimal Total,
    int ItemCount,
    string CounterCode);
