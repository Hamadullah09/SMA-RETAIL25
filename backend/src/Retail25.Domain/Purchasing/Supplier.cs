using Retail25.Domain.Common;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.Purchasing;

/// <summary>
/// Supplier/vendor record (guide p.59–62). Full legacy field set for migration parity.
/// </summary>
public sealed class Supplier : AggregateRoot, IAuditable, ISoftDeletable
{
    private Supplier()
    {
    }

    public Guid LocationId { get; set; }

    /// <summary>Legacy supplier number (guide p.59).</summary>
    public string SupplierNumber { get; set; } = string.Empty;

    public string Company { get; set; } = string.Empty;

    public string? ContactFirstName { get; set; }

    public string? ContactLastName { get; set; }

    public string? Title { get; set; }

    public Address Address { get; set; } = Address.Empty;

    public ContactDetails Contact { get; set; } = ContactDetails.Empty;

    // --- Audit ---

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Result<Supplier> Create(Guid locationId, string company, string supplierNumber)
    {
        if (string.IsNullOrWhiteSpace(company))
            return Result.Failure<Supplier>(new Error("supplier.company_required", "A supplier company name is required."));

        return Result.Success(new Supplier
        {
            LocationId = locationId,
            Company = company.Trim(),
            SupplierNumber = supplierNumber?.Trim() ?? string.Empty,
        });
    }

    public string FullName => $"{ContactFirstName} {ContactLastName}".Trim();
}
