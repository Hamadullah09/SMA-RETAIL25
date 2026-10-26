using System.Globalization;
using RetailPlus.Core.Model;
using RetailPlus.Reports;

namespace RetailPlus.Data.Tests;

/// <summary>The receipt layout: built once from a sale, drawn as text.</summary>
public class ReceiptTests
{
    private static readonly CultureInfo C = CultureInfo.GetCultureInfo("en-US");

    private static Sale Sale(SaleStatus status = SaleStatus.Completed)
    {
        var sale = new Sale { InvoiceNumber = 1005, SoldAt = new DateTime(2026, 10, 26, 12, 28, 0), StationNumber = 1, Staff = "admin", Subtotal = 1830m, Total = 1830m, Status = status };
        sale.Lines.Add(new SaleLine { LineNumber = 1, Kind = SaleLineKind.Sale, Plu = "CNF-1001", Description = "Cadbury Dairy Milk 90g", Quantity = 3, UnitPrice = 450m, LineTotal = 1350m });
        sale.Lines.Add(new SaleLine { LineNumber = 2, Kind = SaleLineKind.Text, Description = "Gift wrapped" });
        sale.Lines.Add(new SaleLine { LineNumber = 3, Kind = SaleLineKind.Sale, Plu = "BEV-3002", Description = "Coca-Cola 1.5L", Quantity = 2, UnitPrice = 260m, DiscountAmount = 40m, LineTotal = 480m });
        sale.Payments.Add(new SalePayment { TenderType = "Cash", Amount = 1830m, Tendered = 2000m, Change = 170m });
        return sale;
    }

    [Fact]
    public void Receipt_has_items_totals_payment_and_change()
    {
        var r = Receipt.FromSale(Sale(), new StoreSettings { CompanyName = "Corner Mart", Address1 = "12 Mall Road", City = "Lahore", Phone = "042-1234567" }, null, null, culture: C);
        Assert.Equal("Corner Mart", r.StoreName);
        Assert.Equal(["12 Mall Road", "Lahore", "Tel 042-1234567"], r.StoreLines);
        Assert.Equal("SALES RECEIPT", r.Title);
        Assert.Null(r.Banner);
        Assert.Equal(3, r.Items.Count);
        Assert.True(r.Items[1].IsText);
        Assert.Equal(520m, r.Items[2].Gross);
        Assert.Equal(2000m, r.Payments.Single().Amount);
        Assert.Equal(170m, r.Change);
        Assert.Equal(2, r.ItemCount);
        Assert.Equal(5m, r.Pieces);
        Assert.Equal(["Thank you for shopping with us!"], r.Footer);
    }

    [Fact]
    public void Header_text_replaces_the_store_block_and_voids_are_marked()
    {
        var r = Receipt.FromSale(Sale(SaleStatus.Voided), new StoreSettings(), "Corner Mart\r\nOpen 9 to 9\r\n", "Come again", "COPY", culture: C);
        Assert.Equal("Corner Mart", r.StoreName);
        Assert.Equal(["Open 9 to 9"], r.StoreLines);
        Assert.Equal("VOID - COPY", r.Banner);
        Assert.Equal(["Come again"], r.Footer);
        Assert.Equal("SMA RETAIL", Receipt.FromSale(Sale(), new StoreSettings(), null, null, culture: C).StoreName);
    }

    [Fact]
    public void Text_receipt_lines_up_amounts_at_the_right_edge()
    {
        var text = ReceiptFormatter.Text(Receipt.FromSale(Sale(), new StoreSettings { CompanyName = "Corner Mart" }, null, null, culture: C), 40, C);
        var lines = text.Split(Environment.NewLine);
        Assert.All(lines, l => Assert.True(l.Length <= 40, l));
        Assert.Contains(lines, l => l.StartsWith("TOTAL", StringComparison.Ordinal) && l.EndsWith("1,830.00", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("CHANGE", StringComparison.Ordinal) && l.EndsWith("170.00", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("CNF-1001  3 x 450.00", StringComparison.Ordinal) && l.EndsWith("1,350.00", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.TrimStart().StartsWith("Discount", StringComparison.Ordinal) && l.EndsWith("-40.00", StringComparison.Ordinal));
        Assert.Contains("Items 2", text, StringComparison.Ordinal);
    }
}
