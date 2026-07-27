using System.Globalization;

namespace Retail25.Domain.ValueObjects;

/// <summary>
/// A percentage expressed the way the legacy system and its users express it: a rate of five
/// percent is <c>5.000</c>, not <c>0.05</c> (user guide p.76). Keeping the user-facing convention
/// in the type removes an entire class of hundred-fold errors.
/// </summary>
public readonly record struct Percentage : IComparable<Percentage>
{
    public const int Scale = 4;

    public Percentage(decimal value) => Value = decimal.Round(value, Scale, MidpointRounding.AwayFromZero);

    /// <summary>The percentage as entered, e.g. <c>5</c> for five percent.</summary>
    public decimal Value { get; }

    /// <summary>The multiplier form, e.g. <c>0.05</c> for five percent.</summary>
    public decimal Rate => Value / 100m;

    public static Percentage Zero => new(0m);

    public bool IsZero => Value == 0m;

    /// <summary>Returns the portion of <paramref name="amount"/> this percentage represents.</summary>
    public Money Of(Money amount) => amount.Multiply(Rate);

    /// <summary>Returns <paramref name="amount"/> reduced by this percentage.</summary>
    public Money DiscountFrom(Money amount) => amount.Multiply(1m - Rate);

    /// <summary>Returns <paramref name="amount"/> increased by this percentage.</summary>
    public Money AddTo(Money amount) => amount.Multiply(1m + Rate);

    /// <summary>
    /// Gross margin, the retail measure the legacy guide is emphatic about (p.32):
    /// <c>((price - cost) / price) * 100</c>. This is not mark-up.
    /// </summary>
    public static Percentage GrossMargin(Money price, Money cost)
        => price.IsZero ? Zero : new Percentage((price.Amount - cost.Amount) / price.Amount * 100m);

    /// <summary>
    /// Inverse of <see cref="GrossMargin"/>: the price that achieves a target margin at a given
    /// cost. Backs the legacy "Suggest A Price" button (p.32).
    /// </summary>
    public static Money PriceForMargin(Money cost, Percentage margin)
        => margin.Value >= 100m
            ? throw new ArgumentOutOfRangeException(nameof(margin), "A gross margin of 100% or more has no finite price.")
            : cost.Divide(1m - margin.Rate);

    public int CompareTo(Percentage other) => Value.CompareTo(other.Value);

    public static bool operator <(Percentage left, Percentage right) => left.CompareTo(right) < 0;

    public static bool operator >(Percentage left, Percentage right) => left.CompareTo(right) > 0;

    public static bool operator <=(Percentage left, Percentage right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Percentage left, Percentage right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString("0.####", CultureInfo.InvariantCulture) + "%";
}
