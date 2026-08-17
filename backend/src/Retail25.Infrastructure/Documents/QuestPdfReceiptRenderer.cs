using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Retail25.Contracts.Terminals;

namespace Retail25.Infrastructure.Documents;

/// <summary>
/// A receipt as a PDF, for printing from a browser.
/// <para>
/// Deliberately shaped like the slip it stands in for: 80 mm wide, monospaced, and as tall as the
/// sale needs. A receipt reformatted onto A4 with proportional text is a different document — the
/// columns stop lining up, and a customer comparing it against a thermal copy of the same sale
/// cannot tell they are the same sale. Continuous height also means no page break lands in the
/// middle of the tender block.
/// </para>
/// <para>
/// It consumes the same <see cref="ReceiptDocument"/> as the agent's ESC/POS renderer. Neither
/// re-derives a total, because both are handed one that was frozen at the moment of sale.
/// </para>
/// </summary>
internal static class QuestPdfReceiptRenderer
{
    private const float Mm = 2.834645f;

    /// <summary>80 mm roll, less the 4 mm of dead margin a thermal head cannot reach.</summary>
    private const float RollWidthMm = 80f;
    private const float SideMarginMm = 4f;

    private const int Columns = 40;

    public static byte[] Render(ReceiptDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        QuestPdfLicence.Accept();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                // Continuous: the roll is as long as the sale. A fixed page height would break a
                // long basket across sheets and land a page break inside the tender block.
                page.ContinuousSize((RollWidthMm - (SideMarginMm * 2)) * Mm, Unit.Point);
                page.Margin(SideMarginMm * Mm);

                // Monospaced, because every alignment below is done with spaces rather than a table.
                // That is what keeps this identical to the thermal slip instead of merely similar.
                page.DefaultTextStyle(style => style.FontFamily(Fonts.Consolas).FontSize(8));

                page.Content().Column(column => Compose(column, doc));
            });
        }).GeneratePdf();
    }

    private static void Compose(ColumnDescriptor column, ReceiptDocument doc)
    {
        void Line(string text = "", bool bold = false, float size = 8)
        {
            var item = column.Item().Text(text);
            item.FontSize(size);

            if (bold)
            {
                item.Bold();
            }
        }

        void Centred(string text, bool bold = false, float size = 8)
            => Line(text.Length >= Columns ? text : text.PadLeft((Columns + text.Length) / 2), bold, size);

        var money = new NumberFormatInfo { NumberDecimalDigits = 2, NumberGroupSeparator = "," };

        string Amount(decimal value) => value.ToString("N", money);

        // Two columns of text, right-aligned on the second, done by padding rather than by a table:
        // a table would lay out proportionally and the receipt would stop being column-aligned.
        string Pair(string left, string right)
        {
            var room = Math.Max(1, Columns - right.Length);
            return (left.Length > room ? left[..room] : left).PadRight(room) + right;
        }

        if (doc.IsTraining)
        {
            Centred("*** TRAINING — NOT A SALE ***", bold: true);
            Line();
        }

        Centred(doc.BusinessName, bold: true, size: 11);

        foreach (var line in doc.BusinessAddress)
        {
            Centred(line);
        }

        if (!string.IsNullOrWhiteSpace(doc.TaxRegistrationNumber))
        {
            Centred(doc.TaxRegistrationNumber);
        }

        Line();
        Line(Pair($"No. {doc.TransactionNumber}", doc.CompletedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
        Line(Pair($"Till {doc.StationCode}", doc.StaffName));

        if (!string.IsNullOrWhiteSpace(doc.CustomerName))
        {
            Line(doc.CustomerName);
        }

        if (doc.IsVoided)
        {
            Line();
            Centred("*** VOIDED ***", bold: true);
        }

        if (doc.IsReprint)
        {
            Centred("— REPRINT —");
        }

        Line(new string('-', Columns));

        var showMoney = doc.Format != ReceiptFormat.PackingSlip;

        foreach (var line in doc.Lines)
        {
            Line(line.Description);

            var quantity = line.Quantity.ToString("0.###", CultureInfo.InvariantCulture);

            if (showMoney)
            {
                var taxFlags = $"{(line.Tax1Applies ? "1" : " ")}{(line.Tax2Applies ? "2" : " ")}";
                Line(Pair($"  {quantity} x {Amount(line.UnitPrice)} {taxFlags}", Amount(line.ExtendedNet)));
            }
            else
            {
                Line($"  {quantity}");
            }

            if (!string.IsNullOrWhiteSpace(line.PriceOriginLabel))
            {
                Line($"    {line.PriceOriginLabel}");
            }

            if (!string.IsNullOrWhiteSpace(line.Note))
            {
                Line($"    {line.Note}");
            }
        }

        if (!showMoney)
        {
            Footer(doc, Line, Centred);
            return;
        }

        Line(new string('-', Columns));

        foreach (var adjustment in doc.Adjustments)
        {
            Line(Pair(adjustment.Label, Amount(adjustment.Amount)));
        }

        Line(Pair("Subtotal", Amount(doc.Subtotal)));

        if (doc.DiscountTotal != 0)
        {
            Line(Pair("Discount", Amount(-doc.DiscountTotal)));
        }

        // Named from the tax configuration, never labelled "Tax": the shop's own words are what a
        // customer and an auditor both expect to see.
        if (doc.Tax1Total != 0)
        {
            Line(Pair(doc.Tax1Name, Amount(doc.Tax1Total)));
        }

        if (doc.Tax2Total != 0)
        {
            Line(Pair(doc.Tax2Name, Amount(doc.Tax2Total)));
        }

        if (doc.AddOnCharge != 0)
        {
            Line(Pair(doc.AddOnChargeName, Amount(doc.AddOnCharge)));
        }

        if (doc.RoundingAdjustment != 0)
        {
            Line(Pair("Rounding", Amount(doc.RoundingAdjustment)));
        }

        Line(Pair("TOTAL", $"{doc.CurrencySymbol} {Amount(doc.GrandTotal)}"), bold: true, size: 10);
        Line();

        foreach (var tender in doc.Tenders)
        {
            Line(Pair(tender.Name, Amount(tender.AmountTendered == 0 ? tender.Amount : tender.AmountTendered)));

            if (!string.IsNullOrWhiteSpace(tender.Reference))
            {
                Line($"  {tender.Reference}");
            }
        }

        if (doc.ChangeGiven != 0)
        {
            Line(Pair("Change", Amount(doc.ChangeGiven)));
        }

        if (doc.LoyaltyPointsEarned != 0 || doc.LoyaltyPointsBalance != 0)
        {
            Line();
            Line(Pair("Points earned", doc.LoyaltyPointsEarned.ToString(CultureInfo.InvariantCulture)));
            Line(Pair("Points balance", doc.LoyaltyPointsBalance.ToString(CultureInfo.InvariantCulture)));
        }

        Footer(doc, Line, Centred);
    }

    private static void Footer(
        ReceiptDocument doc,
        Action<string, bool, float> line,
        Action<string, bool, float> centred)
    {
        if (doc.PrintSignatureLine)
        {
            line(string.Empty, false, 8);
            line(new string('_', Columns), false, 8);
            line("Signature", false, 8);
        }

        if (!string.IsNullOrWhiteSpace(doc.FooterMessage))
        {
            line(string.Empty, false, 8);
            centred(doc.FooterMessage, false, 8);
        }

        // Trailing blank lines stand in for the roll a thermal printer feeds before the cut, so a
        // sheet-printed copy has the same white space at the bottom as the slip it replaces.
        line(string.Empty, false, 8);
        line(string.Empty, false, 8);
    }
}
