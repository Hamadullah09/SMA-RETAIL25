using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// The unit price a line resolved to, and why (doc 04 §2). <see cref="Origin"/> is persisted on
/// <c>SaleLine</c> so a receipt can be explained months later.
/// </summary>
/// <param name="ChargeableQuantity">
/// Differs from the entered quantity for bonus pricing (free units are not charged) and for
/// random-weight barcodes (quantity is derived from the embedded price).
/// </param>
public sealed record PriceResolution(decimal UnitPrice, PriceOrigin Origin, decimal ChargeableQuantity, decimal EffectiveQuantity);

/// <summary>
/// Walks the configured precedence ladder top to bottom and stops at the first rule that matches
/// (doc 04 §2). The order comes from <see cref="PricingRuleSetting"/> rows, not from this file.
/// </summary>
public static class PriceResolver
{
    /// <summary>Random-weight quantities are approximate by the guide's own admission (p.98); 4 dp is the agreed precision.</summary>
    public const int WeightScale = 4;

    public static PriceResolution Resolve(LineInput line, PricingContext context)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var rule in context.ActiveRules)
        {
            var resolution = Evaluate(rule.RuleKey, line, context);
            if (resolution is not null)
            {
                return resolution;
            }
        }

        // The ladder is data, so it can be misconfigured. Regular price is the guaranteed floor
        // (guide p.32) rather than an exception that would stop a till mid-sale.
        return new PriceResolution(line.Product.RegularPrice, PriceOrigin.Regular, line.Quantity, line.Quantity);
    }

    private static PriceResolution? Evaluate(string ruleKey, LineInput line, PricingContext context) => ruleKey switch
    {
        PricingRuleKeys.ManualOverride => ManualOverride(line),
        PricingRuleKeys.RandomWeight => RandomWeight(line),
        PricingRuleKeys.BonusPricing => Bonus(line),
        PricingRuleKeys.VolumeBreak => VolumeBreak(line),
        PricingRuleKeys.RequestedLevel => RequestedLevel(line),
        PricingRuleKeys.ClientLevel => ClientLevel(line, context),
        PricingRuleKeys.SaleWindow => SaleWindow(line, context),
        PricingRuleKeys.RegularPrice => new PriceResolution(line.Product.RegularPrice, PriceOrigin.Regular, line.Quantity, line.Quantity),
        _ => null,
    };

    /// <summary>Rule 1 — a price typed on the item-detail window wins outright (guide p.6).</summary>
    private static PriceResolution? ManualOverride(LineInput line)
    {
        if (line.ManualUnitPrice is not { } manual)
        {
            return null;
        }

        // For a weighed item the override is a price per unit weight, so the weight derived from
        // the embedded price still governs quantity (doc 04 §5).
        if (line.Source == LineSource.RandomWeight && line.EmbeddedPrice is { } embedded)
        {
            var weight = DeriveWeight(embedded, line.Product.RegularPrice);
            return new PriceResolution(manual, PriceOrigin.Manual, weight, weight);
        }

        return new PriceResolution(manual, PriceOrigin.Manual, line.Quantity, line.Quantity);
    }

    /// <summary>Rule 2 — Type 2 barcode: the tag carries the money, the product carries the rate (guide p.98).</summary>
    private static PriceResolution? RandomWeight(LineInput line)
    {
        if (line.Source != LineSource.RandomWeight || line.EmbeddedPrice is not { } embedded)
        {
            return null;
        }

        var unitPrice = line.Product.RegularPrice;

        // No weight rate on the product: the embedded amount is simply the line price (doc 04 §5).
        if (unitPrice <= 0m)
        {
            return new PriceResolution(embedded, PriceOrigin.RandomWeight, 1m, 1m);
        }

        var weight = DeriveWeight(embedded, unitPrice);
        return new PriceResolution(unitPrice, PriceOrigin.RandomWeight, weight, weight);
    }

    /// <summary>Rule 3 — buy X get Y: free units are priced at zero by not being charged (guide p.35).</summary>
    private static PriceResolution? Bonus(LineInput line)
    {
        if (line.Bonus is not { } bonus || bonus.BuyQty <= 0m || line.Quantity < bonus.BuyQty)
        {
            return null;
        }

        var freeUnits = Math.Floor(line.Quantity / bonus.BuyQty) * bonus.FreeQty;
        var chargeable = line.Quantity - freeUnits;

        return chargeable >= line.Quantity
            ? null
            : new PriceResolution(line.Product.RegularPrice, PriceOrigin.Bonus, Math.Max(0m, chargeable), line.Quantity);
    }

    /// <summary>Rule 4 — the highest break point the quantity qualifies for (guide p.34).</summary>
    private static PriceResolution? VolumeBreak(LineInput line)
    {
        var qualifying = line.BreakPoints
            .Where(b => line.Quantity >= b.MinQuantity)
            .OrderByDescending(b => b.MinQuantity)
            .FirstOrDefault();

        if (qualifying is null)
        {
            return null;
        }

        var price = FindLevelPrice(line, qualifying.Level);
        return price is null
            ? null
            : new PriceResolution(price.Value, PriceOrigin.Break, line.Quantity, line.Quantity);
    }

    /// <summary>Rule 5 — the level the cashier picked with F5, if the product actually has one (guide p.6, p.34).</summary>
    private static PriceResolution? RequestedLevel(LineInput line)
    {
        if (line.RequestedPriceLevel is not { } level)
        {
            return null;
        }

        var price = FindLevelPrice(line, level);
        return price is null
            ? null
            : new PriceResolution(price.Value, OriginForLevel(level), line.Quantity, line.Quantity);
    }

    /// <summary>
    /// Rule 6 — the level on the customer record. A missing level falls through to the next rule
    /// rather than erroring, which is the legacy contract (guide p.52).
    /// </summary>
    private static PriceResolution? ClientLevel(LineInput line, PricingContext context)
    {
        if (context.Customer is not { } customer)
        {
            return null;
        }

        var price = FindLevelPrice(line, customer.PriceLevel);
        return price is null
            ? null
            : new PriceResolution(price.Value, PriceOrigin.ClientLevel, line.Quantity, line.Quantity);
    }

    /// <summary>Rule 7 — the date-ranged promotion (guide p.35).</summary>
    private static PriceResolution? SaleWindow(LineInput line, PricingContext context)
    {
        if (line.Sale is not { } sale || !sale.IsActive(context.BusinessDate))
        {
            return null;
        }

        var price = line.Product.RegularPrice * (1m - (sale.DiscountPct / 100m));
        return new PriceResolution(context.Rounding.Round(price), PriceOrigin.Sale, line.Quantity, line.Quantity);
    }

    /// <summary>
    /// A level that exists but is zero counts as "no level price" and falls through, matching the
    /// legacy behaviour described at guide p.52.
    /// </summary>
    private static decimal? FindLevelPrice(LineInput line, int level)
    {
        var entry = line.PriceLevels.FirstOrDefault(p => p.Level == level);
        return entry is null || entry.Price <= 0m ? null : entry.Price;
    }

    private static decimal DeriveWeight(decimal embeddedPrice, decimal unitPrice)
        => unitPrice <= 0m
            ? 1m
            : decimal.Round(embeddedPrice / unitPrice, WeightScale, MidpointRounding.AwayFromZero);

    private static PriceOrigin OriginForLevel(int level) => level switch
    {
        2 => PriceOrigin.Level2,
        3 => PriceOrigin.Level3,
        4 => PriceOrigin.Level4,
        _ => PriceOrigin.Regular,
    };
}
