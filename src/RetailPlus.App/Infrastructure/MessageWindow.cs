using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RetailPlus.App.Infrastructure;

public enum MessageKind
{
    Info,
    Warning,
    Question,
}

/// <summary>A large, plainly worded message box in the program's colours (instead of the small Windows one).</summary>
public sealed class MessageWindow : Window
{
    public MessageWindow(string title, string message, MessageKind kind)
    {
        Title = title;
        Owner = Ui.Owner;
        WindowStartupLocation = Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        MinWidth = 480;
        MaxWidth = 760;

        var (glyph, circle, iconColor) = kind switch
        {
            MessageKind.Warning => ("", Look.Brush("DangerLight"), Look.Brush("Danger")),
            MessageKind.Question => ("", Look.Brush("SunLight"), Look.Brush("Ink")),
            _ => ("", Look.Brush("SunLight"), Look.Brush("Ink")),
        };

        var root = new DockPanel { Margin = new Thickness(28, 26, 28, 22) };
        var icon = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = circle, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 28, Foreground = iconColor, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(icon, Dock.Left);
        root.Children.Add(icon);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        if (kind == MessageKind.Question)
        {
            buttons.Children.Add(MakeButton("Yes", "", "AccentButton", true, isDefault: true));
            buttons.Children.Add(MakeButton("No", "", null, false, isCancel: true));
        }
        else
        {
            buttons.Children.Add(MakeButton("OK", "", "AccentButton", true, isDefault: true, isCancel: true));
        }

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 24, Margin = new Thickness(0, 0, 0, 8) });
        text.Children.Add(new TextBlock { Text = message, FontSize = 18, TextWrapping = TextWrapping.Wrap, MaxWidth = 600, LineHeight = 26 });
        var content = new DockPanel();
        content.Children.Add(buttons);
        content.Children.Add(text);
        root.Children.Add(content);
        Content = root;
    }

    private Button MakeButton(string text, string glyph, string? style, bool result, bool isDefault = false, bool isCancel = false)
    {
        var b = new Button { Content = text, MinWidth = 130, MinHeight = 54, Margin = new Thickness(10, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
        if (style is not null) b.Style = (Style)Application.Current.FindResource(style);
        b.Margin = new Thickness(10, 0, 0, 0);
        Look.SetIcon(b, glyph);
        b.Click += (_, _) => DialogResult = result;
        if (isDefault) Loaded += (_, _) => b.Focus();
        return b;
    }
}
