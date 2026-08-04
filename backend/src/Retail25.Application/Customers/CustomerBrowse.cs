using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Customers;
using Retail25.Domain.ValueObjects;

namespace Retail25.Application.Customers;

public enum CustomerSort
{
    Number = 0,
    Name = 1,
    Company = 2,
    Balance = 3,
}

public sealed record CustomerRowDto(
    long Id,
    long CustomerNumber,
    string FirstName,
    string LastName,
    string? Company,
    string DisplayName,
    string? City,
    string? StateOrProvince,
    string? Phone,
    string? Email,
    string? ClientType,
    decimal CreditLimit,
    decimal BalanceDue,
    int PriceLevel,
    DateOnly? LastPurchaseOn,
    bool IsDeleted);

/// <summary>
/// Everything the customer Form View shows (guide p.46–52): identity, both addresses, contact,
/// the account, and the pricing profile that follows them onto every cart.
/// </summary>
public sealed record CustomerFormDto(
    long Id,
    long LocationId,
    long CustomerNumber,
    string FirstName,
    string LastName,
    string? Company,
    string? Title,
    Address BillingAddress,
    Address ShipToAddress,
    ContactDetails Contact,
    string? ClientType,
    DateOnly? Birthday,
    string? Notes,
    DateOnly? LastPurchaseOn,
    DateOnly? LastMailingOn,
    long AccountNumber,
    decimal CreditLimit,
    decimal BalanceDue,
    decimal UsualDiscountPct,
    int PriceLevel,
    bool ExemptTax1,
    bool ExemptTax2,
    int RewardPoints,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

/// <summary>
/// The customer Browse View (guide p.46), keyset-paged like the inventory browse and sharing its
/// deleted-only mode so "Undelete Items" covers customers as well as stock.
/// </summary>
[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record BrowseCustomersQuery(
    long LocationId,
    string? Search = null,
    string? ClientType = null,
    bool WithBalanceOnly = false,
    bool DeletedOnly = false,
    CustomerSort Sort = CustomerSort.Number,
    bool Descending = false,
    string? Cursor = null,
    int PageSize = 50) : IRequest<CursorPage<CustomerRowDto>>;

[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record GetCustomerFormQuery(long CustomerId) : IRequest<Result<CustomerFormDto>>;

/// <summary>The distinct client types in use, for the browse filter and the form's picker.</summary>
[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record ListClientTypesQuery(long LocationId) : IRequest<IReadOnlyList<string>>;

public sealed class CustomerBrowseHandlers
    : IRequestHandler<BrowseCustomersQuery, CursorPage<CustomerRowDto>>,
      IRequestHandler<GetCustomerFormQuery, Result<CustomerFormDto>>,
      IRequestHandler<ListClientTypesQuery, IReadOnlyList<string>>
{
    public static readonly Error NotFound = new("customer.not_found", "No such customer.");

    private readonly IApplicationDbContext _db;

    public CustomerBrowseHandlers(IApplicationDbContext db) => _db = db;

    public async Task<CursorPage<CustomerRowDto>> Handle(BrowseCustomersQuery request, CancellationToken ct)
    {
        var pageSize = Cursor.PageSize(request.PageSize);

        var query = _db.Customers.AsNoTracking()
            .Where(c => c.LocationId == request.LocationId && c.IsDeleted == request.DeletedOnly);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(c =>
                c.LastName.Contains(term) ||
                c.FirstName.Contains(term) ||
                (c.Company != null && c.Company.Contains(term)) ||
                (c.Contact.Phone != null && c.Contact.Phone.Contains(term)) ||
                (c.Contact.Email != null && c.Contact.Email.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(request.ClientType))
        {
            query = query.Where(c => c.ClientType == request.ClientType);
        }

        var customers = await Paginate(query, request).Take(pageSize + 1).ToListAsync(ct);

        var hasMore = customers.Count > pageSize;
        if (hasMore)
        {
            customers.RemoveAt(customers.Count - 1);
        }

        var rows = await ProjectAsync(customers, ct);

        // Applied after projection because the balance lives on the account row, not the customer.
        if (request.WithBalanceOnly)
        {
            rows = rows.Where(r => r.BalanceDue != 0m).ToList();
        }

        var nextCursor = hasMore && customers.Count > 0
            ? Cursor.Encode(SortKeyOf(customers[^1], request.Sort), Cursor.Number(customers[^1].CustomerNumber))
            : null;

        return new CursorPage<CustomerRowDto>(rows, nextCursor, hasMore);
    }

    public async Task<Result<CustomerFormDto>> Handle(GetCustomerFormQuery request, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);
        if (customer is null)
        {
            return Result.Failure<CustomerFormDto>(NotFound.With("customerId", request.CustomerId));
        }

        var account = await _db.CustomerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.CustomerId == customer.Id, ct);
        var pricing = await _db.CustomerPricingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.CustomerId == customer.Id, ct);

        return Result.Success(ToForm(customer, account, pricing));
    }

    public async Task<IReadOnlyList<string>> Handle(ListClientTypesQuery request, CancellationToken ct)
        => await _db.Customers.AsNoTracking()
            .Where(c => c.LocationId == request.LocationId && !c.IsDeleted && c.ClientType != null)
            .Select(c => c.ClientType!)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(ct);

    /// <summary>
    /// A blank address for a customer who has never had one.
    /// <para>
    /// An owned value object with no stored columns materialises as <c>null</c>, whatever the C#
    /// property initialiser says — the initialiser runs on <c>new</c>, not on the constructor EF uses
    /// to rehydrate. Reading through it without this is a null reference on any customer created
    /// without an address, which is most of them at a till.
    /// </para>
    /// </summary>
    private static readonly Address NoAddress = new();

    private static readonly ContactDetails NoContact = new();

    internal static CustomerFormDto ToForm(Customer customer, CustomerAccount? account, CustomerPricingProfile? pricing)
        => new(
            customer.Id,
            customer.LocationId,
            customer.CustomerNumber,
            customer.FirstName,
            customer.LastName,
            customer.Company,
            customer.Title,
            customer.BillingAddress ?? NoAddress,
            customer.ShipToAddress ?? NoAddress,
            customer.Contact ?? NoContact,
            customer.ClientType,
            customer.Birthday,
            customer.Notes,
            customer.LastPurchaseOn,
            customer.LastMailingOn,
            account?.AccountNumber ?? customer.CustomerNumber,
            account?.CreditLimit ?? 0m,
            account?.BalanceDue ?? 0m,
            pricing?.UsualDiscountPct ?? 0m,
            pricing?.PriceLevel ?? 1,
            pricing?.ExemptTax1 ?? false,
            pricing?.ExemptTax2 ?? false,
            pricing?.RewardPoints ?? 0,
            customer.IsDeleted,
            customer.CreatedAt,
            customer.ModifiedAt);

    internal static CustomerRowDto ToRow(Customer customer, CustomerAccount? account, CustomerPricingProfile? pricing)
        => new(
            customer.Id,
            customer.CustomerNumber,
            customer.FirstName,
            customer.LastName,
            customer.Company,
            customer.FullName,
            (customer.BillingAddress ?? NoAddress).City,
            (customer.BillingAddress ?? NoAddress).StateOrProvince,
            (customer.Contact ?? NoContact).Phone,
            (customer.Contact ?? NoContact).Email,
            customer.ClientType,
            account?.CreditLimit ?? 0m,
            account?.BalanceDue ?? 0m,
            pricing?.PriceLevel ?? 1,
            customer.LastPurchaseOn,
            customer.IsDeleted);

    /// <summary>
    /// Keyset paging, tie-broken on the customer number because it is unique per location and is the
    /// identifier staff and printed statements already use.
    /// </summary>
    private static IQueryable<Customer> Paginate(IQueryable<Customer> query, BrowseCustomersQuery request)
    {
        var after = Cursor.Decode(request.Cursor);
        var tie = Cursor.Long(after?.TieBreak) ?? 0L;
        var descending = request.Descending;

        switch (request.Sort)
        {
            case CustomerSort.Name:
            {
                if (after is { } position)
                {
                    var key = position.SortKey;
                    query = descending
                        ? query.Where(c => c.LastName.CompareTo(key) < 0 || (c.LastName == key && c.CustomerNumber < tie))
                        : query.Where(c => c.LastName.CompareTo(key) > 0 || (c.LastName == key && c.CustomerNumber > tie));
                }

                return descending
                    ? query.OrderByDescending(c => c.LastName).ThenByDescending(c => c.CustomerNumber)
                    : query.OrderBy(c => c.LastName).ThenBy(c => c.CustomerNumber);
            }

            case CustomerSort.Company:
            {
                if (after is { } position)
                {
                    var key = position.SortKey;
                    query = descending
                        ? query.Where(c => c.Company!.CompareTo(key) < 0 || (c.Company == key && c.CustomerNumber < tie))
                        : query.Where(c => c.Company!.CompareTo(key) > 0 || (c.Company == key && c.CustomerNumber > tie));
                }

                return descending
                    ? query.OrderByDescending(c => c.Company).ThenByDescending(c => c.CustomerNumber)
                    : query.OrderBy(c => c.Company).ThenBy(c => c.CustomerNumber);
            }

            default:
            {
                // Balance sorting also falls here: the balance lives on the account row, so the page
                // is drawn by number and ordered by balance in the projection. Sorting across the
                // join would need a second keyset and buys nothing at browse page sizes.
                if (Cursor.Long(after?.SortKey) is { } key)
                {
                    query = descending
                        ? query.Where(c => c.CustomerNumber < key)
                        : query.Where(c => c.CustomerNumber > key);
                }

                return descending
                    ? query.OrderByDescending(c => c.CustomerNumber)
                    : query.OrderBy(c => c.CustomerNumber);
            }
        }
    }

    private static string SortKeyOf(Customer customer, CustomerSort sort) => sort switch
    {
        CustomerSort.Name => customer.LastName,
        CustomerSort.Company => customer.Company ?? string.Empty,
        _ => Cursor.Number(customer.CustomerNumber),
    };

    private async Task<List<CustomerRowDto>> ProjectAsync(List<Customer> customers, CancellationToken ct)
    {
        if (customers.Count == 0)
        {
            return [];
        }

        var ids = customers.Select(c => c.Id).ToList();

        var accounts = await _db.CustomerAccounts.AsNoTracking()
            .Where(a => ids.Contains(a.CustomerId)).ToDictionaryAsync(a => a.CustomerId, a => a, ct);

        var profiles = await _db.CustomerPricingProfiles.AsNoTracking()
            .Where(p => ids.Contains(p.CustomerId)).ToDictionaryAsync(p => p.CustomerId, p => p, ct);

        return customers
            .Select(c => ToRow(c, accounts.GetValueOrDefault(c.Id), profiles.GetValueOrDefault(c.Id)))
            .ToList();
    }
}
