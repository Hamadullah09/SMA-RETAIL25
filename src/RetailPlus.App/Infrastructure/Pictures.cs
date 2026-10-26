using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetailPlus.App.Infrastructure;

/// <summary>
/// Product pictures: whatever the cashier picks (a photo, a screenshot, a picture pasted from a web page) is made a JPEG
/// of at most 800 pixels a side on white, so it is small enough to keep in the database and quick to show.
/// </summary>
public static class Pictures
{
    public const int MaxSide = 800;

    public static byte[] FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Encode(BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]);
    }

    public static byte[] Encode(BitmapSource source)
    {
        double scale = Math.Min(1, (double)MaxSide / Math.Max(source.PixelWidth, source.PixelHeight));
        int w = Math.Max(1, (int)Math.Round(source.PixelWidth * scale)), h = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        // Drawn on white: a JPEG has no transparency, and transparent PNGs would otherwise turn black.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            dc.DrawImage(source, new Rect(0, 0, w, h));
        }
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>A stored picture for showing, or null.</summary>
    public static ImageSource? ToImage(byte[]? data)
    {
        if (data is not { Length: > 0 }) return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(data);
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>A picture file named in the item's photo path (from Retail Plus), if that file can be read here.</summary>
    public static ImageSource? FromPhotoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            return ToImage(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or FileFormatException)
        {
            return null;
        }
    }
}
