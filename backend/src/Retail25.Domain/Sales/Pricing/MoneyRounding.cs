using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// The rounding policy the engine works to, lifted out of <see cref="Currency"/> so the pipeline
/// stays a pure function over plain values (doc 04 §4, decisions P3 and P4).
/// <para>
/// <see cref="Scale"/> and <see cref="Mode"/> govern every monetary rounding; <see cref="MinimumTender"/>
/// governs cash tenders and change only. All three are configuration, never constants in a method body.
/// </para>
/// </summary>
public sealed record MoneyRounding(int Scale, MidpointRounding Mode, decimal MinimumTender)
{
    /// <summary>Two decimal places, away from zero, penny tendering — the seeded retail convention.</summary>
    public static readonly MoneyRounding Retail = new(2, MidpointRounding.AwayFromZero, 0.01m);

    public static MoneyRounding FromCurrency(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new MoneyRounding(currency.Scale, currency.RoundingMidpoint, currency.MinimumTender);
    }

    /// <summary>Rounds to the presentation scale. Applied once per line per tax, never on a running total.</summary>
    public decimal Round(decimal amount) => decimal.Round(amount, Scale, Mode);

    /// <summary>Rounds a cash amount to the smallest coin in circulation (guide p.84).</summary>
    public decimal RoundCash(decimal amount)
    {
        if (MinimumTender <= 0m)
        {
            return Round(amount);
        }

        var steps = decimal.Round(amount / MinimumTender, 0, Mode);
        return Round(steps * MinimumTender);
    }
}
