using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Retail25.Domain.ValueObjects;

namespace Retail25.Infrastructure.Persistence;

/// <summary>
/// Teaches EF how to store the domain's value objects.
/// <para>
/// Without these, the model fails validation on a relational provider — a class of failure the
/// in-memory provider silently tolerates, so it only surfaces the first time the real database is
/// touched. Registering them as conventions rather than per-property means a new column of the same
/// type is mapped automatically instead of being one that somebody forgot.
/// </para>
/// </summary>
internal static class ValueObjectConverters
{
    /// <summary>
    /// Stored as the number a user types: five percent is <c>5.0000</c>, not <c>0.05</c>
    /// (guide p.76). Keeping the user-facing convention in the column as well as in the type means a
    /// person reading the table sees what they entered, and removes a whole class of hundred-fold
    /// errors when someone writes a report against it by hand.
    /// </summary>
    public sealed class PercentageConverter : ValueConverter<Percentage, decimal>
    {
        public PercentageConverter()
            : base(percentage => percentage.Value, value => new Percentage(value))
        {
        }
    }
}
