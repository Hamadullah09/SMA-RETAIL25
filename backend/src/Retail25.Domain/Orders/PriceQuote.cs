using Retail25.Domain.Common;

namespace Retail25.Domain.Orders;

public enum PriceQuoteStatus
{
    Open = 0,
    Converted = 1,
    Expired = 2,
    Cancelled = 3,
}

/// <summary>
/// A price quote (guide p.9) — a priced-and-held offer that converts to a sale if the customer comes
/// back before it expires. Unlike a customer order or layaway, a quote reserves nothing: it is a
/// promise about price, not a claim on stock.
/// </summary>
public sealed class PriceQuote : AggregateRoot, IAuditable
{
    public PriceQuote()
    {
    }

    public long QuoteNumber { get; set; }

    public Guid CustomerId { get; set; }

    public Guid LocationId { get; set; }

    public PriceQuoteStatus Status { get; set; } = PriceQuoteStatus.Open;

    public DateOnly IssuedOn { get; set; }

    public DateOnly? ExpiresOn { get; set; }

    public decimal Total { get; set; }

    public Guid StaffId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}

public sealed class PriceQuoteLine : Entity
{
    public PriceQuoteLine()
    {
    }

    public Guid PriceQuoteId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
