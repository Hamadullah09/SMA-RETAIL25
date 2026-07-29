using System.Globalization;
using System.Text;
using Retail25.Contracts.Terminals;

namespace Retail25.TerminalAgent.Peripherals;

/// <summary>
/// Turns a <see cref="ReceiptDocument"/> into the bytes a slip printer wants.
/// <para>
/// The document knows nothing about printers and the printer profile knows nothing about sales; this
/// is the only place the two meet. Column width comes from the profile rather than from the format,
/// because a 40-column format on a 32-column printer wraps into unreadable mush and stores do fit
/// narrower rolls than the profile name suggests.
/// </para>
/// </summary>
public static class EscPosRenderer
{
    /// <summary>Code page 437 is what almost every ESC/POS printer defaults to.</summary>
    private static readonly Encoding PrinterEncoding = CreatePrinterEncoding();

    public static byte[] Render(ReceiptDocument document, PrinterProfileContract profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profile);

        var columns = ResolveColumns(document.Format, profile);
        var body = RenderText(document, columns);

        using var stream = new MemoryStream();

        Write(stream, EscapeSequence.Parse(profile.SetupCommand));
        Write(stream, PrinterEncoding.GetBytes(body));

        if (profile.PageEject)
        {
            Write(stream, "\n\n\n"u8.ToArray());
        }

        Write(stream, EscapeSequence.Parse(profile.CutterCommand));

        return stream.ToArray();
    }

    /// <summary>
    /// The plain-text form. Exposed because it is also what the on-screen preview shows and what the
    /// snapshot tests assert against — a receipt regression should be visible in a diff, not buried
    /// in a byte array.
    /// </summary>
    public static string RenderText(ReceiptDocument document, int columns)
    {
        ArgumentNullException.ThrowIfNull(document);

        var slip = new StringBuilder();
        var money = document.CurrencySymbol;

        Centre(slip, document.BusinessName.ToUpperInvariant(), columns);
        foreach (var line in document.BusinessAddress)
        {
            Centre(slip, line, columns);
        }

        if (!string.IsNullOrWhiteSpace(document.TaxRegistrationNumber))
        {
            Centre(slip, document.TaxRegistrationNumber!, columns);
        }

        slip.AppendLine();

        if (document.IsReprint)
        {
            Centre(slip, "*** REPRINT ***", columns);
        }

        if (document.IsVoided)
        {
            Centre(slip, "*** VOIDED ***", columns);
        }

        if (document.Format == ReceiptFormat.PackingSlip)
        {
            Centre(slip, "PACKING SLIP", columns);
        }

        slip.AppendLine(Pair(
            $"No. {document.TransactionNumber.ToString(CultureInfo.InvariantCulture)}",
            document.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            columns));

        slip.AppendLine(Pair($"Till {document.StationCode}", document.StaffName, columns));

        if (!string.IsNullOrWhiteSpace(document.CustomerName))
        {
            slip.AppendLine(document.CustomerName!);
        }

        slip.AppendLine(new string('-', columns));

        foreach (var line in document.Lines)
        {
            RenderLine(slip, line, columns, document.Format);
        }

        slip.AppendLine(new string('-', columns));

        // A packing slip carries no money at all (guide p.12).
        if (document.Format == ReceiptFormat.PackingSlip)
        {
            slip.AppendLine();
            Centre(slip, "This is not an invoice", columns);
            return slip.ToString();
        }

        slip.AppendLine(Pair("Subtotal", Money(document.Subtotal, money), columns));

        foreach (var adjustment in document.Adjustments)
        {
            slip.AppendLine(Pair(adjustment.Label, "-" + Money(adjustment.Amount, money), columns));
        }

        if (document.AddOnCharge != 0m)
        {
            slip.AppendLine(Pair(document.AddOnChargeName, Money(document.AddOnCharge, money), columns));
        }

        if (document.Tax1Total != 0m)
        {
            slip.AppendLine(Pair(document.Tax1Name, Money(document.Tax1Total, money), columns));
        }

        if (document.Tax2Total != 0m)
        {
            slip.AppendLine(Pair(document.Tax2Name, Money(document.Tax2Total, money), columns));
        }

        // The rounding penny is printed, never absorbed silently (guide p.84).
        if (document.RoundingAdjustment != 0m)
        {
            slip.AppendLine(Pair("Rounding", Money(document.RoundingAdjustment, money), columns));
        }

        slip.AppendLine(new string('=', columns));
        slip.AppendLine(Pair("TOTAL", Money(document.GrandTotal, money), columns));
        slip.AppendLine();

        foreach (var tender in document.Tenders)
        {
            slip.AppendLine(Pair(tender.Name, Money(tender.AmountTendered, money), columns));

            if (!string.IsNullOrWhiteSpace(tender.Reference))
            {
                slip.AppendLine("  ref " + tender.Reference);
            }
        }

        if (document.ChangeGiven != 0m)
        {
            slip.AppendLine(Pair("Change", Money(document.ChangeGiven, money), columns));
        }

        if (document.LoyaltyPointsEarned > 0)
        {
            slip.AppendLine();
            slip.AppendLine(Pair(
                "Points earned",
                document.LoyaltyPointsEarned.ToString(CultureInfo.InvariantCulture),
                columns));
            slip.AppendLine(Pair(
                "Points balance",
                document.LoyaltyPointsBalance.ToString(CultureInfo.InvariantCulture),
                columns));
        }

        if (document.PrintSignatureLine)
        {
            slip.AppendLine();
            slip.AppendLine("I agree to pay the above total according to");
            slip.AppendLine("the card issuer agreement.");
            slip.AppendLine();
            slip.AppendLine(new string('_', columns));
            Centre(slip, "Signature", columns);
        }

        if (!string.IsNullOrWhiteSpace(document.FooterMessage))
        {
            slip.AppendLine();
            Centre(slip, document.FooterMessage!, columns);
        }

        return slip.ToString();
    }

    /// <summary>
    /// A 20-column roll cannot fit description, quantity, price and extension on one line, so the
    /// narrow format puts the description on its own line and the figures underneath. That is what
    /// the legacy 20-column slip did too, and it is the only arrangement that stays readable.
    /// </summary>
    private static void RenderLine(StringBuilder slip, ReceiptLine line, int columns, ReceiptFormat format)
    {
        var quantity = FormatQuantity(line.Quantity);

        if (format == ReceiptFormat.PackingSlip)
        {
            slip.AppendLine(Pair(Truncate(line.Description, columns - 10), quantity, columns));
            return;
        }

        if (columns < 32)
        {
            slip.AppendLine(Truncate(line.Description, columns));
            slip.AppendLine(Pair($"  {quantity} @ {line.UnitPrice:0.00}", line.ExtendedNet.ToString("0.00", CultureInfo.InvariantCulture), columns));
        }
        else
        {
            var left = $"{quantity,5} {Truncate(line.Description, columns - 22)}";
            var right = $"{line.UnitPrice,8:0.00}{line.ExtendedNet,9:0.00}";
            slip.AppendLine(Pair(left, right, columns));
        }

        if (!string.IsNullOrWhiteSpace(line.PriceOriginLabel))
        {
            slip.AppendLine(CultureInfo.InvariantCulture, $"      ({line.PriceOriginLabel})");
        }

        if (!string.IsNullOrWhiteSpace(line.Note))
        {
            slip.AppendLine("      " + Truncate(line.Note!, columns - 6));
        }
    }

    private static int ResolveColumns(ReceiptFormat format, PrinterProfileContract profile)
    {
        // The profile wins: it describes the paper actually loaded.
        if (profile.Columns > 0)
        {
            return profile.Columns;
        }

        return format switch
        {
            ReceiptFormat.Slip20 => 20,
            ReceiptFormat.Slip40 => 40,
            _ => 48,
        };
    }

    private static string Money(decimal amount, string symbol)
        => (amount < 0 ? "-" : string.Empty) + symbol + Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatQuantity(decimal quantity)
        => quantity == decimal.Truncate(quantity)
            ? quantity.ToString("0", CultureInfo.InvariantCulture)
            : quantity.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Pair(string left, string right, int columns)
    {
        var available = columns - right.Length;

        if (available < 1)
        {
            return right;
        }

        return Truncate(left, available - 1).PadRight(available) + right;
    }

    private static void Centre(StringBuilder slip, string text, int columns)
    {
        var trimmed = Truncate(text, columns);
        var padding = Math.Max(0, (columns - trimmed.Length) / 2);
        slip.AppendLine(new string(' ', padding) + trimmed);
    }

    private static string Truncate(string value, int length)
        => length <= 0 ? string.Empty : value.Length <= length ? value : value[..length];

    private static void Write(Stream stream, byte[] bytes)
    {
        if (bytes.Length > 0)
        {
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    private static Encoding CreatePrinterEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(437);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Code page 437 is unavailable on some trimmed runtimes. ASCII prints correctly for
            // everything a receipt contains; only accented characters degrade.
            return Encoding.ASCII;
        }
    }
}
