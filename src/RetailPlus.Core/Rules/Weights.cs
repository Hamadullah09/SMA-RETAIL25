using System.Globalization;
using System.Text.RegularExpressions;

namespace RetailPlus.Core.Rules;

/// <summary>
/// Weights (and volumes) written with their unit, as in the inventory file's PWEIGHT and TOLRANCE columns:
/// "90 GRAM", "500 MILIGRAM", "1.5 KG", "1.5 L". Units are kept as mg, g, kg, ml, L, oz or lb.
/// </summary>
public static partial class Weights
{
    /// <summary>The units an item's weight can have, lightest first.</summary>
    public static readonly IReadOnlyList<string> Units = ["mg", "g", "kg", "ml", "L", "oz", "lb"];

    /// <summary>The units a weight tolerance can have.</summary>
    public static readonly IReadOnlyList<string> ToleranceUnits = ["mg", "g"];

    /// <summary>The unit as written in the inventory file (the Retail Plus spelling).</summary>
    public static string Word(string unit) => unit switch
    {
        "mg" => "MILIGRAM",
        "g" => "GRAM",
        "kg" => "KG",
        "ml" => "ML",
        "L" => "L",
        _ => unit.ToUpperInvariant(),
    };

    /// <summary>"90 GRAM", or empty when there is no weight.</summary>
    public static string Format(decimal value, string? unit) =>
        value == 0 ? "" : (value.ToString("0.###", CultureInfo.InvariantCulture) + " " + Word(Normalize(unit) ?? "g")).Trim();

    /// <summary>The unit from any usual spelling ("GRAMS", "Gm", "KILO", "MILLIGRAM", "LTR"), or null if unknown.</summary>
    public static string? Normalize(string? unit) => unit?.Trim().TrimEnd('.').ToUpperInvariant() switch
    {
        null or "" => null,
        "MG" or "MILIGRAM" or "MILIGRAMS" or "MILLIGRAM" or "MILLIGRAMS" => "mg",
        "G" or "GM" or "GMS" or "GR" or "GRM" or "GRAM" or "GRAMS" => "g",
        "KG" or "KGS" or "KILO" or "KILOS" or "KILOGRAM" or "KILOGRAMS" => "kg",
        "ML" or "MILLILITRE" or "MILLILITER" or "MILLILITRES" or "MILLILITERS" => "ml",
        "L" or "LT" or "LTR" or "LITRE" or "LITER" or "LITRES" or "LITERS" => "L",
        "OZ" or "OUNCE" or "OUNCES" => "oz",
        "LB" or "LBS" or "POUND" or "POUNDS" => "lb",
        _ => null,
    };

    /// <summary>
    /// Reads "90 GRAM", "90g", "1.5 KG" or just "90" (unit null). False when the number or the unit cannot be read.
    /// </summary>
    public static bool TryParse(string? text, out decimal value, out string? unit)
    {
        value = 0;
        unit = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var m = WeightPattern().Match(text.Trim());
        if (!m.Success || !decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return false;
        if (m.Groups[2].Value.Length == 0) return true;
        unit = Normalize(m.Groups[2].Value);
        return unit is not null;
    }

    [GeneratedRegex(@"^([0-9]+(?:\.[0-9]+)?)\s*([A-Za-z.]*)$")]
    private static partial Regex WeightPattern();
}
