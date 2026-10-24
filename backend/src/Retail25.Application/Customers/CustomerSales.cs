using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Application.Sales.Queries;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Customers;

/// <summary>Which of a customer's sales to list.</summary>
public enum CustomerSaleFilter
{
    All = 0,

    /// <summary>Sales and refunds that stand: what the customer actually bought and gave back.</summary>
    Completed = 1,

    /// <summary>Sales that were cancelled, with the reversals that cancelled them.</summary>
    Voided = 2,
}

/// <summary>One line of a customer's history: a sale, a refund, or a void.</summary>
public sealed record CustomerSaleRow(
    long Id,
    long TransactionNumber,
    DateTimeOffset CompletedAt,
    DateOnly BusinessDate,
    string StationCode,
    string StaffName,
    decimal ItemCount,
    decimal GrandTotal,
    string PaidWith,
    TransactionStatus Status,
    bool IsRefund);

/// <param name="TotalSpent">
/// Net of refunds, across every sale that matches — not just this page — and never counting a voided
/// sale or the reversal that cancelled it, which together come to nothing.
/// </param>
public sealed record CustomerSalesPage(
    IReadOnlyList<CustomerSaleRow> Rows,
    int TotalCount,
    decimal TotalSpent,
    bool HasMore);

/// <summary>
/// A customer's sales, newest first, for the customer screen and the till's client window.
/// <para>
/// Gated on <c>customer.read</c> rather than <c>reports.sales</c>, deliberately. The sales log is
/// every sale in the shop and belongs to whoever reconciles the takings; this is one customer's own
/// purchases, which is what a cashier needs to answer "what did I buy last time?" with the customer
/// standing there. A shop asked for exactly that, for cashiers.
/// </para>
/// <para>
/// Practice sales rung by a trainee never appear: as far as the customer is concerned they did not
/// happen, which is the same rule every report applies.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record CustomerSalesQuery(
    long CustomerId,
    DateOnly? From = null,
    DateOnly? To = null,
    long? TransactionNumber = null,

    /// <summary>A stock code, part of an item's name, or a tag's EPC that was on the sale.</summary>
    string? Item = null,
    CustomerSaleFilter Show = CustomerSaleFilter.All,
    int Skip = 0,
    int Take = 50) : IRequest<Result<CustomerSalesPage>>;

/// <summary>
/// One of a customer's sales in full — the invoice, on screen.
/// <para>
/// Answers only for a sale that is this customer's. Without that check, <c>customer.read</c> would
/// open any sale in the shop to anybody who could guess its id, which is the sales log by another
/// route. A sale that exists but belongs to someone else is reported exactly as one that does not
/// exist, so the answer says nothing about other people's purchases.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Customer.Read)]
public sealed record GetCustomerSaleQuery(long CustomerId, long TransactionId) : IRequest<Result<SaleDetailDto>>;

public sealed class CustomerSalesHandlers
    : IRequestHandler<CustomerSalesQuery, Result<CustomerSalesPage>>,
      IRequestHandler<GetCustomerSaleQuery, Result<SaleDetailDto>>
{
    public static readonly Error SaleNotFound = new("customer.sale_not_found", "This customer has no such sale.");

    /// <summary>The most a single page may ask for. The screen loads more as it is scrolled.</summary>
    public const int MaxTake = 200;

    private readonly IApplicationDbContext _db;

    public CustomerSalesHandlers(IApplicationDbContext db) => _db = db;

    public async Task<Result<CustomerSalesPage>> Handle(CustomerSalesQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var exists = await _db.Customers.AsNoTracking().AnyAsync(c => c.Id == request.CustomerId, ct);
        if (!exists)
        {
            return Result.Failure<CustomerSalesPage>(
                CustomerBrowseHandlers.NotFound.With("customerId", request.CustomerId));
        }

        var query = Filter(request);

        var totalCount = await query.CountAsync(ct);

        var totalSpent = await query
            .Where(t => t.Status == TransactionStatus.Completed)
            .SumAsync(t => (decimal?)t.GrandTotal, ct) ?? 0m;

        var skip = Math.Max(0, request.Skip);

        var transactions = await query
            .OrderByDescending(t => t.CompletedAt)
            .ThenByDescending(t => t.Id)
            .Skip(skip)
            .Take(Math.Clamp(request.Take, 1, MaxTake))
            .ToListAsync(ct);

        var rows = await ProjectAsync(transactions, ct);

        return Result.Success(new CustomerSalesPage(rows, totalCount, totalSpent, skip + rows.Count < totalCount));
    }

    public async Task<Result<SaleDetailDto>> Handle(GetCustomerSaleQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var transaction = await _db.SalesTransactions.AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.Id == request.TransactionId && t.CustomerId == request.CustomerId && !t.IsTraining,
                ct);

        if (transaction is null)
        {
            return Result.Failure<SaleDetailDto>(SaleNotFound
                .With("customerId", request.CustomerId)
                .With("transactionId", request.TransactionId));
        }

        return Result.Success(await SaleDetailProjection.ProjectAsync(_db, transaction, ct));
    }

    private IQueryable<SalesTransaction> Filter(CustomerSalesQuery request)
    {
        var query = _db.SalesTransactions.AsNoTracking()
            .Where(t => t.CustomerId == request.CustomerId && !t.IsTraining);

        if (request.From is { } from)
        {
            query = query.Where(t => t.BusinessDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(t => t.BusinessDate <= to);
        }

        if (request.TransactionNumber is { } number)
        {
            query = query.Where(t => t.TransactionNumber == number);
        }

        query = request.Show switch
        {
            CustomerSaleFilter.Completed => query.Where(t => t.Status == TransactionStatus.Completed),
            CustomerSaleFilter.Voided => query.Where(t => t.Status != TransactionStatus.Completed),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(request.Item))
        {
            var term = request.Item.Trim();

            query = query.Where(t => _db.SaleLines.Any(l =>
                l.TransactionId == t.Id &&
                ((l.NameSnapshot != null && l.NameSnapshot.Contains(term)) ||
                 (l.StockCodeSnapshot != null && l.StockCodeSnapshot.Contains(term)) ||
                 l.Epc == term)));
        }

        return query;
    }

    private async Task<List<CustomerSaleRow>> ProjectAsync(List<SalesTransaction> transactions, CancellationToken ct)
    {
        if (transactions.Count == 0)
        {
            return [];
        }

        var ids = transactions.Select(t => t.Id).ToList();

        var itemCounts = await _db.SaleLines.AsNoTracking()
            .Where(l => ids.Contains(l.TransactionId))
            .GroupBy(l => l.TransactionId)
            .Select(g => new { TransactionId = g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.TransactionId, x => x.Quantity, ct);

        var tenders = await _db.SaleTenders.AsNoTracking()
            .Where(t => ids.Contains(t.TransactionId))
            .Select(t => new { t.TransactionId, t.TenderTypeId })
            .ToListAsync(ct);

        var tenderNames = await _db.TenderTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.DisplayName, ct);

        var stationIds = transactions.Select(t => t.StationId).Distinct().ToList();
        var stations = await _db.Stations.AsNoTracking()
            .Where(s => stationIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.StationCode, ct);

        var staffIds = transactions.Select(t => t.StaffId).Distinct().ToList();
        var staff = await _db.StaffProfiles.AsNoTracking()
            .Where(s => staffIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        var paidWith = tenders
            .GroupBy(t => t.TransactionId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(" + ", g
                    .Select(t => tenderNames.TryGetValue(t.TenderTypeId, out var name) ? name : "Tender")
                    .Distinct()));

        return transactions.Select(t => new CustomerSaleRow(
            t.Id,
            t.TransactionNumber,
            t.CompletedAt,
            t.BusinessDate,
            stations.TryGetValue(t.StationId, out var code) ? code : string.Empty,
            staff.TryGetValue(t.StaffId, out var name) ? name : string.Empty,
            itemCounts.TryGetValue(t.Id, out var items) ? items : 0m,
            t.GrandTotal,
            paidWith.TryGetValue(t.Id, out var paid) ? paid : string.Empty,
            t.Status,
            t.GrandTotal < 0m)).ToList();
    }
}
