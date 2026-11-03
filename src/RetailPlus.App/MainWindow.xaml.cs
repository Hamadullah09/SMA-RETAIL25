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
using RetailPlus.Core.Rules;

namespace RetailPlus.App;

/// <summary>
/// A screen of the program, created on first use. It is listed for users whose role has at least one of
/// <see cref="Permissions"/> (every user when there are none).
/// </summary>
public sealed record NavEntry(string Key, string Title, string Glyph, IReadOnlyList<string> Permissions, Func<UserControl> Create, string? Shortcut = null)
{
    public NavEntry(string key, string title, string glyph, string permission, Func<UserControl> create, string? shortcut = null)
        : this(key, title, glyph, [permission], create, shortcut)
    {
    }

    /// <summary>True when the signed-in user may open the screen.</summary>
    public bool IsAllowed => Permissions.Count == 0 || Permissions.Any(AppServices.Can);
}

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
        new("home", "Dashboard", "", Permissions.DashboardView, () => new DashboardView(), "Ctrl+1"),
        new("pos", "Point of Sale", "", Permissions.PosSell, () => new PosView(), "Ctrl+2"),
        new("journal", "Sales Journal", "", [Permissions.PosSell, Permissions.PosVoidSale, Permissions.ReportsView], () => new SalesJournalView(), "Ctrl+3"),
        new("inventory", "Inventory", "", Permissions.InventoryView, () => new InventoryView(), "Ctrl+4"),
        new("clients", "Clients", "", Permissions.ClientsView, () => new ClientsView(), "Ctrl+5"),
        new("suppliers", "Suppliers", "", [Permissions.SuppliersEdit, Permissions.PurchasingManage], () => new SuppliersView(), "Ctrl+6"),
        new("invoices", "Invoices & A/R", "", Permissions.InvoicesManage, () => new InvoicesView(), "Ctrl+7"),
        new("po", "Purchase Orders", "", Permissions.PurchasingManage, () => new PurchaseOrdersView(), "Ctrl+8"),
        new("orders", "Orders & RMAs", "", [Permissions.ClientsEdit, Permissions.RmaManage], () => new CustomerOrdersView(), "Ctrl+9"),
        new("counts", "Stock counts", "\uE9D5", Permissions.InventoryCount, () => new StockCountsView(), "Ctrl+Shift+C"),
        new("transfers", "Transfers", "\uE8AB", Permissions.InventoryTransfer, () => new TransfersView(), "Ctrl+Shift+M"),
        new("giftcards", "Gift cards", "\uE8C7", Permissions.GiftCardsIssue, () => new GiftCardsView(), "Ctrl+Shift+V"),
        new("reports", "Reports", "", [Permissions.ReportsView, Permissions.ReportsStaff], () => new ReportsView(), "Ctrl+0"),
        new("staff", "Staff", "\uE716", [Permissions.TimeClockPunch, Permissions.TimeClockEdit, Permissions.ReportsStaff], () => new StaffView(), "Ctrl+Shift+E"),
        new("rfid", "RFID readers", "\uE704", Permissions.RfidManage, () => new RfidView(), "Ctrl+Shift+R"),
        new("tools", "Tools", "", [Permissions.BackupManage, Permissions.ImportRun, Permissions.YearEndRun, Permissions.InvoicesManage], () => new ToolsView(), "Ctrl+Shift+T"),
        new("audit", "Audit log", "\uE81C", Permissions.AuditView, () => new AuditView(), "Ctrl+Shift+A"),
        new("setup", "Setup", "", [Permissions.SetupStore, Permissions.SetupUsers, Permissions.SetupStations], () => new SetupView(), "Ctrl+Shift+S"),
        new("help", "Help", "\uE897", [], () => new HelpView(), "Ctrl+Shift+H"),
        new("production", "Production", "\uE90F", [Permissions.ProductionFloor, Permissions.ProductionView], () => new ProductionView(), "Ctrl+Shift+F"),
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
        _rfidTimer.Tick += async (_, _) => await UpdateRfidChipAsync();
        _rfidTimer.Start();
        AppServices.RfidChanged += () => Dispatcher.BeginInvoke(async () => await UpdateRfidChipAsync());
        Loaded += async (_, _) => await UpdateRfidChipAsync();
        AppServices.SyncChanged += () => Dispatcher.BeginInvoke(UpdateSyncText);
        UpdateSyncText();
    }

    /// <summary>The server status at the bottom: quiet, and only on a till that joined a server.</summary>
    private void UpdateSyncText()
    {
        if (AppServices.Sync is null || AppServices.SyncStatus is not { } status)
        {
            SyncText.Visibility = Visibility.Collapsed;
            return;
        }
        SyncText.Text = AppServices.SyncLine(status);
        SyncText.Foreground = Look.Brush(status.Refused > 0 ? "Danger" : "Muted");
        SyncText.Visibility = Visibility.Visible;
    }

    private void SyncText_Click(object sender, MouseButtonEventArgs e)
    {
        if (!AppServices.Can(Core.Rules.Permissions.SetupStations)) return;
        Navigate("setup");
        GetView<SetupView>()?.ShowServerTab();
    }

    private readonly DispatcherTimer _rfidTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    /// <summary>The RFID chip in the header: whether the readers are connected, at a glance.</summary>
    private async Task UpdateRfidChipAsync()
    {
        if (AppServices.RfidSearchText is { } searching)
        {
            ShowChip(searching, "Sun");
            return;
        }
        if (AppServices.Rfid is not { } rfid)
        {
            RfidChip.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            var enabled = (await rfid.GetStatusAsync()).Where(r => r.Enabled).ToList();
            int up = enabled.Count(r => r.Connected);
            if (enabled.Count == 0) ShowChip("RFID: no reader set up", "Stone");
            else if (enabled.Count == 1) ShowChip($"RFID: {enabled[0].Name} {(enabled[0].Connected ? "connected" : "offline")}", enabled[0].Connected ? "Ok" : "Danger");
            else ShowChip($"RFID: {up} of {enabled.Count} readers connected", up == enabled.Count ? "Ok" : up == 0 ? "Danger" : "Sun");
        }
        catch (Exception ex) when (ex is Rfid.RfidException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            ShowChip("RFID hub not reachable", "Danger");
        }
    }

    private void ShowChip(string text, string brush)
    {
        RfidChip.Visibility = Visibility.Visible;
        RfidChipText.Text = text;
        RfidDot.Fill = Look.Brush(brush);
    }

    private void RfidChip_Click(object sender, RoutedEventArgs e)
    {
        if (AppServices.Can(Permissions.RfidManage)) Navigate("rfid");
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("t", System.Globalization.CultureInfo.CurrentCulture);
        DateText.Text = now.ToString("dddd, d MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    }

    private static readonly string[] LevelNames = ["Trainee", "Sales", "Sales and stock", "Supervisor", "Administrator"];

    /// <summary>The screens the signed-in user may open, in the order of the list.</summary>
    public IReadOnlyList<NavEntry> AllowedEntries => (IReadOnlyList<NavEntry>)Nav.ItemsSource;

    public void RefreshChrome()
    {
        var s = AppServices.Session;
        StoreName.Text = string.IsNullOrWhiteSpace(AppServices.Settings.CompanyName) ? "Your store" : AppServices.Settings.CompanyName;
        UserText.Text = s.UserName;
        LevelText.Text = string.IsNullOrEmpty(s.RoleName) ? LevelNames[Math.Clamp(s.AccessLevel, 0, 4)] : s.RoleName;
        Initials.Text = string.Concat(s.UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        TrainingBadge.Visibility = s.IsTrainee ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = AppServices.CanAny(Permissions.SetupStore, Permissions.SetupUsers, Permissions.SetupStations) ? Visibility.Visible : Visibility.Collapsed;
        StationText.Text = $"Station {s.StationNumber:000}  ·  Location {s.LocationCode}";
        DatabaseText.Text = $"Database {AppServices.Data.DatabaseName}";
        Title = string.IsNullOrWhiteSpace(AppServices.Settings.CompanyName) ? Brand.Name : $"{Brand.Name} — {AppServices.Settings.CompanyName}";
        ShowStoreLogo();
        var selected = (Nav.SelectedItem as NavEntry)?.Key;
        Nav.ItemsSource = AllEntries.Where(e => e.IsAllowed).ToList();
        _views.Clear();
        Nav.SelectedItem = ((IEnumerable<NavEntry>)Nav.ItemsSource).FirstOrDefault(e => e.Key == selected) ?? ((IEnumerable<NavEntry>)Nav.ItemsSource).First();
    }

    /// <summary>The store's logo next to the store name, when Setup › Branding has one.</summary>
    public void ShowStoreLogo()
    {
        StoreLogo.Source = AppServices.Logo;
        StoreLogoFrame.Visibility = AppServices.Logo is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>Opens a screen the user may open; false when they may not.</summary>
    public bool Navigate(string key)
    {
        var entry = ((IEnumerable<NavEntry>)Nav.ItemsSource).FirstOrDefault(e => e.Key == key);
        if (entry is not null) Nav.SelectedItem = entry;
        return entry is not null;
    }

    public T? GetView<T>() where T : UserControl => _views.Values.OfType<T>().FirstOrDefault();

    private async void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavEntry entry) return;
        if (!_views.TryGetValue(entry.Key, out var view))
        {
            view = entry.Create();
            Layout.AddIcons(view);
            _views[entry.Key] = view;
        }
        Host.Content = view;
        if (view is IRefreshable r) await Ui.Try(r.RefreshAsync);
    }

    /// <summary>Each screen's key (shown in the list): Ctrl+1 … Ctrl+0, Ctrl+Shift+letter for the last ones.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || Shortcuts.Describe(e) is not { } gesture) return;
        var entry = ((IReadOnlyList<NavEntry>)Nav.ItemsSource).FirstOrDefault(n => n.Shortcut == gesture);
        if (entry is null) return;
        Nav.SelectedItem = entry;
        e.Handled = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => Navigate("setup");

    /// <summary>Ctrl+K: type to go to any screen or common action the user may use.</summary>
    public void OpenPalette()
    {
        var palette = new CommandPalette(this) { Owner = this };
        palette.ShowDialog();
    }

    private void Palette_Click(object sender, RoutedEventArgs e) => OpenPalette();

    /// <summary>The list of keys (Ctrl+Shift+K).</summary>
    public void ShowKeys() => Help_Click(this, new RoutedEventArgs());

    /// <summary>Lets the next person sign in (Ctrl+Shift+U).</summary>
    public void SwitchUser() => SwitchUser_Click(this, new RoutedEventArgs());

    private void Help_Click(object sender, RoutedEventArgs e) => Ui.Info(
        "Every button shows its key on the right, like  F5  or  Ctrl+P. Press the key instead of clicking.\n\n" +
        "Everywhere:\n" +
        "Ctrl+K:  go to: type a few letters of any screen, report or action, then Enter\n" +
        "Ctrl+Shift+H:  help, with every screen explained\n" +
        "Ctrl+1 … Ctrl+0:  open a screen (the key is next to each name on the left)\n" +
        "Tab / Shift+Tab:  move between buttons and boxes;  Space:  press the chosen button\n" +
        "Enter:  save or OK;  Esc:  cancel or close\n\n" +
        "Point of Sale:\n" +
        "Just scan or type: the code goes into the code box. Enter adds the item.\n" +
        "3*CODE:  sell 3 of an item\n" +
        "Ctrl+F find,  Ctrl+M item without code,  F4 client,  F5 quantity,  F6 price,  F7/F8 discounts,\n" +
        "F9 return,  Del remove line,  F12 pay,  Esc cancel the sale\n\n" +
        "Pay window:  type the amount, then F4 … F12 for the kind of payment (Enter = cash).\n\n" +
        "F1, F2 and F3 are left to Windows.", "Keyboard keys");

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
        // The Users, Roles and Server tabs of Setup.
        if (Navigate("setup") && GetView<SetupView>() is { } setup)
        {
            int number = AllEntries.ToList().FindIndex(x => x.Key == "setup") + 1;
            foreach (var tab in new[] { "Users", "Roles", "Server" })
            {
                setup.ShowTab(tab);
                await Settle();
                Save(Path.Combine(folder, $"{number:00}-setup-{tab.ToLowerInvariant()}.png"));
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
        // The Pay window with 500 typed for 380 due (change), 300 typed (short), and 500 taken in cash (change to hand back).
        async Task<Window> Pay(string typed, bool payCash)
        {
            var w = new PayWindow(380m, await AppServices.Setup.GetTenderTypesAsync(), AppServices.Settings.MinimumTender, false, false);
            w.Loaded += (_, _) =>
            {
                Descendant<TextBox>(w)!.Text = typed;
                if (payCash) Descendant<Button>(w, b => Equals(b.Content, "Cash"))?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
            return w;
        }
        await Shoot("pay-change", () => Pay("500", false));
        await Shoot("pay-short", () => Pay("300", false));
        await Shoot("pay-given", () => Pay("500", true));
        await Shoot("receipt", async () => new ReceiptWindow(await ReceiptWindow.ForSaleAsync(SampleSale(), "COPY"), "Invoice 1005"));
        await Shoot("labels", async () =>
        {
            var rows = (await AppServices.Inventory.SearchAsync(AppServices.Session.LocationId, null)).Where(r => !r.IsDeleted).ToList();
            return new LabelsWindow(rows, rows.Take(3).ToList());
        });
        try
        {
            var row = (await AppServices.Inventory.SearchAsync(AppServices.Session.LocationId, null, take: 1)).FirstOrDefault();
            var options = new LabelOptions(true, true, "Rs", string.IsNullOrWhiteSpace(AppServices.Settings.CompanyName) ? Brand.Name : AppServices.Settings.CompanyName!);
            var label = LabelCard.Create(row?.Name ?? "Sample item", row?.Plu ?? "SAMPLE", row?.Barcode ?? Core.Rules.Upc.InStore(1001), row?.Price1 ?? 450m, LabelLayout.OneLabel, options);
            SaveElement(label, Path.Combine(folder, "dlg-label.png"), 4);
            // Every item's label on A4 sheets of 30, as the Save as PDF button writes them, and the first page as a picture.
            var items = (await AppServices.Inventory.SearchAsync(AppServices.Session.LocationId, null)).Where(r => !r.IsDeleted && r.Barcode is not null).ToList();
            var sheet = LabelLayout.All.First(l => l.Columns * l.Rows == 30);
            var pages = LabelSheet.Pages(items.Select(r => (r.Name, r.Plu, r.Barcode!, r.Price1)), sheet, options, cutLines: true);
            if (pages.Count > 0)
            {
                PdfPages.Save(Path.Combine(folder, "labels-a4-30.pdf"), pages, new Size(sheet.PageWidth, sheet.PageHeight), "Barcode labels");
                SaveElement(LabelSheet.Pages(items.Take(30).Select(r => (r.Name, r.Plu, r.Barcode!, r.Price1)), sheet, options, cutLines: true)[0],
                    Path.Combine(folder, "labels-a4-30-page1.png"), 1.5);
            }
            Log.Write($"Screenshot dialog label ok; label PDF of {items.Count} items on {pages.Count} page(s)");
        }
        catch (Exception ex)
        {
            failures++;
            Log.Write($"Screenshot dialog label failed: {ex}");
        }
        try
        {
            var receipt = new ReceiptWindow(await ReceiptWindow.ForSaleAsync(SampleSale(), "COPY"), "Invoice 1005");
            receipt.SavePictureTo(Path.Combine(folder, "dlg-receipt-paper.png"));
            Log.Write("Screenshot dialog receipt-paper ok");
        }
        catch (Exception ex)
        {
            failures++;
            Log.Write($"Screenshot dialog receipt-paper failed: {ex}");
        }
        await Shoot("message", () => Task.FromResult<Window>(new MessageWindow("Cancel this sale?", "All the lines on the screen will be removed. Nothing is saved.", MessageKind.Question)));
        await Shoot("client", () => Task.FromResult<Window>(new PartyEditorWindow(new Core.Model.Client { FirstName = "Sam", LastName = "Example" })));
        await Shoot("item", async () =>
        {
            var row = (await AppServices.Inventory.SearchAsync(AppServices.Session.LocationId, null, take: 1)).FirstOrDefault();
            var item = row is null ? new Core.Model.Item { Plu = "NEW1", Name = "New item" } : (await AppServices.Inventory.GetAsync(row.Id))!;
            return new ItemEditorWindow(item);
        });
        await Shoot("login", () => Task.FromResult<Window>(new LoginWindow()));
        await Shoot("approval", () => Task.FromResult<Window>(new ApprovalWindow(Core.Rules.Permissions.PosVoidSale, "Voiding a sale")));
        await Shoot("palette", () => Task.FromResult<Window>(new CommandPalette(this)));
        await Shoot("punch", () => Task.FromResult<Window>(new PunchWindow()));
        await Shoot("user", async () => new UserEditorWindow(null, await AppServices.Setup.GetRolesAsync(), await AppServices.Setup.GetStoresAsync()));
        await Shoot("time-entry", async () => new TimeEntryWindow(null, await AppServices.Setup.GetUsersAsync()));
        await Shoot("commission-rule", async () =>
        {
            var (departments, categories, items) = await AppServices.Staff.GetRuleChoicesAsync();
            return new RuleEditorWindow(null, await AppServices.Setup.GetUsersAsync(), departments, categories, items);
        });
        await Shoot("giftcard", () => Task.FromResult<Window>(new GiftCardSaleWindow("Gift card", 50m)));
        await Shoot("prices", async () => new PriceChangeWindow(
            (await AppServices.Inventory.PreviewPriceChangesAsync(AppServices.Session.LocationId, new Data.Services.PriceRule(Data.Services.PriceChangeMethod.Percent, 5, 0.99m), false, null)).Take(30).ToList(),
            "Price 1 +5%, rounded up to end in .99"));
        await Shoot("reader", async () =>
        {
            var reader = (await AppServices.RfidData.GetReadersAsync()).FirstOrDefault() ?? Data.Services.RfidService.NewReader("Reader 1");
            var w = new ReaderEditorWindow(reader);
            w.Loaded += (_, _) => w.Tabs.SelectedIndex = 1;
            return w;
        });
        await Shoot("reader-radio", async () =>
        {
            var reader = (await AppServices.RfidData.GetReadersAsync()).FirstOrDefault() ?? Data.Services.RfidService.NewReader("Reader 1");
            var w = new ReaderEditorWindow(reader);
            w.Loaded += (_, _) => w.Tabs.SelectedIndex = 2;
            return w;
        });
        return failures;
    }

    /// <summary>A sale to show the receipt with: three products, a line discount, paid in cash with change.</summary>
    /// <summary>Saves an element laid out on its own as a PNG on white, <paramref name="scale"/> times screen size.</summary>
    private static void SaveElement(FrameworkElement element, string file, double scale)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var white = new DrawingVisual();
        using (var dc = white.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(white);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    internal static Core.Model.Sale SampleSale()
    {
        var sale = new Core.Model.Sale
        {
            InvoiceNumber = 1005, SoldAt = DateTime.Now, StationNumber = 1, Staff = "admin", ClientName = "Ayesha Khan",
            Subtotal = 1830m, Total = 1830m, Status = Core.Model.SaleStatus.Completed,
        };
        sale.Lines.Add(new Core.Model.SaleLine { LineNumber = 1, Kind = Core.Model.SaleLineKind.Sale, Plu = "CNF-1001", Description = "Cadbury Dairy Milk 90g", Quantity = 3, UnitPrice = 450m, LineTotal = 1350m });
        sale.Lines.Add(new Core.Model.SaleLine { LineNumber = 2, Kind = Core.Model.SaleLineKind.Sale, Plu = "SNK-2002", Description = "Lay's Masala 70g", Quantity = 2, UnitPrice = 150m, LineTotal = 300m });
        sale.Lines.Add(new Core.Model.SaleLine { LineNumber = 3, Kind = Core.Model.SaleLineKind.Sale, Plu = "BEV-3002", Description = "Coca-Cola 1.5L", Quantity = 1, UnitPrice = 220m, DiscountAmount = 40m, LineTotal = 180m });
        sale.Payments.Add(new Core.Model.SalePayment { TenderType = "Cash", Amount = 1830m, Tendered = 2000m, Change = 170m });
        return sale;
    }

    private static T? Descendant<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T t && (match is null || match(t))) return t;
            if (Descendant(child, match) is { } found) return found;
        }
        return null;
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
