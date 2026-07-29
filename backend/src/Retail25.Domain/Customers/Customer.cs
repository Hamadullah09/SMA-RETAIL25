using Retail25.Domain.Common;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.Customers;

/// <summary>
/// Customer record (guide p.46–52). The full legacy field set is preserved for migration parity.
/// </summary>
public sealed class Customer : AggregateRoot, IAuditable, ISoftDeletable
{
    private Customer()
    {
    }

    public Guid LocationId { get; set; }

    /// <summary>Legacy sequential customer number (guide p.46).</summary>
    public long CustomerNumber { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? Company { get; set; }

    public string? Title { get; set; }

    public Address BillingAddress { get; set; } = new();

    public Address ShipToAddress { get; set; } = new();

    public ContactDetails Contact { get; set; } = new();

    /// <summary>Segmentation key (guide p.46).</summary>
    public string? ClientType { get; set; }

    public DateOnly? Birthday { get; set; }

    public string? Notes { get; set; }

    public DateOnly? LastPurchaseOn { get; set; }

    public DateOnly? LastMailingOn { get; set; }

    // --- Audit ---

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Result<Customer> Create(Guid locationId, long customerNumber, string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
            return Result.Failure<Customer>(new Error("customer.name_required", "At least a first or last name is required."));

        return Result.Success(new Customer
        {
            LocationId = locationId,
            CustomerNumber = customerNumber,
            FirstName = firstName?.Trim() ?? string.Empty,
            LastName = lastName?.Trim() ?? string.Empty,
        });
    }

    public string FullName => string.IsNullOrWhiteSpace(Company)
        ? $"{FirstName} {LastName}".Trim()
        : Company;
}
