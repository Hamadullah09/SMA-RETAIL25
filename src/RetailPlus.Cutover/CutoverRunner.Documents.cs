using RetailPlus.Core.Model;
using RetailPlus.Cutover.Source;

namespace RetailPlus.Cutover;

public sealed partial class CutoverRunner
{
    /// <summary>
    /// Invoices (every one of a carried store, with their payments and the lines of the sale they came from), open
    /// layaways (with their deposits) and open price quotes, all as the new model's invoices of the three kinds.
    /// Paid-off and cancelled layaways and converted, expired or cancelled quotes are history and stay behind.
    /// </summary>
    private async Task DocumentsAsync(CancellationToken ct)
    {
        Log("Invoices, layaways and quotes");
        var area = _report.Area("Invoices", "invoices");
        var lineArea = _report.Area("Invoice lines", "sale_lines, layaway_lines, price_quote_lines");
        var paymentArea = _report.Area("Invoice payments", "invoice_payments, layaway_payments");
        var invoices = new List<Invoice>();
        var lines = new List<InvoiceLine>();
        var payments = new List<InvoicePayment>();
        var numbers = new HashSet<(Guid, InvoiceKind, long)>();
        var invoicePayments = _src["invoice_payments"].Rows.ToLookup(p => p.Long("invoice_id"));

        Invoice? NewInvoice(SourceRow row, string kindName, InvoiceKind kind, CarriedStore store, long number, AreaResult result)
        {
            if (ClientOf(row.Long("customer_id")) is not { } client) { result.Skip("client not carried over"); return null; }
            if (!numbers.Add((store.StoreId, kind, number)))
            {
                result.Skip($"a second {kindName} with the same number in the store");
                Warn($"{row.Table} {row.Id}: {kindName} number {number} is used twice in store {store.Code}; only the first is carried over");
                return null;
            }
            var staff = StaffOf(row.LongOrNull("staff_id"));
            var c = client.Client;
            return From(new Invoice
            {
                Id = StableIds.For(row.Table, row.Id), StoreId = store.StoreId, Kind = kind, InvoiceNumber = number,
                ClientId = client.Id, AccountNumber = c.AccountNumber, CustomerNumber = c.Number, FirstName = c.FirstName, LastName = c.LastName, Company = c.Company,
                SalesPerson = staff?.Name, SalesPersonId = staff?.Id, ShipTo = c.ShipTo, BillTo = Address(client.Row, "billing_address_"), Email = c.Email,
                CreatedAtUtc = Utc(row.Instant("created_at")),
            }, row);
        }

        foreach (var row in _src["invoices"].Rows.OrderBy(r => r.Id))
        {
            area.Read++;
            paymentArea.Read += invoicePayments[row.Id].Count();
            if (InvoiceStore(row) is not { } store) { area.Skip("store not carried over"); paymentArea.Skip("store not carried over", invoicePayments[row.Id].Count()); continue; }
            var invoice = NewInvoice(row, "invoice", InvoiceKind.Invoice, store, row.Long("invoice_number"), area);
            if (invoice is null) { paymentArea.Skip("invoice not carried over", invoicePayments[row.Id].Count()); continue; }
            var sale = _sales.GetValueOrDefault(row.Long("transaction_id"));
            var paid = invoicePayments[row.Id].OrderBy(p => p.Date("paid_on")).ThenBy(p => p.Id).ToList();
            invoice.StationNumber = sale?.Sale.StationNumber ?? 0;
            invoice.InvoiceDate = row.Date("issued_on") ?? DateOnly.FromDateTime(sale?.Sale.SoldAt ?? DateTime.Today);
            invoice.Total = Money(row.Dec("invoice_total"), "Invoice.Total");
            invoice.Balance = Money(row.Dec("balance_due"), "Invoice.Balance");
            invoice.Penalty = Money(row.Dec("penalty_accrued"), "Invoice.Penalty");
            invoice.LastPaidDate = row.Date("last_payment_on") ?? paid.LastOrDefault()?.Date("paid_on");
            invoice.LastPaidAmount = R2(paid.LastOrDefault()?.Dec("amount") ?? 0);
            invoice.Tax1Rate = sale?.Sale.Tax1Rate ?? 0;
            invoice.Tax2Rate = sale?.Sale.Tax2Rate ?? 0;
            invoice.SaleId = sale?.Sale.Id;
            invoice.IsVoided = IsVoid(row);
            invoice.Notes = row.Date("due_on") is { } due ? $"Due {due:yyyy-MM-dd} (Retail25)" : null;
            invoices.Add(invoice);
            foreach (var l in sale?.Lines ?? [])
            {
                lineArea.Read++;
                lines.Add(new InvoiceLine
                {
                    Id = StableIds.For("invoice-line", l.Id.ToString("N")), InvoiceId = invoice.Id, LineNumber = l.LineNumber, Plu = l.Plu, Description = l.Description,
                    Quantity = l.Quantity, UnitPrice = l.UnitPrice, DiscountAmount = l.DiscountAmount, LineTotal = l.LineTotal,
                });
            }
            if (sale is null) Warn($"invoices {row.Id}: the sale it was made from is not carried over; the invoice has no lines");
            foreach (var p in paid)
                payments.Add(From(new InvoicePayment
                {
                    Id = StableIds.For("invoice-payment", p.Id), StoreId = store.StoreId, InvoiceId = invoice.Id, PaidOn = p.Date("paid_on") ?? invoice.InvoiceDate,
                    Amount = Money(p.Dec("amount"), "InvoicePayment.Amount"), TenderType = TenderName(p.Long("tender_type_id")),
                    AppliedToPenalty = Money(p.Dec("applied_to_penalty"), "InvoicePayment.AppliedToPenalty"),
                    Note = p.Bool("was_distributed") ? "Spread over the account's invoices (Retail25)" : null,
                    CreatedAtUtc = Utc(p.Instant("created_at")),
                }, p));
        }

        var layawayArea = _report.Area("Layaways", "layaways");
        var layawayLines = _src["layaway_lines"].Rows.ToLookup(l => l.Long("layaway_id"));
        var layawayPayments = _src["layaway_payments"].Rows.ToLookup(p => p.Long("layaway_id"));
        foreach (var row in _src["layaways"].Rows.OrderBy(r => r.Id))
        {
            layawayArea.Read++;
            paymentArea.Read += layawayPayments[row.Id].Count();
            string? skip = StoreOf(row.Long("location_id")) is null ? "store not carried over"
                : row.Int("status") == 1 ? "paid in full (history)" : row.Int("status") == 2 ? "cancelled (history)" : null;
            if (skip is not null) { layawayArea.Skip(skip); paymentArea.Skip("layaway " + skip, layawayPayments[row.Id].Count()); continue; }
            var store = StoreOf(row.Long("location_id"))!;
            var invoice = NewInvoice(row, "layaway", InvoiceKind.Layaway, store, row.Long("layaway_number"), layawayArea);
            if (invoice is null) { paymentArea.Skip("layaway not carried over", layawayPayments[row.Id].Count()); continue; }
            var paid = layawayPayments[row.Id].OrderBy(p => p.Date("paid_on")).ThenBy(p => p.Id).ToList();
            invoice.InvoiceDate = row.Date("created_on") ?? DateOnly.FromDateTime(DateTime.Today);
            invoice.Total = Money(row.Dec("total"), "Invoice.Total");
            invoice.Balance = Money(row.Dec("total"), "Invoice.Total") - Money(row.Dec("amount_paid"), "InvoicePayment.Amount");
            invoice.LastPaidDate = paid.LastOrDefault()?.Date("paid_on");
            invoice.LastPaidAmount = R2(paid.LastOrDefault()?.Dec("amount") ?? 0);
            invoice.Notes = "Layaway carried over from Retail25; its goods were taken out of stock then.";
            invoices.Add(invoice);
            int number = 0;
            foreach (var l in layawayLines[row.Id].OrderBy(l => l.Id))
            {
                lineArea.Read++;
                lines.Add(DocumentLine("layaway-line", l, invoice.Id, ++number));
            }
            foreach (var p in paid)
                payments.Add(From(new InvoicePayment
                {
                    Id = StableIds.For("layaway-payment", p.Id), StoreId = store.StoreId, InvoiceId = invoice.Id, PaidOn = p.Date("paid_on") ?? invoice.InvoiceDate,
                    Amount = Money(p.Dec("amount"), "InvoicePayment.Amount"), TenderType = TenderName(p.Long("tender_type_id")), Note = "Deposit",
                    CreatedAtUtc = Utc(p.Instant("created_at")),
                }, p));
            if (R2(paid.Sum(p => p.Dec("amount"))) != R2(row.Dec("amount_paid")))
                Warn($"layaways {row.Id}: the deposits add up to {paid.Sum(p => p.Dec("amount")):0.00}, the layaway says {row.Dec("amount_paid"):0.00} paid; the balance follows the layaway");
        }

        var quoteArea = _report.Area("Price quotes", "price_quotes");
        var quoteLines = _src["price_quote_lines"].Rows.ToLookup(l => l.Long("price_quote_id"));
        foreach (var row in _src["price_quotes"].Rows.OrderBy(r => r.Id))
        {
            quoteArea.Read++;
            string? skip = StoreOf(row.Long("location_id")) is null ? "store not carried over"
                : row.Int("status") switch { 1 => "converted to a sale (history)", 2 => "expired (history)", 3 => "cancelled (history)", _ => null };
            if (skip is not null) { quoteArea.Skip(skip); continue; }
            var store = StoreOf(row.Long("location_id"))!;
            var invoice = NewInvoice(row, "quote", InvoiceKind.Quote, store, row.Long("quote_number"), quoteArea);
            if (invoice is null) continue;
            invoice.InvoiceDate = row.Date("issued_on") ?? DateOnly.FromDateTime(DateTime.Today);
            invoice.Total = Money(row.Dec("total"), "Invoice.Total");
            invoice.Notes = row.Date("expires_on") is { } expires ? $"Valid until {expires:yyyy-MM-dd} (Retail25)" : null;
            invoices.Add(invoice);
            int number = 0;
            foreach (var l in quoteLines[row.Id].OrderBy(l => l.Id))
            {
                lineArea.Read++;
                lines.Add(DocumentLine("quote-line", l, invoice.Id, ++number));
            }
        }

        await _w.UpsertAsync(area, invoices.Where(i => i.Kind == InvoiceKind.Invoice).ToList(), ct);
        await _w.UpsertAsync(layawayArea, invoices.Where(i => i.Kind == InvoiceKind.Layaway).ToList(), ct);
        await _w.UpsertAsync(quoteArea, invoices.Where(i => i.Kind == InvoiceKind.Quote).ToList(), ct);
        await _w.UpsertAsync(lineArea, lines, ct);
        await _w.UpsertAsync(paymentArea, payments, ct);
        var invoiceIds = invoices.Select(i => i.Id).ToHashSet();
        await _w.RemoveMissingAsync<InvoiceLine>(lineArea, db => db.InvoiceLines.Where(l => invoiceIds.Contains(l.InvoiceId)), lines.Select(l => l.Id).ToHashSet(), ct);
        await _w.RemoveMissingAsync<InvoicePayment>(paymentArea, db => db.InvoicePayments.Where(p => invoiceIds.Contains(p.InvoiceId)), payments.Select(p => p.Id).ToHashSet(), ct);
    }

    /// <summary>A line of a layaway or quote, which name a product, a quantity and a unit price.</summary>
    private InvoiceLine DocumentLine(string kind, SourceRow line, Guid invoiceId, int number)
    {
        var item = ItemOf(line.Long("product_id"));
        decimal quantity = Three(line.Dec("quantity"), "InvoiceLine.Quantity");
        decimal price = Money(line.Dec("unit_price"), "InvoiceLine.UnitPrice");
        return From(new InvoiceLine
        {
            Id = StableIds.For(kind, line.Id), InvoiceId = invoiceId, LineNumber = number, Plu = item?.Plu,
            Description = item?.Name ?? "Item " + Number(line.Long("product_id")), Quantity = quantity, UnitPrice = price, LineTotal = R2(quantity * price),
        }, line);
    }
}
