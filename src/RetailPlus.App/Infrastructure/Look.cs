using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RetailPlus.App.Infrastructure;

/// <summary>
/// Attached properties the theme's templates read: an icon glyph (Segoe MDL2 Assets) shown before a button's text, a
/// corner radius, and a placeholder text for text boxes.
/// </summary>
public static class Look
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Look), new FrameworkPropertyMetadata(null));

    public static string? GetIcon(DependencyObject o) => (string?)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string? value) => o.SetValue(IconProperty, value);

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Look), new FrameworkPropertyMetadata(new CornerRadius(24)));

    public static CornerRadius GetCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject o, CornerRadius value) => o.SetValue(CornerRadiusProperty, value);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Look), new FrameworkPropertyMetadata(null));

    public static string? GetPlaceholder(DependencyObject o) => (string?)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string? value) => o.SetValue(PlaceholderProperty, value);

    /// <summary>Called once at start-up, before any window opens: larger text and the warm background everywhere.</summary>
    public static void ApplyDefaults()
    {
        var font = new FontFamily("Segoe UI");
        Control.FontSizeProperty.OverrideMetadata(typeof(Window), new FrameworkPropertyMetadata(17.0, FrameworkPropertyMetadataOptions.Inherits));
        Control.FontFamilyProperty.OverrideMetadata(typeof(Window), new FrameworkPropertyMetadata(font, FrameworkPropertyMetadataOptions.Inherits));
        Control.BackgroundProperty.OverrideMetadata(typeof(Window), new FrameworkPropertyMetadata(Application.Current.FindResource("AppBackground")));
        Control.ForegroundProperty.OverrideMetadata(typeof(Window), new FrameworkPropertyMetadata(Application.Current.FindResource("Ink"), FrameworkPropertyMetadataOptions.Inherits));
    }

    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
