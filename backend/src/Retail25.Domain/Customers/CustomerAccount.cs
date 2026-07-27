using Retail25.Domain.Common;

namespace Retail25.Domain.Customers;

/// <summary>
/// Accounts-receivable information for a customer (guide p.51). Credit limit of 0 means unlimited.
/// </summary>
public sealed class CustomerAccount : Entity, IAuditable
{
    private CustomerAccount()
    {
    }

    public Guid CustomerId { get; set; }

    public long AccountNumber { get; set; }

    /// <summary>0 = unlimited (legacy behaviour, guide p.51).</summary>
    public decimal CreditLimit { get; set; }

    /// <summary>Derived from AR ledger entries. Rebuildable by replay.</summary>
    public decimal BalanceDue { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static CustomerAccount Create(Guid customerId, long accountNumber, decimal creditLimit = 0m)
    {
        return new CustomerAccount
        {
            CustomerId = customerId,
            AccountNumber = accountNumber,
            CreditLimit = creditLimit,
        };
    }
}
