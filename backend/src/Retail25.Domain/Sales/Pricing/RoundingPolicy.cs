using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// How money is rounded during a calculation. Every value comes from the location's
/// <see cref="Currency"/> row, so a store trading in a 0-decimal or 3-decimal currency, or one that
/// has abolished its smallest coin, needs configuration rather than a code change (doc 04 §4, P3/P4).
/// </summary>
/// <param name="Scale">Decimal places money is presented and tendered at.</param>
/// <param name="Mode">Midpoint behaviour. Retail convention is away-from-zero.</param>
/// <param name="MinimumTender">Smallest coin in circulation (legacy "Minimum Tender", guide p.84).</param>
public sealed record RoundingPolicy(int Scale, MidpointRounding Mode, decimal MinimumTender)
{
    public static RoundingPolicy FromCurrency(Currency currency)
        => new(currency.Scale, currency.RoundingMidpoint, currency.MinimumTender);

    public decimal Round(decimal value) => decimal.Round(value, Scale, Mode);

    /// <summary>
    /// Rounds to the nearest physical coin. Applied to cash tenders and change only; electronic
    /// tenders settle exactly (P4).
    /// </summary>
    public decimal RoundToMinimumTender(decimal value)
        => MinimumTender <= 0m ? value : decimal.Round(value / MinimumTender, 0, Mode) * MinimumTender;

    /// <summary>
    /// Splits <paramref name="amount"/> across <paramref name="weights"/> so the parts sum back to
    /// the whole exactly. Any rounding residue is given to the largest weight, which makes
    /// subtotal-discount proration deterministic and testable (doc 04 §4, P2).
    /// </summary>
    public IReadOnlyList<decimal> Allocate(decimal amount, IReadOnlyList<decimal> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var results = new decimal[weights.Count];
        if (weights.Count == 0)
        {
            return results;
        }

        var totalWeight = weights.Sum();
        var target = Round(amount);

        if (totalWeight == 0m)
        {
            // Nothing to weigh against: give the whole amount to the first slot rather than
            // silently discarding it.
            results[0] = target;
            return results;
        }

        var running = 0m;
        var largestIndex = 0;

        for (var i = 0; i < weights.Count; i++)
        {
            results[i] = Round(amount * weights[i] / totalWeight);
            running += results[i];

            if (weights[i] > weights[largestIndex])
            {
                largestIndex = i;
            }
        }

        var residue = target - running;
        if (residue != 0m)
        {
            results[largestIndex] += residue;
        }

        return results;
    }
}
