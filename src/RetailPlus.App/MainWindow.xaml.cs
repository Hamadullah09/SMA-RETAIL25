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
        new("home", "Dashboard", "", 1, () => new DashboardView()),
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

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(15) };

    public MainWindow()
    {
        InitializeComponent();
        RefreshChrome();
        Nav.SelectedIndex = 0;
        PreviewKeyDown += OnPreviewKeyDown;
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("t", System.Globalization.CultureInfo.CurrentCulture);
        DateText.Text = now.ToString("dddd, d MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    }

    private static readonly string[] LevelNames = ["Trainee", "Sales", "Sales and stock", "Supervisor", "Administrator"];

    public void RefreshChrome()
    {
        var s = AppServices.Session;
        StoreName.Text = string.IsNullOrWhiteSpace(AppServices.Settings.CompanyName) ? "Your store" : AppServices.Settings.CompanyName;
        UserText.Text = s.UserName;
        LevelText.Text = LevelNames[Math.Clamp(s.AccessLevel, 0, 4)];
        Initials.Text = string.Concat(s.UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        TrainingBadge.Visibility = s.IsTrainee ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = AppServices.Can(3) ? Visibility.Visible : Visibility.Collapsed;
        StationText.Text = $"Station {s.StationNumber:000}  ·  Location {s.LocationCode}";
        DatabaseText.Text = $"Database {AppServices.Data.DatabaseName}";
        Title = string.IsNullOrWhiteSpace(AppServices.Settings.CompanyName) ? Brand.Name : $"{Brand.Name} — {AppServices.Settings.CompanyName}";
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

    private void Settings_Click(object sender, RoutedEventArgs e) => Navigate("setup");

    private void Help_Click(object sender, RoutedEventArgs e) => Ui.Info(
        "Ctrl + 1 … 9:  open a screen from the list on the left\n\n" +
        "On the Point of Sale screen:\n" +
        "Enter:  add the item whose code you typed (or pay, when the box is empty)\n" +
        "3*CODE:  sell 3 of an item\n" +
        "F3:  find an item\n" +
        "F4:  choose a client\n" +
        "F5:  change the quantity\n" +
        "F6:  change the price\n" +
        "F12:  pay\n" +
        "Delete:  remove the chosen line", "Keyboard keys");

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
        failures += await ShootDialogsAsync(folder);
        return failures == 0 ? 0 : 4;
    }

    /// <summary>Screenshots of the main dialogs, opened without waiting for input.</summary>
    private async Task<int> ShootDialogsAsync(string folder)
    {
        int failures = 0;
        async Task Shoot(string name, Func<Task<Window>> make)
        {
            try
            {
                var w = await make();
                w.Owner = this;
                w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                w.Show();
                await Settle();
                SaveWindow(w, Path.Combine(folder, $"dlg-{name}.png"));
                w.Close();
                Log.Write($"Screenshot dialog {name} ok");
            }
            catch (Exception ex)
            {
                failures++;
                Log.Write($"Screenshot dialog {name} failed: {ex}");
            }
        }
        await Shoot("pay", async () => new PayWindow(123.45m, await AppServices.Setup.GetTenderTypesAsync(), AppServices.Settings.MinimumTender, false, false));
        await Shoot("message", () => Task.FromResult<Window>(new MessageWindow("Cancel this sale?", "All the lines on the screen will be removed. Nothing is saved.", MessageKind.Question)));
        await Shoot("client", () => Task.FromResult<Window>(new PartyEditorWindow(new Core.Model.Client { FirstName = "Sam", LastName = "Example" })));
        await Shoot("item", async () =>
        {
            var row = (await AppServices.Inventory.SearchAsync(AppServices.Session.LocationId, null, take: 1)).FirstOrDefault();
            var item = row is null ? new Core.Model.Item { Plu = "NEW1", Name = "New item" } : (await AppServices.Inventory.GetAsync(row.Id))!;
            return new ItemEditorWindow(item);
        });
        await Shoot("login", () => Task.FromResult<Window>(new LoginWindow()));
        return failures;
    }

    public static async Task Settle()
    {
        await Task.Delay(400);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    public void Save(string file) => SaveWindow(this, file);

    /// <summary>Renders a window's client area (background, margins and all) to a PNG.</summary>
    public static void SaveWindow(Window window, string file)
    {
        var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        root.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
