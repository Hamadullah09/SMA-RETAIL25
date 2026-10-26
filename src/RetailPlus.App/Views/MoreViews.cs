using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;
using RetailPlus.Migration.Import;
using RetailPlus.Reports;

namespace RetailPlus.App.Views;

/// <summary>Purchase orders: generate, edit quantities and costs, post the order, receive the shipment.</summary>
public sealed class PurchaseOrdersView : UserControl, IRefreshable
{
    private readonly DataGrid _list = Layout.Grid(
        Layout.Col("PO", nameof(PurchaseOrderRow.Number), 70),
        Layout.Col("Created", nameof(PurchaseOrderRow.CreatedAt), 100, "d"),
        Layout.Col("Status", nameof(PurchaseOrderRow.Status), 80),
        Layout.Col("Lines", nameof(PurchaseOrderRow.Lines), 50, null, true),
        Layout.Col("Cost", nameof(PurchaseOrderRow.TotalCost), 90, "N2", true));
    private readonly DataGrid _lines = new()
    {
        AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(12, 0, 0, 0),
    };
    private readonly TextBlock _title = new() { Style = Layout.S("SectionTitle") };
    private readonly TextBlock _totals = new() { Foreground = Brushes.Gray, Margin = new Thickness(12, 6, 0, 0) };
    private PurchaseOrder? _current;

    public PurchaseOrdersView()
    {
        _list.Width = 500;
        _list.SelectionMode = DataGridSelectionMode.Single;
        void Ro(string h, string p, double w, string? f = null) => _lines.Columns.Add(new DataGridTextColumn { Header = h, Binding = new System.Windows.Data.Binding(p) { StringFormat = f }, Width = w < 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(w), IsReadOnly = true });
        void Rw(string h, string p, double w) => _lines.Columns.Add(new DataGridTextColumn { Header = h, Binding = new System.Windows.Data.Binding(p) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = w });
        Ro("Stock code", nameof(PurchaseOrderLine.Plu), 130);
        Ro("Description", nameof(PurchaseOrderLine.Description), -1);
        Ro("Supplier", nameof(PurchaseOrderLine.SupplierName), 140);
        Ro("On hand", nameof(PurchaseOrderLine.OnHandAtOrder), 100, "0.###");
        Rw("Case qty", nameof(PurchaseOrderLine.CaseQuantity), 95);
        Rw("Order qty", nameof(PurchaseOrderLine.OrderQuantity), 105);
        Rw("Cost each", nameof(PurchaseOrderLine.CostEach), 105);
        Ro("Line cost", nameof(PurchaseOrderLine.OrderCost), 105, "N2");
        Rw("Received", nameof(PurchaseOrderLine.QuantityReceived), 105);

        var right = new DockPanel();
        DockPanel.SetDock(_title, Dock.Top);
        _title.Margin = new Thickness(12, 0, 0, 6);
        right.Children.Add(_title);
        DockPanel.SetDock(_totals, Dock.Bottom);
        right.Children.Add(_totals);
        right.Children.Add(_lines);
        var body = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left);
        body.Children.Add(_list);
        body.Children.Add(right);
        Content = Layout.Page("Purchase Orders", out var bar, body);
        bar.Children.Add(Layout.Button("New order…", New_Click, true));
        bar.Children.Add(Layout.Button("Add item", AddItem_Click));
        bar.Children.Add(Layout.Button("Remove line", RemoveLine_Click));
        bar.Children.Add(Layout.Button("Save changes", Save_Click));
        bar.Children.Add(Layout.Button("Post order", Post_Click));
        bar.Children.Add(Layout.Button("Receive all", ReceiveAll_Click));
        bar.Children.Add(Layout.Button("Post shipment", Receive_Click));
        bar.Children.Add(Layout.Button("Print", Print_Click));
        bar.Children.Add(Layout.Button("Delete order", Delete_Click));
        _list.SelectionChanged += async (_, _) => await Ui.Try(LoadCurrentAsync);
    }

    public async Task RefreshAsync()
    {
        var selected = (_list.SelectedItem as PurchaseOrderRow)?.Id;
        var rows = await AppServices.Purchasing.ListAsync(AppServices.Session.LocationId);
        _list.ItemsSource = rows;
        _list.SelectedItem = rows.FirstOrDefault(r => r.Id == selected) ?? rows.FirstOrDefault();
    }

    private async Task LoadCurrentAsync()
    {
        if (_list.SelectedItem is not PurchaseOrderRow row) { _current = null; _lines.ItemsSource = null; _title.Text = ""; _totals.Text = ""; return; }
        _current = await AppServices.Purchasing.GetAsync(row.Id);
        if (_current is null) return;
        _lines.ItemsSource = _current.Lines;
        _lines.IsReadOnly = _current.Status == PurchaseOrderStatus.Received;
        _title.Text = $"PO {_current.Number} · {_current.Status} · {_current.Method}" + (string.IsNullOrWhiteSpace(_current.Description) ? "" : " · " + _current.Description);
        _totals.Text = $"{_current.Lines.Count} lines · {_current.Lines.Sum(l => l.OrderQuantity * l.CaseQuantity):0.###} pieces · cost {_current.Lines.Sum(l => l.OrderCost):N2}";
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        var methods = Enum.GetValues<OrderMethod>();
        string[] names = ["Blank order", "One week's sales", "Two weeks' sales", "Reorder points", "Monthly sales (same month last year)"];
        int m = Ui.Choose("How many to order?", names, "New purchase order");
        if (m < 0) return;
        string? supplier = null;
        if (methods[m] != OrderMethod.Blank)
        {
            var suppliers = await AppServices.Parties.GetSupplierNamesAsync();
            var options = new List<string> { "All suppliers" };
            options.AddRange(suppliers);
            int s = Ui.Choose("For which supplier?", options, "New purchase order");
            if (s < 0) return;
            supplier = s == 0 ? null : suppliers[s - 1];
        }
        var description = Ui.Prompt("Description:", "New purchase order", supplier ?? names[m]);
        if (description is null) return;
        await Ui.Try(async () =>
        {
            var po = await AppServices.Purchasing.CreateAsync(AppServices.Session.LocationId, methods[m], description, supplier);
            await RefreshAsync();
            _list.SelectedItem = ((IEnumerable<PurchaseOrderRow>)_list.ItemsSource).FirstOrDefault(r => r.Id == po.Id);
            if (po.Lines.Count == 0 && methods[m] != OrderMethod.Blank) Ui.Info("No items need ordering by that method.");
        });
    }

    private async void AddItem_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var picker = new ItemPickerWindow();
        if (picker.ShowDialog() != true || picker.Selected is not { } item) return;
        var qty = Ui.PromptDecimal("Pieces to order:", "Add item", 1);
        if (qty is null) return;
        await Ui.Try(async () => { await SaveEditsAsync(); await AppServices.Purchasing.AddItemAsync(_current.Id, item.Plu, qty.Value); await RefreshAsync(); await LoadCurrentAsync(); });
    }

    private async void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _lines.SelectedItem is not PurchaseOrderLine line) return;
        _current.Lines.Remove(line);
        await Ui.Try(async () => { await AppServices.Purchasing.SaveAsync(_current); await RefreshAsync(); });
    }

    private async Task SaveEditsAsync()
    {
        if (_current is null) return;
        _lines.CommitEdit(DataGridEditingUnit.Row, true);
        await AppServices.Purchasing.SaveAsync(_current);
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await Ui.Try(async () => { await SaveEditsAsync(); await RefreshAsync(); });

    private async void Post_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || !Ui.Confirm($"Post PO {_current.Number}? The quantities are added to On Order.")) return;
        await Ui.Try(async () => { await SaveEditsAsync(); await AppServices.Purchasing.PostOrderAsync(_current.Id); await RefreshAsync(); });
    }

    private void ReceiveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        foreach (var l in _current.Lines) l.QuantityReceived = l.OrderQuantity * l.CaseQuantity;
        _lines.Items.Refresh();
    }

    private async void Receive_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        _lines.CommitEdit(DataGridEditingUnit.Row, true);
        if (_current.Lines.All(l => l.QuantityReceived == 0)) { Ui.Info("Enter the pieces received in the Received column (or click Receive all)."); return; }
        var shipping = Ui.PromptDecimal("Shipping / freight cost to spread over the items (0 for none):", "Post shipment", 0);
        if (shipping is null) return;
        await Ui.Try(async () =>
        {
            await AppServices.Purchasing.PostShipmentAsync(_current.Id, _current.Lines.Select(l => new ReceivedLine(l.Id, l.QuantityReceived, l.CostEach)).ToList(), shipping.Value);
            Ui.Info("Shipment posted: stock and costs are updated.");
            await RefreshAsync();
        });
    }

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var header = await AppServices.Setup.GetTextAsync(DocumentTextKind.PurchaseOrderHeader);
        var report = new ReportTable
        {
            Title = $"Purchase order {_current.Number}",
            Subtitle = (string.IsNullOrWhiteSpace(header) ? AppServices.Settings.CompanyName : header.Replace("\r\n", " · ", StringComparison.Ordinal)) + " · " + _current.Description,
            Columns = [new("Supplier"), new("Their code"), new("Stock code"), new("Description"), new("Size"), new("Cases", ColumnKind.Quantity), new("Case qty", ColumnKind.Quantity), new("Cost each", ColumnKind.Money), new("Cost", ColumnKind.Money, true)],
            Rows = _current.Lines.Select(l => new object?[] { l.SupplierName, l.ReorderNumber, l.Plu, l.Description, l.Size, l.OrderQuantity, l.CaseQuantity, l.CostEach, l.OrderCost }).ToList(),
        };
        new ReportWindow(report).ShowDialog();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || !Ui.Confirm($"Delete PO {_current.Number}?")) return;
        await Ui.Try(async () => { await AppServices.Purchasing.DeleteAsync(_current.Id); await RefreshAsync(); });
    }
}

/// <summary>Shows a report as a grid with print and export.</summary>
public sealed class ReportWindow : Window
{
    public ReportWindow(ReportTable report)
    {
        Title = report.Title;
        Width = 1100;
        Height = 720;
        Owner = Ui.Owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new ReportPanel(report) { Margin = new Thickness(14) };
    }
}

/// <summary>A report on screen: title, grid, totals line, and Print / CSV / HTML buttons.</summary>
public sealed class ReportPanel : DockPanel
{
    public ReportPanel(ReportTable report)
    {
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = report.Title, FontSize = 18, FontWeight = FontWeights.SemiBold });
        head.Children.Add(new TextBlock { Text = $"{report.Subtitle}  ·  {report.Rows.Count} rows", Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8) });
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(Layout.Button("Print", (_, _) => Ui.PrintReport(report), true));
        bar.Children.Add(Layout.Button("Save CSV", (_, _) => { var f = Ui.SaveFile(Safe(report.Title) + ".csv", "CSV|*.csv"); if (f is not null) File.WriteAllText(f, report.ToCsv()); }));
        bar.Children.Add(Layout.Button("Save HTML", (_, _) => { var f = Ui.SaveFile(Safe(report.Title) + ".html", "HTML|*.html"); if (f is not null) File.WriteAllText(f, report.ToHtml()); }));
        head.Children.Add(bar);
        SetDock(head, Dock.Top);
        Children.Add(head);
        var culture = CultureInfo.CurrentCulture;
        var table = new DataTable();
        foreach (var c in report.Columns) table.Columns.Add(c.Header.Replace(".", "", StringComparison.Ordinal));
        foreach (var r in report.Rows) table.Rows.Add(r.Select((v, i) => (object)report.Columns[i].Format(v, culture)).ToArray());
        if (report.HasTotals) table.Rows.Add(report.TotalsRow().Select((v, i) => (object)report.Columns[i].Format(v, culture)).ToArray());
        var grid = new DataGrid { Style = Layout.S("Grid"), AutoGenerateColumns = false, ItemsSource = table.DefaultView };
        for (int i = 0; i < report.Columns.Count; i++)
        {
            var col = new DataGridTextColumn
            {
                Header = report.Columns[i].Header, Binding = new System.Windows.Data.Binding($"[{table.Columns[i].ColumnName}]"),
                Width = report.Columns[i].Kind == ColumnKind.Text ? new DataGridLength(1, DataGridLengthUnitType.Auto) : new DataGridLength(100),
            };
            if (report.Columns[i].RightAligned) col.ElementStyle = Layout.S("Right");
            grid.Columns.Add(col);
        }
        Children.Add(grid);
    }

    private static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

/// <summary>The Reports menu: pick a report and a period.</summary>
public sealed class ReportsView : UserControl, IRefreshable
{
    private readonly ListBox _list = new() { Width = 340, Margin = new Thickness(0, 0, 16, 0) };
    private readonly DatePicker _from = new() { SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), Margin = new Thickness(0, 0, 8, 6) };
    private readonly DatePicker _to = new() { SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 6) };
    private readonly ContentControl _result = new();

    public ReportsView()
    {
        _list.ItemsSource = ReportService.All;
        _list.DisplayMemberPath = nameof(ReportDefinition.Name);
        var view = (System.Windows.Data.CollectionView)System.Windows.Data.CollectionViewSource.GetDefaultView(_list.ItemsSource);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(ReportDefinition.Group)));
        _list.GroupStyle.Add(new GroupStyle { HeaderTemplate = GroupHeader() });
        var body = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left);
        body.Children.Add(_list);
        body.Children.Add(_result);
        Content = Layout.Page("Reports", out var bar, body);
        bar.Children.Add(Layout.Label("From"));
        bar.Children.Add(_from);
        bar.Children.Add(Layout.Label("To"));
        bar.Children.Add(_to);
        bar.Children.Add(Layout.Button("Run report", async (_, _) => await Ui.Try(RunAsync), true));
        bar.Children.Add(Layout.Button("Today", (_, _) => { _from.SelectedDate = DateTime.Today; _to.SelectedDate = DateTime.Today; }));
        bar.Children.Add(Layout.Button("This month", (_, _) => { _from.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); _to.SelectedDate = DateTime.Today; }));
        bar.Children.Add(Layout.Button("This year", (_, _) => { _from.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1); _to.SelectedDate = DateTime.Today; }));
        _list.MouseDoubleClick += async (_, _) => await Ui.Try(RunAsync);
        _list.SelectedIndex = 0;
    }

    private static DataTemplate GroupHeader()
    {
        var f = new FrameworkElementFactory(typeof(TextBlock));
        f.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
        f.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        f.SetValue(TextBlock.MarginProperty, new Thickness(4, 10, 0, 4));
        f.SetValue(TextBlock.ForegroundProperty, Application.Current.FindResource("Brand"));
        return new DataTemplate { VisualTree = f };
    }

    public async Task RefreshAsync()
    {
        if (_result.Content is null) await RunAsync();
    }

    public async Task RunAsync()
    {
        if (_list.SelectedItem is not ReportDefinition def) return;
        var report = await AppServices.Reports.RunAsync(def.Key, AppServices.Session.LocationId,
            DateOnly.FromDateTime(_from.SelectedDate ?? DateTime.Today), DateOnly.FromDateTime(_to.SelectedDate ?? DateTime.Today));
        _result.Content = new ReportPanel(report);
    }

    /// <summary>Runs every report once (used by the automated check).</summary>
    public static async Task<List<string>> RunAllAsync()
    {
        var failures = new List<string>();
        var from = new DateOnly(DateTime.Today.Year - 5, 1, 1);
        var to = DateOnly.FromDateTime(DateTime.Today);
        foreach (var def in ReportService.All)
        {
            try
            {
                var r = await AppServices.Reports.RunAsync(def.Key, AppServices.Session.LocationId, from, to);
                _ = r.ToCsv();
                _ = r.ToHtml();
                Log.Write($"Report {def.Key}: {r.Rows.Count} rows");
            }
            catch (Exception ex)
            {
                failures.Add($"{def.Key}: {ex.Message}");
                Log.Write($"Report {def.Key} failed: {ex}");
            }
        }
        return failures;
    }
}

/// <summary>Tools: import from Retail Plus 5, backup / restore / rebuild, year end, recurring billing.</summary>
public sealed class ToolsView : UserControl, IRefreshable
{
    private readonly TextBox _log = new()
    {
        IsReadOnly = true, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap, MinHeight = 200,
    };

    public ToolsView()
    {
        var cards = new WrapPanel();
        cards.Children.Add(Card("Import from Retail Plus 5", "Brings in the data of the old Retail Plus 5 program. Use a copy made while the old program was closed.", "Import…", Import_Click));
        cards.Children.Add(Card("Backup", "Saves a full copy of the database to a .bak file.", "Back up now", Backup_Click));
        cards.Children.Add(Card("Restore", "Replaces the database with a backup. Every station must be closed.", "Restore…", Restore_Click));
        cards.Children.Add(Card("Rebuild", "Checks the database and rebuilds its indexes (the old Reindex).", "Rebuild", Rebuild_Click));
        cards.Children.Add(Card("Recurring billing", "Creates the on-account invoices of clients whose next billing date has come.", "Run billing", Recurring_Click));
        cards.Children.Add(Card("Year end", "Keeps each item's on-hand count as last year's closing stock.", "Year end…", YearEnd_Click));
        var body = new DockPanel();
        DockPanel.SetDock(cards, Dock.Top);
        body.Children.Add(cards);
        var logTitle = new TextBlock { Text = "Log", Style = Layout.S("SectionTitle") };
        DockPanel.SetDock(logTitle, Dock.Top);
        body.Children.Add(logTitle);
        body.Children.Add(_log);
        Content = Layout.Page("Tools", out _, body);
    }

    public Task RefreshAsync() => Task.CompletedTask;

    private static Border Card(string title, string text, string button, RoutedEventHandler click)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = title, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 21 });
        p.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("Muted"), Margin = new Thickness(0, 6, 0, 12), Height = 78, FontSize = 16 });
        var b = Layout.Button(button, click, true);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        p.Children.Add(b);
        return new Border { Child = p, Width = 360, Style = Layout.S("CardBorder"), Margin = new Thickness(0, 0, 16, 16) };
    }

    private void Write(string line) => Dispatcher.Invoke(() => { _log.AppendText(line + Environment.NewLine); _log.ScrollToEnd(); });

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var folder = Ui.PickFolder("Retail Plus data folder (the one holding SETUP.DBF)");
        if (folder is null) return;
        if (!File.Exists(Path.Combine(folder, "SETUP.DBF"))) { Ui.Error("That folder has no SETUP.DBF."); return; }
        bool hasData = await AppServices.Setup.HasDataAsync();
        if (hasData && !Ui.Confirm("This database already holds items or clients. Replace ALL of its data with the import?")) return;
        var ini = Path.Combine(folder, "Plus50.INI");
        await Ui.Try(async () =>
        {
            Write($"Importing {folder}…");
            var importer = new DbfImporter(new ImportOptions
            {
                SourceFolder = folder, ConnectionString = AppServices.Data.ConnectionString, Overwrite = hasData,
                StationIniPath = File.Exists(ini) ? ini : null, Progress = Write,
            });
            var report = await Task.Run(() => importer.RunAsync());
            var dir = Path.Combine(Settings.StationConfig.DefaultFolder, "ImportReports", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "report.md"), report.ToMarkdown());
            await File.WriteAllTextAsync(Path.Combine(dir, "report.json"), report.ToJson());
            foreach (var t in report.Totals) Write($"  {t.Name}: source {t.Source} / database {t.Database} {(t.Matches ? "OK" : "DIFFERENT")}");
            Write(report.VerificationPassed ? "Import finished; all control totals match." : "Import finished, but some totals differ — see the report.");
            Write("Report: " + dir);
            await AppServices.RefreshSettingsAsync();
            (Window.GetWindow(this) as MainWindow)?.RefreshChrome();
        });
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        await Ui.Try(async () =>
        {
            Write("Backing up…");
            var file = await AppServices.Backup.BackupAsync(AppServices.Config.BackupFolderOrDefault);
            Write("Backup written to " + file);
        });
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var file = Ui.OpenFile("SQL Server backup|*.bak");
        if (file is null || !Ui.Confirm("Replace the whole database with this backup? Everything entered since the backup is lost.")) return;
        await Ui.Try(async () =>
        {
            Write("Restoring " + file + "…");
            await AppServices.Backup.RestoreAsync(file);
            Write("Restored.");
            await AppServices.RefreshSettingsAsync();
        });
    }

    private async void Rebuild_Click(object sender, RoutedEventArgs e) =>
        await Ui.Try(async () => { Write("Rebuilding…"); Write(await AppServices.Backup.RebuildAsync()); });

    private async void Recurring_Click(object sender, RoutedEventArgs e) =>
        await Ui.Try(async () =>
        {
            int n = await AppServices.Parties.RunRecurringBillingAsync(AppServices.Session.LocationId, DateOnly.FromDateTime(DateTime.Today), AppServices.Invoices);
            Write($"Recurring billing: {n} invoices created.");
        });

    private async void YearEnd_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.Confirm("Run Year End for this location now?")) return;
        await Ui.Try(async () => Write($"Year end: {await AppServices.Inventory.YearEndAsync(AppServices.Session.LocationId)} items recorded."));
    }
}
