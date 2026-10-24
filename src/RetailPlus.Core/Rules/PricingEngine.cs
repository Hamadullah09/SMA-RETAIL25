using RetailPlus.Core.Model;

namespace RetailPlus.Core.Rules;

/// <summary>Result of pricing a quantity of an item.</summary>
/// <param name="UnitPrice">Price per unit before line discounts.</param>
/// <param name="DiscountAmount">Total discount on the line (sale pricing, bonus pricing, client discount).</param>
/// <param name="Explanation">Which rule set the price, for the Item Detail window.</param>
public sealed record PriceResult(decimal UnitPrice, decimal DiscountAmount, string Explanation)
{
    public decimal LineTotal(decimal quantity) => Money.Round(UnitPrice * quantity - DiscountAmount);
}

/// <summary>
/// The Pricing tab rules from the manual: price levels, price break points, date-limited sale pricing and
/// "buy X get Y free" bonus pricing, plus the client's assigned price level and usual discount.
/// </summary>
public static class PricingEngine
{
    public static decimal PriceAtLevel(Item item, int level) => level switch
    {
        2 => item.Price2,
        3 => item.Price3,
        4 => item.Price4,
        _ => item.Price1,
    };

    public static PriceResult Price(Item item, decimal quantity, DateOnly today, int clientPriceLevel = 0, decimal clientDiscountPercent = 0)
    {
        decimal unit = item.Price1;
        string why = "regular price";

        // "A customer always pays the assigned price level if it was entered for any given item."
        if (clientPriceLevel is >= 2 and <= 4 && PriceAtLevel(item, clientPriceLevel) > 0)
        {
            unit = PriceAtLevel(item, clientPriceLevel);
            why = $"client price level {clientPriceLevel}";
        }
        else
        {
            // Break points: Level 2 from BreakQuantity1, Level 3 from BreakQuantity2, Level 4 from BreakQuantity3.
            var qty = Math.Abs(quantity);
            if (item.BreakQuantity3 > 0 && qty >= item.BreakQuantity3 && item.Price4 > 0) { unit = item.Price4; why = "break point level 4"; }
            else if (item.BreakQuantity2 > 0 && qty >= item.BreakQuantity2 && item.Price3 > 0) { unit = item.Price3; why = "break point level 3"; }
            else if (item.BreakQuantity1 > 0 && qty >= item.BreakQuantity1 && item.Price2 > 0) { unit = item.Price2; why = "break point level 2"; }
        }

        decimal discount = 0;
        // Sale pricing: a discount (percent) between the start and end dates.
        if (item.SalePrice > 0 && item.SaleStart is { } start && item.SaleEnd is { } end && today >= start && today <= end)
        {
            discount += Money.Round(unit * quantity * item.SalePrice / 100m);
            why += $", sale {item.SalePrice:0.##}% off";
        }

        // Bonus pricing: buy X, get Y free.
        if (item.BonusPricing && item.BonusBuyQuantity > 0 && item.BonusGetQuantity > 0 && quantity > 0)
        {
            int group = item.BonusBuyQuantity + item.BonusGetQuantity;
            decimal free = Math.Floor(quantity / group) * item.BonusGetQuantity;
            if (free > 0)
            {
                discount += Money.Round(free * unit);
                why += $", buy {item.BonusBuyQuantity} get {item.BonusGetQuantity} free";
            }
        }

        if (clientDiscountPercent > 0)
        {
            var remaining = unit * quantity - discount;
            discount += Money.Round(remaining * clientDiscountPercent / 100m);
            why += $", client discount {clientDiscountPercent:0.##}%";
        }

        return new PriceResult(unit, discount, why);
    }
}
