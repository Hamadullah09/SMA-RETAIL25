using System.Globalization;
using System.Text;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;

namespace RetailPlus.Reports;

/// <summary>Plain-text receipts, invoices and drawer reports for a 40-column (or wider) receipt printer.</summary>
public static class ReceiptFormatter
{
    public static string Sale(Sale sale, StoreSettings store, string? header, string? footer, int width = 40, CultureInfo? culture = null, string? reprint = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var sb = new StringBuilder();
        foreach (var line in StoreBlock(store, header)) sb.AppendLine(Center(line, width));
        sb.AppendLine();
        if (reprint is not null) sb.AppendLine(Center(reprint, width));
        if (sale.Status == SaleStatus.Voided) sb.AppendLine(Center("*** VOID ***", width));
        sb.AppendLine(Pair($"Invoice {sale.InvoiceNumber}", sale.SoldAt.ToString("g", culture), width));
        sb.AppendLine(Pair($"Station {sale.StationNumber:000}", sale.Staff ?? "", width));
        if (!string.IsNullOrWhiteSpace(sale.ClientName)) sb.AppendLine(Fit("Client: " + sale.ClientName, width));
        sb.AppendLine(new string('-', width));
        foreach (var l in sale.Lines.OrderBy(l => l.LineNumber))
        {
            if (l.Kind == SaleLineKind.Text) { sb.AppendLine(Fit(l.Description, width)); continue; }
            sb.AppendLine(Fit(l.Description, width));
            var qty = l.Quantity == 1 ? "" : $"{l.Quantity.ToString("0.###", culture)} @ {l.UnitPrice.ToString("N2", culture)}";
            var tax = (l.Taxable1 ? "T" : "") + (l.Taxable2 ? "S" : "");
            sb.AppendLine(Pair("  " + qty + (l.DiscountAmount != 0 ? $" less {l.DiscountAmount.ToString("N2", culture)}" : ""), l.LineTotal.ToString("N2", culture) + " " + tax.PadRight(2), width));
            if (!string.IsNullOrWhiteSpace(l.SerialNumber)) sb.AppendLine(Fit("  S/N " + l.SerialNumber, width));
        }
        sb.AppendLine(new string('-', width));
        sb.AppendLine(Pair("Subtotal", sale.Subtotal.ToString("N2", culture), width));
        if (sale.SubtotalDiscount != 0) sb.AppendLine(Pair("Discount", "-" + sale.SubtotalDiscount.ToString("N2", culture), width));
        if (sale.Tax1 != 0) sb.AppendLine(Pair($"{sale.Tax1Name ?? "Tax 1"} {sale.Tax1Rate.ToString("0.###", culture)}%" + (sale.TaxInclusive ? " incl." : ""), sale.Tax1.ToString("N2", culture), width));
        if (sale.Tax2 != 0) sb.AppendLine(Pair($"{sale.Tax2Name ?? "Tax 2"} {sale.Tax2Rate.ToString("0.###", culture)}%" + (sale.TaxInclusive ? " incl." : ""), sale.Tax2.ToString("N2", culture), width));
        if (sale.Rounding != 0) sb.AppendLine(Pair("Rounding", sale.Rounding.ToString("N2", culture), width));
        sb.AppendLine(Pair("TOTAL", sale.Total.ToString("N2", culture), width));
        foreach (var p in sale.Payments)
        {
            sb.AppendLine(Pair(p.TenderType, p.Tendered > p.Amount ? p.Tendered.ToString("N2", culture) : p.Amount.ToString("N2", culture), width));
            if (p.Change > 0) sb.AppendLine(Pair("Change", p.Change.ToString("N2", culture), width));
            if (!string.IsNullOrWhiteSpace(p.AuthCode)) sb.AppendLine(Fit("  Auth " + p.AuthCode, width));
        }
        if (store.BonusPointsEnabled && store.PrintPointsOnSlips && sale.PointsEarned > 0)
            sb.AppendLine(Pair("Points earned", sale.PointsEarned.ToString("0.##", culture), width));
        if (!string.IsNullOrWhiteSpace(store.TaxRegistrationNumber)) sb.AppendLine(Center("Tax reg. " + store.TaxRegistrationNumber, width));
        sb.AppendLine();
        foreach (var line in (footer ?? "Thank you!").Split('\n')) sb.AppendLine(Center(line.TrimEnd('\r'), width));
        return sb.ToString();
    }

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

    private static string Fit(string s, int width) => s.Length <= width ? s : s[..width];
    private static string Center(string s, int width) => s.Length >= width ? s[..width] : new string(' ', (width - s.Length) / 2) + s;

    private static string Pair(string left, string right, int width)
    {
        int room = width - right.Length - 1;
        if (room < 1) return Fit(left + " " + right, width);
        return Fit(left, room).PadRight(room) + " " + right;
    }
}
