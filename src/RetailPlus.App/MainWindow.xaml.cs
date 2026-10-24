using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.App.Views;

namespace RetailPlus.App;

/// <summary>A screen of the program, created on first use.</summary>
public sealed record NavEntry(string Key, string Title, string Glyph, int MinimumLevel, Func<UserControl> Create);

/// <summary>Views that reload their data when shown.</summary>
public interface IRefreshable
{
    Task RefreshAsync();
}

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _views = [];

    public static readonly IReadOnlyList<NavEntry> AllEntries =
    [
        new("pos", "Point of Sale", "", 0, () => new PosView()),
        new("journal", "Sales Journal", "", 1, () => new SalesJournalView()),
        new("inventory", "Inventory", "", 2, () => new InventoryView()),
        new("clients", "Clients", "", 1, () => new ClientsView()),
        new("suppliers", "Suppliers", "", 2, () => new SuppliersView()),
        new("invoices", "Invoices & A/R", "", 2, () => new InvoicesView()),
        new("po", "Purchase Orders", "", 2, () => new PurchaseOrdersView()),
        new("orders", "Orders & RMAs", "", 1, () => new CustomerOrdersView()),
        new("reports", "Reports", "", 2, () => new ReportsView()),
        new("tools", "Tools", "", 3, () => new ToolsView()),
        new("setup", "Setup", "", 3, () => new SetupView()),
    ];

    public MainWindow()
    {
        InitializeComponent();
        RefreshChrome();
        Nav.SelectedIndex = 0;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public void RefreshChrome()
    {
        var s = AppServices.Session;
        StoreName.Text = AppServices.Settings.CompanyName ?? "";
        UserText.Text = $"{s.UserName} · level {s.AccessLevel}" + (s.IsTrainee ? " (training)" : "");
        StationText.Text = $"Station {s.StationNumber:000} · {s.LocationCode}";
        DatabaseText.Text = $"Database {AppServices.Data.DatabaseName}";
        Title = $"Retail Plus — {AppServices.Settings.CompanyName}";
        var selected = (Nav.SelectedItem as NavEntry)?.Key;
        Nav.ItemsSource = AllEntries.Where(e => AppServices.Can(e.MinimumLevel)).ToList();
        _views.Clear();
        Nav.SelectedItem = ((IEnumerable<NavEntry>)Nav.ItemsSource).FirstOrDefault(e => e.Key == selected) ?? ((IEnumerable<NavEntry>)Nav.ItemsSource).First();
    }

    public void SetStatus(string text) => StatusText.Text = text;

    public void Navigate(string key)
    {
        var entry = ((IEnumerable<NavEntry>)Nav.ItemsSource).FirstOrDefault(e => e.Key == key);
        if (entry is not null) Nav.SelectedItem = entry;
    }

    public T? GetView<T>() where T : UserControl => _views.Values.OfType<T>().FirstOrDefault();

    private async void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavEntry entry) return;
        if (!_views.TryGetValue(entry.Key, out var view))
        {
            view = entry.Create();
            _views[entry.Key] = view;
        }
        Host.Content = view;
        if (view is IRefreshable r) await Ui.Try(r.RefreshAsync);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        int n = e.Key - Key.D1;
        var list = (IReadOnlyList<NavEntry>)Nav.ItemsSource;
        if (n >= 0 && n < list.Count && n < 9)
        {
            Nav.SelectedIndex = n;
            e.Handled = true;
        }
    }

    private void SwitchUser_Click(object sender, RoutedEventArgs e)
    {
        var login = new LoginWindow { Owner = this };
        if (login.ShowDialog() == true) RefreshChrome();
    }

    /// <summary>Saves a PNG of every screen into <paramref name="folder"/>; returns 0 when all screens rendered.</summary>
    public async Task<int> RunScreenshotTourAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        int failures = 0;
        foreach (var entry in (IReadOnlyList<NavEntry>)Nav.ItemsSource)
        {
            try
            {
                Nav.SelectedItem = entry;
                await Settle();
                if (_views.TryGetValue(entry.Key, out var v) && v is IRefreshable r) await r.RefreshAsync();
                await Settle();
                Save(Path.Combine(folder, $"{(AllEntries.ToList().FindIndex(x => x.Key == entry.Key) + 1):00}-{entry.Key}.png"));
                Log.Write($"Screenshot {entry.Key} ok");
            }
            catch (Exception ex)
            {
                failures++;
                Log.Write($"Screenshot {entry.Key} failed: {ex}");
            }
        }
        return failures == 0 ? 0 : 4;
    }

    public static async Task Settle()
    {
        await Task.Delay(400);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    public void Save(string file) => SaveVisual((FrameworkElement)Content, file);

    public static void SaveVisual(FrameworkElement element, string file)
    {
        element.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
