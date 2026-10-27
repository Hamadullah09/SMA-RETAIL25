using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Views;

public partial class SetupView : UserControl, IRefreshable
{
    private StoreSettings _settings = new();
    private ObservableCollection<TaxConfiguration> _taxes = [];
    private ObservableCollection<TenderType> _tenders = [];
    private ObservableCollection<FxRate> _fx = [];
    private List<FastKey> _keys = [];
    private readonly AccountingSetupPanel? _accounting;
    private readonly BrandingSetupPanel? _branding;

    public SetupView()
    {
        InitializeComponent();
        Layout.AddIcons(this);
        RfidModeBox.ItemsSource = new[]
        {
            new StationModeChoice(Settings.RfidMode.Auto, "Automatic: connect to the readers by itself (recommended)"),
            new StationModeChoice(Settings.RfidMode.Off, "Off: no RFID at this station"),
            new StationModeChoice(Settings.RfidMode.ThisComputer, "This computer connects to the readers"),
            new StationModeChoice(Settings.RfidMode.Hub, "Use the RFID hub on another computer"),
        };
        HubKeyBox.Password = AppServices.Config.RfidHubApiKey ?? "";
        KindColumn.ItemsSource = Enum.GetValues<TenderKind>();
        PanelBox.ItemsSource = Enumerable.Range(0, 26).ToList();
        PrinterBox.ItemsSource = InstalledPrinters();
        ProviderBox.ItemsSource = Enum.GetValues<Data.DatabaseProvider>();
        StationPanel.DataContext = AppServices.Config;
        StationFile.Text = $"These settings belong to this computer only and are kept in {AppServices.Config.FilePath}. Restart the program after changing the database, station or location.";
        // Accounting and branding have their own Save buttons, so they come in from code; This station stays last.
        int at = Tabs.Items.OfType<TabItem>().ToList().FindIndex(t => Equals(t.Header, "Users"));
        if (at < 0) at = Tabs.Items.Count - 1;
        if (AppServices.Can(Core.Rules.Permissions.SetupStore))
            Tabs.Items.Insert(at, new TabItem { Header = "Branding", Content = _branding = new BrandingSetupPanel() });
        if (AppServices.Can(Core.Rules.Permissions.AccountingExport))
            Tabs.Items.Insert(at, new TabItem { Header = "Accounting", Content = _accounting = new AccountingSetupPanel() });
        Tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, Tabs)) ShowSaveButton(); };
    }

    private static List<string> InstalledPrinters()
    {
        try
        {
            using var server = new System.Printing.LocalPrintServer();
            return server.GetPrintQueues([System.Printing.EnumeratedPrintQueueTypes.Local, System.Printing.EnumeratedPrintQueueTypes.Connections]).Select(q => q.FullName).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Shows the tabs the user may use: store settings, users and roles, and this station's settings.</summary>
    private void ShowAllowedTabs()
    {
        bool store = AppServices.Can(Permissions.SetupStore), users = AppServices.Can(Permissions.SetupUsers), station = AppServices.Can(Permissions.SetupStations);
        foreach (var tab in Tabs.Items.OfType<TabItem>())
        {
            bool allowed = tab.Tag switch { "users" => users, "station" => station, _ => store };
            tab.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
        }
        if (Tabs.SelectedItem is not TabItem { Visibility: Visibility.Visible })
            Tabs.SelectedItem = Tabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Visibility == Visibility.Visible);
        ShowSaveButton();
    }

    /// <summary>
    /// Save settings saves the store's tabs and this station's; on the Users and Roles tabs it is hidden, so their own
    /// Save (Ctrl+S) is the one that answers.
    /// </summary>
    private void ShowSaveButton()
    {
        bool mayChange = AppServices.CanAny(Permissions.SetupStore, Permissions.SetupStations);
        SaveButton.Visibility = mayChange && (Tabs.SelectedItem as TabItem)?.Tag as string != "users" ? Visibility.Visible : Visibility.Collapsed;
    }

    public async Task RefreshAsync()
    {
        ShowAllowedTabs();
        if (AppServices.Can(Permissions.SetupUsers))
        {
            await UsersPanel.RefreshAsync();
            await RolesPanel.RefreshAsync();
        }
        _settings = await AppServices.Setup.GetSettingsAsync();
        Tabs.DataContext = _settings;
        StationPanel.DataContext = AppServices.Config;
        _taxes = new(await AppServices.Setup.GetTaxConfigurationsAsync());
        TaxGrid.ItemsSource = _taxes;
        _tenders = new(await AppServices.Setup.GetTenderTypesAsync(false));
        TendersGrid.ItemsSource = _tenders;
        _fx = new(await AppServices.Setup.GetFxRatesAsync());
        FxGrid.ItemsSource = _fx;
        if (_accounting is not null) await _accounting.LoadAsync();
        if (_branding is not null) await _branding.LoadAsync();
        DeptList.ItemsSource = await AppServices.Setup.GetDepartmentsAsync();
        CatList.ItemsSource = await AppServices.Setup.GetCategoriesAsync();
        LocationList.ItemsSource = (await AppServices.Setup.GetLocationsAsync()).Select(l => $"{l.Code}  {l.Name}").ToList();
        HeaderBox.Text = await AppServices.Setup.GetTextAsync(DocumentTextKind.InvoiceHeader);
        FooterBox.Text = await AppServices.Setup.GetTextAsync(DocumentTextKind.InvoiceFooter);
        PoHeaderBox.Text = await AppServices.Setup.GetTextAsync(DocumentTextKind.PurchaseOrderHeader);
        ReturnBox.Text = await AppServices.Setup.GetTextAsync(DocumentTextKind.ReturnAddress);
        if (PanelBox.SelectedIndex < 0) PanelBox.SelectedIndex = 0; else await LoadKeysAsync();
    }

    private async Task LoadKeysAsync()
    {
        int panel = PanelBox.SelectedIndex < 0 ? 0 : (int)PanelBox.SelectedItem;
        var existing = await AppServices.Setup.GetFastKeysAsync(panel);
        _keys = Enumerable.Range(1, 25).Select(n => existing.FirstOrDefault(k => k.KeyNumber == n) ?? new FastKey { Panel = panel, KeyNumber = n, Taxable1 = true, Taxable2 = true }).ToList();
        KeysGrid.ItemsSource = _keys;
    }

    private async void Panel_SelectionChanged(object sender, SelectionChangedEventArgs e) => await Ui.Try(LoadKeysAsync);

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        TaxGrid.CommitEdit(); TendersGrid.CommitEdit(); KeysGrid.CommitEdit(); FxGrid.CommitEdit();
        await Ui.Try(async () =>
        {
            // Each part is saved only by users who may change it.
            if (AppServices.Can(Permissions.SetupStore))
            {
                await AppServices.Setup.SaveSettingsAsync(_settings);
                await AppServices.Setup.SaveTaxConfigurationsAsync(_taxes);
                await AppServices.Setup.SaveFxRatesAsync(_fx);
                await AppServices.Setup.SaveTenderTypesAsync(_tenders);
                await AppServices.Setup.SetTextAsync(DocumentTextKind.InvoiceHeader, HeaderBox.Text);
                await AppServices.Setup.SetTextAsync(DocumentTextKind.InvoiceFooter, FooterBox.Text);
                await AppServices.Setup.SetTextAsync(DocumentTextKind.PurchaseOrderHeader, PoHeaderBox.Text);
                await AppServices.Setup.SetTextAsync(DocumentTextKind.ReturnAddress, ReturnBox.Text);
                foreach (var k in _keys.Where(k => k.Id != Guid.Empty || !string.IsNullOrWhiteSpace(k.Name) || !string.IsNullOrWhiteSpace(k.Plu)))
                    await AppServices.Setup.SaveFastKeyAsync(k);
            }
            if (AppServices.Can(Permissions.SetupStations))
            {
                AppServices.Config.RfidHubApiKey = string.IsNullOrEmpty(HubKeyBox.Password) ? null : HubKeyBox.Password;
                AppServices.Config.Save();
                await AppServices.StartRfidAsync();
            }
            await AppServices.RefreshSettingsAsync();
            (Window.GetWindow(this) as MainWindow)?.RefreshChrome();
            await RefreshAsync();
            Ui.Info("Settings saved.");
        });
    }

    /// <summary>A choice of the RFID mode drop-down.</summary>
    public sealed record StationModeChoice(Settings.RfidMode Value, string Text);

    /// <summary>Opens the "This station" tab (from the RFID screen).</summary>
    public void ShowStationTab() => Tabs.SelectedIndex = Tabs.Items.Count - 1;

    /// <summary>Opens a tab by its header (Users, Roles…), for the screenshot tour.</summary>
    public void ShowTab(string header) =>
        Tabs.SelectedItem = Tabs.Items.OfType<TabItem>().FirstOrDefault(t => string.Equals(t.Header as string, header, StringComparison.OrdinalIgnoreCase)) ?? Tabs.SelectedItem;

    private async void AddDept_Click(object sender, RoutedEventArgs e)
    {
        var name = Ui.Prompt("Department name:", "Add department");
        if (name is null) return;
        await Ui.Try(async () => { await AppServices.Setup.AddDepartmentAsync(name); await RefreshAsync(); });
    }

    private async void DeleteDept_Click(object sender, RoutedEventArgs e)
    {
        if (DeptList.SelectedItem is not string name || !Ui.Confirm($"Delete department {name}? Its items keep no department.")) return;
        await Ui.Try(async () => { await AppServices.Setup.DeleteDepartmentAsync(name); await RefreshAsync(); });
    }

    private async void AddCat_Click(object sender, RoutedEventArgs e)
    {
        var name = Ui.Prompt("Category name:", "Add category");
        if (name is null) return;
        await Ui.Try(async () => { await AppServices.Setup.AddCategoryAsync(name); await RefreshAsync(); });
    }

    private async void DeleteCat_Click(object sender, RoutedEventArgs e)
    {
        if (CatList.SelectedItem is not string name || !Ui.Confirm($"Delete category {name}?")) return;
        await Ui.Try(async () => { await AppServices.Setup.DeleteCategoryAsync(name); await RefreshAsync(); });
    }

    private async void AddLocation_Click(object sender, RoutedEventArgs e)
    {
        var code = Ui.Prompt("Location code (1–3 characters):", "Add location");
        if (string.IsNullOrWhiteSpace(code)) return;
        var name = Ui.Prompt("Location name:", "Add location", code.ToUpperInvariant());
        await Ui.Try(async () => { await AppServices.Setup.AddLocationAsync(code, name); await AppServices.RefreshSettingsAsync(); await RefreshAsync(); });
    }

    private void TestPrinter_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppServices.Printer.Print($"{AppServices.Settings.CompanyName}\nTest receipt\nStation {AppServices.Session.StationNumber:000}\n{DateTime.Now:g}\n", true);
            Ui.Info(string.IsNullOrWhiteSpace(AppServices.Config.ReceiptPrinter) ? $"No printer is set; the receipt was saved in {AppServices.Config.ReceiptFolderOrDefault}." : "Sent to the printer.");
        }
        catch (Exception ex)
        {
            Ui.Error("The printer did not accept the job: " + ex.Message);
        }
    }
}
