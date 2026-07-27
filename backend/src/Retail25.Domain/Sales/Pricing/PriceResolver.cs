using Retail25.Domain.Catalog;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// Pure calculation engine for unit-price resolution (doc 04 §2).
/// Precedence ladder: manual override → random-weight → bonus/BOGO → volume break →
/// requested level → customer level → sale pricing → regular price.
/// First match wins. Each step records PriceOrigin for receipt reproducibility.
/// </summary>
public static class PriceResolver
{
    public static PriceResolution Resolve(
        LineInput input,
        IReadOnlyList<ProductPrice> productPrices,
        IReadOnlyList<PriceBreak> priceBreaks,
        BonusPricing? bonusPricing,
        SalePricing? salePricing)
    {
        var product = input.Product;
        var qty = input.Quantity;

        // Rule 1: Manual override
        if (input.ManualUnitPrice.HasValue)
        {
            return new PriceResolution(
                input.ManualUnitPrice.Value,
                PriceOrigin.Manual,
                qty,
                input.Tax1Override ?? product.Tax1Applies,
                input.Tax2Override ?? product.Tax2Applies,
                input.Type == LineType.Return);
        }

        // Rule 2: Random-weight embedded price (doc 04 §5)
        if (input.Source == PriceSource.RandomWeight && input.ManualUnitPrice.HasValue)
        {
            var unitPrice = product.RegularPrice;
            var embeddedPrice = input.ManualUnitPrice.Value;
            var derivedQty = unitPrice > 0 ? embeddedPrice / unitPrice : 1m;
            return new PriceResolution(unitPrice, PriceOrigin.RandomWeight, derivedQty,
                input.Tax1Override ?? product.Tax1Applies,
                input.Tax2Override ?? product.Tax2Applies,
                false);
        }

        // Rule 3: Bonus / BOGO (guide p.35)
        if (bonusPricing is not null && qty >= bonusPricing.BuyQty)
        {
            var buyQty = bonusPricing.BuyQty;
            var freeQty = bonusPricing.FreeQty;
            var chargeable = qty - Math.Floor(qty / buyQty) * freeQty;
            if (chargeable < qty)
            {
                return new PriceResolution(product.RegularPrice, PriceOrigin.Bonus, chargeable,
                    input.Tax1Override ?? product.Tax1Applies,
                    input.Tax2Override ?? product.Tax2Applies,
                    input.Type == LineType.Return);
            }
        }

        // Rule 4: Volume break point (guide p.34)
        var qualifyingBreak = priceBreaks
            .Where(pb => qty >= pb.MinQuantity)
            .OrderByDescending(pb => pb.MinQuantity)
            .FirstOrDefault();

        if (qualifyingBreak is not null)
        {
            var breakPrice = productPrices.FirstOrDefault(pp => pp.Level == qualifyingBreak.Level);
            if (breakPrice is not null)
            {
                return new PriceResolution(breakPrice.Price, PriceOrigin.Break, qty,
                    input.Tax1Override ?? product.Tax1Applies,
                    input.Tax2Override ?? product.Tax2Applies,
                    input.Type == LineType.Return);
            }
        }

        // Rule 5: Requested price level F5 (guide p.6, p.34)
        if (input.RequestedPriceLevel.HasValue)
        {
            var levelPrice = productPrices.FirstOrDefault(pp => pp.Level == input.RequestedPriceLevel.Value);
            if (levelPrice is not null)
            {
                var origin = input.RequestedPriceLevel.Value switch
                {
                    2 => PriceOrigin.Level2,
                    3 => PriceOrigin.Level3,
                    4 => PriceOrigin.Level4,
                    _ => PriceOrigin.Level2,
                };
                return new PriceResolution(levelPrice.Price, origin, qty,
                    input.Tax1Override ?? product.Tax1Applies,
                    input.Tax2Override ?? product.Tax2Applies,
                    input.Type == LineType.Return);
            }
        }

        // Rule 6: Customer's assigned price level (guide p.52)
        // Caller provides this via the customer's profile; we check the product prices.
        // This is handled by the caller setting RequestedPriceLevel from Customer.PriceLevel.

        // Rule 7: Sale pricing window (guide p.35)
        if (salePricing is not null)
        {
            var salePrice = product.RegularPrice * (1 - salePricing.DiscountPct / 100m);
            return new PriceResolution(salePrice, PriceOrigin.Sale, qty,
                input.Tax1Override ?? product.Tax1Applies,
                input.Tax2Override ?? product.Tax2Applies,
                input.Type == LineType.Return);
        }

        // Rule 8: Regular price (fallback, guide p.32)
        return new PriceResolution(product.RegularPrice, PriceOrigin.Regular, qty,
            input.Tax1Override ?? product.Tax1Applies,
            input.Tax2Override ?? product.Tax2Applies,
            input.Type == LineType.Return);
    }
}
