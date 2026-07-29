using Retail25.Domain.Common;

namespace Retail25.Domain.Sales;

/// <summary>
/// The per-sale tax suspension reached through F11 Special / F6 Taxes (guide p.11).
/// <para>
/// The legacy wording is exact and load-bearing: <i>"This command allows you to change those
/// settings only for the current sale and only for the items that are not already on the POS
/// screen."</i> So the override records the sequence it was stamped at, and lines rung before that
/// point keep the flags they were rung with. It is not retroactive, and making it retroactive would
/// silently reprice items the cashier has already read out to the customer.
/// </para>
/// </summary>
public sealed class CartTaxOverride : Entity
{
    public static readonly Error NotAllowed = new("tax.override_not_allowed", "This store does not permit tax overrides at the till.");

    public CartTaxOverride()
    {
    }

    public Guid CartId { get; set; }

    /// <summary>Null leaves tax 1 to the product flag and the store policy.</summary>
    public bool? Tax1 { get; set; }

    public bool? Tax2 { get; set; }

    /// <summary>The line sequence from which this override takes effect.</summary>
    public int AppliesFromSequence { get; set; }

    public Guid AppliedByStaffId { get; set; }

    public DateTimeOffset AppliedAt { get; set; }

    public static CartTaxOverride Create(Guid cartId, bool? tax1, bool? tax2, int appliesFromSequence, Guid staffId, DateTimeOffset now)
        => new()
        {
            CartId = cartId,
            Tax1 = tax1,
            Tax2 = tax2,
            AppliesFromSequence = appliesFromSequence,
            AppliedByStaffId = staffId,
            AppliedAt = now,
        };
}
