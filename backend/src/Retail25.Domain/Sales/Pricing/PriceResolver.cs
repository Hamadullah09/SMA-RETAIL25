using Retail25.Domain.Catalog;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// Catalog pricing rows for one product, gathered by the caller so the resolver stays pure.
/// </summary>
/// <param name="Prices">Price levels 1–4 defined for the product.</param>
/// <param name="Breaks">Volume break points.</param>
/// <param name="Bonus">Buy-X-get-Y configuration, if any.</param>
/// <param name="SalePricings">Promotional windows; the resolver picks the one covering the business date.</param>
public sealed record ProductPricingData(
    IReadOnlyList<ProductPrice> Prices,
    IReadOnlyList<PriceBreak> Breaks,
    BonusPricing? Bonus,
    IReadOnlyList<SalePricing> SalePricings)
{
    public static readonly ProductPricingData None = new([], [], null, []);
}

/// <summary>
/// Unit-price resolution (doc 04 §2). The ladder is evaluated top to bottom and the first match
/// wins; each outcome records a <see cref="PriceOrigin"/>.
/// <list type="number">
///   <item>Manual override (needs permission)</item>
///   <item>Random-weight embedded price</item>
///   <item>Bonus / BOGO</item>
///   <item>Volume break point</item>
///   <item>Requested price level (F5, needs permission)</item>
///   <item>Customer's assigned price level</item>
///   <item>Sale-pricing window</item>
///   <item>Regular price</item>
/// </list>
/// <para>
/// A price level that is absent — or present but zero — falls through to the next rule rather than
/// failing, which is the documented legacy behaviour (guide p.52).
/// </para>
/// </summary>
public static class PriceResolver
{
    public static PriceResolution Resolve(LineInput input, ProductPricingData catalog, PricingContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(context);

        var product = input.Product;
        var quantity = input.Quantity;

        // Rule 1 — a price typed by staff wins outright, but only if they are allowed to type one.
        if (input.ManualUnitPrice.HasValue && context.Permissions.CanOverridePrice)
        {
            // A manual price on a random-weight line is a price *per unit of weight*, so the
            // quantity still comes from the embedded total (guide p.98).
            if (input.Source == PriceSource.RandomWeight && input.EmbeddedUnitPrice.HasValue)
            {
                var overriddenWeight = DeriveRandomWeightQuantity(
                    input.EmbeddedUnitPrice.Value,
                    input.ManualUnitPrice.Value);

                return new PriceResolution(
                    input.ManualUnitPrice.Value,
                    PriceOrigin.Manual,
                    overriddenWeight,
                    0m,
                    null);
            }

            return new PriceResolution(input.ManualUnitPrice.Value, PriceOrigin.Manual, quantity, 0m, null);
        }

        // Rule 2 — Type 2 random-weight barcode: the tag carries a total price, and the item's
        // unit price tells us how much was weighed out.
        if (input.Source == PriceSource.RandomWeight && input.EmbeddedUnitPrice.HasValue)
        {
            var unitPrice = product.RegularPrice;
            var embedded = input.EmbeddedUnitPrice.Value;

            // "If the Price 1 is left blank or is zero, a quantity of 1 will be subtracted from the
            // Quantity On Hand for each package sold" (guide p.98) — the tag price becomes the line.
            if (unitPrice <= 0m)
            {
                return new PriceResolution(embedded, PriceOrigin.RandomWeight, 1m, 0m, null);
            }

            return new PriceResolution(
                unitPrice,
                PriceOrigin.RandomWeight,
                DeriveRandomWeightQuantity(embedded, unitPrice),
                0m,
                null);
        }

        // Rule 3 — bonus pricing. Free units are still delivered, so they are reported separately
        // rather than being folded into the charged quantity.
        if (catalog.Bonus is not null && catalog.Bonus.BuyQty > 0m && quantity >= catalog.Bonus.BuyQty)
        {
            var free = Math.Floor(quantity / catalog.Bonus.BuyQty) * catalog.Bonus.FreeQty;

            // Never give away more than was asked for.
            free = Math.Min(free, quantity);

            if (free > 0m)
            {
                return new PriceResolution(
                    RegularPriceOf(product, catalog),
                    PriceOrigin.Bonus,
                    quantity - free,
                    free,
                    null);
            }
        }

        // Rule 4 — volume break points. The highest qualifying threshold wins.
        var qualifyingBreak = catalog.Breaks
            .Where(b => b.MinQuantity > 0m && quantity >= b.MinQuantity)
            .OrderByDescending(b => b.MinQuantity)
            .FirstOrDefault();

        if (qualifyingBreak is not null && TryGetLevelPrice(catalog, qualifyingBreak.Level, out var breakPrice))
        {
            return new PriceResolution(breakPrice, PriceOrigin.Break, quantity, 0m, qualifyingBreak.Level);
        }

        // Rule 5 — a level the cashier picked at the till.
        if (input.RequestedPriceLevel.HasValue
            && context.Permissions.CanSelectPriceLevel
            && TryGetLevelPrice(catalog, input.RequestedPriceLevel.Value, out var requestedPrice))
        {
            return new PriceResolution(
                requestedPrice,
                OriginForLevel(input.RequestedPriceLevel.Value),
                quantity,
                0m,
                input.RequestedPriceLevel.Value);
        }

        // Rule 6 — the level standing on the customer's record (guide p.52).
        var customerLevel = context.Customer?.PriceLevel;
        if (customerLevel is > 1 && TryGetLevelPrice(catalog, customerLevel.Value, out var customerPrice))
        {
            return new PriceResolution(customerPrice, PriceOrigin.ClientLevel, quantity, 0m, customerLevel.Value);
        }

        // Rule 7 — a promotional window covering the business date.
        var activeSale = catalog.SalePricings.FirstOrDefault(s => s.IsActive(context.BusinessDate));
        if (activeSale is not null)
        {
            var discounted = RegularPriceOf(product, catalog) * (1m - (activeSale.DiscountPct / 100m));
            return new PriceResolution(
                context.Rounding.Round(discounted),
                PriceOrigin.Sale,
                quantity,
                0m,
                null);
        }

        // Rule 8 — the shelf price.
        return new PriceResolution(RegularPriceOf(product, catalog), PriceOrigin.Regular, quantity, 0m, 1);
    }

    /// <summary>
    /// Weight sold = total price on the tag ÷ price per unit of weight. The legacy guide is explicit
    /// that this is approximate; four decimal places is the agreed tolerance (doc 04 §5).
    /// </summary>
    private static decimal DeriveRandomWeightQuantity(decimal embeddedPrice, decimal unitPrice)
        => unitPrice <= 0m ? 1m : decimal.Round(embeddedPrice / unitPrice, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Level 1 is the regular price. Where an explicit level-1 row exists it wins, so a catalog
    /// import that populated the price table rather than the product column still prices correctly.
    /// </summary>
    private static decimal RegularPriceOf(Product product, ProductPricingData catalog)
        => TryGetLevelPrice(catalog, 1, out var level1) ? level1 : product.RegularPrice;

    private static bool TryGetLevelPrice(ProductPricingData catalog, int level, out decimal price)
    {
        var row = catalog.Prices.FirstOrDefault(p => p.Level == level);

        // A level priced at zero means "not set for this item", not "free" (guide p.52).
        if (row is null || row.Price <= 0m)
        {
            price = 0m;
            return false;
        }

        price = row.Price;
        return true;
    }

    private static PriceOrigin OriginForLevel(int level) => level switch
    {
        2 => PriceOrigin.Level2,
        3 => PriceOrigin.Level3,
        4 => PriceOrigin.Level4,
        _ => PriceOrigin.Regular,
    };
}
