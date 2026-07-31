using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Accounting;
using Retail25.Application.Common;
using Retail25.Domain.Accounting;
using Retail25.Domain.Purchasing;
using Retail25.Domain.Receivables;
using Retail25.Domain.Terminals;

namespace Retail25.Infrastructure.Accounting;

/// <summary>
/// Writes what a bookkeeper can import into anything (doc 09 §1, "adapters shipped").
/// <para>
/// This is the adapter that is always available and never blocks: a provider integration needs
/// credentials, a network and a vendor who is up, and none of those are the shop's problem when the
/// day's takings need posting. Every call still writes a <see cref="SyncLog"/> row, so the CSV path
/// is as auditable as a live API one.
/// </para>
/// </summary>
public sealed class CsvExportConnector : IAccountingConnector
{
    /// <summary>The legacy default, preserved: a bill falls due thirty days out (guide p.112–113).</summary>
    public const int DefaultBillTermDays = 30;

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public CsvExportConnector(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public string Provider => "csv";

    public Task<SyncResult> PushCustomersAsync(SyncScope scope, CancellationToken ct)
        => RunAsync(SyncDirection.Push, "Customers", scope, async () =>
        {
            var customers = await _db.Customers.AsNoTracking()
                .Where(c => c.LocationId == scope.LocationId && !c.IsDeleted)
                .ToListAsync(ct);

            var csv = new CsvWriter().Header("CustomerNumber", "Name", "Company", "Email", "Phone", "City");

            foreach (var customer in customers)
            {
                csv.Row(
                    customer.CustomerNumber, customer.FullName, customer.Company,
                    customer.Contact.Email, customer.Contact.Phone, customer.BillingAddress.City);
            }

            return (customers.Count, csv.ToString());
        }, ct);

    public Task<SyncResult> PushItemsAsync(SyncScope scope, CancellationToken ct)
        => RunAsync(SyncDirection.Push, "Items", scope, async () =>
        {
            var products = await _db.Products.AsNoTracking()
                .Where(p => p.LocationId == scope.LocationId && !p.IsDeleted)
                .ToListAsync(ct);

            var csv = new CsvWriter().Header("StockCode", "Name", "Price", "AvgCost", "OnHand", "Upc");

            foreach (var product in products)
            {
                csv.Row(product.StockCode, product.Name, product.RegularPrice, product.AvgCost, product.OnHand, product.Upc);
            }

            return (products.Count, csv.ToString());
        }, ct);

    public Task<SyncResult> PushVendorsAsync(SyncScope scope, CancellationToken ct)
        => RunAsync(SyncDirection.Push, "Vendors", scope, async () =>
        {
            var suppliers = await _db.Suppliers.AsNoTracking()
                .Where(s => s.LocationId == scope.LocationId && !s.IsDeleted)
                .ToListAsync(ct);

            var csv = new CsvWriter().Header("SupplierNumber", "Company", "Contact", "Email", "Phone");

            foreach (var supplier in suppliers)
            {
                csv.Row(
                    supplier.SupplierNumber, supplier.Company,
                    $"{supplier.ContactFirstName} {supplier.ContactLastName}".Trim(),
                    supplier.Contact.Email, supplier.Contact.Phone);
            }

            return (suppliers.Count, csv.ToString());
        }, ct);

    public Task<SyncResult> PushInvoicesAsync(SyncScope scope, CancellationToken ct)
        => RunAsync(SyncDirection.Push, "Invoices", scope, async () =>
        {
            // Open invoices only — a settled one is the accounting system's history, not ours to
            // re-assert. Nothing is deleted locally either way (doc 09, contra the legacy behaviour).
            var invoices = await _db.Invoices.AsNoTracking()
                .Where(i => i.Status == InvoiceStatus.Open)
                .ToListAsync(ct);

            var customerIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
            var customers = await _db.Customers.AsNoTracking()
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FullName, ct);

            var csv = new CsvWriter().Header(
                "InvoiceNumber", "Customer", "IssuedOn", "DueOn", "InvoiceTotal", "BalanceDue", "Status");

            foreach (var invoice in invoices)
            {
                csv.Row(
                    invoice.InvoiceNumber, customers.GetValueOrDefault(invoice.CustomerId),
                    invoice.IssuedOn, invoice.DueOn, invoice.InvoiceTotal, invoice.BalanceDue, invoice.Status);
            }

            return (invoices.Count, csv.ToString());
        }, ct);

    public Task<SyncResult> PostPosRevenueAsync(Guid locationId, DateOnly businessDate, CancellationToken ct)
        => RunAsync(SyncDirection.Push, "PosRevenue", new SyncScope(locationId, businessDate, businessDate), async () =>
        {
            var sessions = await _db.DrawerSessions
                .Where(d => d.LocationId == locationId)
                .Where(d => d.BusinessDate == businessDate)
                .Where(d => d.Status == DrawerSessionStatus.Closed)
                .ToListAsync(ct);

            if (sessions.Count == 0)
            {
                return (0, string.Empty);
            }

            // One journal for the day, in the shape a bookkeeper posts by hand: takings debit the
            // bank, sales and the taxes collected credit their own accounts.
            var netSales = sessions.Sum(s => s.NetSales);
            var tax1 = sessions.Sum(s => s.Tax1Collected);
            var tax2 = sessions.Sum(s => s.Tax2Collected);
            var banked = netSales + tax1 + tax2;

            var csv = new CsvWriter().Header("Date", "Account", "Debit", "Credit", "Memo");
            var memo = $"POS revenue {businessDate:yyyy-MM-dd} ({sessions.Count} drawer session{(sessions.Count == 1 ? "" : "s")})";

            csv.Row(businessDate, MapOr("BankAccount", "Bank"), banked, 0m, memo);
            csv.Row(businessDate, MapOr("SalesAccount", "Sales"), 0m, netSales, memo);

            if (tax1 != 0m)
            {
                csv.Row(businessDate, MapOr("Tax1Account", "Tax 1 collected"), 0m, tax1, memo);
            }

            if (tax2 != 0m)
            {
                csv.Row(businessDate, MapOr("Tax2Account", "Tax 2 collected"), 0m, tax2, memo);
            }

            return (sessions.Count, csv.ToString());
        }, ct);

    public Task<SyncResult> PostBillAsync(Guid purchaseOrderId, DateOnly dueOn, CancellationToken ct)
        => RunAsync(SyncDirection.Push, "Bill", null, async () =>
        {
            var order = await _db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == purchaseOrderId, ct);

            if (order is null)
            {
                throw new InvalidOperationException($"No purchase order {purchaseOrderId}.");
            }

            var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == order.SupplierId, ct);

            var lines = await _db.PurchaseOrderLines.AsNoTracking()
                .Where(l => l.PurchaseOrderId == order.Id && l.QtyReceived > 0m)
                .ToListAsync(ct);

            var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
            var products = await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.StockCode, ct);

            var csv = new CsvWriter().Header(
                "PoNumber", "Supplier", "DueOn", "StockCode", "QtyReceived", "CostEach", "Extended");

            foreach (var line in lines)
            {
                csv.Row(
                    order.PoNumber, supplier?.Company, dueOn,
                    products.GetValueOrDefault(line.ProductId),
                    line.QtyReceived, line.CostEach, line.QtyReceived * line.CostEach);
            }

            return (lines.Count, csv.ToString());
        }, ct);

    // Pulls are a no-op for a file adapter: a CSV file is something we write, not something the
    // accounting system answers with. Reported honestly rather than faked as an empty success.
    public Task<SyncResult> PullCustomersAsync(Guid locationId, CancellationToken ct) => NotSupported("Customers");

    public Task<SyncResult> PullItemsAsync(Guid locationId, CancellationToken ct) => NotSupported("Items");

    public Task<SyncResult> PullVendorsAsync(Guid locationId, CancellationToken ct) => NotSupported("Vendors");

    private static Task<SyncResult> NotSupported(string entity)
        => Task.FromResult(SyncResult.Failed(
            $"The CSV adapter cannot pull {entity.ToLowerInvariant()} — a file export is one-way. " +
            "Configure a provider adapter to pull."));

    /// <summary>
    /// Runs one sync step, timing it and recording the attempt either way. A failure is logged and
    /// returned, never thrown: accounting is downstream of selling and must not be able to break it.
    /// </summary>
    private async Task<SyncResult> RunAsync(
        SyncDirection direction,
        string entity,
        SyncScope? scope,
        Func<Task<(int Count, string Output)>> work,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var request = scope is null ? entity : $"{entity} location={scope.LocationId} from={scope.From} to={scope.To}";

        try
        {
            var (count, output) = await work();
            stopwatch.Stop();

            await LogAsync(direction, entity, request, output, SyncStatus.Success, null, count, stopwatch.ElapsedMilliseconds, ct);
            return SyncResult.Ok(count, output);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();

            await LogAsync(direction, entity, request, null, SyncStatus.Failed, exception.Message, 0, stopwatch.ElapsedMilliseconds, ct);
            return SyncResult.Failed(exception.Message);
        }
    }

    private async Task LogAsync(
        SyncDirection direction,
        string entity,
        string request,
        string? response,
        SyncStatus status,
        string? error,
        int recordCount,
        long durationMs,
        CancellationToken ct)
    {
        _db.SyncLogs.Add(new SyncLog
        {
            Provider = Provider,
            Direction = direction,
            Entity = entity,
            RequestPayload = request,
            ResponsePayload = response,
            Status = status,
            ErrorMessage = error,
            RecordCount = recordCount,
            OccurredAt = _clock.Now,
            DurationMs = durationMs,
        });

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The mapped account name, or a sensible label if nobody has mapped it yet. The pre-flight
    /// check is what stops an unmapped account reaching a real posting; this keeps the file readable
    /// in the meantime rather than emitting a bare guid.
    /// </summary>
    private string MapOr(string localKey, string fallback)
        => _db.ExternalEntityMaps
            .Where(m => m.Provider == Provider && m.EntityType == "Account" && m.LocalKey == localKey)
            .Select(m => m.RemoteName ?? m.RemoteId)
            .FirstOrDefault() ?? fallback;
}
