using Retail25.Domain.Common;
using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales;

/// <summary>
/// One leg of an N-way split payment (guide p.8). The legs sum to the grand total plus the cash
/// rounding adjustment; that identity is asserted by a property test rather than trusted.
/// </summary>
public sealed class SaleTender : Entity
{
    public SaleTender()
    {
    }

    public long TransactionId { get; set; }

    public long TenderTypeId { get; set; }

    public TenderBehaviour Behaviour { get; set; }

    /// <summary>Amount applied to the balance, in the location's base currency.</summary>
    public decimal Amount { get; set; }

    /// <summary>What the customer handed over. For cash this exceeds <see cref="Amount"/> when change is due.</summary>
    public decimal AmountTendered { get; set; }

    public decimal ChangeGiven { get; set; }

    public long? CurrencyId { get; set; }

    /// <summary>Units of the tender currency per unit of the base currency (guide p.9).</summary>
    public decimal ExchangeRate { get; set; } = 1m;

    /// <summary>Cheque number, gift certificate serial, or whatever the tender type demands.</summary>
    public string? Reference { get; set; }

    public string? AuthCode { get; set; }

    public string? CardLast4 { get; set; }

    public string? GatewayReference { get; set; }
}
