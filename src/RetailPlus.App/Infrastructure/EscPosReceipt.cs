using System.Globalization;
using RetailPlus.Devices;
using RetailPlus.Reports;

namespace RetailPlus.App.Infrastructure;

/// <summary>
/// A receipt as an ESC/POS job: the logo, the store name in large letters, the items, a tall TOTAL, the change on white
/// on black, and the invoice number as a barcode. The printer driver adds the feed, cut and drawer pulse.
/// </summary>
public static class EscPosReceipt
{
    /// <param name="logo">The logo as a raster (see <see cref="EscPos.Raster"/>), or null for none.</param>
    public static EscPos Build(Receipt r, int width = 40, (byte[] Bits, int WidthBytes, int Height)? logo = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string M(decimal v) => v.ToString("N2", culture);
        var job = new EscPos().Center(true);
        if (logo is { } l) job.Raster(l.Bits, l.WidthBytes, l.Height).Line();
        job.Bold(true).DoubleSize(true).Line(ReceiptFormatter.Fit(r.StoreName, width / 2)).DoubleSize(false).Bold(false);
        foreach (var s in r.StoreLines) job.Line(ReceiptFormatter.Fit(s, width));
        job.Line(new string('=', width)).Bold(true).Line(r.Title).Bold(false);
        if (r.Banner is not null) job.Reverse(true).Bold(true).Line($"  {r.Banner}  ").Bold(false).Reverse(false);
        job.Center(false);
        foreach (var line in ReceiptFormatter.Heading(r, width, culture)) job.Line(line);
        job.Line(new string('-', width));
        foreach (var i in r.Items)
        {
            if (i.IsText)
            {
                job.Line(ReceiptFormatter.Fit(i.Name, width));
                continue;
            }
            job.Bold(true).Line(ReceiptFormatter.Fit(i.Name, width)).Bold(false);
            foreach (var line in ReceiptFormatter.ItemDetail(i, width, culture)) job.Line(line);
        }
        job.Line(new string('-', width));
        foreach (var t in r.Totals) job.Line(ReceiptFormatter.Pair(t.Label, M(t.Amount), width));
        job.Bold(true).DoubleHeight(true).Line(ReceiptFormatter.Pair("TOTAL", M(r.Total), width)).DoubleHeight(false).Bold(false);
        foreach (var p in r.Payments)
        {
            job.Line(ReceiptFormatter.Pair(p.Label, M(p.Amount), width));
            if (p.Note is not null) job.Line(ReceiptFormatter.Fit("  " + p.Note, width));
        }
        if (r.Change > 0) job.Bold(true).Reverse(true).Line(ReceiptFormatter.Pair(" CHANGE", M(r.Change) + " ", width)).Reverse(false).Bold(false);
        job.Line(new string('-', width)).Center(true);
        job.Line(ReceiptFormatter.Summary(r, culture));
        foreach (var n in r.Notes) job.Line(n);
        job.Line();
        job.Bold(true);
        foreach (var f in r.Footer) job.Line(ReceiptFormatter.Fit(f, width));
        job.Bold(false).Line();
        if (r.InvoiceNumber > 0) job.Barcode128(r.InvoiceNumber.ToString(CultureInfo.InvariantCulture)).Line();
        return job.Center(false);
    }
}
