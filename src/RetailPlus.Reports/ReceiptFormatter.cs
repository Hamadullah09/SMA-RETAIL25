using System.Globalization;
using System.Text;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;

namespace RetailPlus.Reports;

/// <summary>Plain-text receipts, invoices and drawer reports for a 40-column (or wider) receipt printer.</summary>
public static class ReceiptFormatter
{
    public static string Sale(Sale sale, StoreSettings store, string? header, string? footer, int width = 40, CultureInfo? culture = null, string? reprint = null) =>
        Text(Receipt.FromSale(sale, store, header, footer, reprint, culture: culture), width, culture);

    /// <summary>A receipt as plain text: framed store block, items with code and quantity, a boxed TOTAL, change.</summary>
    public static string Text(Receipt r, int width = 40, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string M(decimal v) => v.ToString("N2", culture);
        var sb = new StringBuilder();
        sb.AppendLine(Center(r.StoreName.ToUpper(culture), width));
        foreach (var line in r.StoreLines) sb.AppendLine(Center(line, width));
        sb.AppendLine(new string('=', width));
        sb.AppendLine(Center(r.Title, width));
        if (r.Banner is not null) sb.AppendLine(Center($"*** {r.Banner} ***", width));
        sb.AppendLine();
        foreach (var line in Heading(r, width, culture)) sb.AppendLine(line);
        sb.AppendLine(new string('-', width));
        foreach (var i in r.Items)
        {
            sb.AppendLine(Fit(i.Name, width));
            if (i.IsText) continue;
            foreach (var line in ItemDetail(i, width, culture)) sb.AppendLine(line);
        }
        sb.AppendLine(new string('-', width));
        foreach (var t in r.Totals) sb.AppendLine(Pair(t.Label, M(t.Amount), width));
        sb.AppendLine(new string('=', width));
        sb.AppendLine(Pair("TOTAL", M(r.Total), width));
        sb.AppendLine(new string('=', width));
        foreach (var p in r.Payments)
        {
            sb.AppendLine(Pair(p.Label, M(p.Amount), width));
            if (p.Note is not null) sb.AppendLine(Fit("  " + p.Note, width));
        }
        if (r.Change > 0) sb.AppendLine(Pair("CHANGE", M(r.Change), width));
        sb.AppendLine(new string('-', width));
        sb.AppendLine(Center(Summary(r, culture), width));
        foreach (var n in r.Notes) sb.AppendLine(Center(n, width));
        sb.AppendLine();
        foreach (var f in r.Footer) sb.AppendLine(Center(f, width));
        return sb.ToString();
    }

    /// <summary>Invoice, date, cashier, station and customer lines.</summary>
    public static IEnumerable<string> Heading(Receipt r, int width, CultureInfo culture)
    {
        yield return Pair($"Invoice #{r.InvoiceNumber}", r.SoldAt.ToString("g", culture), width);
        yield return Pair("Cashier: " + (r.Cashier ?? "-"), $"Station {r.Station:000}", width);
        if (r.Customer is not null) yield return Fit("Customer: " + r.Customer, width);
    }

    /// <summary>Under an item's name: code, quantity x price and the amount; then its discount and note.</summary>
    public static IEnumerable<string> ItemDetail(ReceiptItem i, int width, CultureInfo culture)
    {
        var qty = $"{i.Quantity.ToString("0.###", culture)} x {i.UnitPrice.ToString("N2", culture)}";
        yield return Pair("  " + (string.IsNullOrWhiteSpace(i.Code) ? "" : i.Code + "  ") + qty, i.Gross.ToString("N2", culture), width);
        if (i.Discount != 0) yield return Pair("  Discount", (-i.Discount).ToString("N2", culture), width);
        if (i.Note is not null) yield return Fit("  " + i.Note, width);
    }

    /// <summary>"Items 3 · Qty 5".</summary>
    public static string Summary(Receipt r, CultureInfo culture) =>
        $"Items {r.ItemCount}  \u00b7  Qty {r.Pieces.ToString("0.###", culture)}";

    public static string Drawer(DrawerReport report, StoreSettings store, int width = 40, CultureInfo? culture = null, bool final = true)
    {
        culture ??= CultureInfo.CurrentCulture;
        var s = report.Session;
        var sb = new StringBuilder();
        sb.AppendLine(Center(store.CompanyName ?? "", width));
        sb.AppendLine(Center(final ? "END OF DAY (Z) REPORT" : "DRAWER (X) REPORT", width));
        sb.AppendLine(Pair($"Station {s.StationNumber:000}", "", width));
        sb.AppendLine(Pair("Opened", s.OpenedAt.ToString("g", culture), width));
        if (s.ClosedAt is { } closed) sb.AppendLine(Pair("Closed", closed.ToString("g", culture), width));
        sb.AppendLine(new string('-', width));
        void Money(string label, decimal value) => sb.AppendLine(Pair(label, value.ToString("N2", culture), width));
        sb.AppendLine(Pair("Customers", s.CustomerCount.ToString(culture), width));
        Money("Net sales", s.NetSales);
        Money("Tax 1", s.Tax1Total);
        Money("Tax 2", s.Tax2Total);
        Money("Gross sales", report.GrossSales);
        Money("Non-taxable sales", s.NoTaxSales);
        Money("Customer credits", s.CustomerCredits);
        Money("Bottle returns", s.BottleReturns);
        Money("Cost of goods", s.Cogs);
        Money("Gross profit", report.GrossProfit);
        sb.AppendLine(new string('-', width));
        foreach (var t in report.Tenders) Money(t.TenderType, t.Amount);
        Money("Total receipts", s.TotalReceipts);
        sb.AppendLine(new string('-', width));
        Money("Opening float", s.OpeningFloat);
        Money("Pay ins", s.PayIns);
        Money("Pay outs", s.PayOuts);
        Money("Expected cash", report.ExpectedCash);
        int noSales = report.Movements.Count(m => m.Kind == DrawerMovementKind.NoSale);
        if (noSales > 0) sb.AppendLine(Pair("No-sale openings", noSales.ToString(culture), width));
        return sb.ToString();
    }

    public static string Statement(Client client, IReadOnlyList<Invoice> open, IReadOnlyList<InvoicePayment> payments, StoreSettings store, int width = 60, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var sb = new StringBuilder();
        foreach (var line in StoreBlock(store, null)) sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine("STATEMENT  " + DateTime.Today.ToString("d", culture));
        sb.AppendLine();
        sb.AppendLine(client.Company ?? $"{client.FirstName} {client.LastName}".Trim());
        if (!string.IsNullOrWhiteSpace(client.Address1)) sb.AppendLine(client.Address1);
        if (!string.IsNullOrWhiteSpace(client.Address2)) sb.AppendLine(client.Address2);
        sb.AppendLine($"{client.City} {client.State} {client.Zip}".Trim());
        sb.AppendLine();
        sb.AppendLine(new string('-', width));
        sb.AppendLine(Pair("Invoice     Date", "Total       Balance", width));
        foreach (var i in open)
            sb.AppendLine(Pair($"{i.InvoiceNumber,-10}  {i.InvoiceDate.ToString("d", culture)}", $"{i.Total.ToString("N2", culture),10}  {i.Balance.ToString("N2", culture),12}", width));
        if (payments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Payments received");
            foreach (var p in payments) sb.AppendLine(Pair($"  {p.PaidOn.ToString("d", culture)} {p.TenderType}", p.Amount.ToString("N2", culture), width));
        }
        sb.AppendLine(new string('-', width));
        sb.AppendLine(Pair("BALANCE OWING", open.Sum(i => i.Balance).ToString("N2", culture), width));
        return sb.ToString();
    }

    private static IEnumerable<string> StoreBlock(StoreSettings store, string? header)
    {
        if (!string.IsNullOrWhiteSpace(header))
        {
            foreach (var line in header.Split('\n')) yield return line.TrimEnd('\r');
            yield break;
        }
        if (!string.IsNullOrWhiteSpace(store.CompanyName)) yield return store.CompanyName!;
        if (!string.IsNullOrWhiteSpace(store.Address1)) yield return store.Address1!;
        if (!string.IsNullOrWhiteSpace(store.Address2)) yield return store.Address2!;
        var city = $"{store.City} {store.State} {store.Zip}".Trim();
        if (city.Length > 0) yield return city;
        if (!string.IsNullOrWhiteSpace(store.Phone)) yield return store.Phone!;
    }

    public static string Fit(string s, int width) => s.Length <= width ? s : s[..width];
    public static string Center(string s, int width) => s.Length >= width ? s[..width] : new string(' ', (width - s.Length) / 2) + s;

    public static string Pair(string left, string right, int width)
    {
        int room = width - right.Length - 1;
        if (room < 1) return Fit(left + " " + right, width);
        return Fit(left, room).PadRight(room) + " " + right;
    }
}
