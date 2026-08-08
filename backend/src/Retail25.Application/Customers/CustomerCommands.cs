using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;
using Retail25.Domain.ValueObjects;

namespace Retail25.Application.Customers;

public sealed record CustomerIdentitySection(
    string FirstName,
    string LastName,
    string? Company,
    string? Title,
    string? ClientType,
    DateOnly? Birthday,
    string? Notes);

public sealed record CustomerAddressSection(Address BillingAddress, Address ShipToAddress, ContactDetails Contact);

/// <summary>The account and pricing fields that follow a customer onto every cart (guide p.51–52).</summary>
public sealed record CustomerAccountSection(
    decimal CreditLimit,
    decimal UsualDiscountPct,
    int PriceLevel,
    bool ExemptTax1,
    bool ExemptTax2);

/// <summary>
/// Creates a customer. The number is drawn from the location's sequence rather than supplied,
/// because two clerks adding a customer at the same moment must not be handed the same one — and
/// because a migrated store continues its own numbering from the administered starting point.
/// </summary>
[RequiresPermission(PermissionKeys.Customer.Write)]
public sealed record CreateCustomerCommand(
    long LocationId,
    CustomerIdentitySection Identity,
    CustomerAddressSection? Addresses = null,
    CustomerAccountSection? Account = null) : IRequest<Result<CustomerFormDto>>;

[RequiresPermission(PermissionKeys.Customer.Write)]
public sealed record UpdateCustomerCommand(
    long CustomerId,
    CustomerIdentitySection? Identity = null,
    CustomerAddressSection? Addresses = null,
    CustomerAccountSection? Account = null) : IRequest<Result<CustomerFormDto>>;

[RequiresPermission(PermissionKeys.Customer.Delete)]
public sealed record DeleteCustomerCommand(long CustomerId) : IRequest<Result>;

[RequiresPermission(PermissionKeys.Customer.Delete)]
public sealed record RestoreCustomerCommand(long CustomerId) : IRequest<Result>;

public sealed class CustomerCommandHandlers
    : IRequestHandler<CreateCustomerCommand, Result<CustomerFormDto>>,
      IRequestHandler<UpdateCustomerCommand, Result<CustomerFormDto>>,
      IRequestHandler<DeleteCustomerCommand, Result>,
      IRequestHandler<RestoreCustomerCommand, Result>
{
    public static readonly Error NotFound = new("customer.not_found", "No such customer.");
    public static readonly Error HasBalance = new("customer.has_balance", "This customer still owes money. Settle the account before deleting.");
    public static readonly Error PriceLevelInvalid = new("customer.price_level_invalid", "A price level must be between 1 and 4.");

    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;
    private readonly ISequenceGenerator _sequences;

    public CustomerCommandHandlers(IApplicationDbContext db, IPosNotifier notifier, ISequenceGenerator sequences)
    {
        _db = db;
        _notifier = notifier;
        _sequences = sequences;
    }

    public async Task<Result<CustomerFormDto>> Handle(CreateCustomerCommand request, CancellationToken ct)
    {
        if (request.Account is { } requested && requested.PriceLevel is < 1 or > 4)
        {
            return Result.Failure<CustomerFormDto>(PriceLevelInvalid.With("value", requested.PriceLevel));
        }

        var number = await _sequences.NextAsync(SequenceKind.Customer, request.LocationId, ct);

        var created = Customer.Create(request.LocationId, number, request.Identity.FirstName, request.Identity.LastName);
        if (created.IsFailure)
        {
            return Result.Failure<CustomerFormDto>(created.Error);
        }

        var customer = created.Value;
        ApplyIdentity(customer, request.Identity);

        if (request.Addresses is { } addresses)
        {
            ApplyAddresses(customer, addresses);
        }

        _db.Customers.Add(customer);

        // Saved before the account and profile are built, because both take the customer's id and the
        // database is what assigns it. Without this they are created against customer 0 — and then
        // the first on-account sale is refused with "this customer has no account", which is true of
        // the row and false of the customer.
        await _db.SaveChangesAsync(ct);

        // Every customer gets an account and a pricing profile at creation, even at the defaults.
        // Creating them lazily would mean the first on-account sale writes configuration rows inside
        // a payment transaction, which is the worst possible moment to discover a constraint.
        var account = CustomerAccount.Create(customer.Id, number, request.Account?.CreditLimit ?? 0m);
        var pricing = CustomerPricingProfile.Create(customer.Id);
        ApplyPricing(pricing, request.Account);

        _db.CustomerAccounts.Add(account);
        _db.CustomerPricingProfiles.Add(pricing);

        await _db.SaveChangesAsync(ct);
        await PublishAsync(customer, account, pricing, ct);

        return Result.Success(CustomerBrowseHandlers.ToForm(customer, account, pricing));
    }

    public async Task<Result<CustomerFormDto>> Handle(UpdateCustomerCommand request, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);
        if (customer is null)
        {
            return Result.Failure<CustomerFormDto>(NotFound.With("customerId", request.CustomerId));
        }

        if (request.Account is { } requested && requested.PriceLevel is < 1 or > 4)
        {
            return Result.Failure<CustomerFormDto>(PriceLevelInvalid.With("value", requested.PriceLevel));
        }

        if (request.Identity is { } identity)
        {
            ApplyIdentity(customer, identity);
        }

        if (request.Addresses is { } addresses)
        {
            ApplyAddresses(customer, addresses);
        }

        var account = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.CustomerId == customer.Id, ct);
        var pricing = await _db.CustomerPricingProfiles.FirstOrDefaultAsync(p => p.CustomerId == customer.Id, ct);

        if (request.Account is { } section)
        {
            if (account is null)
            {
                account = CustomerAccount.Create(customer.Id, customer.CustomerNumber, section.CreditLimit);
                _db.CustomerAccounts.Add(account);
            }
            else
            {
                // The balance is derived from the AR ledger and is deliberately not settable here.
                account.CreditLimit = section.CreditLimit;
            }

            if (pricing is null)
            {
                pricing = CustomerPricingProfile.Create(customer.Id);
                _db.CustomerPricingProfiles.Add(pricing);
            }

            ApplyPricing(pricing, section);
        }

        await _db.SaveChangesAsync(ct);
        await PublishAsync(customer, account, pricing, ct);

        return Result.Success(CustomerBrowseHandlers.ToForm(customer, account, pricing));
    }

    public async Task<Result> Handle(DeleteCustomerCommand request, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);
        if (customer is null)
        {
            return Result.Failure(NotFound.With("customerId", request.CustomerId));
        }

        var balance = await _db.CustomerAccounts.AsNoTracking()
            .Where(a => a.CustomerId == customer.Id).Select(a => (decimal?)a.BalanceDue).FirstOrDefaultAsync(ct) ?? 0m;

        if (balance != 0m)
        {
            // Hiding a customer who owes money would remove them from every statement run while the
            // debt stayed on the ledger — the balance would be real and unattributable.
            return Result.Failure(HasBalance.With("balance", balance));
        }

        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync(ct);
        await _notifier.RowRemovedAsync(customer.LocationId, GridKeys.Customer, customer.Id, ct);

        return Result.Success();
    }

    public async Task<Result> Handle(RestoreCustomerCommand request, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);
        if (customer is null)
        {
            return Result.Failure(NotFound.With("customerId", request.CustomerId));
        }

        if (customer.IsDeleted)
        {
            customer.Restore();
            await _db.SaveChangesAsync(ct);

            var account = await _db.CustomerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.CustomerId == customer.Id, ct);
            var pricing = await _db.CustomerPricingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.CustomerId == customer.Id, ct);
            await PublishAsync(customer, account, pricing, ct);
        }

        return Result.Success();
    }

    private static void ApplyIdentity(Customer customer, CustomerIdentitySection identity)
    {
        customer.FirstName = identity.FirstName?.Trim() ?? string.Empty;
        customer.LastName = identity.LastName?.Trim() ?? string.Empty;
        customer.Company = Blank(identity.Company);
        customer.Title = Blank(identity.Title);
        customer.ClientType = Blank(identity.ClientType);
        customer.Birthday = identity.Birthday;
        customer.Notes = identity.Notes;
    }

    private static void ApplyAddresses(Customer customer, CustomerAddressSection addresses)
    {
        // Each address is replaced with a fresh record. Owned value objects must not be shared
        // between entities — one instance claimed by two owners is a persistence-layer error.
        customer.BillingAddress = addresses.BillingAddress with { };
        customer.ShipToAddress = addresses.ShipToAddress with { };
        customer.Contact = addresses.Contact with { };
    }

    private static void ApplyPricing(CustomerPricingProfile pricing, CustomerAccountSection? section)
    {
        if (section is null)
        {
            return;
        }

        pricing.UsualDiscountPct = section.UsualDiscountPct;
        pricing.PriceLevel = section.PriceLevel;
        pricing.ExemptTax1 = section.ExemptTax1;
        pricing.ExemptTax2 = section.ExemptTax2;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Task PublishAsync(Customer customer, CustomerAccount? account, CustomerPricingProfile? pricing, CancellationToken ct)
        => _notifier.RowChangedAsync(
            customer.LocationId,
            GridKeys.Customer,
            customer.Id,
            CustomerBrowseHandlers.ToRow(customer, account, pricing),
            ct);
}
