using System.Globalization;

namespace Retail25.Domain.ValueObjects;

/// <summary>
/// A count of stock. Fractional by design: random-weight items sell 1.243 kg, purchase orders
/// allow split cases ("order 1.5 cases of 12 = 18 items", user guide p.66), and kits explode into
/// fractional component usage.
/// </summary>
public readonly record struct Quantity : IComparable<Quantity>
{
    /// <summary>Matches the ledger column precision.</summary>
    public const int Scale = 4;

    public Quantity(decimal value) => Value = decimal.Round(value, Scale, MidpointRounding.AwayFromZero);

    public decimal Value { get; }

    public static Quantity Zero => new(0m);

    public static Quantity One => new(1m);

    public bool IsZero => Value == 0m;

    public bool IsNegative => Value < 0m;

    public bool IsWhole => Value == decimal.Truncate(Value);

    public Quantity Add(Quantity other) => new(Value + other.Value);

    public Quantity Subtract(Quantity other) => new(Value - other.Value);

    public Quantity Multiply(decimal factor) => new(Value * factor);

    public Quantity Negate() => new(-Value);

    public Quantity Abs() => new(Math.Abs(Value));

    /// <summary>
    /// Rounds up to a whole multiple of <paramref name="caseQuantity"/>. Purchase-order formulas
    /// round to full cases unless the buyer deliberately enters a split case.
    /// </summary>
    public Quantity RoundUpToCase(decimal caseQuantity)
        => caseQuantity <= 0m ? this : new Quantity(Math.Ceiling(Value / caseQuantity) * caseQuantity);

    public int CompareTo(Quantity other) => Value.CompareTo(other.Value);

    public static Quantity operator +(Quantity left, Quantity right) => left.Add(right);

    public static Quantity operator -(Quantity left, Quantity right) => left.Subtract(right);

    public static Quantity operator -(Quantity value) => value.Negate();

    public static Quantity operator *(Quantity left, decimal right) => left.Multiply(right);

    public static bool operator <(Quantity left, Quantity right) => left.CompareTo(right) < 0;

    public static bool operator >(Quantity left, Quantity right) => left.CompareTo(right) > 0;

    public static bool operator <=(Quantity left, Quantity right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Quantity left, Quantity right) => left.CompareTo(right) >= 0;

    public static implicit operator decimal(Quantity quantity) => quantity.Value;

    public override string ToString() => Value.ToString("0.####", CultureInfo.InvariantCulture);
}
