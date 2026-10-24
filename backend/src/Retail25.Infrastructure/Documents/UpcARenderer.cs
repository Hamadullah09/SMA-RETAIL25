using Retail25.Application.Documents;
using Retail25.Domain.Identification;
using ZXing;
using ZXing.OneD;

namespace Retail25.Infrastructure.Documents;

/// <summary>
/// Turns a UPC-A into bars, in the same vector-friendly form <see cref="Code39Renderer"/> produces.
/// </summary>
public static class UpcARenderer
{
    /// <summary>
    /// Only a complete, correct UPC-A is drawn. ZXing will happily take eleven digits and invent the
    /// twelfth, but a label whose bars carry a different number from the product record is a label
    /// that rings up nothing — so the stored value must already be valid.
    /// </summary>
    public static bool TryEncode(string? value, out BarcodePattern pattern)
    {
        pattern = new BarcodePattern([], string.Empty);

        var text = value?.Trim();

        if (!UpcA.IsValid(text))
        {
            return false;
        }

        try
        {
            // Width 0: ZXing's natural module count, quiet zones included. Scaling happens when the
            // bars are drawn, so asking for pixels here would only quantise them.
            var matrix = new UPCAWriter().encode(text, BarcodeFormat.UPC_A, 0, 1, new Dictionary<EncodeHintType, object>());

            var modules = new bool[matrix.Width];

            for (var x = 0; x < matrix.Width; x++)
            {
                modules[x] = matrix[x, 0];
            }

            pattern = new BarcodePattern(modules, text!);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>Which bars a given label gets, under the symbology chosen for the run.</summary>
public static class LabelBarcodes
{
    public static BarcodePattern? For(PriceTag tag, LabelSymbology symbology)
    {
        ArgumentNullException.ThrowIfNull(tag);

        switch (symbology)
        {
            case LabelSymbology.UpcA:
                return UpcARenderer.TryEncode(tag.Barcode, out var upcOnly) ? upcOnly : null;

            case LabelSymbology.Code39:
                return Code39Renderer.TryEncode(tag.Barcode ?? tag.StockCode, out var legacy) ? legacy : null;

            default:
                if (UpcARenderer.TryEncode(tag.Barcode, out var upc))
                {
                    return upc;
                }

                return Code39Renderer.TryEncode(tag.Barcode ?? tag.StockCode, out var fallback) ? fallback : null;
        }
    }
}
