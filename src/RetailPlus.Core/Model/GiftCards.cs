namespace RetailPlus.Core.Model;

public enum GiftCardKind
{
    /// <summary>A plastic card that can be reloaded.</summary>
    Card,
    /// <summary>A paper certificate: issued once for an amount, not reloaded.</summary>
    Certificate,
}

/// <summary>
/// A gift card or gift certificate, by the number printed (or encoded) on it. Its balance is the sum of its
/// <see cref="GiftCardTransaction"/> rows, so every till's issues and redemptions add up on the server. The card
/// belongs to the store that issued it; it can be spent in any store that knows it.
/// </summary>
public class GiftCard : Entity, IStoreScoped
{
    /// <summary>The issuing store.</summary>
    public Guid StoreId { get; set; }
    /// <summary>Unique across the company.</summary>
    public string Number { get; set; } = "";
    public GiftCardKind Kind { get; set; }
    /// <summary>Store local time.</summary>
    public DateTime IssuedAt { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    /// <summary>A deactivated card (lost, stolen, replaced) cannot be spent or reloaded.</summary>
    public bool IsActive { get; set; } = true;
    public Guid? ClientId { get; set; }
    public Client? Client { get; set; }
    public string? Note { get; set; }
    public List<GiftCardTransaction> Transactions { get; set; } = [];
}

public enum GiftCardTransactionKind
{
    /// <summary>The card was sold with its first amount.</summary>
    Issue,
    /// <summary>More money put on the card.</summary>
    Reload,
    /// <summary>Spent: a payment by the card (negative).</summary>
    Redeem,
    /// <summary>Money put back on the card when a sale paid by it was refunded.</summary>
    Refund,
    /// <summary>A correction by a supervisor, or a voided sale undone.</summary>
    Adjust,
}

/// <summary>One change to a gift card's balance, with the till that made it.</summary>
public class GiftCardTransaction : Entity, IStationStamped
{
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    public Guid GiftCardId { get; set; }
    public GiftCardTransactionKind Kind { get; set; }
    /// <summary>Positive adds to the balance, negative takes from it.</summary>
    public decimal Amount { get; set; }
    /// <summary>Store local time.</summary>
    public DateTime At { get; set; }
    /// <summary>The sale it was part of (no foreign key: the sale may be on another till's database).</summary>
    public Guid? SaleId { get; set; }
    public long? InvoiceNumber { get; set; }
    public string? User { get; set; }
    public string? Note { get; set; }
}

public enum LoyaltyTransactionKind
{
    /// <summary>Points earned on a sale.</summary>
    Earn,
    /// <summary>Points given up for a reward (negative).</summary>
    Redeem,
    /// <summary>A correction made at the client screen.</summary>
    Adjust,
    /// <summary>The points a client had when their record was brought over from Retail Plus.</summary>
    Opening,
}

/// <summary>
/// A change to a client's bonus points. <see cref="Client.Points"/> is kept equal to the sum of these rows (like an
/// item's on-hand count and its stock movements), so points earned at several tills add up on the server.
/// </summary>
public class LoyaltyTransaction : Entity, IStationStamped
{
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    public Guid ClientId { get; set; }
    public LoyaltyTransactionKind Kind { get; set; }
    public decimal Points { get; set; }
    /// <summary>Store local time.</summary>
    public DateTime At { get; set; }
    public Guid? SaleId { get; set; }
    public long? InvoiceNumber { get; set; }
    public string? User { get; set; }
    public string? Note { get; set; }
}
