namespace Retail25.Application.Documents;

/// <summary>
/// The label stock a sheet is printed on (guide App. L). Sizes are the vendors' published ones —
/// a label template that is a millimetre out prints fine on screen and crooked on paper.
/// </summary>
public enum LabelStock
{
    /// <summary>Avery 5160 — 30 per sheet, 3 × 10, 2.625" × 1". The default address label.</summary>
    Avery5160 = 0,

    /// <summary>Avery 8160 — the inkjet twin of 5160, same grid.</summary>
    Avery8160 = 1,

    /// <summary>Avery 8163 — 10 per sheet, 2 × 5, 4" × 2".</summary>
    Avery8163 = 2,

    /// <summary>S-644N — 6 per sheet, 2 × 3, 4" × 3". Shipping stock.</summary>
    S644N = 3,
}

/// <summary>
/// What each stock is called on the box, for the picker. Lives here rather than with the layout
/// geometry so the API can offer the choice without reaching into the rendering implementation.
/// </summary>
public static class LabelStockNames
{
    public static string Describe(LabelStock stock) => stock switch
    {
        LabelStock.Avery5160 => "Avery 5160 — 30 per sheet (2⅝\" × 1\")",
        LabelStock.Avery8160 => "Avery 8160 — 30 per sheet, inkjet (2⅝\" × 1\")",
        LabelStock.Avery8163 => "Avery 8163 — 10 per sheet (4\" × 2\")",
        LabelStock.S644N => "S-644N — 6 per sheet (4\" × 3\")",
        _ => stock.ToString(),
    };
}

/// <summary>One label's worth of an item.</summary>
/// <param name="EpcToEncode">
/// The tag to program, when the label stock carries an RFID inlay and the printer can encode it.
/// Carried on the print job rather than acted on: there is no encoder abstraction in this system —
/// the RFID hardware here reads tags at a till, it does not write them — so this is the value a
/// capable printer's firmware would consume, and nothing more. Said plainly so it is not mistaken
/// for working RFID encoding.
/// </param>
public sealed record PriceTag(
    string StockCode,
    string Name,
    decimal Price,
    string? Barcode = null,
    string? BinLocation = null,
    string? EpcToEncode = null);

/// <summary>A sheet of labels: what to print, how many of each, and on what stock.</summary>
public sealed record LabelSheetRequest(
    LabelStock Stock,
    IReadOnlyList<LabelLine> Lines,
    bool ShowBarcode = true,
    /// <summary>Labels already used on a part-used sheet, so printing resumes rather than wasting them.</summary>
    int SkipLabels = 0);

public sealed record LabelLine(PriceTag Tag, int Copies = 1);

public interface ILabelRenderer
{
    /// <summary>A sheet of price tags on the chosen stock.</summary>
    byte[] RenderPriceTags(LabelSheetRequest request);

    /// <summary>The same, but barcode-first: a shelf-edge or bin label where the code is the point.</summary>
    byte[] RenderBarcodeLabels(LabelSheetRequest request);
}
