using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Reports;

namespace RetailPlus.App.Views;

/// <summary>
/// Stock counts: start a count of a location, count items by scanning or typing their codes, by reading their RFID
/// tags or from a CSV file, review the differences and their value, then post the count (or cancel it).
/// </summary>
public sealed class StockCountsView : UserControl, IRefreshable
{
    private readonly DataGrid _list = Layout.Grid(
        Layout.Col("Count", nameof(StockCountRow.Number), 95),
        Layout.Col("Location", nameof(StockCountRow.LocationCode), 70),
        Layout.Col("Status", nameof(StockCountRow.Status), 75),
        Layout.Col("Started", nameof(StockCountRow.StartedAt), 120, "g"),
        Layout.Col("Lines", nameof(StockCountRow.Lines), 50, null, true),
        Layout.Col("Value", nameof(StockCountRow.VarianceCost), 85, "N2", true));
    private readonly DataGrid _lines = Layout.Grid(
        Layout.Col("Stock code", nameof(StockCountLine.Plu), 115),
        Layout.Col("Description", nameof(StockCountLine.Description)),
        Layout.Col("Expected", nameof(StockCountLine.Expected), 75, "0.###", true),
        Layout.Col("Counted", nameof(StockCountLine.Counted), 75, "0.###", true),
        Layout.Col("Variance", nameof(StockCountLine.Variance), 75, "0.###", true),
        Layout.Col("Value", nameof(StockCountLine.VarianceCost), 85, "N2", true));
    private readonly TextBox _code = new() { Width = 240, Margin = new Thickness(0, 0, 8, 6), FontSize = 19 };
    private readonly TextBox _quantity = new() { Width = 90, Margin = new Thickness(0, 0, 8, 6), Text = "1", FontSize = 19, HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly CheckBox _replace = new() { Content = "Replace what was counted", Margin = new Thickness(0, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _differences = new() { Content = "Differences only", Margin = new Thickness(0, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _openOnly = new() { Content = "Open only", Margin = new Thickness(0, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { Style = Layout.S("SectionTitle"), Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _message = new() { Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _totals = new() { Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 8, 0, 0) };
    private StockCount? _current;

    public StockCountsView()
    {
        _list.SelectionMode = DataGridSelectionMode.Single;
        _lines.SelectionMode = DataGridSelectionMode.Single;

        var entry = new WrapPanel();
        entry.Children.Add(Layout.Label("Scan or type a code"));
        entry.Children.Add(_code);
        entry.Children.Add(Layout.Label("Quantity"));
        entry.Children.Add(_quantity);
        entry.Children.Add(Layout.Button("Count it", async (_, _) => await Ui.Try(CountEnteredAsync), true));
        var options = new WrapPanel();
        options.Children.Add(_replace);
        options.Children.Add(_differences);

        var right = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
        foreach (var top in new UIElement[] { _title, entry, options, _message })
        {
            DockPanel.SetDock(top, Dock.Top);
            right.Children.Add(top);
        }
        DockPanel.SetDock(_totals, Dock.Bottom);
        right.Children.Add(_totals);
        right.Children.Add(_lines);

        Content = Layout.Page("Stock counts", out var bar, Split(_list, right));
        bar.Children.Add(Layout.Button("New count…", New_Click, true));
        bar.Children.Add(Layout.Button("Count sheet…", Sheet_Click));
        bar.Children.Add(Layout.Button("Read RFID tags…", Rfid_Click));
        bar.Children.Add(Layout.Button("Import CSV…", Import_Click));
        bar.Children.Add(Layout.Button("Change counted…", Change_Click));
        bar.Children.Add(Layout.Button("Remove line", Remove_Click));
        bar.Children.Add(Layout.Button("Variance report", Variance_Click));
        bar.Children.Add(Layout.Button("Post count…", Post_Click));
        bar.Children.Add(Layout.Button("Cancel count", CancelCount_Click));
        bar.Children.Add(_openOnly);

        _list.SelectionChanged += async (_, _) => await Ui.Try(LoadCurrentAsync);
        _openOnly.Click += async (_, _) => await Ui.Try(RefreshAsync);
        _differences.Click += (_, _) => ShowLines();
        _lines.MouseDoubleClick += Change_Click;
        _code.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await Ui.Try(CountEnteredAsync);
        };
        _message.Text = "Start a count, then scan each item (or type its code and press Enter). Expected stock is taken when an item is first counted, "
                        + "so the shop can keep selling while it counts.";
        RouteScans(this, _code);
    }

    /// <summary>
    /// A barcode scanner types like a keyboard: whatever is typed while no text box has the focus goes into the code box,
    /// so a scan works right after a click on the list or a button (as at the Point of Sale).
    /// </summary>
    internal static void RouteScans(UserControl view, TextBox box)
    {
        void Route(object sender, TextCompositionEventArgs e)
        {
            if (!view.IsVisible || e.Text.Length == 0 || char.IsControl(e.Text[0]) || Keyboard.FocusedElement is TextBox) return;
            e.Handled = true;
            box.Focus();
            box.Text += e.Text;
            box.CaretIndex = box.Text.Length;
        }
        view.Loaded += (_, _) => { if (Window.GetWindow(view) is { } w) { w.PreviewTextInput -= Route; w.PreviewTextInput += Route; } };
        view.Unloaded += (_, _) => { if (Window.GetWindow(view) is { } w) w.PreviewTextInput -= Route; };
    }

    /// <summary>A list on the left and its detail on the right, sharing the width two to three.</summary>
    internal static Grid Split(UIElement left, UIElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 340 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 420 });
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    public async Task RefreshAsync()
    {
        var selected = (_list.SelectedItem as StockCountRow)?.Id ?? _current?.Id;
        var rows = await AppServices.StockCounts.ListAsync(openOnly: _openOnly.IsChecked == true);
        _list.ItemsSource = rows;
        _list.SelectedItem = rows.FirstOrDefault(r => r.Id == selected) ?? rows.FirstOrDefault();
        if (_list.SelectedItem is null) await LoadCurrentAsync();
    }

    private async Task LoadCurrentAsync()
    {
        _current = _list.SelectedItem is StockCountRow row ? await AppServices.StockCounts.GetAsync(row.Id) : null;
        ShowLines();
    }

    private void ShowLines()
    {
        if (_current is null)
        {
            _title.Text = "No count chosen";
            _lines.ItemsSource = null;
            _totals.Text = "";
            return;
        }
        var c = _current;
        _title.Text = $"Count {c.Number} · {c.Location?.Code} · {c.Status}" + (string.IsNullOrWhiteSpace(c.Note) ? "" : " · " + c.Note);
        var lines = _differences.IsChecked == true ? c.Lines.Where(l => l.Variance != 0).ToList() : c.Lines;
        _lines.ItemsSource = lines;
        var diff = c.Lines.Where(l => l.Variance != 0).ToList();
        _totals.Text = $"{c.Lines.Count} items counted · {c.Lines.Sum(l => l.Counted):0.###} pieces · {diff.Count} with a difference · "
                       + $"value of the differences {c.Lines.Sum(l => l.VarianceCost).ToString("N2", CultureInfo.CurrentCulture)}"
                       + (c.Status == StockCountStatus.Posted ? $" · posted {c.PostedAt:g} by {c.PostedBy}" : "");
    }

    private StockCount? OpenCount()
    {
        if (_current is { Status: StockCountStatus.Open } count) return count;
        Ui.Info(_current is null ? "Start a count with New count first." : $"Count {_current.Number} is {_current.Status.ToString().ToLowerInvariant()}. Start a new count.");
        return null;
    }

    private async Task ReloadAsync(Guid? selectLine = null)
    {
        await RefreshAsync();
        if (selectLine is Guid id && _lines.ItemsSource is IEnumerable<StockCountLine> lines)
        {
            _lines.SelectedItem = lines.FirstOrDefault(l => l.Id == id);
            if (_lines.SelectedItem is not null) _lines.ScrollIntoView(_lines.SelectedItem);
        }
    }

    /// <summary>Counts the item whose code was scanned or typed (a matrix item asks which of its lines).</summary>
    public async Task CountEnteredAsync()
    {
        if (OpenCount() is not { } count) return;
        var code = _code.Text.Trim();
        if (code.Length == 0) return;
        if (!decimal.TryParse(_quantity.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity))
        {
            Ui.Error("Type the quantity in numbers, for example 12.");
            return;
        }
        var (item, variant) = await AppServices.Inventory.FindByCodeAsync(count.LocationId, code);
        if (item is null)
        {
            _message.Text = $"No item has the code {code}.";
            _message.Foreground = Look.Brush("Danger");
            System.Media.SystemSounds.Beep.Play();
            _code.SelectAll();
            return;
        }
        if (item.ItemType == ItemType.Matrix && variant is null && item.Variants.Count > 0)
        {
            var options = item.Variants.OrderBy(v => v.Ordinal).ToList();
            int pick = Ui.Choose($"Which {item.Name}?", options.Select(v => $"{v.Value1} {v.Value2} {v.Value3}".Trim()).ToList(), "Count");
            if (pick < 0) return;
            variant = options[pick];
        }
        var line = await AppServices.StockCounts.CountItemAsync(count.Id, item.Id, variant?.Id, quantity, _replace.IsChecked == true);
        _message.Text = $"{line.Description}: {line.Counted:0.###} counted" + (line.Variance == 0 ? ", as expected." : $" (expected {line.Expected:0.###}).");
        _message.Foreground = Look.Brush("Muted");
        _code.Clear();
        _quantity.Text = "1";
        _code.Focus();
        await ReloadAsync(line.Id);
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        var locations = AppServices.Locations;
        var location = locations.FirstOrDefault(l => l.Id == AppServices.Session.LocationId) ?? locations.FirstOrDefault();
        if (locations.Count > 1)
        {
            int pick = Ui.Choose("Count which location?", locations.Select(l => $"{l.Code}  {l.Name}").ToList(), "New count");
            if (pick < 0) return;
            location = locations[pick];
        }
        if (location is null) return;
        var note = Ui.Prompt("A note for this count (for example the shelves or department counted):", "New count");
        if (note is null) return;
        await Ui.Try(async () =>
        {
            var count = await AppServices.StockCounts.StartAsync(location.Id, note, AppServices.Session.UserName);
            _current = null;
            await RefreshAsync();
            _list.SelectedItem = ((IEnumerable<StockCountRow>)_list.ItemsSource).FirstOrDefault(r => r.Id == count.Id);
            _code.Focus();
        });
    }

    private async void Sheet_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) { Ui.Info("Choose a count first."); return; }
        int how = Ui.Choose("Count sheet", ["With the on-hand count of each item", "Blind: without on-hand counts"], "Count sheet");
        if (how < 0) return;
        await Ui.Try(async () => new ReportWindow(await AppServices.Reports.CountSheetAsync(_current.Id, how == 0)).ShowDialog());
    }

    private async void Rfid_Click(object sender, RoutedEventArgs e)
    {
        if (OpenCount() is not { } count) return;
        var window = new RfidCountWindow($"Count {count.Number}") { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true || window.Tags.Count == 0) return;
        await Ui.Try(async () =>
        {
            var result = await AppServices.StockCounts.CountTagsAsync(count.Id, window.Tags);
            Ui.Info($"{result.Counted:0} piece(s) counted from {window.Tags.Count} tag(s)."
                    + (result.AlreadyCounted > 0 ? $" {result.AlreadyCounted} tag(s) were counted before." : "")
                    + (result.NotFound.Count > 0 ? $"\n\n{result.NotFound.Count} tag(s) belong to no item:\n" + string.Join("\n", result.NotFound.Take(10)) : ""));
            await ReloadAsync();
        });
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (OpenCount() is not { } count) return;
        var file = Ui.OpenFile("CSV file|*.csv|Text file|*.txt|All files|*.*");
        if (file is null) return;
        await Ui.Try(async () =>
        {
            var rows = Integrations.Csv.Parse(await File.ReadAllTextAsync(file));
            var result = await AppServices.StockCounts.ImportAsync(count.Id, rows, _replace.IsChecked == true);
            Ui.Info($"{result.Counted:0.###} piece(s) counted from the file (a stock code or barcode and a quantity per row)."
                    + (result.NotFound.Count > 0 ? $"\n\nNot counted:\n" + string.Join("\n", result.NotFound.Take(15)) + (result.NotFound.Count > 15 ? $"\n… and {result.NotFound.Count - 15} more" : "") : ""));
            await ReloadAsync();
        });
    }

    private async void Change_Click(object sender, RoutedEventArgs e)
    {
        if (OpenCount() is null || _lines.SelectedItem is not StockCountLine line) return;
        var counted = Ui.PromptDecimal($"Counted quantity of {line.Description}:", "Change counted", line.Counted);
        if (counted is null) return;
        await Ui.Try(async () => { await AppServices.StockCounts.SetCountedAsync(line.Id, counted.Value); await ReloadAsync(line.Id); });
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (OpenCount() is null || _lines.SelectedItem is not StockCountLine line) return;
        if (!Ui.Confirm($"Take {line.Description} off this count? It will not be changed when the count is posted.")) return;
        await Ui.Try(async () => { await AppServices.StockCounts.RemoveLineAsync(line.Id); await ReloadAsync(); });
    }

    private async void Variance_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) { Ui.Info("Choose a count first."); return; }
        await Ui.Try(async () => new ReportWindow(await AppServices.Reports.CountVarianceAsync(_current.Id, differencesOnly: true)).ShowDialog());
    }

    private async void Post_Click(object sender, RoutedEventArgs e)
    {
        if (OpenCount() is not { } count) return;
        int changed = count.Lines.Count(l => l.Variance != 0);
        int how = Ui.Choose($"Post count {count.Number}: {changed} item(s) change, by {count.Lines.Sum(l => l.VarianceCost):N2} at cost. Items that were not counted:",
            ["Leave them as they are", "Set them to zero (a full count of the location)"], "Post count");
        if (how < 0) return;
        if (how == 1 && !Ui.Confirm("Every item of this location that was not counted will be set to zero. Post the count?")) return;
        await Ui.Try(async () =>
        {
            var posted = await AppServices.StockCounts.PostAsync(count.Id, how == 1, AppServices.Session.UserName);
            Ui.Info($"Count {count.Number} posted: {posted.ItemsChanged} item(s) changed by {posted.UnitsChanged:0.###} piece(s), "
                    + $"{posted.VarianceCost:N2} at cost." + (posted.SetToZero > 0 ? $" {posted.SetToZero} item(s) not counted were set to zero." : ""));
            await ReloadAsync();
        });
    }

    private async void CancelCount_Click(object sender, RoutedEventArgs e)
    {
        if (OpenCount() is not { } count || !Ui.Confirm($"Cancel count {count.Number}? Nothing it counted changes the stock.")) return;
        await Ui.Try(async () => { await AppServices.StockCounts.CancelAsync(count.Id, AppServices.Session.UserName); await ReloadAsync(); });
    }
}

/// <summary>
/// Transfers of stock to another location of this store or to another store: make a list of items, ship it (the stock
/// leaves), and receive it at the other end, entering what arrived.
/// </summary>
public sealed class TransfersView : UserControl, IRefreshable
{
    /// <summary>A transfer line on screen, with what arrived (editable while the transfer is in transit).</summary>
    public sealed class LineView(StockTransferLine line)
    {
        public StockTransferLine Line { get; } = line;
        public int LineNumber => Line.LineNumber;
        public string Plu => Line.Plu;
        public string Description => Line.Description;
        public decimal Shipped => Line.QuantityShipped;
        public decimal Arrived { get; set; } = line.QuantityReceived;
        public string Note => Line.CreatedAtDestination ? "New item at the receiving location" : "";
    }

    private readonly DataGrid _list = Layout.Grid(
        Layout.Col("Transfer", nameof(TransferRow.Number), 95),
        Layout.Col("From", nameof(TransferRow.From), 90),
        Layout.Col("To", nameof(TransferRow.To), 90),
        Layout.Col("Status", nameof(TransferRow.Status), 75),
        Layout.Col("Created", nameof(TransferRow.CreatedAt), 110, "g"),
        Layout.Col("Pieces", nameof(TransferRow.Shipped), 60, "0.###", true));
    private readonly DataGrid _lines = new() { Style = Layout.S("Grid"), IsReadOnly = false, SelectionMode = DataGridSelectionMode.Single };
    private readonly DataGridTextColumn _arrivedColumn;
    private readonly TextBox _code = new() { Width = 240, Margin = new Thickness(0, 0, 8, 6), FontSize = 19 };
    private readonly TextBox _quantity = new() { Width = 90, Margin = new Thickness(0, 0, 8, 6), Text = "1", FontSize = 19, HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly CheckBox _openOnly = new() { Content = "Open only", IsChecked = true, Margin = new Thickness(0, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { Style = Layout.S("SectionTitle"), Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _message = new() { Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private StockTransfer? _current;
    private TransferRow? _row;

    public TransfersView()
    {
        _list.SelectionMode = DataGridSelectionMode.Single;
        void Ro(string header, string path, double width, string? format = null, bool right = false)
        {
            var c = Layout.Col(header, path, width, format, right);
            c.IsReadOnly = true;
            _lines.Columns.Add(c);
        }
        Ro("Line", nameof(LineView.LineNumber), 45);
        Ro("Stock code", nameof(LineView.Plu), 115);
        Ro("Description", nameof(LineView.Description), -1);
        Ro("Sent", nameof(LineView.Shipped), 70, "0.###", true);
        _arrivedColumn = new DataGridTextColumn
        {
            Header = "Arrived", Width = 90,
            Binding = new System.Windows.Data.Binding(nameof(LineView.Arrived)) { StringFormat = "0.###", UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus },
        };
        _lines.Columns.Add(_arrivedColumn);
        Ro("", nameof(LineView.Note), 230);

        var entry = new WrapPanel();
        entry.Children.Add(Layout.Label("Scan or type a code"));
        entry.Children.Add(_code);
        entry.Children.Add(Layout.Label("Quantity"));
        entry.Children.Add(_quantity);
        entry.Children.Add(Layout.Button("Add item", async (_, _) => await Ui.Try(AddEnteredAsync), true));

        var right = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
        foreach (var top in new UIElement[] { _title, entry, _message })
        {
            DockPanel.SetDock(top, Dock.Top);
            right.Children.Add(top);
        }
        right.Children.Add(_lines);
        Content = Layout.Page("Transfers", out var bar, StockCountsView.Split(_list, right));
        bar.Children.Add(Layout.Button("New transfer…", New_Click, true));
        bar.Children.Add(Layout.Button("Find item…", Find_Click));
        bar.Children.Add(Layout.Button("Change quantity…", Quantity_Click));
        bar.Children.Add(Layout.Button("Remove line", Remove_Click));
        bar.Children.Add(Layout.Button("Ship", Ship_Click));
        bar.Children.Add(Layout.Button("Receive", Receive_Click));
        bar.Children.Add(Layout.Button("Print packing list", Print_Click));
        bar.Children.Add(Layout.Button("Cancel transfer", CancelTransfer_Click));
        bar.Children.Add(_openOnly);

        _list.SelectionChanged += async (_, _) => await Ui.Try(LoadCurrentAsync);
        _openOnly.Click += async (_, _) => await Ui.Try(RefreshAsync);
        _code.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await Ui.Try(AddEnteredAsync);
        };
        StockCountsView.RouteScans(this, _code);
    }

    public async Task RefreshAsync()
    {
        var selected = (_list.SelectedItem as TransferRow)?.Id ?? _current?.Id;
        var rows = await AppServices.Transfers.ListAsync(_openOnly.IsChecked == true);
        _list.ItemsSource = rows;
        _list.SelectedItem = rows.FirstOrDefault(r => r.Id == selected) ?? rows.FirstOrDefault();
        if (_list.SelectedItem is null) await LoadCurrentAsync();
    }

    private async Task LoadCurrentAsync()
    {
        _row = _list.SelectedItem as TransferRow;
        _current = _row is null ? null : await AppServices.Transfers.GetAsync(_row.Id);
        if (_current is null)
        {
            _title.Text = "No transfer chosen";
            _lines.ItemsSource = null;
            _message.Text = "A transfer sends stock to another location or store. Make one with New transfer, add the items, then Ship. "
                            + "The receiving store sees it after the next synchronisation and receives it there.";
            return;
        }
        var t = _current;
        bool inTransit = t.Status == StockTransferStatus.Shipped;
        var lines = t.Lines.Select(l => new LineView(l)).ToList();
        if (inTransit) foreach (var l in lines) l.Arrived = l.Shipped;
        _lines.ItemsSource = lines;
        _arrivedColumn.IsReadOnly = !inTransit;
        _arrivedColumn.Visibility = t.Status == StockTransferStatus.Draft || t.Status == StockTransferStatus.Cancelled ? Visibility.Collapsed : Visibility.Visible;
        _title.Text = $"Transfer {t.Number} · {_row!.From} → {_row.To} · {t.Status}" + (string.IsNullOrWhiteSpace(t.Note) ? "" : " · " + t.Note);
        _message.Text = t.Status switch
        {
            StockTransferStatus.Draft => "Add the items to send, then Ship: their stock leaves this location.",
            StockTransferStatus.Shipped => $"In transit since {t.ShippedAt:g}. When it arrives, enter what arrived in the Arrived column, then Receive.",
            StockTransferStatus.Received => $"Received {t.ReceivedAt:g} by {t.ReceivedBy}: {t.Lines.Sum(l => l.QuantityReceived):0.###} of {t.Lines.Sum(l => l.QuantityShipped):0.###} piece(s).",
            _ => $"Cancelled {t.CancelledAt:g}. Nothing was sent.",
        };
    }

    private StockTransfer? Draft()
    {
        if (_current is { Status: StockTransferStatus.Draft } t) return t;
        Ui.Info(_current is null ? "Make a transfer with New transfer first." : $"Transfer {_current.Number} is {_current.Status.ToString().ToLowerInvariant()}; it cannot be changed.");
        return null;
    }

    private async Task AddEnteredAsync()
    {
        if (Draft() is not { } t) return;
        var code = _code.Text.Trim();
        if (code.Length == 0) return;
        if (!decimal.TryParse(_quantity.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity))
        {
            Ui.Error("Type the quantity in numbers, for example 6.");
            return;
        }
        var line = await AppServices.Transfers.AddItemAsync(t.Id, code, quantity);
        _code.Clear();
        _quantity.Text = "1";
        _code.Focus();
        await RefreshAsync();
        _lines.SelectedItem = (_lines.ItemsSource as IEnumerable<LineView>)?.FirstOrDefault(l => l.Line.Id == line.Id);
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        var from = AppServices.Locations.FirstOrDefault(l => l.Id == AppServices.Session.LocationId) ?? AppServices.Locations.FirstOrDefault();
        if (from is null) return;
        List<TransferDestination> destinations = [];
        if (!await Ui.Try(async () => destinations = (await AppServices.Transfers.DestinationsAsync()).Where(d => d.LocationId != from.Id).ToList())) return;
        if (destinations.Count == 0)
        {
            Ui.Info("There is nowhere to send stock to yet: add a second location in Setup › Lists. Other stores' locations come with the synchronisation.");
            return;
        }
        int pick = Ui.Choose($"Send stock from {from.Code} to:", destinations.Select(d => d.Text).ToList(), "New transfer");
        if (pick < 0) return;
        var note = Ui.Prompt("A note for the receiving store (optional):", "New transfer");
        if (note is null) return;
        await Ui.Try(async () =>
        {
            var t = await AppServices.Transfers.CreateAsync(from.Id, destinations[pick].LocationId, note, AppServices.Session.UserName);
            _current = null;
            await RefreshAsync();
            _list.SelectedItem = ((IEnumerable<TransferRow>)_list.ItemsSource).FirstOrDefault(r => r.Id == t.Id);
            _code.Focus();
        });
    }

    private async void Find_Click(object sender, RoutedEventArgs e)
    {
        if (Draft() is null) return;
        var picker = new ItemPickerWindow();
        if (picker.ShowDialog() != true || picker.Selected is not { } item) return;
        _code.Text = item.Plu;
        await Ui.Try(AddEnteredAsync);
    }

    private async void Quantity_Click(object sender, RoutedEventArgs e)
    {
        if (Draft() is null || _lines.SelectedItem is not LineView line) return;
        var qty = Ui.PromptDecimal($"Quantity of {line.Description} to send:", "Change quantity", line.Shipped);
        if (qty is null) return;
        await Ui.Try(async () => { await AppServices.Transfers.SetQuantityAsync(line.Line.Id, qty.Value); await RefreshAsync(); });
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Draft() is null || _lines.SelectedItem is not LineView line) return;
        await Ui.Try(async () => { await AppServices.Transfers.RemoveLineAsync(line.Line.Id); await RefreshAsync(); });
    }

    private async void Ship_Click(object sender, RoutedEventArgs e)
    {
        if (Draft() is not { } t) return;
        if (!Ui.Confirm($"Ship transfer {t.Number} ({t.Lines.Sum(l => l.QuantityShipped):0.###} piece(s)) to {_row?.To}? The stock leaves this location now.")) return;
        await Ui.Try(async () =>
        {
            await AppServices.Transfers.ShipAsync(t.Id, AppServices.Session.UserName);
            await RefreshAsync();
            ((MainWindow)Window.GetWindow(this)).SetStatus($"Transfer {t.Number} shipped.");
        });
    }

    private async void Receive_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { Status: StockTransferStatus.Shipped } t)
        {
            Ui.Info("Choose a transfer in transit (shipped) to receive it.");
            return;
        }
        _lines.CommitEdit(DataGridEditingUnit.Row, true);
        var lines = (_lines.ItemsSource as IEnumerable<LineView>)?.ToList() ?? [];
        if (lines.Any(l => l.Arrived < 0)) { Ui.Error("A quantity that arrived cannot be below zero."); return; }
        int differ = lines.Count(l => l.Arrived != l.Shipped);
        if (!Ui.Confirm($"Receive transfer {t.Number} into {_row?.To}?" + (differ > 0 ? $" {differ} line(s) arrived short or over; that is kept on the transfer." : ""))) return;
        await Ui.Try(async () =>
        {
            var receipt = await AppServices.Transfers.ReceiveAsync(t.Id, lines.ToDictionary(l => l.Line.Id, l => l.Arrived), AppServices.Session.UserName);
            var text = $"Transfer {t.Number} received: {receipt.Received:0.###} piece(s).";
            if (receipt.CreatedItems.Count > 0) text += "\n\nThese items were new at the receiving location and were created from the sending store's details:\n" + string.Join("\n", receipt.CreatedItems);
            if (receipt.ShortLines.Count > 0) text += "\n\nShort:\n" + string.Join("\n", receipt.ShortLines);
            if (receipt.OverLines.Count > 0) text += "\n\nOver:\n" + string.Join("\n", receipt.OverLines);
            Ui.Info(text);
            await RefreshAsync();
        });
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { } t) return;
        var report = new ReportTable
        {
            Title = $"Transfer {t.Number}",
            Subtitle = $"{_row?.From} to {_row?.To} · {t.Status}" + (t.ShippedAt is DateTime s ? $" · shipped {s:g}" : "") + (string.IsNullOrWhiteSpace(t.Note) ? "" : " · " + t.Note),
            Columns = [new("Line", ColumnKind.Count), new("Stock code"), new("Barcode"), new("Description"), new("Size"), new("Sent", ColumnKind.Quantity, true),
                new("Arrived", ColumnKind.Quantity, true)],
            Rows = t.Lines.Select(l => new object?[] { l.LineNumber, l.Plu, l.Barcode, l.Description, l.Size, l.QuantityShipped,
                t.Status == StockTransferStatus.Received ? l.QuantityReceived : null }).ToList(),
        };
        new ReportWindow(report).ShowDialog();
    }

    private async void CancelTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (Draft() is not { } t || !Ui.Confirm($"Cancel transfer {t.Number}? Nothing was sent yet.")) return;
        await Ui.Try(async () => { await AppServices.Transfers.CancelAsync(t.Id); await RefreshAsync(); });
    }
}
