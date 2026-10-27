using System.Globalization;
using RetailPlus.Core.Model;
using RetailPlus.Cutover.Source;
using RetailPlus.Data.Services;

namespace RetailPlus.Cutover;

public sealed partial class CutoverRunner
{
    /// <summary>A carried-over sale, for the invoices made from it.</summary>
    private sealed record CarriedSale(Sale Sale, List<SaleLine> Lines);

    private readonly Dictionary<long, CarriedSale> _sales = [];

    private static bool Is(SourceRow row, string column, string name, int number) =>
        string.Equals(row.Text(column), name, StringComparison.OrdinalIgnoreCase) || row.Text(column) == Number(number);

    /// <summary>A sale Retail25 counts: not practice (training), and not the reversal Retail25 writes when it voids a sale.</summary>
    private static bool IsRealSale(SourceRow sale) => !sale.Bool("is_training") && !Is(sale, "status", "Reversal", 2);

    /// <summary>
    /// Sales history: every sale keeps its number, station, time, staff, client, totals, tax rates, lines and payments.
    /// History does not move stock again (the stock levels already reflect it). A voided sale is one voided sale here,
    /// as Retail25's reversal is how it recorded the void; practice sales are left out, as trainees' sales are here.
    /// </summary>
    private async Task SalesAsync(CancellationToken ct)
    {
        Log("Sales history");
        var area = _report.Area("Sales", "sales_transactions, sale_tax_snapshots, sale_adjustments");
        var lineArea = _report.Area("Sale lines", "sale_lines, commission_ledger_entries");
        var tagArea = _report.Area("Sale line tags", "sale_lines.epc");
        var paymentArea = _report.Area("Sale payments", "sale_tenders");
        var transactions = _src["sales_transactions"];
        var lines = _src["sale_lines"].Rows.ToLookup(l => l.Long("transaction_id"));
        var tenders = _src["sale_tenders"].Rows.ToLookup(t => t.Long("transaction_id"));
        var adjustments = _src["sale_adjustments"].Rows.ToLookup(a => a.Long("transaction_id"));
        var taxes = _src["sale_tax_snapshots"].Rows.GroupBy(t => t.Long("transaction_id")).ToDictionary(g => g.Key, g => g.OrderBy(t => t.Id).First());
        var commission = _src["commission_ledger_entries"].Rows.GroupBy(c => c.Long("sale_line_id")).ToDictionary(g => g.Key, g => g.Sum(c => c.Dec("amount")));

        var sales = new List<Sale>();
        var saleLines = new List<SaleLine>();
        var tags = new List<SaleLineTag>();
        var payments = new List<SalePayment>();
        foreach (var row in transactions.Rows.OrderBy(r => r.Instant("completed_at")).ThenBy(r => r.Id))
        {
            area.Read++;
            lineArea.Read += lines[row.Id].Count();
            paymentArea.Read += tenders[row.Id].Count();
            string? skip = StoreOf(row.Long("location_id")) is null ? "store not carried over"
                : row.Bool("is_training") ? "practice sale (training)"
                : Is(row, "status", "Reversal", 2) ? "the reversal Retail25 wrote to void a sale (the sale itself is voided here)"
                : null;
            if (skip is not null)
            {
                area.Skip(skip);
                lineArea.Skip(skip, lines[row.Id].Count());
                paymentArea.Skip(skip, tenders[row.Id].Count());
                continue;
            }
            var store = StoreOf(row.Long("location_id"))!;
            var staff = StaffOf(row.Long("staff_id"));
            var client = ClientOf(row.LongOrNull("customer_id"));
            var tax = taxes.GetValueOrDefault(row.Id);
            bool voided = Is(row, "status", "Voided", 1);
            var reversal = transactions.Find(row.LongOrNull("voided_by_transaction_id"));
            var voider = StaffOf(reversal?.Long("staff_id")) ?? StaffOf(row.LongOrNull("void_approved_by_staff_id"));
            if (!_stations.TryGetValue(row.Long("station_id"), out int station))
                Warn($"sales_transactions {row.Id}: station {row.Long("station_id")} is not carried over; the sale is recorded at station 0");

            var notes = adjustments[row.Id].Select(a => string.Create(CultureInfo.InvariantCulture, $"{a.Text("label") ?? a.Text("type")} {-a.Dec("amount"):0.00}")).ToList();
            if (voided && row.Text("void_reason") is { } reason) notes.Add("Void: " + reason);
            decimal addOn = row.Dec("add_on_charge_total");
            var sale = From(new Sale
            {
                Id = StableIds.For("sale", row.Id), InvoiceNumber = row.Long("transaction_number"), StoreId = store.StoreId, StationNumber = station,
                LocationId = store.LocationId, SoldAt = Local(store, row.Instant("completed_at") ?? row.Instant("created_at") ?? DateTimeOffset.UnixEpoch),
                Staff = staff?.Name, StaffId = staff?.Id, ClientId = client?.Id, ClientName = DisplayName(client?.Client), CustomerNumber = client?.Client.Number,
                Status = voided ? SaleStatus.Voided : SaleStatus.Completed,
                IsOnAccount = row.LongOrNull("invoice_id") is not null || tenders[row.Id].Any(t => _tenderKinds.GetValueOrDefault(t.Long("tender_type_id")) == TenderKind.OnAccount),
                Subtotal = Money(row.Dec("subtotal") + addOn, "Sale.Subtotal"), SubtotalDiscount = Money(row.Dec("discount_total"), "Sale.SubtotalDiscount"),
                Tax1 = Money(row.Dec("tax1total"), "Sale.Tax1"), Tax2 = Money(row.Dec("tax2total"), "Sale.Tax2"),
                Rounding = Money(row.Dec("rounding_adjustment"), "Sale.Rounding"),
                Total = Money(row.Dec("grand_total") + row.Dec("rounding_adjustment"), "Sale.Total"),
                Cogs = Money(row.Dec("cost_of_goods_sold"), "Sale.Cogs"),
                Tax1Name = tax?.Text("tax1name"), Tax1Rate = Three(tax?.Dec("tax1rate") ?? 0, "Sale.Tax1Rate"),
                Tax2Name = tax?.Text("tax2name"), Tax2Rate = Three(tax?.Dec("tax2rate") ?? 0, "Sale.Tax2Rate"),
                TaxInclusive = tax?.Bool("tax_inclusive") ?? false, Tax2Compound = tax?.Bool("tax2compound") ?? false,
                PointsEarned = row.Long("loyalty_points_earned"), PointsRedeemed = row.Long("loyalty_points_redeemed"),
                Notes = notes.Count == 0 ? null : string.Join("; ", notes),
                VoidedAt = voided ? Local(store, reversal?.Instant("completed_at") ?? row.Instant("modified_at") ?? row.Instant("completed_at") ?? DateTimeOffset.UnixEpoch) : null,
                VoidedBy = voided ? voider?.Name : null, VoidedById = voided ? voider?.Id : null,
                CreatedAtUtc = Utc(row.Instant("created_at")),
            }, row);
            sales.Add(sale);
            var mappedLines = new List<SaleLine>();

            int lastNumber = 0;
            foreach (var l in lines[row.Id].OrderBy(l => l.Int("sequence")).ThenBy(l => l.Id))
            {
                var item = ItemOf(l.Long("product_id"));
                var kind = Is(l, "line_type", "Return", 1) ? SaleLineKind.Return : Is(l, "line_type", "TradeIn", 2) ? SaleLineKind.TradeIn : SaleLineKind.Sale;
                decimal quantity = Three(kind == SaleLineKind.Sale ? l.Dec("quantity") : -Math.Abs(l.Dec("quantity")), "SaleLine.Quantity");
                decimal unitPrice = Money(l.Dec("unit_price"), "SaleLine.UnitPrice");
                decimal lineTotal = Money(l.Dec("extended_net"), "SaleLine.LineTotal");
                var line = From(new SaleLine
                {
                    Id = StableIds.For("sale-line", l.Id), SaleId = sale.Id, LineNumber = lastNumber = Math.Max(lastNumber + 1, l.Int("sequence")), Kind = kind,
                    ItemId = item?.Id, Plu = l.Text("stock_code_snapshot")?.ToUpperInvariant() ?? item?.Plu,
                    Description = l.Text("name_snapshot") ?? item?.Name ?? "Item " + Number(l.Long("product_id")),
                    Quantity = quantity, UnitPrice = unitPrice, LineTotal = lineTotal, DiscountAmount = R2(unitPrice * quantity - lineTotal),
                    Cogs = R2(l.Dec("unit_cost_snapshot") * quantity), Department = item?.Department,
                    Taxable1 = l.Bool("tax1applies"), Taxable2 = l.Bool("tax2applies"),
                    Tax1 = Money(l.Dec("tax1amount"), "SaleLine.Tax1"), Tax2 = Money(l.Dec("tax2amount"), "SaleLine.Tax2"),
                    SerialNumber = l.Text("serial_number"),
                    VariantId = l.LongOrNull("variant_id") is long v && _variants.TryGetValue(v, out var variant) ? variant.Id : null,
                    Commission = Money(commission.GetValueOrDefault(l.Id), "SaleLine.Commission"), Restock = l.Bool("returned_to_stock"),
                    GiftCardNumber = item?.Type == ItemType.GiftCard && l.Text("serial_number") is { } card ? GiftCardService.Normalize(card) : null,
                }, l);
                saleLines.Add(line);
                mappedLines.Add(line);
                tagArea.Read += l.Text("epc") is null ? 0 : 1;
                if (l.Text("epc") is { } epc)
                    tags.Add(new SaleLineTag { Id = StableIds.For("sale-line-tag", l.Id), SaleLineId = line.Id, Epc = epc.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant() });
            }
            if (addOn != 0)
            {
                // Retail25's add-on charge has no column here: it is a line of its own, so the sale still adds up.
                var line = new SaleLine
                {
                    Id = StableIds.For("sale-add-on", row.Id), SaleId = sale.Id, LineNumber = lastNumber + 1, Kind = SaleLineKind.Sale,
                    Description = tax?.Text("add_on_name") ?? "Add-on charge", Quantity = 1, UnitPrice = Money(addOn, "SaleLine.UnitPrice"), LineTotal = Money(addOn, "SaleLine.LineTotal"),
                    Taxable1 = tax?.Bool("add_on_taxable") ?? false, Taxable2 = tax?.Bool("add_on_taxable") ?? false,
                };
                saleLines.Add(line);
                mappedLines.Add(line);
            }

            foreach (var t in tenders[row.Id].OrderBy(t => t.Id))
            {
                var currency = t.LongOrNull("currency_id") is long c ? _currencies.GetValueOrDefault(c) : null;
                var kind = _tenderKinds.GetValueOrDefault(t.Long("tender_type_id"), TenderKind.Other);
                var reference = t.Text("reference") ?? (t.Text("card_last4") is { } last4 ? "****" + last4 : null);
                decimal rate = currency is null ? 0 : Round(t.Dec("exchange_rate"), 6, "SalePayment.ExchangeRate");
                payments.Add(From(new SalePayment
                {
                    Id = StableIds.For("sale-tender", t.Id), SaleId = sale.Id, TenderType = TenderName(t.Long("tender_type_id")),
                    Amount = Money(t.Dec("amount"), "SalePayment.Amount"), Tendered = Money(t.Dec("amount_tendered"), "SalePayment.Tendered"),
                    Change = Money(t.Dec("change_given"), "SalePayment.Change"), AuthCode = t.Text("auth_code"),
                    Reference = kind == TenderKind.GiftCard && reference is not null ? GiftCardService.Normalize(reference) : reference,
                    Currency = currency, ExchangeRate = rate, ForeignAmount = currency is null ? 0 : R2(t.Dec("amount") * rate),
                }, t));
            }
            _sales[row.Id] = new CarriedSale(sale, mappedLines);
        }
        await _w.UpsertAsync(area, sales, ct);
        await _w.UpsertAsync(lineArea, saleLines, ct);
        await _w.UpsertAsync(tagArea, tags, ct);
        await _w.UpsertAsync(paymentArea, payments, ct);

        // Numbers from Retail25 stay; new ones start at station × 10,000,000 + 1, so they must stay below that.
        long highest = sales.Count == 0 ? 0 : sales.Max(s => s.InvoiceNumber);
        if (highest >= NumberCounter.NumbersPerStation)
            Warn($"Retail25 sale numbers reach {highest}, into the range station 1 numbers from; new sale numbers may repeat old ones");
    }
}
