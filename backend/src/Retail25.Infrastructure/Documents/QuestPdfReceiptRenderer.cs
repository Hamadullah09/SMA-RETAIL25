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
public static class QuestPdfReceiptRenderer
{
    private const float Mm = 2.834645f;

    /// <summary>80 mm roll, less the 4 mm of dead margin a thermal head cannot reach.</summary>
    private const float RollWidthMm = 80f;
    private const float SideMarginMm = 4f;

    private const int Columns = 40;

    public static byte[] Render(ReceiptDocument doc) => Build(doc).GeneratePdf();

    /// <summary>
    /// The composed receipt, before it becomes bytes.
    /// <para>
    /// Exposed for the same reason <c>BuildEnvelope</c> is: a byte count says nothing about whether
    /// the columns line up, and this can be rendered to an image and looked at.
    /// </para>
    /// </summary>
    public static IDocument Build(ReceiptDocument doc)
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
        });
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

        // Centred by the layout engine, not by padding with spaces. Padding only lands in the middle
        // when the text is the same size as the 40-column body; the shop name is larger, so counting
        // characters put it visibly off to one side.
        void Centred(string text, bool bold = false, float size = 8)
        {
            var item = column.Item().AlignCenter().Text(text);
            item.FontSize(size);

            if (bold)
            {
                item.Bold();
            }
        }

        var money = new NumberFormatInfo { NumberDecimalDigits = 2, NumberGroupSeparator = "," };

        string Amount(decimal value) => value.ToString("N", money);

        // Two columns of text, right-aligned on the second, done by padding rather than by a table:
        // a table would lay out proportionally and the receipt would stop being column-aligned.
        string Pair(string left, string right)
        {
            var room = Math.Max(1, Columns - right.Length);
            return (left.Length > room ? left[..room] : left).PadRight(room) + right;
        }

        // The same, with two columns held back on the right for tax flags. Every money line reserves
        // them whether or not it has flags, so the amounts form one column down the whole slip
        // instead of the item lines sitting two characters left of the totals.
        string Row(string left, string amount, string flags)
        {
            var trailing = " " + (flags.Length == 0 ? "  " : flags);
            var room = Math.Max(1, Columns - amount.Length - trailing.Length);

            return (left.Length > room ? left[..room] : left).PadRight(room) + amount + trailing;
        }

        string Money(string left, decimal value) => Row(left, Amount(value), string.Empty);

        // The tax's own initial rather than "1" and "2". On a line reading "12 x 3,900.00 12" the
        // flags were indistinguishable from the quantity; G and P are not. Taken from the configured
        // tax names, so a VAT shop gets V and nothing here needs changing.
        static string Flag(bool applies, string taxName)
            => applies && !string.IsNullOrWhiteSpace(taxName) ? taxName.Trim()[..1].ToUpperInvariant() : " ";

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
                var flags = $"{Flag(line.Tax1Applies, doc.Tax1Name)}{Flag(line.Tax2Applies, doc.Tax2Name)}";
                Line(Row($"  {quantity} x {Amount(line.UnitPrice)}", Amount(line.ExtendedNet), flags));
            }
            else
            {
                Line($"  {quantity}");
            }

            // Suppressed on a packing slip, which shows no money by design (guide p.12). "Price level
            // 2" is a pricing decision, and a slip that travels with the goods to a customer should
            // not carry one -- least of all on the one document chosen for showing no prices.
            if (showMoney && !string.IsNullOrWhiteSpace(line.PriceOriginLabel))
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
            Line(Money(adjustment.Label, adjustment.Amount));
        }

        Line(Money("Subtotal", doc.Subtotal));

        if (doc.DiscountTotal != 0)
        {
            Line(Money("Discount", -doc.DiscountTotal));
        }

        // Named from the tax configuration, never labelled "Tax": the shop's own words are what a
        // customer and an auditor both expect to see.
        if (doc.Tax1Total != 0)
        {
            Line(Money(doc.Tax1Name, doc.Tax1Total));
        }

        if (doc.Tax2Total != 0)
        {
            Line(Money(doc.Tax2Name, doc.Tax2Total));
        }

        if (doc.AddOnCharge != 0)
        {
            Line(Money(doc.AddOnChargeName, doc.AddOnCharge));
        }

        if (doc.RoundingAdjustment != 0)
        {
            Line(Money("Rounding", doc.RoundingAdjustment));
        }

        // Bold at the body size, not larger. At 10pt a 40-character line no longer fits 72mm, so the
        // most important line on the receipt wrapped and the amount fell onto its own line under the
        // word TOTAL. Emphasis that breaks the number it is emphasising is worse than no emphasis.
        Line(new string('=', Columns));
        Line(Row("TOTAL", $"{doc.CurrencySymbol} {Amount(doc.GrandTotal)}", string.Empty), bold: true);
        Line();

        foreach (var tender in doc.Tenders)
        {
            Line(Money(tender.Name, tender.AmountTendered == 0 ? tender.Amount : tender.AmountTendered));

            if (!string.IsNullOrWhiteSpace(tender.Reference))
            {
                Line($"  {tender.Reference}");
            }
        }

        if (doc.ChangeGiven != 0)
        {
            Line(Money("Change", doc.ChangeGiven));
        }

        if (doc.LoyaltyPointsEarned != 0 || doc.LoyaltyPointsBalance != 0)
        {
            Line();
            Line(Row("Points earned", doc.LoyaltyPointsEarned.ToString(CultureInfo.InvariantCulture), string.Empty));
            Line(Row("Points balance", doc.LoyaltyPointsBalance.ToString(CultureInfo.InvariantCulture), string.Empty));
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
