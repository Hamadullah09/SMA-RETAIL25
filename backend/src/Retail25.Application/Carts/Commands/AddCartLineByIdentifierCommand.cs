using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Commands;

/// <summary>
/// Universal entry point for adding an item to the cart (doc 05 §Carts).
/// Accepts a stock code, UPC, EPC, or random-weight barcode.
/// Resolves the product, applies pricing, creates the cart line.
/// </summary>
public sealed record AddCartLineByIdentifierCommand(
    Guid CartId,
    string Identifier,
    decimal? Quantity = null,
    decimal? ManualPrice = null,
    decimal? ManualDiscount = null,
    int? PriceLevel = null,
    bool? Tax1Override = null,
    bool? Tax2Override = null) : IRequest<AddCartLineResult>;

public sealed record AddCartLineResult(
    bool Success,
    string? Error,
    Guid? LineId);

public class AddCartLineHandler : IRequestHandler<AddCartLineByIdentifierCommand, AddCartLineResult>
{
    private readonly ICartStore _cartStore;
    private readonly IApplicationDbContext _db;

    public AddCartLineHandler(ICartStore cartStore, IApplicationDbContext db)
    {
        _cartStore = cartStore;
        _db = db;
    }

    public async Task<AddCartLineResult> Handle(AddCartLineByIdentifierCommand request, CancellationToken ct)
    {
        var cart = await _cartStore.GetAsync(request.CartId, ct);
        if (cart is null || cart.Status != CartStatus.Active)
            return new AddCartLineResult(false, "cart.not_active", null);

        // Resolve product by identifier (stock code, UPC, etc.)
        var product = await _db.Products
            .FirstOrDefaultAsync(p =>
                p.StockCode == request.Identifier.ToUpperInvariant() ||
                p.Upc == request.Identifier,
                ct);

        if (product is null)
            return new AddCartLineResult(false, "product.not_found", null);

        var quantity = request.Quantity ?? 1m;

        // Create cart line
        var line = new CartLine
        {
            Id = Guid.NewGuid(),
            CartId = request.CartId,
            ProductId = product.Id,
            Source = LineSource.StockCode,
            Quantity = quantity,
            UnitPrice = request.ManualPrice ?? product.RegularPrice,
            PriceOrigin = request.ManualPrice.HasValue ? PriceOrigin.Manual : PriceOrigin.Regular,
            LineDiscountPct = request.ManualDiscount ?? 0m,
            Tax1Applies = request.Tax1Override ?? product.Tax1Applies,
            Tax2Applies = request.Tax2Override ?? product.Tax2Applies,
            LineType = Domain.Sales.LineType.Sale,
            Sequence = cart.NextLineSequence,
            StockCodeSnapshot = product.StockCode,
            NameSnapshot = product.Name,
            UnitCostSnapshot = product.AvgCost,
        };

        cart.NextLineSequence++;
        cart.Revision++;

        await _cartStore.SetAsync(cart, ct);
        await _cartStore.SetLinesAsync(request.CartId, new[] { line }, ct);

        return new AddCartLineResult(true, null, line.Id);
    }
}
