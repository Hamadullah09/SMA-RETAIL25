using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RetailPlus.Data.Configuration;

/// <summary>
/// Column precisions. Amounts use the model default (12,2). The source keeps unit costs with 3 decimals; quantities
/// get 3 decimals so weights fit; rates and percents match the source's N(6,3).
/// </summary>
internal static class Precision
{
    public static PropertyBuilder<decimal> UnitCost(this PropertyBuilder<decimal> p) => p.HasPrecision(12, 3);
    public static PropertyBuilder<decimal> Quantity(this PropertyBuilder<decimal> p) => p.HasPrecision(12, 3);
    public static PropertyBuilder<decimal> Rate(this PropertyBuilder<decimal> p) => p.HasPrecision(7, 3);
}
