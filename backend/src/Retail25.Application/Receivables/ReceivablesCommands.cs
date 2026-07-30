using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Customers;
using Retail25.Domain.Receivables;

namespace Retail25.Application.Receivables;

public sealed record InvoiceRowDto(
    Guid Id,
    long InvoiceNumber,
    DateOnly IssuedOn,
    DateOnly DueOn,
    decimal InvoiceTotal,
    decimal PenaltyAccrued,
    decimal BalanceDue,
    InvoiceStatus Status,
    DateOnly? LastPaymentOn);

public sealed record CustomerAccountRowDto(
    Guid CustomerId,
    long AccountNumber,
    string CustomerName,
    decimal CreditLimit,
    decimal BalanceDue,
    int OpenInvoiceCount);

public sealed record ArLedgerEntryDto(Guid Id, Guid InvoiceId, AREntryType EntryType, decimal Amount, DateTimeOffset OccurredAt);

public sealed record CustomerStatementDto(
    Guid CustomerId,
    string CustomerName,
    long AccountNumber,
    decimal CreditLimit,
    decimal BalanceDue,
    IReadOnlyList<InvoiceRowDto> Invoices,
    IReadOnlyList<ArLedgerEntryDto> Ledger);

public sealed record ReceivablesAgingRowDto(
    Guid CustomerId,
    string CustomerName,
    decimal Current,
    decimal Days30,
    decimal Days60,
    decimal Days90Plus,
    decimal Total);

[RequiresPermission(PermissionKeys.Ar.Read)]
public sealed record BrowseCustomerAccountsQuery(
    Guid LocationId,
    string? Search = null,
    bool WithBalanceOnly = false,
    string? Cursor = null,
    int PageSize = 50) : IRequest<CursorPage<CustomerAccountRowDto>>;

[RequiresPermission(PermissionKeys.Ar.Read)]
public sealed record GetCustomerStatementQuery(Guid CustomerId) : IRequest<Result<CustomerStatementDto>>;

[RequiresPermission(PermissionKeys.Ar.Read)]
public sealed record GetReceivablesAgingQuery(Guid LocationId) : IRequest<IReadOnlyList<ReceivablesAgingRowDto>>;

public sealed record TakeInvoicePaymentResult(decimal AmountApplied, decimal AmountUnapplied, IReadOnlyList<InvoiceRowDto> UpdatedInvoices);

/// <summary>
/// Applies one payment across a customer's open invoices, oldest due date first, and within each
/// invoice penalty before principal (guide p.58) — both rules explicit, neither inferred from the
/// ledger after the fact.
/// </summary>
[RequiresPermission(PermissionKeys.Ar.Payment)]
public sealed record TakeInvoicePaymentCommand(Guid CustomerId, decimal Amount, Guid TenderTypeId, string? Reference = null)
    : IRequest<Result<TakeInvoicePaymentResult>>;

[RequiresPermission(PermissionKeys.Ar.VoidInvoice)]
public sealed record VoidInvoiceCommand(Guid InvoiceId, string? Reason = null) : IRequest<Result<InvoiceRowDto>>;

/// <summary>Reverses a prior payment (e.g. a bounced cheque) — never more than was actually paid.</summary>
[RequiresPermission(PermissionKeys.Ar.Refund)]
public sealed record RefundInvoiceCommand(Guid InvoiceId, decimal Amount, string? Reason = null) : IRequest<Result<InvoiceRowDto>>;

/// <summary>
/// Posts one month's late charge to every open invoice past its grace period, for every
/// <see cref="LateChargePolicy"/> that is enabled — skipping any invoice already charged within the
/// last 30 days so a job that runs more than once a day cannot double-charge. Callable directly (an
/// administrator forcing a run) and by the nightly recurring job.
/// </summary>
[RequiresPermission(PermissionKeys.Ar.LateCharges)]
public sealed record AccrueLateChargesCommand(Guid? LocationId = null) : IRequest<Result<int>>;

public sealed class ReceivablesHandlers :
    IRequestHandler<BrowseCustomerAccountsQuery, CursorPage<CustomerAccountRowDto>>,
    IRequestHandler<GetCustomerStatementQuery, Result<CustomerStatementDto>>,
    IRequestHandler<GetReceivablesAgingQuery, IReadOnlyList<ReceivablesAgingRowDto>>,
    IRequestHandler<TakeInvoicePaymentCommand, Result<TakeInvoicePaymentResult>>,
    IRequestHandler<VoidInvoiceCommand, Result<InvoiceRowDto>>,
    IRequestHandler<RefundInvoiceCommand, Result<InvoiceRowDto>>,
    IRequestHandler<AccrueLateChargesCommand, Result<int>>
{
    public static readonly Error AccountNotFound = new("receivables.account_not_found", "No such customer account.");
    public static readonly Error InvoiceNotFound = new("receivables.invoice_not_found", "No such invoice.");
    public static readonly Error TenderTypeUnknown = new("receivables.tender_type_unknown", "That tender type is not configured.");
    public static readonly Error InvalidAmount = new("receivables.invalid_amount", "Amount must be greater than zero.");
    public static readonly Error AlreadyVoid = new("receivables.already_void", "This invoice is already void.");
    public static readonly Error CannotRefundVoided = new("receivables.cannot_refund_voided", "A void invoice has nothing to refund.");
    public static readonly Error RefundExceedsPaid = new("receivables.refund_exceeds_paid", "Cannot refund more than has actually been paid on this invoice.");

    /// <summary>A rolling window, not a calendar month — the same shape as the grace period itself.</summary>
    private const int LateChargeIntervalDays = 30;

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public ReceivablesHandlers(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<CursorPage<CustomerAccountRowDto>> Handle(BrowseCustomerAccountsQuery request, CancellationToken ct)
    {
        var pageSize = Cursor.PageSize(request.PageSize);

        var query =
            from account in _db.CustomerAccounts.AsNoTracking()
            join customer in _db.Customers.AsNoTracking() on account.CustomerId equals customer.Id
            where customer.LocationId == request.LocationId && !customer.IsDeleted
            select new { account, customer };

        if (request.WithBalanceOnly)
        {
            query = query.Where(x => x.account.BalanceDue > 0m);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x =>
                x.customer.FirstName.Contains(term) || x.customer.LastName.Contains(term) ||
                (x.customer.Company != null && x.customer.Company.Contains(term)));
        }

        var after = Cursor.Decode(request.Cursor);
        if (after is { } cursor && Cursor.Long(cursor.SortKey) is { } key)
        {
            query = query.Where(x => x.account.AccountNumber > key);
        }

        var page = await query.OrderBy(x => x.account.AccountNumber).Take(pageSize + 1).ToListAsync(ct);

        var hasMore = page.Count > pageSize;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var accountIds = page.Select(x => x.account.CustomerId).ToList();
        var openCounts = await _db.Invoices.AsNoTracking()
            .Where(i => accountIds.Contains(i.CustomerId) && i.Status == InvoiceStatus.Open)
            .GroupBy(i => i.CustomerId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var rows = page.Select(x => new CustomerAccountRowDto(
            x.account.CustomerId,
            x.account.AccountNumber,
            x.customer.FullName,
            x.account.CreditLimit,
            x.account.BalanceDue,
            openCounts.GetValueOrDefault(x.account.CustomerId))).ToList();

        var last = page.Count > 0 ? page[^1] : null;
        var nextCursor = hasMore && last is not null ? Cursor.Encode(Cursor.Number(last.account.AccountNumber), string.Empty) : null;

        return new CursorPage<CustomerAccountRowDto>(rows, nextCursor, hasMore);
    }

    public async Task<Result<CustomerStatementDto>> Handle(GetCustomerStatementQuery request, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);
        if (customer is null)
        {
            return Result.Failure<CustomerStatementDto>(AccountNotFound.With("customerId", request.CustomerId));
        }

        var account = await _db.CustomerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.CustomerId == request.CustomerId, ct);
        if (account is null)
        {
            return Result.Failure<CustomerStatementDto>(AccountNotFound.With("customerId", request.CustomerId));
        }

        // Materialized first: ToInvoiceRow is a plain C# method the EF provider cannot translate to SQL.
        var invoiceEntities = await _db.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == request.CustomerId)
            .OrderByDescending(i => i.IssuedOn)
            .ToListAsync(ct);
        var invoices = invoiceEntities.Select(ToInvoiceRow).ToList();

        var ledger = await _db.ARLedgerEntries.AsNoTracking()
            .Where(e => e.CustomerId == request.CustomerId)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new ArLedgerEntryDto(e.Id, e.InvoiceId, e.EntryType, e.Amount, e.OccurredAt))
            .ToListAsync(ct);

        return Result.Success(new CustomerStatementDto(
            customer.Id, customer.FullName, account.AccountNumber, account.CreditLimit, account.BalanceDue, invoices, ledger));
    }

    public async Task<IReadOnlyList<ReceivablesAgingRowDto>> Handle(GetReceivablesAgingQuery request, CancellationToken ct)
    {
        var today = _clock.Today();

        var query =
            from invoice in _db.Invoices.AsNoTracking()
            join customer in _db.Customers.AsNoTracking() on invoice.CustomerId equals customer.Id
            where customer.LocationId == request.LocationId && invoice.Status == InvoiceStatus.Open
            select new { invoice, customer };

        var openInvoices = await query.ToListAsync(ct);

        return openInvoices
            .GroupBy(x => new { x.customer.Id, x.customer.FullName })
            .Select(g =>
            {
                decimal current = 0m, d30 = 0m, d60 = 0m, d90 = 0m;

                foreach (var x in g)
                {
                    var owed = x.invoice.BalanceDue + x.invoice.PenaltyAccrued;
                    var daysPastDue = today.DayNumber - x.invoice.DueOn.DayNumber;

                    if (daysPastDue <= 0) current += owed;
                    else if (daysPastDue <= 30) d30 += owed;
                    else if (daysPastDue <= 60) d60 += owed;
                    else d90 += owed;
                }

                return new ReceivablesAgingRowDto(g.Key.Id, g.Key.FullName, current, d30, d60, d90, current + d30 + d60 + d90);
            })
            .OrderByDescending(r => r.Total)
            .ToList();
    }

    public async Task<Result<TakeInvoicePaymentResult>> Handle(TakeInvoicePaymentCommand request, CancellationToken ct)
    {
        if (request.Amount <= 0m)
        {
            return Result.Failure<TakeInvoicePaymentResult>(InvalidAmount);
        }

        var account = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.CustomerId == request.CustomerId, ct);
        if (account is null)
        {
            return Result.Failure<TakeInvoicePaymentResult>(AccountNotFound.With("customerId", request.CustomerId));
        }

        if (!await _db.TenderTypes.AsNoTracking().AnyAsync(t => t.Id == request.TenderTypeId, ct))
        {
            return Result.Failure<TakeInvoicePaymentResult>(TenderTypeUnknown.With("tenderTypeId", request.TenderTypeId));
        }

        var openInvoices = await _db.Invoices
            .Where(i => i.CustomerId == request.CustomerId && i.Status == InvoiceStatus.Open)
            .OrderBy(i => i.DueOn).ThenBy(i => i.IssuedOn)
            .ToListAsync(ct);

        var today = _clock.Today();
        var remaining = request.Amount;
        var updated = new List<InvoiceRowDto>();
        var payments = new List<InvoicePayment>();

        foreach (var invoice in openInvoices)
        {
            if (remaining <= 0m)
            {
                break;
            }

            var owedOnInvoice = invoice.BalanceDue + invoice.PenaltyAccrued;
            if (owedOnInvoice <= 0m)
            {
                continue;
            }

            var applied = Math.Min(remaining, owedOnInvoice);
            var toPenalty = Math.Min(applied, invoice.PenaltyAccrued);
            var toPrincipal = applied - toPenalty;

            invoice.PenaltyAccrued -= toPenalty;
            invoice.BalanceDue -= toPrincipal;
            invoice.LastPaymentOn = today;
            invoice.ModifiedAt = _clock.Now;

            if (invoice.BalanceDue <= 0m && invoice.PenaltyAccrued <= 0m)
            {
                invoice.Status = InvoiceStatus.Paid;
            }

            var payment = new InvoicePayment
            {
                InvoiceId = invoice.Id,
                Amount = applied,
                AppliedToPenalty = toPenalty,
                AppliedToPrincipal = toPrincipal,
                TenderTypeId = request.TenderTypeId,
                PaidOn = today,
                CreatedAt = _clock.Now,
            };
            _db.InvoicePayments.Add(payment);
            payments.Add(payment);

            _db.ARLedgerEntries.Add(new ARLedgerEntry
            {
                CustomerId = request.CustomerId,
                InvoiceId = invoice.Id,
                EntryType = AREntryType.Payment,
                Amount = -applied,
                OccurredAt = _clock.Now,
            });

            remaining -= applied;
            updated.Add(ToInvoiceRow(invoice));
        }

        var amountApplied = request.Amount - remaining;

        // "Distributed" describes what actually happened to this payment, not what could have —
        // one invoice fully covering it is an ordinary single payment, not a distribution.
        var wasDistributed = payments.Count > 1;
        foreach (var payment in payments)
        {
            payment.WasDistributed = wasDistributed;
        }

        account.BalanceDue -= amountApplied;

        await _db.SaveChangesAsync(ct);

        return Result.Success(new TakeInvoicePaymentResult(amountApplied, remaining, updated));
    }

    public async Task<Result<InvoiceRowDto>> Handle(VoidInvoiceCommand request, CancellationToken ct)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == request.InvoiceId, ct);
        if (invoice is null)
        {
            return Result.Failure<InvoiceRowDto>(InvoiceNotFound.With("invoiceId", request.InvoiceId));
        }

        if (invoice.Status == InvoiceStatus.Void)
        {
            return Result.Failure<InvoiceRowDto>(AlreadyVoid);
        }

        var owed = invoice.BalanceDue + invoice.PenaltyAccrued;

        _db.ARLedgerEntries.Add(new ARLedgerEntry
        {
            CustomerId = invoice.CustomerId,
            InvoiceId = invoice.Id,
            EntryType = AREntryType.Void,
            Amount = -owed,
            OccurredAt = _clock.Now,
        });

        var account = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.CustomerId == invoice.CustomerId, ct);
        if (account is not null)
        {
            account.BalanceDue -= owed;
        }

        invoice.BalanceDue = 0m;
        invoice.PenaltyAccrued = 0m;
        invoice.Status = InvoiceStatus.Void;
        invoice.ModifiedAt = _clock.Now;

        await _db.SaveChangesAsync(ct);

        return Result.Success(ToInvoiceRow(invoice));
    }

    public async Task<Result<InvoiceRowDto>> Handle(RefundInvoiceCommand request, CancellationToken ct)
    {
        if (request.Amount <= 0m)
        {
            return Result.Failure<InvoiceRowDto>(InvalidAmount);
        }

        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == request.InvoiceId, ct);
        if (invoice is null)
        {
            return Result.Failure<InvoiceRowDto>(InvoiceNotFound.With("invoiceId", request.InvoiceId));
        }

        if (invoice.Status == InvoiceStatus.Void)
        {
            return Result.Failure<InvoiceRowDto>(CannotRefundVoided);
        }

        var alreadyPaid = invoice.InvoiceTotal - invoice.BalanceDue;
        if (request.Amount > alreadyPaid)
        {
            return Result.Failure<InvoiceRowDto>(RefundExceedsPaid.With("alreadyPaid", alreadyPaid));
        }

        invoice.BalanceDue += request.Amount;
        invoice.Status = InvoiceStatus.Open;
        invoice.ModifiedAt = _clock.Now;

        _db.ARLedgerEntries.Add(new ARLedgerEntry
        {
            CustomerId = invoice.CustomerId,
            InvoiceId = invoice.Id,
            EntryType = AREntryType.Refund,
            Amount = request.Amount,
            OccurredAt = _clock.Now,
        });

        var account = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.CustomerId == invoice.CustomerId, ct);
        if (account is not null)
        {
            account.BalanceDue += request.Amount;
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(ToInvoiceRow(invoice));
    }

    public async Task<Result<int>> Handle(AccrueLateChargesCommand request, CancellationToken ct)
    {
        var policies = await _db.LateChargePolicies.AsNoTracking()
            .Where(p => p.IsEnabled && (request.LocationId == null || p.LocationId == request.LocationId))
            .ToListAsync(ct);

        if (policies.Count == 0)
        {
            return Result.Success(0);
        }

        var today = _clock.Today();
        var count = 0;

        foreach (var policy in policies)
        {
            var cutoffDue = today.AddDays(-policy.GracePeriodDays);

            var customerIds = await _db.Customers.AsNoTracking()
                .Where(c => c.LocationId == policy.LocationId)
                .Select(c => c.Id)
                .ToListAsync(ct);

            var candidates = await _db.Invoices
                .Where(i => i.Status == InvoiceStatus.Open && customerIds.Contains(i.CustomerId) && i.DueOn <= cutoffDue)
                .ToListAsync(ct);

            if (candidates.Count == 0)
            {
                continue;
            }

            var invoiceIds = candidates.Select(i => i.Id).ToList();
            var lastCharged = await _db.ARLedgerEntries.AsNoTracking()
                .Where(e => invoiceIds.Contains(e.InvoiceId) && e.EntryType == AREntryType.LateCharge)
                .GroupBy(e => e.InvoiceId)
                .Select(g => new { InvoiceId = g.Key, LastOn = g.Max(e => e.OccurredAt) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.LastOn, ct);

            foreach (var invoice in candidates)
            {
                var lastChargedOn = lastCharged.TryGetValue(invoice.Id, out var occurredAt)
                    ? DateOnly.FromDateTime(occurredAt.DateTime)
                    : invoice.IssuedOn;

                if (today.DayNumber - lastChargedOn.DayNumber < LateChargeIntervalDays)
                {
                    continue;
                }

                // Charged on the principal balance only — penalty does not compound on itself.
                if (invoice.BalanceDue <= 0m)
                {
                    continue;
                }

                var charge = decimal.Round(invoice.BalanceDue * (policy.MonthlyRate / 100m), 2, MidpointRounding.AwayFromZero);
                if (charge <= 0m)
                {
                    continue;
                }

                invoice.PenaltyAccrued += charge;
                invoice.ModifiedAt = _clock.Now;

                _db.ARLedgerEntries.Add(new ARLedgerEntry
                {
                    CustomerId = invoice.CustomerId,
                    InvoiceId = invoice.Id,
                    EntryType = AREntryType.LateCharge,
                    Amount = charge,
                    OccurredAt = _clock.Now,
                });

                var account = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.CustomerId == invoice.CustomerId, ct);
                if (account is not null)
                {
                    account.BalanceDue += charge;
                }

                count++;
            }
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(count);
    }

    private static InvoiceRowDto ToInvoiceRow(Invoice invoice) => new(
        invoice.Id,
        invoice.InvoiceNumber,
        invoice.IssuedOn,
        invoice.DueOn,
        invoice.InvoiceTotal,
        invoice.PenaltyAccrued,
        invoice.BalanceDue,
        invoice.Status,
        invoice.LastPaymentOn);
}
