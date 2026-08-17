namespace Retail25.Application.Documents;

/// <summary>Who a statement is going to, laid out for a #10 window envelope.</summary>
public sealed record EnvelopeRequest(
    string RecipientName,
    string? Company,
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? Postcode,
    string StoreName,
    string? StoreLine1,
    string? StoreCity,
    string? StorePostcode);

public sealed record CatalogueItem(
    string StockCode,
    string Name,
    string? Description,
    decimal Price,
    string DepartmentName,
    string? Barcode);

/// <summary>A price list or catalogue, grouped the way the shelf is.</summary>
public sealed record CatalogueRequest(
    string StoreName,
    DateOnly PrintedOn,
    IReadOnlyList<CatalogueItem> Items,
    bool ShowBarcodes = false);

public interface IDocumentRenderer
{
    /// <summary>
    /// A #10 envelope (4.125" × 9.5") with the address placed for a standard window.
    /// </summary>
    byte[] RenderCom10Envelope(EnvelopeRequest request);

    /// <summary>A multi-page price list, grouped by department.</summary>
    byte[] RenderCatalogue(CatalogueRequest request);

    /// <summary>
    /// A receipt, for a printer the browser can reach.
    /// <para>
    /// The till's own receipt goes to a thermal printer as ESC/POS through the terminal agent, and
    /// that remains the fast path. This is for every case where that printer is not the answer: a
    /// till whose printer is offline, an office reprinting a copy for a customer, a shop with an A4
    /// laser and no thermal unit at all. Until this existed there was no way to print a receipt
    /// from a browser, which made a broken printer an outage rather than an inconvenience.
    /// </para>
    /// <para>
    /// It takes the same <see cref="ReceiptDocument"/> the agent renders, so the two cannot drift.
    /// That type was written for exactly this — it holds no escape codes precisely so that more than
    /// one renderer can consume it.
    /// </para>
    /// </summary>
    byte[] RenderReceipt(Retail25.Contracts.Terminals.ReceiptDocument document);
}
