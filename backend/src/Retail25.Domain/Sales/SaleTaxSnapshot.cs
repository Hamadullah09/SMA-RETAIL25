using Retail25.Domain.Common;

namespace Retail25.Domain.Sales;

/// <summary>
/// Frozen copy of the tax configuration at sale time (guide p.56). A reprint shows the taxes
/// that were in force at the time of the original sale, not the current rates.
/// </summary>
public sealed class SaleTaxSnapshot : Entity
{
    private SaleTaxSnapshot()
    {
    }

    public Guid TransactionId { get; set; }

    public string Tax1Name { get; set; } = string.Empty;

    public decimal Tax1Rate { get; set; }

    public string Tax2Name { get; set; } = string.Empty;

    public decimal Tax2Rate { get; set; }

    public bool Tax2Compound { get; set; }

    public string AddOnName { get; set; } = string.Empty;

    public decimal AddOnRate { get; set; }

    public bool AddOnTaxable { get; set; }

    public bool TaxInclusive { get; set; }

    public string? TaxRegistrationNumber { get; set; }
}
