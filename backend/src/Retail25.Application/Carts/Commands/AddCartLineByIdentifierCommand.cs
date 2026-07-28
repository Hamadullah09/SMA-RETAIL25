using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Catalog;
using Retail25.Domain.Identification;
using Retail25.Domain.Sales;
using Retail25.Domain.Sales.Pricing;

namespace Retail25.Application.Carts.Commands;

/// <summary>
/// The single entry point for putting an item on a sale, whatever the cashier scanned, waved or
/// typed (doc 05 §Carts). It accepts a stock code, a UPC, an RFID EPC or a Type 2 random-weight
/// barcode and works out which it is.
/// </summary>
/// <param name="CartId">Cart to add to.</param>
/// <param name="Identifier">Whatever the terminal captured.</param>
/// <param name="Quantity">Quantity, defaulting to one.</param>
/// <param name="ManualPrice">A price typed by staff, honoured only with permission.</param>
/// <param name="ManualDiscount">A discount typed by staff, honoured only with permission.</param>
/// <param name="PriceLevel">Price level chosen with F5.</param>
/// <param name="Tax1Override">Tax 1 forced on or off with F6.</param>
/// <param name="Tax2Override">Tax 2 forced on or off with F7.</param>
/// <param name="LineType">Sale, return or trade-in.</param>
/// <param name="VariantId">Chosen matrix variant, where the product has one.</param>
public sealed record AddCartLineByIdentifierCommand(
    Guid CartId,
    string Identifier,
    decimal? Quantity = null,
    decimal? ManualPrice = null,
    decimal? ManualDiscount = null,
    int? PriceLevel = null,
    bool? Tax1Override = null,
    bool? Tax2Override = null,
    LineType LineType = LineType.Sale,
    Guid? VariantId = null) : IRequest<AddCartLineResult>;

/// <summary>
/// Outcome of adding a line. The quote is returned with it so the till can repaint the whole
/// total in one round trip — adding a line can re-price other lines by crossing a volume break.
/// </summary>
public sealed record AddCartLineResult(
    bool Success,
    string? Error,
    Guid? LineId,
    SalePricingResult? Quote);

public class AddCartLineHandler : IRequestHandler<AddCartLineByIdentifierCommand, AddCartLineResult>
{
    private readonly ICartStore _cartStore;
    private readonly IApplicationDbContext _db;
    private readonly ICartPricingService _pricing;

    public AddCartLineHandler(ICartStore cartStore, IApplicationDbContext db, ICartPricingService pricing)
    {
        _cartStore = cartStore;
        _db = db;
        _pricing = pricing;
    }

    public async Task<AddCartLineResult> Handle(AddCartLineByIdentifierCommand request, CancellationToken ct)
    {
        var cart = await _cartStore.GetAsync(request.CartId, ct);
        if (cart is null || cart.Status != CartStatus.Active)
        {
            return new AddCartLineResult(false, "cart.not_active", null, null);
        }

        var identifier = request.Identifier?.Trim() ?? string.Empty;
        if (identifier.Length == 0)
        {
            return new AddCartLineResult(false, "identifier.empty", null, null);
        }

        var match = await ResolveIdentifierAsync(cart.LocationId, identifier, ct);
        if (match is null)
        {
            return new AddCartLineResult(false, "product.not_found", null, null);
        }

        var line = new CartLine
        {
            CartId = request.CartId,
            ProductId = match.Product.Id,
            VariantId = request.VariantId,
            SerializedUnitId = match.SerializedUnitId,
            Source = match.Source,
            Quantity = request.Quantity ?? 1m,
            ManualUnitPrice = request.ManualPrice,
            EmbeddedUnitPrice = match.EmbeddedPrice,
            ManualDiscountPct = request.ManualDiscount,
            RequestedPriceLevel = request.PriceLevel,
            Tax1Override = request.Tax1Override,
            Tax2Override = request.Tax2Override,
            LineType = request.LineType,
            Sequence = cart.NextLineSequence,
            StockCodeSnapshot = match.Product.StockCode,
            NameSnapshot = match.Product.Name,
            UnitCostSnapshot = match.Product.AvgCost,
        };

        cart.NextLineSequence++;
        cart.Revision++;

        var existing = await _cartStore.GetLinesAsync(request.CartId, ct);
        await _cartStore.SetAsync(cart, ct);
        await _cartStore.SetLinesAsync(request.CartId, [.. existing, line], ct);

        // Re-price the whole cart: this line may have crossed a volume break or triggered a
        // bonus, either of which changes lines that were already there.
        var quote = await _pricing.QuoteAsync(request.CartId, ct);
        if (quote.IsFailure)
        {
            return new AddCartLineResult(false, quote.Error.Code, line.Id, null);
        }

        await ApplyQuoteToLinesAsync(request.CartId, quote.Value, ct);

        return new AddCartLineResult(true, null, line.Id, quote.Value);
    }

    /// <summary>
    /// Works out what was scanned. Order matters: a random-weight barcode is checked before a plain
    /// stock code because its embedded segment would otherwise be read as a code in its own right.
    /// </summary>
    private async Task<IdentifierMatch?> ResolveIdentifierAsync(Guid locationId, string identifier, CancellationToken ct)
    {
        // 1. Type 2 random-weight barcode: the tag carries a price, and the stock code is a
        //    5-digit slice of it (guide p.98). Recognised only where the store has switched the
        //    behaviour on, otherwise the digits are treated as an ordinary code.
        var scanRandomWeight = await _db.PosPolicies
            .AsNoTracking()
            .Where(p => p.LocationId == locationId)
            .Select(p => (bool?)p.ScanRandomWeightBarcodes)
            .FirstOrDefaultAsync(ct) ?? false;

        if (scanRandomWeight
            && RandomWeightBarcodeParser.TryParse(identifier, out var randomWeight)
            && randomWeight is not null)
        {
            var weighed = await _db.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.LocationId == locationId && p.StockCode == randomWeight.StockCode, ct);

            if (weighed is not null)
            {
                return new IdentifierMatch(weighed, LineSource.Barcode, randomWeight.EmbeddedPrice, null);
            }
        }

        // 2. RFID EPC: one tag is one physical unit, so it also pins the serialized unit.
        if (Domain.ValueObjects.Epc.TryCreate(identifier, out var epc) && epc is not null)
        {
            var unit = await _db.SerializedUnits
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Epc == epc.Value.Value, ct);

            if (unit is not null)
            {
                var tagged = await _db.Products
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == unit.ProductId, ct);

                if (tagged is not null)
                {
                    return new IdentifierMatch(tagged, LineSource.Rfid, null, unit.Id);
                }
            }
        }

        // 3. Stock code, then UPC.
        var upper = identifier.ToUpperInvariant();

        var byStockCode = await _db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.LocationId == locationId && p.StockCode == upper, ct);

        if (byStockCode is not null)
        {
            return new IdentifierMatch(byStockCode, LineSource.StockCode, null, null);
        }

        var byUpc = await _db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.LocationId == locationId && p.Upc == identifier, ct);

        return byUpc is null ? null : new IdentifierMatch(byUpc, LineSource.Barcode, null, null);
    }

    /// <summary>
    /// Writes the quote's decisions back onto the cart lines so the till, a second station and a
    /// reconnecting browser all see the same numbers without re-running the engine.
    /// </summary>
    private async Task ApplyQuoteToLinesAsync(Guid cartId, SalePricingResult quote, CancellationToken ct)
    {
        var lines = await _cartStore.GetLinesAsync(cartId, ct);
        var bySequence = quote.Lines.ToDictionary(l => l.Sequence);

        foreach (var line in lines)
        {
            if (!bySequence.TryGetValue(line.Sequence, out var priced))
            {
                continue;
            }

            line.UnitPrice = priced.UnitPrice;
            line.PriceOrigin = priced.PriceOrigin;
            line.LineDiscountPct = priced.LineDiscountPct;
            line.Tax1Applies = priced.Tax1Applies;
            line.Tax2Applies = priced.Tax2Applies;
            line.ChargeableQuantity = priced.ChargeableQuantity;
            line.FreeQuantity = priced.FreeQuantity;
            line.NetAmount = priced.NetAmount;
            line.Tax1Amount = priced.Tax1Amount;
            line.Tax2Amount = priced.Tax2Amount;
        }

        await _cartStore.SetLinesAsync(cartId, lines, ct);
    }

    /// <summary>What an identifier turned out to refer to.</summary>
    private sealed record IdentifierMatch(
        Product Product,
        LineSource Source,
        decimal? EmbeddedPrice,
        Guid? SerializedUnitId);
}
