using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Views;

/// <summary>
/// Setup › Accounting: the account code of the accounting program for each tender, department, tax and the rest, the
/// journal export for a date range, and the automatic export at End of Day.
/// </summary>
public sealed class AccountingSetupPanel : UserControl
{
    /// <summary>A row of the accounts table.</summary>
    public sealed class Row(AccountMapping mapping, string what)
    {
        public AccountMapping Mapping { get; } = mapping;
        public string What { get; } = what;
        public string AccountCode { get => Mapping.AccountCode; set => Mapping.AccountCode = value; }
        public string? AccountName { get => Mapping.AccountName; set => Mapping.AccountName = value; }
    }

    private readonly DataGrid _grid = new() { Style = Layout.S("Grid"), IsReadOnly = false, SelectionMode = DataGridSelectionMode.Single, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly DatePicker _from = new() { SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 6) };
    private readonly DatePicker _to = new() { SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 6) };
    private readonly CheckBox _auto = new() { Content = "At End of Day, save the day's journal in this folder:", Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBox _folder = new() { Width = 520, Margin = new Thickness(0, 0, 8, 6) };
    private List<Row> _rows = [];

    public AccountingSetupPanel()
    {
        _grid.Columns.Add(new DataGridTextColumn { Header = "Amounts of", Binding = new System.Windows.Data.Binding(nameof(Row.What)), Width = 330, IsReadOnly = true });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Account code", Binding = new System.Windows.Data.Binding(nameof(Row.AccountCode)), Width = 160 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Account name (optional)", Binding = new System.Windows.Data.Binding(nameof(Row.AccountName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        var root = new DockPanel { Margin = new Thickness(12) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock
        {
            Foreground = Look.Brush("Muted"), TextWrapping = TextWrapping.Wrap, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8),
            Text = "The account codes of your accounting program. The export is a journal for each business day, in balance: sales by department, gift cards sold, "
                   + "discounts, taxes and rounding against the money taken by each tender; payments on account; cost of goods sold; stock received, adjusted, counted "
                   + "and transferred. Amounts without an account are exported with the code " + AccountingService.Unmapped + ".",
        });
        var save = Layout.Button("Save accounts", async (_, _) => await Ui.Try(SaveAsync), true);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        top.Children.Add(save);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        bottom.Children.Add(new TextBlock { Text = "Export", Style = Layout.S("SectionTitle") });
        var range = new WrapPanel();
        range.Children.Add(Layout.Label("From"));
        range.Children.Add(_from);
        range.Children.Add(Layout.Label("To"));
        range.Children.Add(_to);
        range.Children.Add(Layout.Button("Export journal…", async (_, _) => await Ui.Try(ExportAsync)));
        bottom.Children.Add(range);
        bottom.Children.Add(_auto);
        var folder = new WrapPanel();
        folder.Children.Add(_folder);
        folder.Children.Add(Layout.Button("Choose folder…", (_, _) => { if (Ui.PickFolder("Folder for the End of Day journal") is { } f) _folder.Text = f; }));
        folder.Children.Add(Layout.Button("Save the End of Day export", async (_, _) => await Ui.Try(SaveAutoAsync)));
        bottom.Children.Add(folder);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(_grid);
        Content = root;
    }

    public async Task LoadAsync()
    {
        var settings = AppServices.Settings;
        _rows = (await AppServices.Accounting.MappingTableAsync()).Select(m => new Row(m, Describe(m, settings))).ToList();
        _grid.ItemsSource = _rows;
        var station = await AppServices.Setup.GetStationSettingsAsync(AppServices.Session.StationNumber);
        _auto.IsChecked = station.GetValueOrDefault($"{AccountingService.SettingsSection}/{AccountingService.AutoExportKey}") == "1";
        _folder.Text = station.GetValueOrDefault($"{AccountingService.SettingsSection}/{AccountingService.ExportFolderKey}") ?? "";
    }

    private static string Describe(AccountMapping m, StoreSettings settings) => m.Kind switch
    {
        AccountMappingKind.Tender => "Payments by " + m.Key,
        AccountMappingKind.Department => "Sales of " + m.Key,
        AccountMappingKind.Sales => "Sales without a department of their own",
        AccountMappingKind.Tax1 => settings.Tax1Name ?? "Tax 1",
        AccountMappingKind.Tax2 => settings.Tax2Name ?? "Tax 2",
        AccountMappingKind.GiftCardLiability => "Gift cards sold and not spent",
        AccountMappingKind.CostOfGoods => "Cost of goods sold",
        AccountMappingKind.Inventory => "Inventory (stock at cost)",
        AccountMappingKind.AccountsReceivable => "Accounts receivable (on account, layaways)",
        AccountMappingKind.Discounts => "Discounts on the whole sale",
        AccountMappingKind.Rounding => "Cash rounding",
        AccountMappingKind.StockAdjustments => "Stock found or lost (adjustments, counts)",
        AccountMappingKind.PurchasesClearing => "Stock received, supplier not billed yet",
        AccountMappingKind.InventoryInTransit => "Stock in transit to another store",
        AccountMappingKind.GiftCardAdjustments => "Gift card balances a supervisor corrected",
        _ => m.Kind.ToString(),
    };

    private async Task SaveAsync()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        await AppServices.Accounting.SaveMappingsAsync(_rows.Select(r => r.Mapping));
        await LoadAsync();
        Ui.Info("Accounts saved.");
    }

    private async Task ExportAsync()
    {
        var from = DateOnly.FromDateTime(_from.SelectedDate ?? DateTime.Today);
        var to = DateOnly.FromDateTime(_to.SelectedDate ?? DateTime.Today);
        if (to < from) { Ui.Error("The end date is before the start date."); return; }
        var file = Ui.SaveFile($"journal-{from:yyyyMMdd}-{to:yyyyMMdd}.csv", "CSV file|*.csv");
        if (file is null) return;
        var export = await AppServices.Accounting.ExportAsync(from, to);
        await File.WriteAllTextAsync(file, export.ToCsv(), new System.Text.UTF8Encoding(true));
        var c = CultureInfo.CurrentCulture;
        Ui.Info($"{export.Totals.Count} journal(s), {export.Lines.Count} line(s), {export.Totals.Sum(t => t.Debit).ToString("N2", c)} debit and credit, saved to {file}."
                + (export.Unmapped.Count > 0 ? "\n\nNo account is set for:\n" + string.Join("\n", export.Unmapped) : ""));
    }

    private async Task SaveAutoAsync()
    {
        bool on = _auto.IsChecked == true;
        var folder = _folder.Text.Trim();
        if (on && folder.Length == 0) { Ui.Error("Choose the folder for the End of Day journal."); return; }
        int station = AppServices.Session.StationNumber;
        await AppServices.Setup.SetStationSettingAsync(station, AccountingService.SettingsSection, AccountingService.AutoExportKey, on ? "1" : "0");
        await AppServices.Setup.SetStationSettingAsync(station, AccountingService.SettingsSection, AccountingService.ExportFolderKey, folder.Length == 0 ? null : folder);
        Ui.Info(on ? $"At End of Day, station {station:000} saves the day's journal in {folder}." : "The journal is no longer saved at End of Day.");
    }

    /// <summary>
    /// End of Day: when this station saves the journal automatically, writes each business day of the closed drawer
    /// session to the chosen folder. Returns the files written (none when the export is off).
    /// </summary>
    public static async Task<List<string>> ExportAtEndOfDayAsync(DateTime openedAt)
    {
        var files = new List<string>();
        int station = AppServices.Session.StationNumber;
        var settings = await AppServices.Setup.GetStationSettingsAsync(station);
        if (settings.GetValueOrDefault($"{AccountingService.SettingsSection}/{AccountingService.AutoExportKey}") != "1") return files;
        var folder = settings.GetValueOrDefault($"{AccountingService.SettingsSection}/{AccountingService.ExportFolderKey}");
        if (string.IsNullOrWhiteSpace(folder)) return files;
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var day = DateOnly.FromDateTime(openedAt); day <= today; day = day.AddDays(1))
            files.Add(await AppServices.Accounting.ExportDayToFolderAsync(folder, day, station));
        return files;
    }
}

/// <summary>Setup › Branding: the store's logo (app header, receipts, printed reports) and the watermark printed faintly behind them.</summary>
public sealed class BrandingSetupPanel : UserControl
{
    private readonly Image _logo = new() { Height = 140, Width = 280, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Image _watermark = new() { Height = 140, Width = 280, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };

    public BrandingSetupPanel()
    {
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock
        {
            Foreground = Look.Brush("Muted"), TextWrapping = TextWrapping.Wrap, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12),
            Text = "Pictures are kept in the database (made at most 800 pixels a side), so every till of the store shows them. Without a logo the SMA logo is used.",
        });
        var cards = new WrapPanel();
        cards.Children.Add(Card("Logo", "In the header of the app, on receipts and on printed reports.", _logo, StoreImageKind.Logo));
        cards.Children.Add(Card("Watermark", "Printed faintly behind receipts and reports.", _watermark, StoreImageKind.Watermark));
        root.Children.Add(cards);
        Content = root;
    }

    private Border Card(string title, string text, Image preview, StoreImageKind kind)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = title, Style = Layout.S("SectionTitle") });
        p.Children.Add(new TextBlock { Text = text, Foreground = Look.Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        p.Children.Add(new Border { Child = preview, Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Left });
        var buttons = new WrapPanel();
        buttons.Children.Add(Layout.Button("Choose picture…", async (_, _) => await ChooseAsync(kind), true));
        buttons.Children.Add(Layout.Button("Remove picture", async (_, _) => await Ui.Try(() => SetAsync(kind, null))));
        p.Children.Add(buttons);
        return new Border { Child = p, Width = 420, Style = Layout.S("CardBorder"), Margin = new Thickness(0, 0, 16, 16) };
    }

    public async Task LoadAsync()
    {
        await AppServices.RefreshBrandingAsync();
        _logo.Source = AppServices.Logo;
        _watermark.Source = AppServices.Watermark;
    }

    private async Task ChooseAsync(StoreImageKind kind)
    {
        var file = Ui.OpenFile("Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*");
        if (file is null) return;
        byte[] picture;
        try
        {
            picture = Pictures.FromFile(file);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or FileFormatException or ArgumentException)
        {
            Ui.Error("That file is not a picture this program can read: " + ex.Message);
            return;
        }
        await Ui.Try(() => SetAsync(kind, picture));
    }

    /// <summary>Stores (or with null removes) a picture and shows it everywhere at once.</summary>
    public async Task SetAsync(StoreImageKind kind, byte[]? picture)
    {
        await AppServices.Branding.SetAsync(kind, picture);
        await LoadAsync();
        (Window.GetWindow(this) as MainWindow ?? Application.Current.MainWindow as MainWindow)?.ShowStoreLogo();
    }
}
