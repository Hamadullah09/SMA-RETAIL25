using Retail25.Domain.Common;

namespace Retail25.Domain.Receivables;

/// <summary>
/// A stored-value gift card (guide p.7, p.106) — the electronic sibling of <see cref="GiftCertificate"/>.
/// Issued for a value, redeemed at the till a purchase at a time until it reaches zero.
/// <para>
/// Sold as a <c>ProductType.GiftCard</c> line at the till like any other item; this entity is what
/// that sale actually creates — the stored value the card can later be redeemed against — separately
/// from the catalogue line that represents "a gift card" as a purchasable thing.
/// </para>
/// </summary>
public sealed class GiftCard : AggregateRoot, IAuditable
{
    public static readonly Error SerialRequired = new("gift_card.serial_required", "A gift card needs a serial number.");
    public static readonly Error ValueMustBePositive = new("gift_card.value_must_be_positive", "A gift card must be issued for a positive value.");

    public GiftCard()
    {
    }

    public string SerialNumber { get; set; } = string.Empty;

    public decimal OriginalValue { get; set; }

    public decimal RemainingValue { get; set; }

    public long? IssuedToCustomerId { get; set; }

    public DateOnly IssuedOn { get; set; }

    public DateOnly? ExpiresOn { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static Result<GiftCard> Issue(string serialNumber, decimal value, DateOnly issuedOn, long? customerId, DateOnly? expiresOn)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            return Result.Failure<GiftCard>(SerialRequired);
        }

        if (value <= 0m)
        {
            return Result.Failure<GiftCard>(ValueMustBePositive);
        }

        return Result.Success(new GiftCard
        {
            SerialNumber = serialNumber.Trim().ToUpperInvariant(),
            OriginalValue = value,
            RemainingValue = value,
            IssuedToCustomerId = customerId,
            IssuedOn = issuedOn,
            ExpiresOn = expiresOn,
        });
    }

    /// <summary>Spends up to <paramref name="amount"/>, never past zero. Returns what was actually applied.</summary>
    public decimal Redeem(decimal amount)
    {
        var applied = Math.Min(Math.Max(0m, amount), RemainingValue);
        RemainingValue -= applied;
        IsActive = RemainingValue > 0m;
        return applied;
    }
}
