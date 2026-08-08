using ZXing;
using ZXing.Common;
using ZXing.OneD;

namespace Retail25.Infrastructure.Documents;

/// <summary>One barcode, reduced to the runs of black and white a renderer can draw.</summary>
/// <param name="Modules">True where a bar is black. One entry per narrowest-possible element.</param>
public sealed record BarcodePattern(IReadOnlyList<bool> Modules, string Text)
{
    public int Width => Modules.Count;
}

/// <summary>
/// Turns a stock code into Code 39, the symbology the legacy system printed (guide App. L).
/// <para>
/// Deliberately produces a bar pattern rather than an image. Drawing the bars straight into the PDF
/// keeps them vector-sharp at any size, which is what a scanner needs — a rasterised barcode scaled
/// to fit a label is the usual reason a printed one will not read. It also avoids pulling in an
/// image-encoding backend, which ZXing.Net leaves to a platform-specific package.
/// </para>
/// </summary>
public static class Code39Renderer
{
    /// <summary>
    /// The 43 characters basic Code 39 carries. Everything else is refused.
    /// <para>
    /// ZXing will silently fall back to <em>extended</em> Code 39 for anything outside this set,
    /// encoding one character as two. That only reads on a scanner configured for Full ASCII — on a
    /// counter scanner in its default mode it comes back as gibberish, and the operator gets a failed
    /// lookup with a barcode sitting right there that looks fine. Refusing is the better answer.
    /// </para>
    /// </summary>
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    /// <summary>
    /// Code 39 encodes upper-case letters, digits and a handful of symbols, and nothing else. A
    /// lower-case stock code is upper-cased rather than refused; anything genuinely unencodable is
    /// reported so the caller can print the label without a barcode instead of printing a bad one.
    /// </summary>
    public static bool TryEncode(string? value, out BarcodePattern pattern)
    {
        pattern = new BarcodePattern([], string.Empty);

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim().ToUpperInvariant();

        foreach (var character in text)
        {
            if (!Alphabet.Contains(character, StringComparison.Ordinal))
            {
                return false;
            }
        }

        try
        {
            var writer = new Code39Writer();

            // Width 0 lets ZXing choose the natural module count — we scale when drawing, so asking
            // for a pixel width here would only quantise the bars and cost the scanner accuracy.
            var matrix = writer.encode(text, BarcodeFormat.CODE_39, 0, 1, new Dictionary<EncodeHintType, object>());

            var modules = new bool[matrix.Width];

            for (var x = 0; x < matrix.Width; x++)
            {
                modules[x] = matrix[x, 0];
            }

            pattern = new BarcodePattern(modules, text);
            return true;
        }
        catch (ArgumentException)
        {
            // Code 39 refused the content — a character outside its alphabet.
            return false;
        }
    }
}
