using MediatR;
using Retail25.Application.Abstractions;
using Retail25.Domain.Sales.Pricing;

namespace Retail25.Application.Carts.Queries;

/// <summary>
/// Prices a cart without changing it. The till calls this after any change it did not itself make —
/// attaching a customer, a second station adding a line, reconnecting after a dropout — so the
/// screen always shows what the server would actually charge.
/// </summary>
/// <param name="CartId">Cart to price.</param>
public sealed record QuoteCartQuery(Guid CartId) : IRequest<QuoteCartResult>;

/// <summary>
/// The priced cart, or the reason it could not be priced.
/// </summary>
/// <param name="Success">Whether a quote was produced.</param>
/// <param name="Error">Stable error key when it was not.</param>
/// <param name="Quote">The priced result.</param>
public sealed record QuoteCartResult(bool Success, string? Error, SalePricingResult? Quote);

public class QuoteCartHandler : IRequestHandler<QuoteCartQuery, QuoteCartResult>
{
    private readonly ICartPricingService _pricing;

    public QuoteCartHandler(ICartPricingService pricing) => _pricing = pricing;

    public async Task<QuoteCartResult> Handle(QuoteCartQuery request, CancellationToken ct)
    {
        var result = await _pricing.QuoteAsync(request.CartId, ct);

        return result.IsSuccess
            ? new QuoteCartResult(true, null, result.Value)
            : new QuoteCartResult(false, result.Error.Code, null);
    }
}
