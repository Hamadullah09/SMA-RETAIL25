namespace Retail25.Contracts.Terminals;

/// <summary>Which legacy output format a document is rendered for (guide p.78–80).</summary>
public enum ReceiptFormat
{
    /// <summary>Full-page invoice with addresses and terms.</summary>
    Invoice = 0,

    /// <summary>40-column slip.</summary>
    Slip40 = 1,

    /// <summary>20-column slip for narrow roll printers.</summary>
    Slip20 = 2,

    /// <summary>Quantities and descriptions, no money (guide p.12).</summary>
    PackingSlip = 3,
}

public sealed record ReceiptLine(
    string StockCode,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal ExtendedNet,
    string? PriceOriginLabel,
    string? Note,
    bool Tax1Applies,
    bool Tax2Applies,
    bool IsCredit);

public sealed record ReceiptAdjustment(string Label, decimal Amount);

public sealed record ReceiptTender(string Name, decimal Amount, decimal AmountTendered, decimal ChangeGiven, string? Reference);

/// <summary>
/// A rendered sales document, independent of any printer.
/// <para>
/// The agent turns this into ESC/POS bytes for whatever hardware the till has; the browser turns the
/// same object into an on-screen preview. Keeping escape codes out of the document is what lets a
/// store change printer make without a release, and it is why this type lives in Contracts rather
/// than in either process.
/// </para>
/// </summary>
public sealed record ReceiptDocument(
    Guid TransactionId,
    long TransactionNumber,
    ReceiptFormat Format,
    string BusinessName,
    IReadOnlyList<string> BusinessAddress,
    string? TaxRegistrationNumber,
    string StationCode,
    string StaffName,
    string? CustomerName,
    IReadOnlyList<string>? CustomerAddress,
    DateTimeOffset CompletedAt,
    IReadOnlyList<ReceiptLine> Lines,
    IReadOnlyList<ReceiptAdjustment> Adjustments,
    decimal Subtotal,
    decimal DiscountTotal,
    string Tax1Name,
    decimal Tax1Total,
    string Tax2Name,
    decimal Tax2Total,
    string AddOnChargeName,
    decimal AddOnCharge,
    decimal RoundingAdjustment,
    decimal GrandTotal,
    IReadOnlyList<ReceiptTender> Tenders,
    decimal ChangeGiven,
    int LoyaltyPointsEarned,
    int LoyaltyPointsBalance,
    string CurrencySymbol,
    string? FooterMessage,
    bool IsReprint,
    bool IsVoided,
    bool PrintSignatureLine);
