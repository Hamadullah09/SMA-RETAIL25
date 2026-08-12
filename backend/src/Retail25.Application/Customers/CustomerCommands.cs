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

    /// <summary>
    /// Somebody with this contact detail is already on file. Names are not enough to refuse on —
    /// two customers really can be called the same thing — but an email address or a telephone
    /// number is a claim to be the same person, and acting on it twice is how one customer ends up
    /// with two balances, two credit limits and half their loyalty points.
    /// </summary>
    public static readonly Error DuplicateContact = new(
        "customer.duplicate_contact",
        "A customer with that contact detail is already on file.");

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

        // Checked before the number is drawn, so a refused customer does not burn one out of the
        // location's sequence and leave a gap somebody later tries to explain.
        var duplicate = await FindDuplicateAsync(request.LocationId, request.Addresses?.Contact, null, ct);
        if (duplicate is not null)
        {
            return Result.Failure<CustomerFormDto>(DuplicateContact
                .With("existingCustomerId", duplicate.Id)
                .With("existingCustomerNumber", duplicate.CustomerNumber)
                .With("existingCustomerName", duplicate.DisplayName)
                .With("matchedOn", duplicate.MatchedOn));
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

        // Checked on the way in as well as on create. Guarding only creation leaves the same
        // collision one edit away — type the address onto the wrong record and there are two
        // customers claiming one inbox again, by a route nothing was watching.
        if (request.Addresses?.Contact is { } editedContact)
        {
            var duplicate = await FindDuplicateAsync(customer.LocationId, editedContact, customer.Id, ct);
            if (duplicate is not null)
            {
                return Result.Failure<CustomerFormDto>(DuplicateContact
                    .With("existingCustomerId", duplicate.Id)
                    .With("existingCustomerNumber", duplicate.CustomerNumber)
                    .With("existingCustomerName", duplicate.DisplayName)
                    .With("matchedOn", duplicate.MatchedOn));
            }
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

    /// <summary>What an existing customer looked like, when one turned out to already be on file.</summary>
    private sealed record DuplicateMatch(long Id, long CustomerNumber, string DisplayName, string MatchedOn);

    /// <summary>
    /// Looks for a customer who is already on file with the same email address or telephone number.
    /// <para>
    /// Names are deliberately not enough. Two customers can genuinely be called the same thing, and
    /// refusing on a name would stop a shop serving a family. An email address or a phone number is
    /// a claim to be a particular person, and that is worth acting on — the live database holds two
    /// "Hamadullah Arain" rows created minutes apart, each now able to accrue its own balance,
    /// credit limit and loyalty points.
    /// </para>
    /// <para>
    /// Compared case-insensitively and with punctuation stripped from the number, because
    /// <c>+92 21 3257 4100</c> and <c>+922132574100</c> are one telephone and a plain equality test
    /// says they are two. Soft-deleted rows are excluded: a customer who was deleted and is being
    /// re-added is not a duplicate.
    /// </para>
    /// </summary>
    private async Task<DuplicateMatch?> FindDuplicateAsync(
        long locationId,
        ContactDetails? contact,
        long? excludingCustomerId,
        CancellationToken ct)
    {
        var email = contact?.Email?.Trim();
        var phone = Digits(contact?.Phone);
        var mobile = Digits(contact?.Mobile);

        if (string.IsNullOrWhiteSpace(email) && phone is null && mobile is null)
        {
            return null;
        }

        // Narrowed in the database to the location's live rows, then compared in memory: the phone
        // comparison strips punctuation, which no provider can translate, and a shop's customer
        // list is the wrong size to worry about either way.
        var candidates = await _db.Customers.AsNoTracking()
            .Where(c => c.LocationId == locationId && !c.IsDeleted)
            .Where(c => excludingCustomerId == null || c.Id != excludingCustomerId)
            .Select(c => new
            {
                c.Id,
                c.CustomerNumber,
                c.FirstName,
                c.LastName,
                c.Contact.Email,
                c.Contact.Phone,
                c.Contact.Mobile,
            })
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            var matchedOn =
                Same(email, candidate.Email) ? "email"
                : SameNumber(phone, candidate.Phone) || SameNumber(phone, candidate.Mobile) ? "phone"
                : SameNumber(mobile, candidate.Phone) || SameNumber(mobile, candidate.Mobile) ? "mobile"
                : null;

            if (matchedOn is not null)
            {
                return new DuplicateMatch(
                    candidate.Id,
                    candidate.CustomerNumber,
                    $"{candidate.FirstName} {candidate.LastName}".Trim(),
                    matchedOn);
            }
        }

        return null;

        static bool Same(string? left, string? right)
            => !string.IsNullOrWhiteSpace(left)
               && !string.IsNullOrWhiteSpace(right)
               && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

        static bool SameNumber(string? left, string? right)
            => left is not null && Digits(right) is { } other && left == other;
    }

    /// <summary>
    /// The digits of a telephone number and nothing else. Returns null for anything too short to
    /// identify somebody — an extension or a stray character should not match half the shop.
    /// </summary>
    private static string? Digits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());

        return digits.Length >= 7 ? digits : null;
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
