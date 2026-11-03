using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>One line of a sale as the tax office is told it.</summary>
public sealed record FiscalInvoiceItem(string ItemCode, string ItemName, decimal Quantity, string? PctCode, decimal TaxRate, decimal SaleValue, decimal TotalAmount,
    decimal TaxCharged, decimal Discount);

/// <summary>
/// A sale as reported to the tax office's POS integration (FBR in Pakistan): totals without and with tax, the payment
/// mode, and its lines. The sender turns it into the tax office's own format.
/// </summary>
public sealed record FiscalInvoiceRequest(Guid SaleId, string StoreCode, int StationNumber, long InvoiceNumber, DateTime SoldAt, string? BuyerName, decimal TotalBillAmount,
    decimal TotalQuantity, decimal TotalSaleValue, decimal TotalTaxCharged, decimal Discount, FiscalPaymentMode PaymentMode, bool IsReturn,
    IReadOnlyList<FiscalInvoiceItem> Items);

/// <summary>How a sale was paid, as FBR's POS integration numbers it (1 cash … 6 cheque).</summary>
public enum FiscalPaymentMode
{
    Cash = 1,
    Card = 2,
    GiftVoucher = 3,
    LoyaltyCard = 4,
    Mixed = 5,
    Cheque = 6,
}

/// <summary>What the tax office answered: its invoice number, or why it refused.</summary>
public sealed record FiscalResult(bool Accepted, string? FiscalNumber, string? Error);

/// <summary>
/// Sends a sale to the tax office. The real one (FBR) lives in the server and is off unless configured; tests and
/// development use <see cref="FakeFiscalInvoiceSender"/>, which records what would be sent.
/// </summary>
public interface IFiscalInvoiceSender
{
    /// <summary>False when the integration is off: nothing is queued or sent.</summary>
    bool Enabled { get; }

    /// <summary>Store codes the integration is for; empty for every store.</summary>
    IReadOnlyCollection<string> Stores { get; }

    Task<FiscalResult> SendAsync(FiscalInvoiceRequest request, CancellationToken cancellationToken);
}

/// <summary>A sender that sends nothing: it keeps what it was given and answers with made-up numbers (or refuses, when told to).</summary>
public sealed class FakeFiscalInvoiceSender : IFiscalInvoiceSender
{
    private int _next;

    public bool Enabled { get; set; } = true;
    public IReadOnlyCollection<string> Stores { get; set; } = [];
    /// <summary>When set, every send is refused with this message.</summary>
    public string? Refuse { get; set; }
    public List<FiscalInvoiceRequest> Sent { get; } = [];

    public Task<FiscalResult> SendAsync(FiscalInvoiceRequest request, CancellationToken cancellationToken)
    {
        lock (Sent)
        {
            if (Refuse is not null) return Task.FromResult(new FiscalResult(false, null, Refuse));
            Sent.Add(request);
            return Task.FromResult(new FiscalResult(true, $"FAKE-{request.StoreCode}-{++_next:000000}", null));
        }
    }
}

/// <summary>A row of the fiscal queue with the sale it is for.</summary>
public sealed record FiscalQueueRow(FiscalInvoice Item, string StoreCode, int StationNumber, DateTime SoldAt, decimal Total);

/// <summary>
/// The queue of sales to report to the tax office: sales that arrive from the tills after the integration was switched on
/// are queued, sent (retried with a growing wait when the office does not answer), and the fiscal number is written on the
/// sale, which takes it to the tills for reprints.
/// </summary>
public sealed class FiscalService(DataContext data)
{
    /// <summary>How many times a sale is tried before it waits for someone to try it again by hand.</summary>
    public const int MaxAttempts = 10;

    /// <summary>
    /// Queues the completed sales that arrived since the last look (of the stores the sender is for). The first look only
    /// sets where to start from, so sales made before the integration was switched on are not sent. Returns how many were queued.
    /// </summary>
    public async Task<int> EnqueueAsync(IFiscalInvoiceSender sender, CancellationToken cancellationToken = default)
    {
        if (!sender.Enabled) return 0;
        await using var db = data.Create();
        var settings = await db.FinanceSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null) return 0;
        long high = await Changes.HighMarkAsync(db, cancellationToken);
        if (settings.FiscalCursor == 0)
        {
            settings.FiscalCursor = high;
            await db.SaveChangesAsync(cancellationToken);
            return 0;
        }
        var stores = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, cancellationToken);
        var wanted = sender.Stores.Count == 0 ? stores.Keys.ToHashSet()
            : stores.Where(s => sender.Stores.Contains(s.Value, StringComparer.OrdinalIgnoreCase)).Select(s => s.Key).ToHashSet();
        var sales = await Changes.Between(db, db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Completed && s.FiscalInvoiceNumber == null), settings.FiscalCursor, high)
            .Where(s => !db.FiscalInvoices.Any(f => f.SaleId == s.Id))
            .Select(s => new { s.Id, s.StoreId, s.InvoiceNumber }).ToListAsync(cancellationToken);
        int queued = 0;
        foreach (var s in sales.Where(s => wanted.Contains(s.StoreId)))
        {
            db.FiscalInvoices.Add(new FiscalInvoice { SaleId = s.Id, StoreId = s.StoreId, InvoiceNumber = s.InvoiceNumber, Status = FiscalInvoiceStatus.Pending });
            queued++;
        }
        settings.FiscalCursor = high;
        await db.SaveChangesAsync(cancellationToken);
        return queued;
    }

    /// <summary>
    /// Sends what is due in the queue, oldest first (at most <paramref name="max"/>). An accepted sale gets its fiscal
    /// number, on the queue row and on the sale; a refused or unanswered one is tried again later. Returns how many were sent.
    /// </summary>
    public async Task<int> SendDueAsync(IFiscalInvoiceSender sender, int max = 50, CancellationToken cancellationToken = default)
    {
        if (!sender.Enabled) return 0;
        var now = SyncColumns.Now();
        List<FiscalInvoice> due;
        await using (var db = data.Create())
            due = await db.FiscalInvoices.AsNoTracking()
                .Where(f => (f.Status == FiscalInvoiceStatus.Pending || f.Status == FiscalInvoiceStatus.Failed) && f.Attempts < MaxAttempts
                    && (f.NextAttemptUtc == null || f.NextAttemptUtc <= now))
                .OrderBy(f => f.CreatedAtUtc).Take(max).ToListAsync(cancellationToken);
        int sent = 0;
        foreach (var item in due)
        {
            FiscalResult result;
            try
            {
                var request = await RequestAsync(item.SaleId, cancellationToken);
                result = request is null ? new FiscalResult(false, null, "The sale no longer exists.") : await sender.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                result = new FiscalResult(false, null, ex.Message);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = new FiscalResult(false, null, "The tax office did not answer in time.");
            }
            await using var db = data.Create();
            var row = await db.FiscalInvoices.SingleAsync(f => f.Id == item.Id, cancellationToken);
            row.Attempts++;
            if (result.Accepted && !string.IsNullOrWhiteSpace(result.FiscalNumber))
            {
                row.Status = FiscalInvoiceStatus.Sent;
                row.FiscalNumber = FinanceNumbers.Cut(result.FiscalNumber.Trim(), 60);
                row.SentAtUtc = SyncColumns.Now();
                row.LastError = null;
                row.NextAttemptUtc = null;
                var sale = await db.Sales.SingleOrDefaultAsync(s => s.Id == item.SaleId, cancellationToken);
                // Written as an edit, so the sale with its number goes down to the store's tills with their next download.
                if (sale is not null) sale.FiscalInvoiceNumber = row.FiscalNumber;
                sent++;
            }
            else
            {
                row.Status = FiscalInvoiceStatus.Failed;
                row.LastError = FinanceNumbers.Cut(result.Error ?? "Refused without a reason.", 400);
                row.NextAttemptUtc = SyncColumns.Now().AddMinutes(Math.Min(240, 2 << Math.Min(row.Attempts, 7)));
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        return sent;
    }

    /// <summary>A sale whose copy from a till came without the number (sent before it heard of it) gets the number back.</summary>
    public async Task<int> RestampAsync(CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var lost = await db.FiscalInvoices.AsNoTracking().Where(f => f.Status == FiscalInvoiceStatus.Sent && f.FiscalNumber != null)
            .Join(db.Sales.Where(s => s.FiscalInvoiceNumber == null), f => f.SaleId, s => s.Id, (f, s) => new { Sale = s, f.FiscalNumber })
            .Take(500).ToListAsync(cancellationToken);
        foreach (var l in lost) l.Sale.FiscalInvoiceNumber = l.FiscalNumber;
        await db.SaveChangesAsync(cancellationToken);
        return lost.Count;
    }

    /// <summary>Tries a failed or skipped sale again at the next send.</summary>
    public async Task RetryAsync(Guid id)
    {
        await using var db = data.Create();
        var row = await db.FiscalInvoices.SingleOrDefaultAsync(f => f.Id == id) ?? throw new BusinessRuleException("That queue row no longer exists.");
        if (row.Status == FiscalInvoiceStatus.Sent) throw new BusinessRuleException($"Sale {row.InvoiceNumber} was sent already ({row.FiscalNumber}).");
        row.Status = FiscalInvoiceStatus.Pending;
        row.Attempts = 0;
        row.NextAttemptUtc = null;
        await db.SaveChangesAsync();
    }

    public async Task<List<FiscalQueueRow>> QueueAsync(Guid? store, FiscalInvoiceStatus? status, int take = 200)
    {
        await using var db = data.Create();
        var q = db.FiscalInvoices.AsNoTracking().AsQueryable();
        if (store is Guid s) q = q.Where(f => f.StoreId == s);
        if (status is FiscalInvoiceStatus st) q = q.Where(f => f.Status == st);
        var rows = await q.Join(db.Sales.AsNoTracking(), f => f.SaleId, sale => sale.Id, (f, sale) => new { f, sale.StationNumber, sale.SoldAt, sale.Total })
            .OrderByDescending(x => x.f.CreatedAtUtc).Take(Math.Clamp(take, 1, 1000)).ToListAsync();
        var codes = await db.Stores.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code);
        return rows.Select(r => new FiscalQueueRow(r.f, codes.GetValueOrDefault(r.f.StoreId, ""), r.StationNumber, r.SoldAt, r.Total)).ToList();
    }

    /// <summary>The request for a sale: its lines without and with tax, discounts, and how it was paid.</summary>
    public async Task<FiscalInvoiceRequest?> RequestAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var sale = await db.Sales.AsNoTracking().Include(s => s.Lines).Include(s => s.Payments).AsSplitQuery().SingleOrDefaultAsync(s => s.Id == saleId, cancellationToken);
        if (sale is null) return null;
        var store = await db.Stores.AsNoTracking().Where(s => s.Id == sale.StoreId).Select(s => s.Code).SingleAsync(cancellationToken);
        var kinds = await db.TenderTypes.AsNoTracking().ToDictionaryAsync(t => t.Name, t => t.Kind, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var items = sale.Lines.Where(l => l.Kind != SaleLineKind.Text).OrderBy(l => l.LineNumber).Select(l =>
        {
            decimal tax = l.Tax1 + l.Tax2;
            decimal value = sale.TaxInclusive ? l.LineTotal - tax : l.LineTotal;
            decimal rate = value == 0 ? 0 : Math.Round(tax * 100m / value, 2);
            return new FiscalInvoiceItem(l.Plu ?? "", l.Description, l.Quantity, null, rate, Money.Round(value), Money.Round(value + tax), Money.Round(tax), Money.Round(l.DiscountAmount));
        }).ToList();
        var tenders = sale.Payments.Select(p => kinds.GetValueOrDefault(p.TenderType, TenderKind.Other)).Distinct().ToList();
        var mode = tenders.Count > 1 ? FiscalPaymentMode.Mixed : tenders.FirstOrDefault() switch
        {
            TenderKind.Card => FiscalPaymentMode.Card,
            TenderKind.Cheque => FiscalPaymentMode.Cheque,
            TenderKind.GiftCard => FiscalPaymentMode.GiftVoucher,
            _ => FiscalPaymentMode.Cash,
        };
        decimal taxTotal = sale.Tax1 + sale.Tax2;
        return new FiscalInvoiceRequest(sale.Id, store, sale.StationNumber, sale.InvoiceNumber, sale.SoldAt, sale.ClientName, sale.Total, items.Sum(i => i.Quantity),
            sale.Total - taxTotal, taxTotal, sale.SubtotalDiscount + items.Sum(i => i.Discount), mode, sale.Total < 0, items);
    }
}
