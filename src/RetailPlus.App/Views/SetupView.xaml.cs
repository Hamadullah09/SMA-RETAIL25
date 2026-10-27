using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;
using RetailPlus.Sync;

namespace RetailPlus.App.Views;

public partial class SetupView : UserControl, IRefreshable
{
    private StoreSettings _settings = new();
    private ObservableCollection<TaxConfiguration> _taxes = [];
    private ObservableCollection<TenderType> _tenders = [];
    private List<FastKey> _keys = [];

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

    public async Task RefreshAsync()
    {
        _settings = await AppServices.Setup.GetSettingsAsync();
        Tabs.DataContext = _settings;
        StationPanel.DataContext = AppServices.Config;
        _taxes = new(await AppServices.Setup.GetTaxConfigurationsAsync());
        TaxGrid.ItemsSource = _taxes;
        _tenders = new(await AppServices.Setup.GetTenderTypesAsync(false));
        TendersGrid.ItemsSource = _tenders;
        UsersGrid.ItemsSource = await AppServices.Setup.GetUsersAsync();
        DeptList.ItemsSource = await AppServices.Setup.GetDepartmentsAsync();
        CatList.ItemsSource = await AppServices.Setup.GetCategoriesAsync();
        LocationList.ItemsSource = (await AppServices.Setup.GetLocationsAsync()).Select(l => $"{l.Code}  {l.Name}").ToList();
        HeaderBox.Text = await AppServices.Setup.GetTextAsync(DocumentTextKind.InvoiceHeader);
        FooterBox.Text = await AppServices.Setup.GetTextAsync(DocumentTextKind.InvoiceFooter);
        PoHeaderBox.Text = await AppServices.Setup.GetTextAsync(DocumentTextKind.PurchaseOrderHeader);
        ReturnBox.Text = await AppServices.Setup.GetTextAsync(DocumentTextKind.ReturnAddress);
        if (PanelBox.SelectedIndex < 0) PanelBox.SelectedIndex = 0; else await LoadKeysAsync();
        await RefreshServerAsync();
    }

    // Server --------------------------------------------------------------------------------------------------------

    /// <summary>Opens the Server tab (from the status line).</summary>
    public void ShowServerTab() => Tabs.SelectedItem = ServerTab;

    private async Task RefreshServerAsync()
    {
        ServerUrlBox.Text = AppServices.Config.ServerUrl ?? "";
        IntervalBox.Text = AppServices.Config.SyncIntervalMinutes.ToString(System.Globalization.CultureInfo.CurrentCulture);
        await AppServices.RefreshSyncStatusAsync();
        bool joined = AppServices.Sync is not null;
        bool canJoin = AppServices.Can(Core.Rules.Permissions.SetupStations);
        JoinButton.IsEnabled = canJoin;
        CodeBox.IsEnabled = canJoin;
        SendNowButton.IsEnabled = SendAgainButton.IsEnabled = SavePackageButton.IsEnabled = LoadPackageButton.IsEnabled = joined;
        if (!joined || AppServices.SyncStatus is not { } status)
        {
            ServerStatusText.Text = AppServices.Data.Connection.Provider == Data.DatabaseProvider.Sqlite
                ? "This till works on its own: it has not joined a server."
                : "This till uses a shared SQL Server database; only a till with its own database file can join a server.";
            ServerErrorText.Text = "";
            return;
        }
        ServerStatusText.Text = $"Station {AppServices.Session.StationNumber:000} of the server's store.  {AppServices.SyncLine(status)}.";
        ServerErrorText.Text = status.LastError ?? (status.Refused > 0 ? $"The server refused {status.Refused} package(s); they are in {AppServices.Sync!.RefusedFolder}." : "");
    }

    private async void Join_Click(object sender, RoutedEventArgs e)
    {
        var url = ServerUrlBox.Text.Trim();
        var code = CodeBox.Text.Trim();
        if (url.Length == 0 || code.Length == 0)
        {
            Ui.Info("Enter the server address and the enrolment code you were given.");
            return;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            Ui.Error($"{url} is not a web address. It looks like https://pos.example.com.");
            return;
        }
        if (!Ui.Confirm("Join this till to the server? Its data moves to the server's store and it takes the station number the server gave it. Everything it has is then sent to the server.")) return;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var joined = await Task.Run(() => TillSync.JoinAsync(AppServices.Data, url, code, AppServices.AppVersion));
            await AppServices.JoinedAsync(url, joined);
            CodeBox.Text = "";
            (Window.GetWindow(this) as MainWindow)?.RefreshChrome();
            Ui.Info($"This till is now station {joined.StationNumber:000} of {joined.StoreName}. Its data is being sent to the server.");
            AppServices.SyncSoon();
        }
        catch (Exception ex) when (ex is SyncRefusedException or SyncAuthException or PackageRejectedException or BusinessRuleException or HttpRequestException or TaskCanceledException)
        {
            Ui.Error("The till could not join the server: " + ex.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            await RefreshServerAsync();
        }
    }

    private async void SendNow_Click(object sender, RoutedEventArgs e)
    {
        SendNowButton.IsEnabled = false;
        ServerStatusText.Text = "Sending…";
        var result = await AppServices.SyncNowAsync();
        await RefreshServerAsync();
        if (result is { Ok: true }) Ui.Info($"Sent to the server{(result.Received is { Changed: > 0 } r ? $"; {r.Changed} changes received" : "")}.");
    }

    private async void SendAgain_Click(object sender, RoutedEventArgs e)
    {
        if (AppServices.Sync is not { } sync || !Ui.Confirm("Send everything this till has, and receive everything again from the server? It can take a while.")) return;
        await Ui.Try(async () =>
        {
            await sync.ResetAsync();
            await AppServices.SyncNowAsync();
            await RefreshServerAsync();
        });
    }

    private async void SavePackage_Click(object sender, RoutedEventArgs e)
    {
        if (AppServices.Sync is not { } sync || Ui.PickFolder("Where to save the package (a USB stick)") is not { } folder) return;
        bool full = FullCopyBox.IsChecked == true;
        await Ui.Try(async () =>
        {
            var file = await Task.Run(() => sync.SavePackageToFileAsync(folder, full));
            Ui.Info($"Saved {Path.GetFileName(file)} in {folder}. Upload it on the web (Tills › Upload a package).");
        });
    }

    private async void LoadPackage_Click(object sender, RoutedEventArgs e)
    {
        if (AppServices.Sync is not { } sync || Ui.OpenFile("Sync packages (*.smapkg)|*.smapkg") is not { } file) return;
        await Ui.Try(async () =>
        {
            ApplyResult result;
            try
            {
                result = await Task.Run(() => sync.LoadPackageFromFileAsync(file));
            }
            catch (PackageRejectedException ex)
            {
                throw new BusinessRuleException(ex.Message);
            }
            if (result.Status == Core.Model.SyncBatchStatus.Failed) throw new BusinessRuleException("The package was not loaded: " + result.Error);
            await AppServices.RefreshSettingsAsync();
            await RefreshServerAsync();
            Ui.Info(result.Status == Core.Model.SyncBatchStatus.Duplicate ? "This package was loaded before." : $"Package loaded: {result.Changed} changes.");
        });
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
        TaxGrid.CommitEdit(); TendersGrid.CommitEdit(); KeysGrid.CommitEdit();
        await Ui.Try(async () =>
        {
            await AppServices.Setup.SaveSettingsAsync(_settings);
            await AppServices.Setup.SaveTaxConfigurationsAsync(_taxes);
            await AppServices.Setup.SaveTenderTypesAsync(_tenders);
            await AppServices.Setup.SetTextAsync(DocumentTextKind.InvoiceHeader, HeaderBox.Text);
            await AppServices.Setup.SetTextAsync(DocumentTextKind.InvoiceFooter, FooterBox.Text);
            await AppServices.Setup.SetTextAsync(DocumentTextKind.PurchaseOrderHeader, PoHeaderBox.Text);
            await AppServices.Setup.SetTextAsync(DocumentTextKind.ReturnAddress, ReturnBox.Text);
            foreach (var k in _keys.Where(k => k.Id != Guid.Empty || !string.IsNullOrWhiteSpace(k.Name) || !string.IsNullOrWhiteSpace(k.Plu)))
                await AppServices.Setup.SaveFastKeyAsync(k);
            AppServices.Config.RfidHubApiKey = string.IsNullOrEmpty(HubKeyBox.Password) ? null : HubKeyBox.Password;
            AppServices.Config.ServerUrl = string.IsNullOrWhiteSpace(ServerUrlBox.Text) ? null : ServerUrlBox.Text.Trim();
            if (int.TryParse(IntervalBox.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out var minutes))
                AppServices.Config.SyncIntervalMinutes = Math.Clamp(minutes, 1, 24 * 60);
            AppServices.Config.Save();
            AppServices.RestartSync();
            await AppServices.StartRfidAsync();
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

    private UserAccount? CurrentUser => UsersGrid.SelectedItem as UserAccount;

    private async void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var name = Ui.Prompt("User name:", "Add user");
        if (string.IsNullOrWhiteSpace(name)) return;
        var level = Ui.Choose("Access level:", ["0 — trainee", "1 — sales only", "2 — add and change", "3 — supervisor", "4 — administrator"], "Add user");
        if (level < 0) return;
        var password = Ui.Prompt($"Password for {name}:", "Add user", password: true);
        if (string.IsNullOrEmpty(password)) return;
        await Ui.Try(async () => { await AppServices.Setup.SaveUserAsync(new UserAccount { UserName = name, AccessLevel = level }, password); await RefreshAsync(); });
    }

    private async void UserLevel_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentUser is not { } u) return;
        var level = Ui.Choose($"Access level of {u.UserName}:", ["0 — trainee", "1 — sales only", "2 — add and change", "3 — supervisor", "4 — administrator"], "Change level");
        if (level < 0) return;
        u.AccessLevel = level;
        await Ui.Try(async () => { await AppServices.Setup.SaveUserAsync(u, null); await RefreshAsync(); });
    }

    private async void UserPassword_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentUser is not { } u) return;
        var password = Ui.Prompt($"New password for {u.UserName}:", "Set password", password: true);
        if (string.IsNullOrEmpty(password)) return;
        await Ui.Try(async () => { await AppServices.Setup.SaveUserAsync(u, password); await RefreshAsync(); });
    }

    private async void DeleteUser_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentUser is not { } u || !Ui.Confirm($"Delete user {u.UserName}?")) return;
        await Ui.Try(async () => { await AppServices.Setup.DeleteUserAsync(u.Id); await RefreshAsync(); });
    }

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
