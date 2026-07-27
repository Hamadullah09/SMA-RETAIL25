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
}
