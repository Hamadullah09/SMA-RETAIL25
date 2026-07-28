using Retail25.Domain.Common;
using Retail25.Domain.Sales.Pricing;

namespace Retail25.Application.Abstractions;

/// <summary>
/// Prices a cart by assembling the configuration the engine needs and running it.
/// <para>
/// This is the only path to a total. Nothing else in the system is permitted to add up a cart —
/// having a single entry point is what makes the golden-file suite meaningful, because the totals
/// the tests pin are the same totals the till, the receipt and the accounting export use.
/// </para>
/// </summary>
public interface ICartPricingService
{
    /// <summary>
    /// Prices the cart as it currently stands. Fails only when the store is not configured — a
    /// missing tax configuration or currency — never because of the cart's contents.
    /// </summary>
    Task<Result<SalePricingResult>> QuoteAsync(Guid cartId, CancellationToken ct = default);
}
