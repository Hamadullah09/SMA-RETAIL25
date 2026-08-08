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
}
