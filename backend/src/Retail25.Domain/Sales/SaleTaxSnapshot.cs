using Retail25.Domain.Common;
using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales;

/// <summary>
/// The tax configuration frozen at the moment of sale.
/// <para>
/// The guide is explicit (p.56): <i>"When re-printing an invoice, the same taxes and charges are
/// applied that were in effect at the time of the original sale."</i> Rates change; this row is why
/// a document printed after a change still reconciles to the money that was actually taken.
/// </para>
/// </summary>
public sealed class SaleTaxSnapshot : Entity
{
    public SaleTaxSnapshot()
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

    public static SaleTaxSnapshot From(Guid transactionId, TaxConfiguration tax)
    {
        ArgumentNullException.ThrowIfNull(tax);
        return new SaleTaxSnapshot
        {
            TransactionId = transactionId,
            Tax1Name = tax.Tax1Name,
            Tax1Rate = tax.Tax1Rate.Value,
            Tax2Name = tax.Tax2Name,
            Tax2Rate = tax.Tax2Rate.Value,
            Tax2Compound = tax.Tax2Compound,
            AddOnName = tax.AddOnChargeName,
            AddOnRate = tax.AddOnChargeRate.Value,
            AddOnTaxable = tax.AddOnChargeTaxable,
            TaxInclusive = tax.TaxationType == TaxationType.Inclusive,
            TaxRegistrationNumber = tax.RegistrationNumber,
        };
    }
}
