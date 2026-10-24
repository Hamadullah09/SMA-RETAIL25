using RetailPlus.Core.Model;

namespace RetailPlus.Core.Rules;

/// <summary>The bonus points system (Setup / Options / Bonus Points Setup).</summary>
public static class BonusPoints
{
    /// <summary>
    /// Points earned on a sale: Points Per Dollar × amount spent before taxes. "Points are still awarded on sales that
    /// contain discounted items, but not on sales where there is already a discount on the subtotal."
    /// </summary>
    public static decimal Earned(StoreSettings settings, decimal netBeforeTax, bool hasSubtotalDiscount)
    {
        if (!settings.BonusPointsEnabled || hasSubtotalDiscount || netBeforeTax <= 0) return 0;
        return Math.Floor(netBeforeTax * settings.PointsPerDollar * 100m) / 100m;
    }

    public static bool QualifiesForReward(StoreSettings settings, decimal points, bool hasSubtotalDiscount) =>
        settings.BonusPointsEnabled && settings.PointsRequired > 0 && points >= settings.PointsRequired && !hasSubtotalDiscount;

    /// <summary>
    /// The reward: a percent discount, a fixed credit, or both ("a 10% discount up to a maximum of 20 dollars").
    /// Taking it reduces the client's points by Minimum Required.
    /// </summary>
    public static decimal RewardAmount(StoreSettings settings, decimal subtotal)
    {
        decimal percent = settings.PercentDiscountReward ? Money.Round(subtotal * settings.RewardPercent / 100m) : 0;
        if (settings.PercentDiscountReward && settings.FixedDiscountReward) return Math.Min(percent, settings.RewardAmount);
        if (settings.PercentDiscountReward) return percent;
        if (settings.FixedDiscountReward) return Math.Min(settings.RewardAmount, subtotal);
        return 0;
    }
}

/// <summary>Late charges on overdue invoices (Setup / Options / Overdue Interest Charges).</summary>
public static class LateCharges
{
    /// <summary>
    /// Months overdue counted from the last payment date (or the invoice date) once the grace period has passed, and
    /// the penalty: balance × monthly rate × months. Returns (0, 0) inside the grace period.
    /// </summary>
    public static (int Months, decimal Penalty) Calculate(decimal balance, DateOnly invoiceDate, DateOnly? lastPaid,
        DateOnly today, decimal ratePerMonth, int graceDays)
    {
        if (balance <= 0 || ratePerMonth <= 0) return (0, 0);
        var from = lastPaid ?? invoiceDate;
        int days = today.DayNumber - from.DayNumber;
        if (days <= graceDays) return (0, 0);
        int months = Math.Max(1, (int)Math.Ceiling((days - graceDays) / 30.0));
        return (months, Money.Round(balance * ratePerMonth / 100m * months));
    }

    /// <summary>Splits a payment: the penalty is paid first, the rest goes to the balance owing.</summary>
    public static (decimal ToPenalty, decimal ToBalance) SplitPayment(decimal amount, decimal outstandingPenalty)
    {
        decimal toPenalty = Math.Min(amount, Math.Max(0, outstandingPenalty));
        return (toPenalty, amount - toPenalty);
    }
}

/// <summary>Order quantity formulas of File / New purchase order.</summary>
public static class Reordering
{
    /// <summary>
    /// Suggested order in pieces before case rounding.
    /// </summary>
    /// <param name="averageWeeklySales">Average sales per week over the last 3 weeks.</param>
    /// <param name="lastYearSameMonth">Units sold in the same month last year.</param>
    public static decimal SuggestedPieces(OrderMethod method, Item item, decimal averageWeeklySales, decimal lastYearSameMonth)
    {
        decimal available = item.OnHand + item.OnOrder;
        decimal pieces = method switch
        {
            OrderMethod.Blank => 0,
            OrderMethod.OneWeek => averageWeeklySales * 1 - available + item.CustomerOrdered,
            OrderMethod.TwoWeeks => averageWeeklySales * 2 - available + item.CustomerOrdered,
            OrderMethod.ReorderPoints => item.OnHand < item.ReorderPoint
                ? (item.ReorderQuantity > 0 ? item.ReorderQuantity : item.BaseStock - item.OnHand)
                : 0,
            OrderMethod.MonthlySales => lastYearSameMonth + item.BaseStock - available,
            _ => 0,
        };
        return Math.Max(0, pieces);
    }

    /// <summary>Rounds to the nearest full case: returns cases when the case quantity is above 1, pieces otherwise.</summary>
    public static decimal ToOrderQuantity(decimal pieces, decimal caseQuantity)
    {
        if (pieces <= 0) return 0;
        if (caseQuantity <= 1) return Math.Ceiling(pieces);
        return Math.Max(1, Math.Round(pieces / caseQuantity, 0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Weighted average cost after receiving goods, with an optional share of shipping cost.</summary>
    public static decimal AverageCostAfterReceipt(decimal onHand, decimal averageCost, decimal received, decimal unitCost, decimal shippingShare)
    {
        decimal oldQty = Math.Max(0, onHand);
        decimal total = oldQty + received;
        if (total <= 0) return unitCost;
        decimal oldAvg = averageCost > 0 ? averageCost : unitCost;
        return Math.Round((oldQty * oldAvg + received * unitCost + shippingShare) / total, 3, MidpointRounding.AwayFromZero);
    }
}

/// <summary>UPC type 2 random-weight barcodes from deli scales: 2ABBBBCDDDDE.</summary>
public static class RandomWeightBarcode
{
    /// <summary>
    /// Parses a random-weight barcode: the item code is ABBBB (5 digits), the embedded net price is DDDD in 99.99 form.
    /// </summary>
    public static bool TryParse(string? code, out string itemCode, out decimal price)
    {
        itemCode = "";
        price = 0;
        if (code is null || code.Length != 12 || code[0] != '2' || !code.All(char.IsAsciiDigit)) return false;
        itemCode = code.Substring(1, 5);
        price = int.Parse(code.AsSpan(7, 4), System.Globalization.CultureInfo.InvariantCulture) / 100m;
        return true;
    }

    /// <summary>Quantity sold: embedded price divided by Price 1; 1 when Price 1 is zero.</summary>
    public static decimal Quantity(decimal embeddedPrice, decimal price1) =>
        price1 > 0 ? Math.Round(embeddedPrice / price1, 3, MidpointRounding.AwayFromZero) : 1;
}
