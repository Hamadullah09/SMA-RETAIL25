using Retail25.Domain.Common;

namespace Retail25.Domain.Sales;

/// <summary>
/// Per-sale tax override (guide p.11, doc 04 §3). When set, it overrides the tax flags for
/// lines added AFTER the override. Lines with a lower sequence keep their original flags.
/// This non-retroactive behaviour is a legacy contract.
/// </summary>
public sealed class CartTaxOverride : Entity
{
    private CartTaxOverride()
    {
    }

    public Guid CartId { get; set; }

    /// <summary>If true, tax 1 applies to lines added after this override.</summary>
    public bool? Tax1 { get; set; }

    /// <summary>If true, tax 2 applies to lines added after this override.</summary>
    public bool? Tax2 { get; set; }

    /// <summary>The line sequence from which this override takes effect.</summary>
    public int AppliesFromSequence { get; set; }

    /// <summary>
    /// Raises an override that takes effect from <paramref name="appliesFromSequence"/> onward.
    /// The caller passes the cart's next line sequence, which is what makes the change apply to
    /// what is rung up next rather than to what is already on the screen (guide p.11).
    /// </summary>
    /// <param name="cartId">Cart the override belongs to.</param>
    /// <param name="appliesFromSequence">First line sequence the override reaches.</param>
    /// <param name="tax1">Tax 1 forced on or off; null leaves it to the usual rules.</param>
    /// <param name="tax2">Tax 2 forced on or off; null leaves it to the usual rules.</param>
    public static CartTaxOverride Create(Guid cartId, int appliesFromSequence, bool? tax1, bool? tax2) => new()
    {
        CartId = cartId,
        AppliesFromSequence = appliesFromSequence,
        Tax1 = tax1,
        Tax2 = tax2,
    };
}
