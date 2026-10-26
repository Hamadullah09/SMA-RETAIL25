using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using RetailPlus.App.Infrastructure;
using RetailPlus.Data.Services;
using ZXing;
using ZXing.OneD;

namespace RetailPlus.App.Dialogs;

/// <summary>An item on the label list and how many labels it gets.</summary>
public sealed class LabelLine : INotifyPropertyChanged
{
    private int _copies;

    public required int ItemId { get; init; }
    public required string Plu { get; init; }
    public required string Name { get; init; }
    public string? Barcode { get; set; }
    public decimal Price { get; init; }
    public decimal OnHand { get; init; }

    public int Copies
    {
        get => _copies;
        set { _copies = Math.Clamp(value, 0, 999); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Copies))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>A label size: one label per page on a label printer, or a sheet of labels on A4.</summary>
public sealed record LabelLayout(string Name, double WidthMm, double HeightMm, int Columns = 1, int Rows = 1, double PageWidthMm = 0, double PageHeightMm = 0, double TopMm = 0)
{
    public const double Mm = 96 / 25.4;
    public bool IsSheet => Columns * Rows > 1;
    public double PageWidth => (IsSheet ? PageWidthMm : WidthMm) * Mm;
    public double PageHeight => (IsSheet ? PageHeightMm : HeightMm) * Mm;
    public override string ToString() => Name;

    public static readonly IReadOnlyList<LabelLayout> All =
    [
        new("A4 sheet: 30 labels (3 × 10, 70 × 29.7 mm)", 70, 29.7, 3, 10, 210, 297),
        new("A4 sheet: 24 labels (3 × 8, 70 × 37 mm)", 70, 37, 3, 8, 210, 297, 0.5),
        new("Label printer: 50 × 30 mm", 50, 30),
        new("Label printer: 38 × 25 mm", 38, 25),
    ];

    /// <summary>The label of a label printer (one label per page), for samples.</summary>
    public static LabelLayout OneLabel => All.First(l => !l.IsSheet);
}

/// <summary>
/// Print labels: barcode labels for all the inventory's items, the flagged ones or those chosen in the list, as many of
/// each as asked, on A4 sheets of 30 or 24 or on a label printer; printed, or saved as a PDF. Items without a barcode
/// get an in-store UPC-A first.
/// </summary>
public sealed class LabelsWindow : Window
{
    private ObservableCollection<LabelLine> _lines = [];
    private readonly ComboBox _scope = new() { MinWidth = 380 };
    private readonly ComboBox _layout = new() { ItemsSource = LabelLayout.All, SelectedIndex = 0, MinWidth = 380 };
    private readonly CheckBox _cutLines = new() { Content = "Cut lines between the labels", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _showPrice = new() { Content = "Show the price", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
    private readonly CheckBox _showStore = new() { Content = "Show the store name", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBox _currency = new() { Text = "Rs", Width = 80, MaxLength = 4 };
    private readonly Border _sample = new() { Background = Brushes.White, BorderBrush = Look.Brush("Line"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _summary = new() { FontSize = 17, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly DataGrid _grid;

    /// <param name="all">Every item of the inventory.</param>
    /// <param name="chosen">The items chosen in the list, offered as the other choice.</param>
    public LabelsWindow(IReadOnlyList<ItemRow> all, IReadOnlyList<ItemRow>? chosen = null)
    {
        Title = "Print labels";
        Width = 1180;
        Height = Math.Min(900, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var flagged = all.Where(r => r.IsFlagged).ToList();
        var scopes = new List<(string Text, IReadOnlyList<ItemRow> Rows)>
        {
            ($"All items in the inventory ({all.Count})", all),
            (flagged.Count == 0 ? "Flagged items (none flagged yet: Ctrl+G in Inventory)" : $"Flagged items ({flagged.Count})", flagged),
        };
        if (chosen is { Count: > 0 } && chosen.Count < all.Count) scopes.Add(($"Only the {chosen.Count} chosen in the list", chosen));
        _scope.ItemsSource = scopes.Select(s => s.Text).ToList();
        _scope.SelectionChanged += (_, _) => Load(scopes[Math.Max(0, _scope.SelectedIndex)].Rows);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = true, SelectionMode = DataGridSelectionMode.Extended,
            Style = (Style)Application.Current.FindResource("Grid"), IsReadOnly = false,
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "Stock code", Binding = new Binding(nameof(LabelLine.Plu)), IsReadOnly = true, Width = 120 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Item", Binding = new Binding(nameof(LabelLine.Name)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Barcode", Binding = new Binding(nameof(LabelLine.Barcode)) { TargetNullValue = "(new one)" }, IsReadOnly = true, Width = 150 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "In stock", Binding = new Binding(nameof(LabelLine.OnHand)) { StringFormat = "0.###" }, IsReadOnly = true, Width = 90 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Labels", Binding = new Binding(nameof(LabelLine.Copies)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 90 });
        _grid.SelectionChanged += (_, _) => ShowSample();

        var quick = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        quick.Children.Add(Tool("One per piece in stock", "Ctrl+1", (_, _) => { foreach (var l in _lines) l.Copies = Math.Max(1, (int)Math.Ceiling(l.OnHand)); }));
        quick.Children.Add(Tool("One of each", "Ctrl+2", (_, _) => { foreach (var l in _lines) l.Copies = 1; }));

        var left = new DockPanel { Margin = new Thickness(0, 0, 20, 0) };
        DockPanel.SetDock(_summary, Dock.Top);
        DockPanel.SetDock(quick, Dock.Bottom);
        left.Children.Add(_summary);
        left.Children.Add(quick);
        left.Children.Add(_grid);

        var right = new StackPanel { Width = 420 };
        right.Children.Add(new TextBlock { Text = "Items", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        right.Children.Add(_scope);
        right.Children.Add(new TextBlock { Text = "Label", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
        right.Children.Add(_layout);
        right.Children.Add(_cutLines);
        right.Children.Add(_showPrice);
        right.Children.Add(_showStore);
        var currency = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        currency.Children.Add(new TextBlock { Text = "Before the price:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        currency.Children.Add(_currency);
        right.Children.Add(currency);
        right.Children.Add(new TextBlock { Text = "How it looks (enlarged)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 8) });
        right.Children.Add(new Viewbox { Child = _sample, Stretch = Stretch.Uniform, MaxWidth = 420, MaxHeight = 250, HorizontalAlignment = HorizontalAlignment.Left });
        _layout.SelectionChanged += (_, _) => { _cutLines.Visibility = Layout.IsSheet ? Visibility.Visible : Visibility.Collapsed; Refresh(); };
        _showPrice.Click += (_, _) => Refresh();
        _showStore.Click += (_, _) => Refresh();
        _currency.TextChanged += (_, _) => Refresh();

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var print = Tool("Print…", "Ctrl+P", async (_, _) => await PrintAsync());
        print.Style = (Style)Application.Current.FindResource("AccentButton");
        buttons.Children.Add(print);
        var pdf = Tool("Save as PDF…", "Ctrl+S", async (_, _) => await SavePdfAsync());
        pdf.Style = (Style)Application.Current.FindResource("DarkButton");
        Look.SetIcon(pdf, "\uEA90");
        buttons.Children.Add(pdf);
        buttons.Children.Add(Tool("See all pages…", "F5", async (_, _) => await PreviewAsync()));
        var close = Tool("Close", null, (_, _) => Close());
        close.IsCancel = true;
        buttons.Children.Add(close);

        var body = new Grid { Margin = new Thickness(22, 18, 22, 18) };
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(left);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        body.Children.Add(buttons);
        Content = body;
        Views.Layout.AddIcons(this);
        _scope.SelectedIndex = 0;
    }

    /// <summary>Lists the items to label, one label each to begin with.</summary>
    private void Load(IReadOnlyList<ItemRow> rows)
    {
        _lines = new(rows.Select(r => new LabelLine { ItemId = r.Id, Plu = r.Plu, Name = r.Name, Barcode = r.Barcode, Price = r.Price1, OnHand = r.OnHand, Copies = 1 }));
        foreach (var l in _lines) l.PropertyChanged += (_, _) => Refresh();
        _grid.ItemsSource = _lines;
        Refresh();
        if (_lines.Count > 0) _grid.SelectedIndex = 0;
    }

    private static Button Tool(string text, string? key, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.FindResource("ToolButton") };
        Look.SetShortcut(b, key);
        b.Click += click;
        return b;
    }

    private LabelLayout Layout => (LabelLayout)_layout.SelectedItem;

    private LabelOptions Options => new(_showPrice.IsChecked == true, _showStore.IsChecked == true, _currency.Text.Trim(),
        string.IsNullOrWhiteSpace(AppServices.Settings.CompanyName) ? Brand.Name : AppServices.Settings.CompanyName!);

    private void Refresh()
    {
        int labels = _lines.Sum(l => l.Copies);
        int missing = _lines.Count(l => l.Barcode is null && l.Copies > 0);
        var layout = Layout;
        string pages = layout.IsSheet ? $" on {Math.Ceiling(labels / (double)(layout.Columns * layout.Rows)):0} A4 page(s), {layout.Columns * layout.Rows} a page" : "";
        _summary.Text = _lines.Count == 0
            ? "No items here. To label a group of items, flag them in Inventory (Flag / unflag, Ctrl+G), then choose Flagged items."
            : $"{labels:N0} label(s) for {_lines.Count(l => l.Copies > 0)} item(s){pages}. Change the number in the Labels column."
              + (missing > 0 ? $"\n{missing} item(s) have no barcode yet: they get an in-store UPC-A when you print." : "");
        ShowSample();
    }

    private void ShowSample()
    {
        var line = _grid.SelectedItem as LabelLine ?? _lines.FirstOrDefault();
        _sample.Child = line is null ? null : LabelCard.Create(line.Name, line.Plu, line.Barcode ?? Core.Rules.Upc.InStore(1), line.Price, Layout, Options);
    }

    /// <summary>Gives the items without a barcode an in-store UPC-A, so every label can be scanned.</summary>
    private async Task<bool> EnsureBarcodesAsync()
    {
        var missing = _lines.Where(l => l.Barcode is null && l.Copies > 0).ToList();
        if (missing.Count == 0) return true;
        bool ok = await Ui.Try(async () =>
        {
            await AppServices.Inventory.AssignInStoreBarcodesAsync(missing.Select(l => l.ItemId));
            foreach (var l in missing) l.Barcode = (await AppServices.Inventory.GetAsync(l.ItemId))?.Barcode;
        });
        _grid.Items.Refresh();
        Refresh();
        return ok;
    }

    private List<FixedPage> Pages() => LabelSheet.Pages(
        _lines.Where(l => l.Barcode is not null).SelectMany(l => Enumerable.Repeat(l, l.Copies)).Select(l => (l.Name, l.Plu, l.Barcode!, l.Price)),
        Layout, Options, _cutLines.IsChecked == true);

    private FixedDocument Document()
    {
        var layout = Layout;
        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = new Size(layout.PageWidth, layout.PageHeight);
        foreach (var page in Pages())
        {
            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            doc.Pages.Add(content);
        }
        return doc;
    }

    /// <summary>Saves the labels as a PDF (to print later, send, or print on another computer), then offers to open it.</summary>
    private async Task SavePdfAsync()
    {
        if (_lines.Sum(l => l.Copies) == 0) { Ui.Info("Set at least one label in the Labels column."); return; }
        if (!await EnsureBarcodesAsync()) return;
        var file = Ui.SaveFile($"Barcode labels {DateTime.Now:yyyy-MM-dd}.pdf", "PDF document|*.pdf");
        if (file is null) return;
        var layout = Layout;
        var pages = Pages();
        bool saved = await Ui.Try(() =>
        {
            PdfPages.Save(file, pages, new Size(layout.PageWidth, layout.PageHeight), "Barcode labels");
            return Task.CompletedTask;
        });
        if (saved && Ui.Confirm($"{pages.Count} page(s) of labels saved to\n{file}\n\nOpen the PDF now?", "Saved as PDF"))
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
    }

    private async Task PreviewAsync()
    {
        if (!await EnsureBarcodesAsync()) return;
        new Window
        {
            Title = "Labels: all pages", Owner = this, Width = 1000, Height = Math.Min(900, SystemParameters.WorkArea.Height - 40),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new DocumentViewer { Document = Document() },
        }.ShowDialog();
    }

    private async Task PrintAsync()
    {
        int count = _lines.Sum(l => l.Copies);
        if (count == 0) { Ui.Info("Set at least one label in the Labels column."); return; }
        if (count > 1000 && !Ui.Confirm($"Print {count:N0} labels?")) return;
        if (!await EnsureBarcodesAsync()) return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        try
        {
            var layout = Layout;
            dialog.PrintTicket.PageMediaSize = new PageMediaSize(layout.PageWidth, layout.PageHeight);
            dialog.PrintDocument(Document().DocumentPaginator, "SMA RETAIL labels");
        }
        catch (Exception ex) when (ex is InvalidOperationException or PrintSystemException)
        {
            Ui.Error("The labels could not be printed: " + ex.Message);
        }
    }
}

public sealed record LabelOptions(bool ShowPrice, bool ShowStore, string Currency, string StoreName);

/// <summary>Labels laid out on pages: one per page on a label printer, rows and columns on a sheet.</summary>
public static class LabelSheet
{
    /// <param name="cutLines">On a sheet, thin dashed lines around each label to cut along.</param>
    public static List<FixedPage> Pages(IEnumerable<(string Name, string Plu, string Barcode, decimal Price)> labels, LabelLayout layout, LabelOptions options, bool cutLines)
    {
        var all = labels.ToList();
        var pages = new List<FixedPage>();
        int perPage = layout.Columns * layout.Rows;
        double w = layout.WidthMm * LabelLayout.Mm, h = layout.HeightMm * LabelLayout.Mm;
        for (int start = 0; start < all.Count; start += perPage)
        {
            var page = new FixedPage { Width = layout.PageWidth, Height = layout.PageHeight, Background = Brushes.White };
            for (int i = 0; i < perPage && start + i < all.Count; i++)
            {
                var (name, plu, barcode, price) = all[start + i];
                double x = i % layout.Columns * w, y = (layout.TopMm * LabelLayout.Mm) + i / layout.Columns * h;
                var card = LabelCard.Create(name, plu, barcode, price, layout, options);
                FixedPage.SetLeft(card, x);
                FixedPage.SetTop(card, y);
                page.Children.Add(card);
                if (cutLines && layout.IsSheet)
                {
                    var cut = new Rectangle { Width = w, Height = h, Stroke = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)), StrokeThickness = 0.5, StrokeDashArray = [4, 4] };
                    FixedPage.SetLeft(cut, x);
                    FixedPage.SetTop(cut, y);
                    page.Children.Add(cut);
                }
            }
            pages.Add(page);
        }
        return pages;
    }
}

/// <summary>One barcode label, drawn at its real size: store, item name, barcode with its digits, price and stock code.</summary>
public static class LabelCard
{
    public static FrameworkElement Create(string name, string plu, string barcode, decimal price, LabelLayout layout, LabelOptions options)
    {
        double w = layout.WidthMm * LabelLayout.Mm, h = layout.HeightMm * LabelLayout.Mm, pad = 2.2 * LabelLayout.Mm;
        double scale = h / (30 * LabelLayout.Mm);   // text sizes are set for a 30 mm high label
        var ink = Brushes.Black;
        var grid = new Grid { Width = w, Height = h, Background = Brushes.White };
        var panel = new DockPanel { Margin = new Thickness(pad, pad * 0.8, pad, pad * 0.7), LastChildFill = true };

        if (options.ShowStore)
        {
            var store = Text(options.StoreName.ToUpperInvariant(), 6.2 * scale, FontWeights.SemiBold, Brushes.DimGray, TextAlignment.Center);
            DockPanel.SetDock(store, Dock.Top);
            panel.Children.Add(store);
        }
        var title = Text(name, 8.6 * scale, FontWeights.Bold, ink, TextAlignment.Center);
        title.MaxHeight = 8.6 * scale * 2.6;
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);

        var bottom = new DockPanel { Margin = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var code = Text(plu, 6.4 * scale, FontWeights.Normal, Brushes.DimGray, TextAlignment.Left);
        code.VerticalAlignment = VerticalAlignment.Bottom;
        if (options.ShowPrice)
        {
            var amount = Text((options.Currency.Length > 0 ? options.Currency + " " : "") + price.ToString("N2", CultureInfo.CurrentCulture),
                12.5 * scale, FontWeights.Bold, ink, TextAlignment.Right);
            amount.FontFamily = new FontFamily("Century Gothic, Segoe UI");
            DockPanel.SetDock(amount, Dock.Right);
            bottom.Children.Add(amount);
        }
        bottom.Children.Add(code);
        panel.Children.Add(bottom);

        double barArea = w - 2 * pad;
        var bars = BarcodeArt.Product(barcode, barArea, ink);
        bars.HorizontalAlignment = HorizontalAlignment.Center;
        bars.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(new Viewbox { Child = bars, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(0, 1.5, 0, 0) });
        grid.Children.Add(panel);
        return grid;
    }

    private static TextBlock Text(string text, double size, FontWeight weight, Brush brush, TextAlignment align) => new()
    {
        Text = text, FontSize = size, FontWeight = weight, Foreground = brush, TextAlignment = align, TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = new FontFamily("Segoe UI"),
    };
}

/// <summary>Barcodes drawn as shapes (sharp at any size and on any printer), from ZXing's encoders.</summary>
public static class BarcodeArt
{
    /// <summary>
    /// A product barcode as printed on goods: UPC-A with its digits laid out the standard way (first and last digit
    /// outside, guard bars longer), EAN-13 and EAN-8 with their digits below, anything else as Code 128.
    /// </summary>
    public static FrameworkElement Product(string code, double maxWidth, Brush ink)
    {
        bool valid = Core.Rules.Upc.IsValid(code);
        if (valid && code.Length == 12) return UpcA(code, maxWidth, ink);
        var (writer, format) = code.Length switch
        {
            13 when valid => ((OneDimensionalCodeWriter)new EAN13Writer(), BarcodeFormat.EAN_13),
            8 when valid => (new EAN8Writer(), BarcodeFormat.EAN_8),
            _ => (new Code128Writer(), BarcodeFormat.CODE_128),
        };
        var modules = Modules(writer.encode(code, format, 0, 1));
        double module = Math.Min(1.25, maxWidth / modules.Length);
        var panel = new StackPanel();
        panel.Children.Add(Bars(modules, module, 34, _ => false, 0, ink));
        panel.Children.Add(new TextBlock { Text = code, FontFamily = new FontFamily("Consolas"), FontSize = module * 8, HorizontalAlignment = HorizontalAlignment.Center, Foreground = ink });
        return panel;
    }

    private static Canvas UpcA(string code, double maxWidth, Brush ink)
    {
        var modules = Modules(new UPCAWriter().encode(code, BarcodeFormat.UPC_A, 0, 1));   // the 95 modules
        const int side = 9;   // room for the first and last digit, in modules
        double module = Math.Min(1.25, maxWidth / (modules.Length + 2 * side));
        double barHeight = 30 * module / 1.25 + 8, digit = module * 7.4;
        // Guard bars and the first and last digit's bars run down between the digits.
        static bool Long(int i) => i is <= 9 or (>= 45 and <= 49) or >= 85;
        var canvas = new Canvas { Width = (modules.Length + 2 * side) * module, Height = barHeight + digit * 1.05 };
        var bars = Bars(modules, module, barHeight, Long, digit * 0.55, ink);
        Canvas.SetLeft(bars, side * module);
        canvas.Children.Add(bars);
        void Digits(string text, double fromModule, double toModule, double size)
        {
            var t = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = size, Foreground = ink, TextAlignment = TextAlignment.Center, Width = (toModule - fromModule) * module };
            Canvas.SetLeft(t, fromModule * module);
            Canvas.SetTop(t, barHeight - size * 0.12);
            canvas.Children.Add(t);
        }
        Digits(code[..1], 0, side - 1, digit * 0.8);
        Digits(code[1..6], side + 10, side + 45, digit);
        Digits(code[6..11], side + 50, side + 85, digit);
        Digits(code[11..], side + 95 + 1, side + 95 + side, digit * 0.8);
        return canvas;
    }

    /// <summary>The modules of a one-row barcode, from its first bar to its last.</summary>
    private static bool[] Modules(ZXing.Common.BitMatrix matrix)
    {
        int first = 0, last = matrix.Width - 1;
        while (first < matrix.Width && !matrix[first, 0]) first++;
        while (last > first && !matrix[last, 0]) last--;
        return Enumerable.Range(first, last - first + 1).Select(x => matrix[x, 0]).ToArray();
    }

    private static Path Bars(bool[] modules, double module, double height, Func<int, bool> isLong, double extra, Brush ink)
    {
        var group = new GeometryGroup();
        for (int x = 0; x < modules.Length;)
        {
            if (!modules[x]) { x++; continue; }
            int start = x;
            while (x < modules.Length && modules[x]) x++;
            group.Children.Add(new RectangleGeometry(new Rect(start * module, 0, (x - start) * module, height + (isLong(start) ? extra : 0))));
        }
        return new Path { Data = group, Fill = ink, Width = modules.Length * module, Height = height + extra, SnapsToDevicePixels = true };
    }
}
