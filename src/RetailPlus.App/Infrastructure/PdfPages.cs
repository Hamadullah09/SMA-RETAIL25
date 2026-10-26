using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetailPlus.App.Infrastructure;

/// <summary>
/// Saves pages drawn by the app as a PDF: each page is rendered exactly as on screen and stored as a black-and-white
/// image (Flate compressed). At 384 dpi one screen unit is exactly 4 dots, so barcode bars keep their exact widths.
/// </summary>
public static class PdfPages
{
    public const int Dpi = 384;

    /// <summary>Writes <paramref name="pages"/> (each laid out at <paramref name="pageSize"/>, in screen units) to <paramref name="file"/>.</summary>
    public static void Save(string file, IEnumerable<FrameworkElement> pages, Size pageSize, string title)
    {
        using var stream = File.Create(file);
        Write(stream, pages, pageSize, title);
    }

    public static void Write(Stream output, IEnumerable<FrameworkElement> pages, Size pageSize, string title)
    {
        var pdf = new Writer(output);
        double widthPt = pageSize.Width * 72 / 96, heightPt = pageSize.Height * 72 / 96;
        var pageIds = new List<int>();
        const int catalogId = 1, pagesId = 2, infoId = 3;
        int next = 4;
        foreach (var page in pages)
        {
            var (bits, w, h) = Render(page, pageSize);
            int imageId = next++, contentId = next++, pageId = next++;
            pdf.Stream(imageId, $"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceGray /BitsPerComponent 1", Deflate(bits), deflated: true);
            pdf.Stream(contentId, "", Encoding.ASCII.GetBytes(FormattableString.Invariant($"q {widthPt:0.###} 0 0 {heightPt:0.###} 0 0 cm /Im0 Do Q")), deflated: false);
            pdf.Object(pageId, FormattableString.Invariant(
                $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {widthPt:0.###} {heightPt:0.###}] /Resources << /XObject << /Im0 {imageId} 0 R >> >> /Contents {contentId} 0 R >>"));
            pageIds.Add(pageId);
        }
        pdf.Object(pagesId, $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => id + " 0 R"))}] /Count {pageIds.Count} >>");
        pdf.Object(catalogId, $"<< /Type /Catalog /Pages {pagesId} 0 R >>");
        pdf.Object(infoId, $"<< /Title ({Escape(title)}) /Producer ({Escape(Brand.Name)}) /CreationDate (D:{DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}) >>");
        pdf.Finish(catalogId, infoId, next);
    }

    /// <summary>The page as 1-bit rows (1 = white, the PDF default for DeviceGray), first row at the top.</summary>
    private static (byte[] Bits, int Width, int Height) Render(FrameworkElement page, Size size)
    {
        page.Measure(size);
        page.Arrange(new Rect(size));
        page.UpdateLayout();
        int w = (int)Math.Ceiling(size.Width * Dpi / 96), h = (int)Math.Ceiling(size.Height * Dpi / 96);
        var bitmap = new RenderTargetBitmap(w, h, Dpi, Dpi, PixelFormats.Pbgra32);
        var paper = new DrawingVisual();
        using (var dc = paper.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(size));
        bitmap.Render(paper);
        bitmap.Render(page);
        var pixels = new byte[w * 4];
        int rowBytes = (w + 7) / 8;
        var bits = new byte[rowBytes * h];
        for (int y = 0; y < h; y++)
        {
            bitmap.CopyPixels(new Int32Rect(0, y, w, 1), pixels, w * 4, 0);
            for (int x = 0; x < w; x++)
            {
                int lum = (pixels[x * 4] * 29 + pixels[x * 4 + 1] * 150 + pixels[x * 4 + 2] * 77) >> 8;
                if (lum >= 128) bits[y * rowBytes + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }
        return (bits, w, h);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var buffer = new MemoryStream();
        using (var z = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
        return buffer.ToArray();
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);

    /// <summary>Numbered objects, then the cross-reference table that points at each one.</summary>
    private sealed class Writer
    {
        private readonly Stream _out;
        private readonly Dictionary<int, long> _offsets = [];

        public Writer(Stream output)
        {
            _out = output;
            Raw("%PDF-1.4\n%âãÏÓ\n");
        }

        private void Raw(string text)
        {
            var bytes = Encoding.Latin1.GetBytes(text);
            _out.Write(bytes);
        }

        public void Object(int id, string body)
        {
            _offsets[id] = _out.Position;
            Raw($"{id} 0 obj\n{body}\nendobj\n");
        }

        public void Stream(int id, string dictionary, byte[] data, bool deflated)
        {
            _offsets[id] = _out.Position;
            Raw($"{id} 0 obj\n<< {dictionary}{(deflated ? " /Filter /FlateDecode" : "")} /Length {data.Length} >>\nstream\n");
            _out.Write(data);
            Raw("\nendstream\nendobj\n");
        }

        public void Finish(int rootId, int infoId, int size)
        {
            long xref = _out.Position;
            var sb = new StringBuilder($"xref\n0 {size}\n0000000000 65535 f \n");
            for (int id = 1; id < size; id++)
                sb.Append(CultureInfo.InvariantCulture, $"{(_offsets.TryGetValue(id, out var at) ? at : 0):D10} 00000 {(_offsets.ContainsKey(id) ? 'n' : 'f')} \n");
            sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {size} /Root {rootId} 0 R /Info {infoId} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            Raw(sb.ToString());
        }
    }
}
