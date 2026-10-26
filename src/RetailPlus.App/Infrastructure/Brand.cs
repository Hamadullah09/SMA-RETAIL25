using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetailPlus.App.Infrastructure;

/// <summary>The product name and logo shown to users.</summary>
public static class Brand
{
    public const string Name = "SMA RETAIL";
    public const string Company = "SMA Technology";

    /// <summary>The SMA figure with the magnifier (transparent background), from the company logo.</summary>
    public static ImageSource Mark { get; } = new BitmapImage(new Uri("pack://application:,,,/Assets/sma-mark.png"));
}
