namespace RetailPlus.Core.Rules;

public static class Money
{
    /// <summary>Rounds an amount to cents, halves away from zero (as cash registers do).</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Gross margin in percent: ((price - cost) / price) × 100, as the manual defines it.</summary>
    public static decimal MarginPercent(decimal price, decimal cost) =>
        price == 0 ? 0 : Math.Round((price - cost) / price * 100m, 3, MidpointRounding.AwayFromZero);

    /// <summary>"Suggest A Price": the price that gives the wanted gross margin on a cost.</summary>
    public static decimal SuggestPrice(decimal cost, decimal marginPercent)
    {
        if (marginPercent >= 100) throw new ArgumentOutOfRangeException(nameof(marginPercent), "A margin must be below 100%.");
        return Round(cost / (1 - marginPercent / 100m));
    }

    /// <summary>
    /// Cash rounding to the smallest coin (Setup / Options / Minimum Tender). Only applied when paying by cash.
    /// </summary>
    public static decimal RoundToMinimumTender(decimal amount, decimal minimumTender)
    {
        if (minimumTender <= 0.01m) return Round(amount);
        return Math.Round(amount / minimumTender, 0, MidpointRounding.AwayFromZero) * minimumTender;
    }
}
