using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetailPlus.App.Infrastructure;

/// <summary>
/// The SMA logo as a black-and-white raster for receipt printers. It is dithered (Floyd-Steinberg) rather than cut at a
/// threshold, so the orange figure keeps its shading on thermal paper.
/// </summary>
public static class ReceiptLogo
{
    private static (byte[] Bits, int WidthBytes, int Height)? _raster;

    /// <param name="widthDots">Width in printer dots (203 dpi: 192 dots is 24 mm).</param>
    public static (byte[] Bits, int WidthBytes, int Height) Raster(int widthDots = 192)
    {
        if (_raster is { } cached) return cached;
        var source = (BitmapSource)Brand.Mark;
        double scale = (double)widthDots / source.PixelWidth;
        var bitmap = new FormatConvertedBitmap(new TransformedBitmap(source, new ScaleTransform(scale, scale)), PixelFormats.Bgra32, null, 0);
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight, stride = w * 4;
        var pixels = new byte[stride * h];
        bitmap.CopyPixels(pixels, stride, 0);

        // Lightness of each dot on white paper (0 black, 1 white).
        var light = new float[w * h];
        for (int i = 0; i < w * h; i++)
        {
            float alpha = pixels[i * 4 + 3] / 255f;
            float lum = (0.114f * pixels[i * 4] + 0.587f * pixels[i * 4 + 1] + 0.299f * pixels[i * 4 + 2]) / 255f;
            light[i] = 1 - alpha * (1 - lum);
        }

        int widthBytes = (w + 7) / 8;
        var bits = new byte[widthBytes * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                bool black = light[i] < 0.5f;
                if (black) bits[y * widthBytes + x / 8] |= (byte)(0x80 >> (x % 8));
                float error = light[i] - (black ? 0 : 1);
                if (x + 1 < w) light[i + 1] += error * 7 / 16;
                if (y + 1 < h)
                {
                    if (x > 0) light[i + w - 1] += error * 3 / 16;
                    light[i + w] += error * 5 / 16;
                    if (x + 1 < w) light[i + w + 1] += error * 1 / 16;
                }
            }
        }
        _raster = (bits, widthBytes, h);
        return _raster.Value;
    }
}
