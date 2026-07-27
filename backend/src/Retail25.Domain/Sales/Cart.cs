using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;

namespace Retail25.Domain.Sales;

/// <summary>
/// Server-authoritative cart stored in Redis with Postgres write-behind. One active cart per
/// station. The cart lives on the server because RFID reads arrive from a daemon, not a browser.
/// </summary>
public sealed class Cart : AggregateRoot, IAuditable
{
    public Cart()
    {
    }

    public Guid StationId { get; set; }

    public Guid LocationId { get; set; }

    public Guid StaffId { get; set; }

    public Guid? CustomerId { get; set; }

    public CartStatus Status { get; set; } = CartStatus.Active;

    /// <summary>Label for suspended carts (F4 Suspend, guide p.11).</summary>
    public string? HeldName { get; set; }

    /// <summary>Next line sequence number for the non-retroactive tax override (doc 04 §3).</summary>
    public int NextLineSequence { get; set; }

    /// <summary>Optimistic concurrency revision. Every mutation increments this.</summary>
    public int Revision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    /// <summary>Auto-expire abandoned carts. Suspended carts are never expired by this timer.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
