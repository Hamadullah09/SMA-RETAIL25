using Retail25.Domain.Common;

namespace Retail25.Domain.Sales;

/// <summary>
/// One tender in an N-way split payment (guide p.8). The sum of all tenders for a transaction
/// must equal the grand total (± rounding to MinimumTender for cash).
/// </summary>
public sealed class SaleTender : Entity
{
    public SaleTender()
    {
    }

    public Guid TransactionId { get; set; }

    public Guid TenderTypeId { get; set; }

    /// <summary>Amount in base currency.</summary>
    public decimal Amount { get; set; }

    /// <summary>Amount tendered by the customer (for cash: what they handed over).</summary>
    public decimal AmountTendered { get; set; }

    /// <summary>Change given back (cash tenders only).</summary>
    public decimal ChangeGiven { get; set; }

    public Guid? CurrencyId { get; set; }

    public decimal ExchangeRate { get; set; } = 1m;

    /// <summary>Card authorisation code from the payment gateway.</summary>
    public string? AuthCode { get; set; }

    /// <summary>Last 4 digits of the card.</summary>
    public string? CardLast4 { get; set; }

    /// <summary>Gateway reference / transaction id.</summary>
    public string? GatewayReference { get; set; }
}
