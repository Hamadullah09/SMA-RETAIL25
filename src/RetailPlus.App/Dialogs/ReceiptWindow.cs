using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Reports;
using ZXing;
using ZXing.OneD;

namespace RetailPlus.App.Dialogs;

/// <summary>A sale receipt on screen, drawn like the printed slip, with Print, Receipt printer and Save picture.</summary>
public sealed class ReceiptWindow : Window
{
    private readonly Receipt _receipt;

    public ReceiptWindow(Receipt receipt, string title)
    {
        _receipt = receipt;
        Title = title;
        Width = 720;
        Height = Math.Min(960, SystemParameters.WorkArea.Height - 40);
        Owner = Ui.Owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var paper = ReceiptPaper.Create(receipt);
        paper.Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 3, Direction = 270, Opacity = 0.16 };
        var zoom = new Border { Child = paper, LayoutTransform = new ScaleTransform(1.3, 1.3), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 24) };
        var scroll = new ScrollViewer { Content = zoom, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 6, 16, 14) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        Button Add(string text, string? style, RoutedEventHandler click)
        {
            var b = new Button { Content = text, MinHeight = 54, MinWidth = 130, Margin = new Thickness(0, 0, 10, 0) };
            if (style is not null) b.Style = (Style)Application.Current.FindResource(style);
            b.Click += click;
            buttons.Children.Add(b);
            return b;
        }
        Add("Print…", "AccentButton", (_, _) => PrintOnWindowsPrinter());
        var thermal = Add("Receipt printer", null, (_, _) => PrintOnReceiptPrinter());
        Look.SetIcon(thermal, Views.Layout.IconFor("Print"));
        Add("Save picture…", null, (_, _) => SavePicture());
        Add("Close", null, (_, _) => Close()).IsCancel = true;

        var root = new DockPanel();
        root.Children.Add(buttons);
        root.Children.Add(scroll);
        Content = root;
        Views.Layout.AddIcons(this);
    }

    /// <summary>The receipt of a saved sale with this store's receipt header and footer.</summary>
    public static async Task<Receipt> ForSaleAsync(Sale sale, string? banner = null)
    {
        var header = await AppServices.Setup.GetTextAsync(DocumentTextKind.InvoiceHeader);
        var footer = await AppServices.Setup.GetTextAsync(DocumentTextKind.InvoiceFooter);
        return Receipt.FromSale(sale, AppServices.Settings, header, footer, banner, Brand.Name);
    }

    /// <summary>The receipt laid out on its own (not the zoomed one on screen), at the width of an 80 mm roll.</summary>
    private FrameworkElement Standalone()
    {
        var paper = ReceiptPaper.Create(_receipt);
        paper.Measure(new Size(ReceiptPaper.PaperWidth, double.PositiveInfinity));
        paper.Arrange(new Rect(paper.DesiredSize));
        paper.UpdateLayout();
        return paper;
    }

    private void PrintOnWindowsPrinter()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        try
        {
            dialog.PrintVisual(Standalone(), Title);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Printing.PrintSystemException)
        {
            Ui.Error("The receipt could not be printed: " + ex.Message);
        }
    }

    private void PrintOnReceiptPrinter()
    {
        try
        {
            int width = AppServices.Config.ReceiptWidth;
            AppServices.Printer.Print(EscPosReceipt.Build(_receipt, width, ReceiptLogo.Raster()), ReceiptFormatter.Text(_receipt, width), false);
            Ui.Info(string.IsNullOrWhiteSpace(AppServices.Config.ReceiptPrinter)
                ? "No receipt printer is set up at this station, so the receipt was saved as a text file. Set the printer in Setup > This station."
                : "Sent to the receipt printer.");
        }
        catch (Exception ex)
        {
            Ui.Error("The receipt could not be printed: " + ex.Message);
        }
    }

    private void SavePicture()
    {
        var file = Ui.SaveFile(Title + ".png", "PNG picture|*.png");
        if (file is not null) SavePictureTo(file);
    }

    /// <summary>Saves the whole receipt as a PNG at three times screen resolution.</summary>
    public void SavePictureTo(string file)
    {
        var paper = Standalone();
        const double scale = 3;   // sharp enough to print or send
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(paper.ActualWidth * scale), (int)Math.Ceiling(paper.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(paper);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(file);
        encoder.Save(stream);
    }
}

/// <summary>
/// A receipt drawn like the printed slip: logo and store, the sale's details, items with code and quantity, a yellow
/// TOTAL band, the change in green, the footer and the invoice number as a barcode, on paper with a torn edge.
/// </summary>
public static class ReceiptPaper
{
    /// <summary>79 mm: the print width of an 80 mm receipt roll.</summary>
    public const double PaperWidth = 300;
    private const double Inner = PaperWidth - 36;

    public static FrameworkElement Create(Receipt r)
    {
        var c = CultureInfo.CurrentCulture;
        string M(decimal v) => v.ToString("N2", c);
        var ink = Look.Brush("Ink");
        var muted = Look.Brush("Muted");
        var display = new FontFamily("Century Gothic, Segoe UI");
        var body = new StackPanel { Margin = new Thickness(18, 20, 18, 8) };

        body.Children.Add(new Image { Source = Brand.Mark, Height = 62, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
        body.Children.Add(Text(r.StoreName, 22, FontWeights.Bold, ink, display, TextAlignment.Center));
        foreach (var line in r.StoreLines) body.Children.Add(Text(line, 11.5, FontWeights.Normal, muted, null, TextAlignment.Center));
        body.Children.Add(new Border
        {
            Background = Look.Brush("Charcoal"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 4, 16, 5),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0),
            Child = Text(r.Title, 11.5, FontWeights.Bold, Look.Brush("Sun")),
        });
        if (r.Banner is not null)
        {
            body.Children.Add(new Border
            {
                BorderBrush = Look.Brush("Danger"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 2, 14, 3),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0),
                Child = Text(r.Banner, 11.5, FontWeights.Bold, Look.Brush("Danger")),
            });
        }

        var info = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        info.Children.Add(Row("Invoice", "#" + r.InvoiceNumber.ToString(c), 12, muted, ink, FontWeights.SemiBold));
        info.Children.Add(Row("Date", r.SoldAt.ToString("dd MMM yyyy, h:mm tt", c), 12, muted, ink));
        info.Children.Add(Row("Cashier", $"{r.Cashier ?? "-"}  ·  Station {r.Station:000}", 12, muted, ink));
        if (r.Customer is not null) info.Children.Add(Row("Customer", r.Customer, 12, muted, ink, FontWeights.SemiBold));
        body.Children.Add(info);
        body.Children.Add(Dashes());

        foreach (var i in r.Items)
        {
            if (i.IsText)
            {
                body.Children.Add(Text(i.Name, 11.5, FontWeights.Normal, muted, null, TextAlignment.Left, new Thickness(0, 0, 0, 6)));
                continue;
            }
            var item = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            item.Children.Add(Row(i.Name, M(i.Gross), 13, ink, ink, FontWeights.SemiBold, FontWeights.SemiBold));
            var detail = (string.IsNullOrWhiteSpace(i.Code) ? "" : i.Code + "  ·  ") + $"{i.Quantity.ToString("0.###", c)} × {M(i.UnitPrice)}";
            item.Children.Add(Text(detail, 11, FontWeights.Normal, muted));
            if (i.Discount != 0) item.Children.Add(Row("Discount", M(-i.Discount), 11.5, Look.Brush("Danger"), Look.Brush("Danger")));
            if (i.Note is not null) item.Children.Add(Text(i.Note, 11, FontWeights.Normal, muted));
            body.Children.Add(item);
        }
        body.Children.Add(Dashes(top: 2));

        foreach (var t in r.Totals) body.Children.Add(Row(t.Label, M(t.Amount), 12.5, muted, ink));
        var total = new Grid();
        total.ColumnDefinitions.Add(new ColumnDefinition());
        total.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var totalLabel = Text("TOTAL", 15, FontWeights.Bold, ink);
        totalLabel.VerticalAlignment = VerticalAlignment.Center;
        var totalAmount = Text(M(r.Total), 25, FontWeights.Bold, ink, display);
        Grid.SetColumn(totalAmount, 1);
        total.Children.Add(totalLabel);
        total.Children.Add(totalAmount);
        body.Children.Add(new Border { Background = Look.Brush("Sun"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 8, 0, 8), Child = total });

        foreach (var p in r.Payments)
        {
            body.Children.Add(Row("Paid by " + p.Label, M(p.Amount), 13, muted, ink, FontWeights.Normal, FontWeights.SemiBold));
            if (p.Note is not null) body.Children.Add(Text(p.Note, 11, FontWeights.Normal, muted));
        }
        if (r.Change > 0)
        {
            body.Children.Add(new Border
            {
                Background = Look.Brush("OkLight"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 6, 0, 0),
                Child = Row("Change", M(r.Change), 16, Look.Brush("Ok"), Look.Brush("Ok"), FontWeights.Bold, FontWeights.Bold),
            });
        }
        body.Children.Add(Text(ReceiptFormatter.Summary(r, c), 11.5, FontWeights.Normal, muted, null, TextAlignment.Center, new Thickness(0, 12, 0, 0)));
        foreach (var n in r.Notes) body.Children.Add(Text(n, 11.5, FontWeights.Normal, muted, null, TextAlignment.Center));
        body.Children.Add(Dashes());

        foreach (var f in r.Footer) body.Children.Add(Text(f, 15, FontWeights.SemiBold, ink, display, TextAlignment.Center));
        if (r.InvoiceNumber > 0)
        {
            var code = r.InvoiceNumber.ToString(CultureInfo.InvariantCulture);
            body.Children.Add(Barcode(code, ink));
            body.Children.Add(Text(code, 11, FontWeights.Normal, muted, new FontFamily("Consolas"), TextAlignment.Center));
        }
        body.Children.Add(Text("Powered by " + Brand.Name, 9.5, FontWeights.Normal, Look.Brush("Stone"), null, TextAlignment.Center, new Thickness(0, 10, 0, 0)));

        var sheet = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(6, 6, 0, 0), Child = body };
        var paper = new StackPanel { Width = PaperWidth, SnapsToDevicePixels = true };
        paper.Children.Add(sheet);
        paper.Children.Add(TornEdge());
        return paper;
    }

    private static TextBlock Text(string text, double size, FontWeight weight, Brush brush, FontFamily? family = null,
        TextAlignment align = TextAlignment.Left, Thickness margin = default)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight, Foreground = brush, TextAlignment = align, TextWrapping = TextWrapping.Wrap, Margin = margin };
        if (family is not null) t.FontFamily = family;
        return t;
    }

    /// <summary>A label on the left and a value on the right.</summary>
    private static Grid Row(string left, string right, double size, Brush leftBrush, Brush rightBrush,
        FontWeight? leftWeight = null, FontWeight? rightWeight = null)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = Text(left, size, leftWeight ?? FontWeights.Normal, leftBrush);
        var r = Text(right, size, rightWeight ?? FontWeights.Normal, rightBrush, null, TextAlignment.Right);
        r.Margin = new Thickness(10, 0, 0, 0);
        r.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(r, 1);
        g.Children.Add(l);
        g.Children.Add(r);
        return g;
    }

    private static Line Dashes(double top = 10) => new()
    {
        X1 = 0, X2 = Inner, Stroke = Look.Brush("Stone"), StrokeThickness = 1, StrokeDashArray = [4, 3], Margin = new Thickness(0, top, 0, 10),
    };

    /// <summary>The invoice number as a Code 128 barcode (the same kind the receipt printer prints).</summary>
    private static Path Barcode(string text, Brush ink)
    {
        var matrix = new Code128Writer().encode(text, BarcodeFormat.CODE_128, 0, 1);
        const double module = 1.5, height = 40;
        var bars = new GeometryGroup();
        for (int x = 0; x < matrix.Width;)
        {
            if (!matrix[x, 0]) { x++; continue; }
            int start = x;
            while (x < matrix.Width && matrix[x, 0]) x++;
            bars.Children.Add(new RectangleGeometry(new Rect(start * module, 0, (x - start) * module, height)));
        }
        return new Path
        {
            Data = bars, Fill = ink, Width = matrix.Width * module, Height = height, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 2), SnapsToDevicePixels = true,
        };
    }

    /// <summary>The zigzag bottom of a torn-off receipt.</summary>
    private static Path TornEdge()
    {
        const double tooth = 10, depth = 7;
        var figure = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true, IsFilled = true };
        for (double x = 0; x < PaperWidth; x += tooth)
        {
            figure.Segments.Add(new LineSegment(new Point(Math.Min(x + tooth / 2, PaperWidth), depth), true));
            figure.Segments.Add(new LineSegment(new Point(Math.Min(x + tooth, PaperWidth), 0), true));
        }
        return new Path { Data = new PathGeometry([figure]), Fill = Brushes.White, Width = PaperWidth, Height = depth };
    }
}
