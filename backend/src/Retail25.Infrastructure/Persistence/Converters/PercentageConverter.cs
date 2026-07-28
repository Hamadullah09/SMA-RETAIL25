using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Retail25.Domain.ValueObjects;

namespace Retail25.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a <see cref="Percentage"/> as the number a shopkeeper would type: five percent is
/// <c>5.0000</c>, not <c>0.05</c>.
/// <para>
/// Keeping the database in the same units as the settings screen means a rate can be read, checked
/// against a tax notice, or corrected by hand without anyone having to remember which convention
/// this particular column uses.
/// </para>
/// </summary>
public sealed class PercentageConverter : ValueConverter<Percentage, decimal>
{
    public PercentageConverter()
        : base(percentage => percentage.Value, value => new Percentage(value))
    {
    }
}
