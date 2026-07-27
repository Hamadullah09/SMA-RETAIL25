using System.Globalization;

namespace Retail25.Domain.ValueObjects;

/// <summary>
/// A monetary amount in a specific currency.
/// <para>
/// Stored at <see cref="StorageScale"/> (4 dp) and only rounded to a currency's presentation scale
/// at document and tender boundaries — this is what prevents penny drift across a long receipt.
/// The scale, rounding mode and smallest coin are <b>configuration</b> (see
/// <c>Retail25.Domain.Configuration.Currency</c>), never constants in a calculation.
/// </para>
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    /// <summary>Ledger precision. Chosen so unit prices of fractional-cent commodities survive.</summary>
    public const int StorageScale = 4;

    public Money(decimal amount, string currencyCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);
        Amount = decimal.Round(amount, StorageScale, MidpointRounding.AwayFromZero);
        CurrencyCode = currencyCode.ToUpperInvariant();
    }

    public decimal Amount { get; }

    /// <summary>ISO 4217 alphabetic code. Currencies themselves are database rows.</summary>
    public string CurrencyCode { get; }

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public static Money Zero(string currencyCode) => new(0m, currencyCode);

    public Money Add(Money other) => new(Amount + Same(other).Amount, CurrencyCode);

    public Money Subtract(Money other) => new(Amount - Same(other).Amount, CurrencyCode);

    public Money Multiply(decimal factor) => new(Amount * factor, CurrencyCode);

    public Money Divide(decimal divisor) => divisor == 0m
        ? throw new DivideByZeroException("Cannot divide a monetary amount by zero.")
        : new Money(Amount / divisor, CurrencyCode);

    public Money Negate() => new(-Amount, CurrencyCode);

    public Money Abs() => new(Math.Abs(Amount), CurrencyCode);

    /// <summary>
    /// Rounds to a presentation scale using the supplied mode. Both come from currency
    /// configuration so a store using 3-decimal or 0-decimal currencies needs no code change.
    /// </summary>
    public Money RoundTo(int scale, MidpointRounding mode)
        => new(decimal.Round(Amount, scale, mode), CurrencyCode);

    /// <summary>
    /// Rounds to the smallest physical coin accepted (legacy "Minimum Tender", user guide p.84).
    /// Used for cash tenders and change only; electronic tenders stay exact.
    /// </summary>
    public Money RoundToNearest(decimal increment, MidpointRounding mode)
    {
        if (increment <= 0m)
        {
            return this;
        }

        var steps = decimal.Round(Amount / increment, 0, mode);
        return new Money(steps * increment, CurrencyCode);
    }

    /// <summary>
    /// Splits an amount into <paramref name="weights"/> proportions without losing or inventing a
    /// minor unit. The rounding residue goes to the largest weight, which is the documented
    /// behaviour for subtotal-discount proration and freight allocation.
    /// </summary>
    public IReadOnlyList<Money> Allocate(IReadOnlyList<decimal> weights, int scale, MidpointRounding mode)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0)
        {
            return [];
        }

        var totalWeight = weights.Sum();
        if (totalWeight == 0m)
        {
            // Degenerate input (all-zero weights): give everything to the first slot rather than
            // silently dropping the amount.
            var only = new Money[weights.Count];
            for (var i = 0; i < weights.Count; i++)
            {
                only[i] = Zero(CurrencyCode);
            }

            only[0] = this;
            return only;
        }

        var results = new Money[weights.Count];
        var running = 0m;
        var largestIndex = 0;

        for (var i = 0; i < weights.Count; i++)
        {
            var share = decimal.Round(Amount * weights[i] / totalWeight, scale, mode);
            results[i] = new Money(share, CurrencyCode);
            running += share;

            if (weights[i] > weights[largestIndex])
            {
                largestIndex = i;
            }
        }

        var residue = decimal.Round(Amount, scale, mode) - running;
        if (residue != 0m)
        {
            results[largestIndex] = new Money(results[largestIndex].Amount + residue, CurrencyCode);
        }

        return results;
    }

    public int CompareTo(Money other) => Amount.CompareTo(Same(other).Amount);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator -(Money value) => value.Negate();

    public static Money operator *(Money left, decimal right) => left.Multiply(right);

    public static Money operator /(Money left, decimal right) => left.Divide(right);

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public override string ToString() => Amount.ToString("0.####", CultureInfo.InvariantCulture) + " " + CurrencyCode;

    private Money Same(Money other) => other.CurrencyCode == CurrencyCode
        ? other
        : throw new InvalidOperationException(
            $"Cannot combine {CurrencyCode} with {other.CurrencyCode}. Convert through an exchange rate first.");
}
