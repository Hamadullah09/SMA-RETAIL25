using System.Globalization;
using RetailPlus.Core.Model;

namespace RetailPlus.Reports;

/// <summary>A line of a receipt: an item with its code, quantity and price, or a line of text.</summary>
public sealed record ReceiptItem(string Name, string? Code, decimal Quantity, decimal UnitPrice, decimal Discount, decimal Total, string? Note, bool IsText = false)
{
    /// <summary>Quantity times price, before the line discount.</summary>
    public decimal Gross => Total + Discount;
}

/// <summary>A labelled amount: subtotal, a tax, a payment.</summary>
public sealed record ReceiptAmount(string Label, decimal Amount, string? Note = null);

/// <summary>
/// A sale receipt laid out once from the sale and the store's settings, then drawn as plain text
/// (<see cref="ReceiptFormatter.Text"/>), or by the app as an ESC/POS job and on screen.
/// </summary>
public sealed class Receipt
{
    public required string StoreName { get; init; }
    /// <summary>Address and phone lines under the store name.</summary>
    public IReadOnlyList<string> StoreLines { get; init; } = [];
    /// <summary>SALES RECEIPT, or REFUND when money went back to the customer.</summary>
    public required string Title { get; init; }
    /// <summary>COPY, VOID, TRAINING; null on a normal receipt.</summary>
    public string? Banner { get; init; }
    public long InvoiceNumber { get; init; }
    public DateTime SoldAt { get; init; }
    public int Station { get; init; }
    public string? Cashier { get; init; }
    public string? Customer { get; init; }
    public IReadOnlyList<ReceiptItem> Items { get; init; } = [];
    /// <summary>Subtotal, discount, taxes and rounding, in that order.</summary>
    public IReadOnlyList<ReceiptAmount> Totals { get; init; } = [];
    public decimal Total { get; init; }
    /// <summary>What the customer handed over, per payment.</summary>
    public IReadOnlyList<ReceiptAmount> Payments { get; init; } = [];
    public decimal Change { get; init; }
    /// <summary>Lines of goods on the sale.</summary>
    public int ItemCount { get; init; }
    /// <summary>Pieces of goods on the sale (sum of the quantities).</summary>
    public decimal Pieces { get; init; }
    /// <summary>Points earned, tax registration.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
    public IReadOnlyList<string> Footer { get; init; } = [];

    /// <summary>
    /// The receipt of a saved sale. The receipt header text, when set, replaces the store block (its first line is the
    /// name); otherwise the store's name, address and phone are used, and <paramref name="fallbackName"/> when no name is set.
    /// </summary>
    public static Receipt FromSale(Sale sale, StoreSettings store, string? header, string? footer, string? banner = null,
        string fallbackName = "SMA RETAIL", CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var headerLines = Lines(header);
        string name;
        List<string> storeLines;
        if (headerLines.Count > 0)
        {
            name = headerLines[0];
            storeLines = headerLines.Skip(1).ToList();
        }
        else
        {
            name = string.IsNullOrWhiteSpace(store.CompanyName) ? fallbackName : store.CompanyName.Trim();
            storeLines = new[] { store.Address1, store.Address2, $"{store.City} {store.State} {store.Zip}".Trim(), string.IsNullOrWhiteSpace(store.Phone) ? null : "Tel " + store.Phone }
                .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim()).ToList();
        }

        var items = sale.Lines.OrderBy(l => l.LineNumber).Select(l => l.Kind == SaleLineKind.Text
            ? new ReceiptItem(l.Description, null, 0, 0, 0, 0, null, IsText: true)
            : new ReceiptItem(l.Description, l.Plu, l.Quantity, l.UnitPrice, l.DiscountAmount, l.LineTotal,
                string.IsNullOrWhiteSpace(l.SerialNumber) ? null : "S/N " + l.SerialNumber)).ToList();

        string Rate(decimal rate) => rate.ToString("0.###", culture) + "%" + (sale.TaxInclusive ? " incl." : "");
        var totals = new List<ReceiptAmount> { new("Subtotal", sale.Subtotal) };
        if (sale.SubtotalDiscount != 0) totals.Add(new("Discount", -sale.SubtotalDiscount));
        if (sale.Tax1 != 0) totals.Add(new($"{sale.Tax1Name ?? "Tax 1"} {Rate(sale.Tax1Rate)}", sale.Tax1));
        if (sale.Tax2 != 0) totals.Add(new($"{sale.Tax2Name ?? "Tax 2"} {Rate(sale.Tax2Rate)}", sale.Tax2));
        if (sale.Rounding != 0) totals.Add(new("Rounding", sale.Rounding));

        var notes = new List<string>();
        if (store.BonusPointsEnabled && store.PrintPointsOnSlips && sale.PointsEarned > 0) notes.Add("Points earned: " + sale.PointsEarned.ToString("0.##", culture));
        if (!string.IsNullOrWhiteSpace(store.TaxRegistrationNumber)) notes.Add("Tax reg. " + store.TaxRegistrationNumber);

        var goods = sale.Lines.Where(l => l.Kind is SaleLineKind.Sale or SaleLineKind.Return).ToList();
        var footerLines = Lines(footer);
        return new Receipt
        {
            StoreName = name,
            StoreLines = storeLines,
            Title = sale.Total < 0 ? "REFUND" : "SALES RECEIPT",
            Banner = sale.Status == SaleStatus.Voided ? (banner is null ? "VOID" : "VOID - " + banner) : banner,
            InvoiceNumber = sale.InvoiceNumber,
            SoldAt = sale.SoldAt,
            Station = sale.StationNumber,
            Cashier = sale.Staff,
            Customer = string.IsNullOrWhiteSpace(sale.ClientName) ? null : sale.ClientName,
            Items = items,
            Totals = totals,
            Total = sale.Total,
            Payments = sale.Payments.Select(p => new ReceiptAmount(p.TenderType, p.Tendered > p.Amount ? p.Tendered : p.Amount,
                string.IsNullOrWhiteSpace(p.AuthCode) ? null : "Auth " + p.AuthCode)).ToList(),
            Change = sale.Payments.Sum(p => p.Change),
            ItemCount = goods.Count,
            Pieces = goods.Sum(l => Math.Abs(l.Quantity)),
            Notes = notes,
            Footer = footerLines.Count > 0 ? footerLines : ["Thank you for shopping with us!"],
        };
    }

    private static List<string> Lines(string? text) =>
        (text ?? "").Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0).ToList();
}
