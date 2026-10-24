using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Migration.Import;

public sealed partial class DbfImporter
{
    /// <summary>Numbers given to invoices whose number was already taken; kept clear of the live numbering.</summary>
    private const long DuplicateInvoiceBase = 900_000_000;

    /// <summary>TOTALnnn.DBF receipt columns and the tender names they become.</summary>
    private static readonly (string Field, string Tender)[] DrawerTenders =
    [
        ("R_CASH", "Cash"), ("R_VISA", "Visa"), ("R_MC", "MasterCard"), ("R_AMEX", "Amex"), ("R_DISCOVER", "Discover"),
        ("R_STORECD", "Store Card"), ("R_OC", "Other Card"), ("R_CHEQUE", "Cheque"), ("R_ACCOUNT", "On Account"), ("R_LAYAWAY", "Layaway"),
    ];

    [GeneratedRegex(@"^(SALES|TOTAL)(\d{3})\.DBF$", RegexOptions.IgnoreCase)]
    private static partial Regex StationFile();

    [GeneratedRegex(@"^PO(\d{1,10})$", RegexOptions.IgnoreCase)]
    private static partial Regex PurchaseOrderFile();

    private async Task ImportInvoicesAsync(CancellationToken ct)
    {
        var path = FindFile(_root, "INVOICES.DBF");
        if (path is null) return;
        using var table = Open(path);
        var result = Result(table, nameof(Invoice));
        var rows = new List<Pending>();
        var used = new HashSet<(InvoiceKind, long)>();
        long duplicates = 0;
        decimal liveTotal = 0, liveBalance = 0;
        foreach (var record in table.Records())
        {
            if (record.IsBlank) { result.Blank++; continue; }
            if (record.IsDeleted) { result.Deleted++; rows.Add(TraceOnly(nameof(Invoice), record)); continue; }
            int docType = record.Int("DOCTYPE", 0, 9);
            var kind = docType switch
            {
                2 => InvoiceKind.Quote,
                3 => InvoiceKind.Layaway,
                0 or 1 => InvoiceKind.Invoice,
                _ => InvoiceKind.Invoice,
            };
            if (docType > 3) _log.Warn(record.Where, $"unknown DOCTYPE {docType}; imported as an invoice");
            long number = (long)record.Dec("INV_NO");
            var notes = record.Memo("NOTES");
            if (!used.Add((kind, number)))
            {
                long replacement = DuplicateInvoiceBase + ++duplicates;
                _log.Warn(record.Where, $"{kind} number {number} is used twice; imported as {replacement}");
                notes = AppendNote(notes, $"Imported as number {replacement}: number {number} was used by another {kind.ToString().ToLowerInvariant()}.");
                number = replacement;
            }
            else
            {
                _maxInvoiceNumber = Math.Max(_maxInvoiceNumber, number);
            }
            var date = record.Date("INV_DATE");
            if (date is null) _log.Warn(record.Where, "INV_DATE is empty; the import date was used");
            var accountNumber = record.Text("ACCTNO");
            var customerNumber = record.Text("CUSTNO");
            var invoice = new Invoice
            {
                InvoiceNumber = number,
                Kind = kind,
                ClientId = ClientId(accountNumber, customerNumber),
                AccountNumber = accountNumber,
                CustomerNumber = customerNumber,
                FirstName = record.Text("FNAME"),
                LastName = record.Text("LNAME"),
                Company = record.Text("COMPANY"),
                InvoiceDate = date ?? DateOnly.FromDateTime(_now),
                Total = record.Dec("TOTAL"),
                Balance = record.Dec("BALANCE"),
                DiscountPercent = record.Dec("DISCOUNTED"),
                SalesPerson = record.Text("SALESPER"),
                LastPaidDate = record.Date("LASTPAID"),
                LastPaidAmount = record.Dec("LASTAMT"),
                Penalty = record.Dec("PENALTY"),
                Tax1Rate = record.Dec("RTAX1"),
                Tax2Rate = record.Dec("RTAX2"),
                ShipTo = record.Text("SHIPTO"),
                BillTo = record.Text("BILLTO"),
                Notes = notes,
                Email = record.Text("EMAIL"),
                IsFlagged = record.Bool("PRTFLAG"),
            };
            liveTotal += invoice.Total;
            liveBalance += invoice.Balance;
            rows.Add(One(invoice, nameof(Invoice), record));
        }
        result.Imported = await SaveAsync(rows, ct);
        int expected = result.Records - result.Blank - result.Deleted;
        Check("Invoices: records (live)", expected, async (db, t) => await db.Invoices.CountAsync(t));
        Check("Invoices: total amount", liveTotal, async (db, t) => await db.Invoices.SumAsync(i => i.Total, t));
        Check("Invoices: balance owing", liveBalance, async (db, t) => await db.Invoices.Where(i => !i.IsVoided).SumAsync(i => i.Balance, t));
    }

    private async Task ImportRmasAsync(CancellationToken ct)
    {
        var path = FindFile(_root, "RMA.DBF");
        if (path is null) return;
        using var table = Open(path);
        var result = Result(table, nameof(Rma));
        var rows = new List<Pending>();
        foreach (var record in table.Records())
        {
            if (record.IsBlank) { result.Blank++; continue; }
            if (record.IsDeleted) { result.Deleted++; rows.Add(TraceOnly(nameof(Rma), record)); continue; }
            var rma = new Rma
            {
                Plu = record.Text("PLU"),
                Description = record.Text("PROD"),
                Size = record.Text("BSIZE"),
                Quantity = record.Dec("QTY"),
                UnitPrice = record.Dec("UNITPRICE"),
                FirstName = record.Text("FNAME"),
                LastName = record.Text("LNAME"),
                Company = record.Text("COMPANY"),
                CustomerPhone = record.Text("CUST_PHONE"),
                CustomerEmail = record.Text("CUST_EMAIL"),
                CustomerRmaNumber = record.Text("CUST_RMANO"),
                CustomerRmaDate = record.Date("CUST_RDATE"),
                IsCustomerRma = record.Bool("ISCUST_RMA"),
                Supplier = record.Text("SUPPLIER"),
                SupplierFirstName = record.Text("SUPP_FNAME"),
                SupplierLastName = record.Text("SUPP_LNAME"),
                SupplierPhone = record.Text("SUPP_PHONE"),
                SupplierEmail = record.Text("SUPP_EMAIL"),
                SupplierRmaNumber = record.Text("SUPP_RMANO"),
                SupplierRmaDate = record.Date("SUPP_RDATE"),
                IsSupplierRma = record.Bool("ISSUPP_RMA"),
                SetDate = record.Date("SET_DATE"),
                SetType = record.Text("SET_TYPE"),
                IsActive = record.Bool("IS_ACTIVE"),
                Notes = record.Memo("NOTES"),
            };
            foreach (var n in new[] { AsNumber(rma.CustomerRmaNumber), AsNumber(rma.SupplierRmaNumber) })
                if (n is { } value) _maxRmaNumber = Math.Max(_maxRmaNumber, value);
            rows.Add(One(rma, nameof(Rma), record));
        }
        result.Imported = await SaveAsync(rows, ct);
        int expected = result.Records - result.Blank - result.Deleted;
        Check("RMAs: records (live)", expected, async (db, t) => await db.Rmas.CountAsync(t));
    }

    /// <summary>
    /// SALESnnn.DBF (sale lines of station nnn) and TOTALnnn.DBF (its End of Day totals). Best effort: lines are
    /// grouped into sales by invoice number and date, and the payment is taken from the line's method of payment.
    /// </summary>
    private async Task ImportSalesAsync(string code, int locationId, string folder, CancellationToken ct)
    {
        int saleLines = 0, sessions = 0;
        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var match = StationFile().Match(Path.GetFileName(file));
            if (!match.Success) continue;
            int station = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (station is < 1 or > 999) continue;
            _stationNumbers.Add(station);
            if (match.Groups[1].Value.Equals("SALES", StringComparison.OrdinalIgnoreCase))
                saleLines += await ImportSaleLinesAsync(locationId, station, file, ct);
            else
                sessions += await ImportDrawerTotalsAsync(locationId, station, file, ct);
        }
        int lines = saleLines, totals = sessions;
        Check($"Sales {code}: sale lines (live)", lines,
            async (db, t) => await db.SaleLines.CountAsync(l => db.Sales.Any(s => s.Id == l.SaleId && s.LocationId == locationId), t));
        Check($"Sales {code}: End of Day totals", totals, async (db, t) => await db.DrawerSessions.CountAsync(s => s.LocationId == locationId, t));
    }

    private async Task<int> ImportSaleLinesAsync(int locationId, int station, string path, CancellationToken ct)
    {
        using var table = Open(path);
        var result = Result(table, nameof(Sale));
        var traceOnly = new List<Pending>();
        var groups = new List<(string Key, List<SourceRecord> Lines)>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in table.Records())
        {
            if (record.IsBlank) { result.Blank++; continue; }
            if (record.IsDeleted) { result.Deleted++; traceOnly.Add(TraceOnly(nameof(SaleLine), record)); continue; }
            var key = (record.Text("INVNO") ?? "") + "|" + record.Date("SALEDATE")?.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            if (!index.TryGetValue(key, out var at))
            {
                index[key] = at = groups.Count;
                groups.Add((key, []));
            }
            groups[at].Lines.Add(record);
        }

        var rows = new List<Pending>(traceOnly);
        int lineCount = 0;
        foreach (var (_, lines) in groups)
        {
            var first = lines[0];
            var date = first.Date("SALEDATE") ?? DateOnly.FromDateTime(_now);
            var time = TimeOnly.TryParseExact(first.Text("SALESTIME") ?? "", ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : TimeOnly.MinValue;
            var invoiceText = first.Text("INVNO");
            long invoiceNumber = AsNumber(invoiceText) ?? 0;
            if (invoiceNumber == 0 && invoiceText is not null) _log.Warn(first.Where, "INVNO is not a number; sale imported with invoice number 0");
            _maxInvoiceNumber = Math.Max(_maxInvoiceNumber, invoiceNumber);
            var customerNumber = first.Text("CUSTNO");
            var sale = new Sale
            {
                InvoiceNumber = invoiceNumber,
                StationNumber = station,
                LocationId = locationId,
                SoldAt = date.ToDateTime(time),
                Staff = first.Text("STAFF"),
                ClientId = ClientId(null, customerNumber),
                ClientName = first.Text("CLIENT"),
                CustomerNumber = customerNumber,
                Status = lines.Any(l => l.Text("SALESTATUS")?.Contains("VOID", StringComparison.OrdinalIgnoreCase) == true) ? SaleStatus.Voided : SaleStatus.Completed,
                Tax1Name = first.Text("TAX1NAME"),
                Tax2Name = first.Text("TAX2NAME"),
                Notes = "Imported from " + table.RelativePath,
            };
            var pending = new Pending(sale);
            int lineNumber = 0;
            foreach (var record in lines)
            {
                decimal quantity = record.Dec("QTY");
                decimal tax1 = record.Dec("TAX1"), tax2 = record.Dec("TAX2");
                var plu = record.Text("PLU");
                var line = Remember(new SaleLine
                {
                    LineNumber = ++lineNumber,
                    Kind = quantity < 0 ? SaleLineKind.Return : SaleLineKind.Sale,
                    ItemId = ItemId(locationId, plu),
                    Plu = plu,
                    Description = record.Text("PROD") ?? "",
                    Quantity = quantity,
                    UnitPrice = record.Dec("PRICE"),
                    DiscountAmount = record.Dec("DISCOUNT"),
                    LineTotal = record.Dec("TOTAL"),
                    Cogs = record.Dec("COGS"),
                    Department = record.Text("DEPT"),
                    Taxable1 = tax1 != 0,
                    Taxable2 = tax2 != 0,
                    Tax1 = tax1,
                    Tax2 = tax2,
                    SupplierName = record.Text("SUPPLIER"),
                    Commission = record.Dec("COMISSION"),
                }, record);
                sale.Lines.Add(line);
                pending.Traces.Add((line, nameof(SaleLine), record, record.ToJson()));
                lineCount++;
            }
            sale.Subtotal = sale.Lines.Sum(l => l.LineTotal);
            sale.Tax1 = sale.Lines.Sum(l => l.Tax1);
            sale.Tax2 = sale.Lines.Sum(l => l.Tax2);
            sale.Total = sale.Subtotal + sale.Tax1 + sale.Tax2;
            sale.Cogs = sale.Lines.Sum(l => l.Cogs);
            foreach (var byTender in lines.GroupBy(l => l.Text("MOP") ?? "Cash", StringComparer.OrdinalIgnoreCase))
            {
                var tenderLines = byTender.ToList();
                sale.Payments.Add(new SalePayment
                {
                    TenderType = byTender.Key,
                    Amount = tenderLines.Sum(l => l.Dec("TOTAL") + l.Dec("TAX1") + l.Dec("TAX2")),
                    Tendered = tenderLines.Sum(l => l.Dec("TOTAL") + l.Dec("TAX1") + l.Dec("TAX2")),
                    AuthCode = tenderLines.Select(l => l.Text("AUTHCODE")).FirstOrDefault(a => a is not null),
                    Reference = tenderLines.Select(l => l.Text("REFNO")).FirstOrDefault(a => a is not null),
                });
            }
            sale.IsOnAccount = sale.Payments.Any(p => p.TenderType.Contains("ACCOUNT", StringComparison.OrdinalIgnoreCase));
            rows.Add(pending);
        }
        result.Imported = await SaveAsync(rows, ct);
        return lineCount;
    }

    private async Task<int> ImportDrawerTotalsAsync(int locationId, int station, string path, CancellationToken ct)
    {
        using var table = Open(path);
        var result = Result(table, nameof(DrawerSession));
        var rows = new List<Pending>();
        foreach (var record in table.Records())
        {
            if (record.IsBlank) { result.Blank++; continue; }
            if (record.IsDeleted) { result.Deleted++; rows.Add(TraceOnly(nameof(DrawerSession), record)); continue; }
            var date = record.Date("SALES_DATE");
            if (date is null) _log.Warn(record.Where, "SALES_DATE is empty; the table date was used");
            var day = date ?? table.LastUpdated ?? DateOnly.FromDateTime(_now);
            var time = TimeOnly.TryParseExact(record.Text("SALES_TIME") ?? "", ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : TimeOnly.MinValue;
            var closedAt = day.ToDateTime(time);
            decimal tax1 = record.Dec("TAX1_GROSS"), tax2 = record.Dec("TAX2_GROSS"), receipts = record.Dec("R_TOTAL");
            var session = new DrawerSession
            {
                StationNumber = station,
                LocationId = locationId,
                OpenedAt = closedAt,
                ClosedAt = closedAt,
                LastSaleAt = closedAt,
                PayIns = record.Dec("PAYIN"),
                PayOuts = record.Dec("PAYOUT"),
                CustomerCount = record.Int("CUSTCOUNT", 0),
                Tax1Total = tax1,
                Tax2Total = tax2,
                // Net sales: receipts less the two taxes and the second charge (to confirm against a real End of Day).
                NetSales = receipts - tax1 - tax2 - record.Dec("CH2_GROSS"),
                NoTaxSales = record.Dec("NOTAXSALE"),
                Cogs = record.Dec("R_COGS"),
                CustomerCredits = record.Dec("CUSCREDITS"),
                BottleReturns = record.Dec("BOTLRETURN"),
                TotalReceipts = receipts,
                IsFlagged = record.Bool("PRTFLAG"),
            };
            foreach (var (field, tender) in DrawerTenders)
                if (record.Dec(field) is var amount && amount != 0)
                    session.Tenders.Add(new DrawerSessionTender { TenderType = tender, Amount = amount });
            rows.Add(One(session, nameof(DrawerSession), record));
        }
        result.Imported = await SaveAsync(rows, ct);
        return result.Imported;
    }

    /// <summary>
    /// One purchase order per file PO&lt;number&gt;.&lt;LOC&gt; in the location's PO folder (PO.DBF is the empty template).
    /// The status is inferred: received when any line has a quantity received, posted when it was sent or read.
    /// </summary>
    private async Task ImportPurchaseOrdersAsync(string code, int locationId, string folder, CancellationToken ct)
    {
        int orders = 0, lineCount = 0;
        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var extension = Path.GetExtension(file).TrimStart('.');
            if (!extension.Equals(code, StringComparison.OrdinalIgnoreCase)) continue;
            var match = PurchaseOrderFile().Match(name);
            if (!match.Success) continue;
            long number = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            using var table = Open(file);
            var result = Result(table, nameof(PurchaseOrder));
            if (await NumberTakenAsync(number, ct))
            {
                _log.Warn(table.RelativePath, $"purchase order number {number} already imported from another location; this order was not imported");
                _report.NotImported.Add($"{table.RelativePath}: duplicate purchase order number {number}");
                continue;
            }
            _maxPurchaseOrderNumber = Math.Max(_maxPurchaseOrderNumber, number);
            var created = table.LastUpdated?.ToDateTime(TimeOnly.MinValue) ?? _now;
            var order = new PurchaseOrder
            {
                Number = number,
                LocationId = locationId,
                CreatedAt = created,
                Method = OrderMethod.Blank,
                Description = "Imported from " + table.RelativePath,
            };
            var pending = new Pending(order);
            bool sent = false;
            DateOnly? lastShipment = null;
            foreach (var record in table.Records())
            {
                if (record.IsBlank) { result.Blank++; continue; }
                if (record.IsDeleted) { result.Deleted++; pending.Traces.Add((null, nameof(PurchaseOrderLine), record, record.ToJson())); continue; }
                var plu = record.Text("PLU") ?? "";
                var supplierName = record.Text("SUPPLIER");
                decimal caseQuantity = record.Dec("PKG");
                var line = Remember(new PurchaseOrderLine
                {
                    ItemId = ItemId(locationId, plu),
                    Plu = plu,
                    Description = record.Text("PROD") ?? "",
                    Size = record.Text("BSIZE"),
                    CaseQuantity = caseQuantity > 0 ? caseQuantity : 1,
                    OrderQuantity = record.Dec("ORDQTY"),
                    CostEach = record.Dec("LASTCOST"),
                    OrderCost = record.Dec("ORDERCOST"),
                    OnHandAtOrder = record.Dec("ONHAND"),
                    OnOrderAtOrder = record.Dec("ONORDER"),
                    BaseStock = record.Dec("BASTOCK"),
                    ReorderPoint = record.Dec("ROP"),
                    ReorderQuantity = record.Dec("ROQ"),
                    BackOrders = record.Dec("BKORDER"),
                    QuantityReceived = record.Dec("RECVD"),
                    SupplierId = SupplierId(supplierName),
                    SupplierName = supplierName,
                    ReorderNumber = record.Text("REORDERNO"),
                    IsFlagged = record.Bool("PRTFLAG"),
                }, record);
                if (record.Date("LASTSHIP") is { } shipped && (lastShipment is null || shipped > lastShipment)) lastShipment = shipped;
                sent |= record.Text("ORDERSENT") is not null || record.Date("WASREAD") is not null || record.Date("SREAD") is not null;
                order.Lines.Add(line);
                pending.Traces.Add((line, nameof(PurchaseOrderLine), record, record.ToJson()));
            }
            order.Status = order.Lines.Any(l => l.QuantityReceived != 0) ? PurchaseOrderStatus.Received
                : sent ? PurchaseOrderStatus.Posted : PurchaseOrderStatus.Draft;
            if (order.Status != PurchaseOrderStatus.Draft) order.PostedAt = created;
            if (order.Status == PurchaseOrderStatus.Received) order.ReceivedAt = lastShipment?.ToDateTime(TimeOnly.MinValue) ?? created;
            await SaveAsync([pending], ct);
            result.Imported = order.Lines.Count;
            orders++;
            lineCount += order.Lines.Count;
        }
        int expectedOrders = orders, expectedLines = lineCount;
        Check($"Purchase orders {code}: orders", expectedOrders, async (db, t) => await db.PurchaseOrders.CountAsync(p => p.LocationId == locationId, t));
        Check($"Purchase orders {code}: lines (live)", expectedLines,
            async (db, t) => await db.PurchaseOrderLines.CountAsync(l => db.PurchaseOrders.Any(p => p.Id == l.PurchaseOrderId && p.LocationId == locationId), t));
    }

    private async Task<bool> NumberTakenAsync(long number, CancellationToken ct)
    {
        await using var db = NewContext();
        return await db.PurchaseOrders.AnyAsync(p => p.Number == number, ct);
    }
}
