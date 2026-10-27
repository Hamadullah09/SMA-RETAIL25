using RetailPlus.Core.Model;

namespace RetailPlus.Core.Rules;

/// <summary>
/// Tenders in a foreign currency. A rate is the number of units of the currency one unit of home currency buys (as in
/// <see cref="FxRate"/>): with a rate of 0.0036 USD, 100 USD is worth 27,777.78 at home.
/// </summary>
public static class ForeignCurrency
{
    /// <summary>The home-currency value of an amount in the currency, to the cent.</summary>
    public static decimal ToHome(decimal foreign, decimal rate)
    {
        if (rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate), "An exchange rate is above zero.");
        return Money.Round(foreign / rate);
    }

    /// <summary>A home-currency amount in the currency, rounded up to the cent so that it is always enough.</summary>
    public static decimal ToForeign(decimal home, decimal rate)
    {
        if (rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate), "An exchange rate is above zero.");
        return Math.Ceiling(home * rate * 100m) / 100m;
    }

    /// <summary>
    /// A payment in a foreign currency: the customer hands over <paramref name="foreignGiven"/> of the currency towards
    /// <paramref name="dueHome"/>. The tender pays at most what is due; the rest of what was handed over is change,
    /// given back in home currency. Both amounts are kept on the payment.
    /// </summary>
    public static TenderEntry Pay(string tenderName, TenderKind kind, string currency, decimal rate, decimal foreignGiven, decimal dueHome)
    {
        if (foreignGiven <= 0) throw new ArgumentOutOfRangeException(nameof(foreignGiven), "The amount handed over is above zero.");
        decimal home = ToHome(foreignGiven, rate);
        decimal amount = Math.Min(home, Math.Max(0, dueHome));
        return new TenderEntry(tenderName, kind, amount, home, Currency: currency, ForeignAmount: foreignGiven, ExchangeRate: rate);
    }
}
