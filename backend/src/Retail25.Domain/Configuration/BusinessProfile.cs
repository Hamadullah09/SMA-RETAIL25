using Retail25.Domain.Common;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.Configuration;

/// <summary>
/// Business identity information (guide p.76). Name, address, licence number.
/// Printed on invoices and receipts.
/// </summary>
public sealed class BusinessProfile : AggregateRoot, IAuditable
{
    private BusinessProfile()
    {
    }

    public long LocationId { get; set; }

    public string BusinessName { get; set; } = string.Empty;

    public Address Address { get; set; } = new();

    public ContactDetails Contact { get; set; } = new();

    public string? LicenceNumber { get; set; }

    public string? TaxRegistrationNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static BusinessProfile Create(long locationId, string businessName)
    {
        return new BusinessProfile
        {
            LocationId = locationId,
            BusinessName = businessName.Trim(),
        };
    }
}
