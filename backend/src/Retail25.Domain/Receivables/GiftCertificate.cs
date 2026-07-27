using Retail25.Domain.Common;

namespace Retail25.Domain.Receivables;

/// <summary>
/// A serial-numbered paper gift certificate (guide p.7, p.106). Redeemed at face value as a tender.
/// </summary>
public sealed class GiftCertificate : AggregateRoot, IAuditable
{
    private GiftCertificate()
    {
    }

    public string SerialNumber { get; set; } = string.Empty;

    public decimal OriginalValue { get; set; }

    public decimal RemainingValue { get; set; }

    public Guid? IssuedToCustomerId { get; set; }

    public DateOnly IssuedOn { get; set; }

    public DateOnly? ExpiresOn { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
