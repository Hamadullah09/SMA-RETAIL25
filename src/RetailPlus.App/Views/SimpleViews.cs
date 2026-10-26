using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;
using RetailPlus.Reports;

namespace RetailPlus.App.Views;

/// <summary>Layout helpers for the screens built in code.</summary>
internal static class Layout
{
    public static Style S(string key) => (Style)Application.Current.FindResource(key);

    public static Button Button(string text, RoutedEventHandler click, bool accent = false)
    {
        var b = new Button { Content = text, Style = S(accent ? "AccentButton" : "ToolButton") };
        Look.SetIcon(b, IconFor(text));
        b.Click += click;
        return b;
    }

    /// <summary>
    /// Gives every text button under <paramref name="root"/> the icon and the keyboard shortcut for its text, unless it
    /// has its own (Save and Cancel buttons keep Enter and Esc).
    /// </summary>
    public static void AddIcons(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button { Content: string text } b)
            {
                if (Look.GetIcon(b) is null) Look.SetIcon(b, IconFor(text));
                if (Look.GetShortcut(b) is null && !b.IsDefault && !b.IsCancel) Look.SetShortcut(b, ShortcutFor(text));
            }
            AddIcons(child);
        }
    }

    /// <summary>The usual key for a button's action, so the same action has the same key on every screen.</summary>
    public static string? ShortcutFor(string text)
    {
        var t = text.ToUpperInvariant();
        (string Start, string Key)[] map =
        [
            ("NEW", "Ctrl+N"), ("EDIT", "F2"), ("VIEW", "F2"), ("OPEN", "F2"), ("DELETE", "Del"), ("UNDELETE", "Ctrl+U"),
            ("PRINT", "Ctrl+P"), ("SAVE", "Ctrl+S"), ("EXPORT", "Ctrl+E"), ("IMPORT", "Ctrl+I"), ("SHOW", "F5"), ("RUN", "F5"),
            ("REFRESH", "F5"), ("FIND", "F3"), ("TAKE PAYMENT", "F12"), ("FLAG", "Ctrl+G"),
        ];
        foreach (var (start, key) in map)
            if (t.StartsWith(start, StringComparison.Ordinal)) return key;
        return null;
    }

    /// <summary>An icon for a button, chosen from the first word(s) of its text so that the same action looks the same everywhere.</summary>
    public static string? IconFor(string text)
    {
        var t = text.ToUpperInvariant();
        (string Start, string Glyph)[] map =
        [
            ("NEW", ""), ("ADD", ""), ("EDIT", ""), ("DUPLICATE", ""), ("DELETE", ""), ("UNDELETE", ""),
            ("REMOVE", ""), ("SHOW", ""), ("RUN", ""), ("PRINT", ""), ("SAVE", ""), ("EXPORT", ""),
            ("IMPORT", ""), ("TAKE PAYMENT", ""), ("DISTRIBUTE", ""), ("SELL", ""), ("STATEMENT", ""),
            ("LATE", ""), ("VOID", ""), ("VIEW", ""), ("POST ORDER", ""), ("POST SHIPMENT", ""),
            ("RECEIVE", ""), ("TODAY", ""), ("THIS", ""), ("MARK", ""), ("CLOSE", ""), ("FLAG", ""),
            ("CLEAR", ""), ("CHANGE PRICES", ""), ("COPY", ""), ("ADJUST", ""), ("BACK UP", ""),
            ("CANCEL", ""), ("TEST", ""), ("CHANGE LEVEL", ""), ("SET PASSWORD", ""), ("RESTORE", ""), ("REBUILD", ""), ("YEAR", ""), ("IMPORT…", ""),
        ];
        foreach (var (start, glyph) in map)
            if (t.StartsWith(start, StringComparison.Ordinal)) return glyph;
        return null;
    }

    public static DataGridTextColumn Col(string header, string path, double width = -1, string? format = null, bool right = false)
    {
        var c = new DataGridTextColumn
        {
            Header = header, Binding = new Binding(path) { StringFormat = format },
            // Widths were chosen for 14 px text; the larger text needs about a fifth more room.
            Width = width < 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(Math.Round(width * 1.2)),
        };
        if (right) c.ElementStyle = S("Right");
        return c;
    }

    public static DataGrid Grid(params DataGridColumn[] columns)
    {
        var g = new DataGrid { Style = S("Grid") };
        foreach (var c in columns) g.Columns.Add(c);
        return g;
    }

    public static DockPanel Page(string title, out WrapPanel toolbar, UIElement body, UIElement? right = null)
    {
        var root = new DockPanel();
        var t = new TextBlock { Text = title, Style = S("PageTitle") };
        DockPanel.SetDock(t, Dock.Top);
        root.Children.Add(t);
        toolbar = new WrapPanel { Style = S("Toolbar") };
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        if (right is not null)
        {
            DockPanel.SetDock(right, Dock.Right);
            root.Children.Add(right);
        }
        root.Children.Add(body);
        return root;
    }

    public static TextBlock Label(string text) => new() { Text = text, Style = S("FieldLabel") };
}

/// <summary>The sales journal: every sale with its receipt, reprint and void.</summary>
public sealed class SalesJournalView : UserControl, IRefreshable
{
    private readonly DatePicker _from = new() { SelectedDate = DateTime.Today.AddDays(-7), Margin = new Thickness(0, 0, 8, 6) };
    private readonly DatePicker _to = new() { SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 6) };
    private readonly CheckBox _thisStation = new() { Content = "This station only", Margin = new Thickness(0, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _summary = new() { Margin = new Thickness(0, 8, 0, 0), Foreground = Brushes.Gray };
    private readonly DataGrid _grid = Layout.Grid(
        Layout.Col("Invoice", nameof(SaleRow.InvoiceNumber), 90),
        Layout.Col("Date", nameof(SaleRow.SoldAt), 150, "g"),
        Layout.Col("Station", nameof(SaleRow.StationNumber), 70),
        Layout.Col("Staff", nameof(SaleRow.Staff), 110),
        Layout.Col("Client", nameof(SaleRow.ClientName)),
        Layout.Col("Paid by", nameof(SaleRow.Tenders), 150),
        Layout.Col("Subtotal", nameof(SaleRow.Subtotal), 90, "N2", true),
        Layout.Col("Tax", nameof(SaleRow.Tax1), 80, "N2", true),
        Layout.Col("Total", nameof(SaleRow.Total), 100, "N2", true),
        Layout.Col("Status", nameof(SaleRow.Status), 90));

    public SalesJournalView()
    {
        var body = new DockPanel();
        DockPanel.SetDock(_summary, Dock.Bottom);
        body.Children.Add(_summary);
        body.Children.Add(_grid);
        Content = Layout.Page("Sales Journal", out var bar, body);
        bar.Children.Add(Layout.Label("From"));
        bar.Children.Add(_from);
        bar.Children.Add(Layout.Label("To"));
        bar.Children.Add(_to);
        bar.Children.Add(_thisStation);
        bar.Children.Add(Layout.Button("Show", async (_, _) => await Ui.Try(RefreshAsync), true));
        bar.Children.Add(Layout.Button("View receipt", View_Click));
        bar.Children.Add(Layout.Button("Void sale", Void_Click));
        _grid.MouseDoubleClick += View_Click;
    }

    public async Task RefreshAsync()
    {
        var from = DateOnly.FromDateTime(_from.SelectedDate ?? DateTime.Today);
        var to = DateOnly.FromDateTime(_to.SelectedDate ?? DateTime.Today);
        var rows = await AppServices.Sales.JournalAsync(from, to, _thisStation.IsChecked == true ? AppServices.Session.StationNumber : null);
        _grid.ItemsSource = rows;
        var done = rows.Where(r => r.Status == SaleStatus.Completed).ToList();
        _summary.Text = $"{done.Count} sales · total {done.Sum(r => r.Total):N2} · {rows.Count - done.Count} voided";
    }

    private async void View_Click(object sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not SaleRow row) return;
        await Ui.Try(async () =>
        {
            var sale = await AppServices.Sales.GetSaleAsync(row.Id);
            if (sale is null) return;
            new ReceiptWindow(await ReceiptWindow.ForSaleAsync(sale, "COPY"), $"Invoice {sale.InvoiceNumber}").ShowDialog();
        });
    }

    private async void Void_Click(object sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not SaleRow row || row.Status == SaleStatus.Voided) return;
        if (!await LoginWindow.SupervisorApprovesAsync("Voiding a sale")) return;
        var reason = Ui.Prompt($"Void invoice {row.InvoiceNumber} ({row.Total:N2})? Reason:", "Void sale");
        if (reason is null) return;
        await Ui.Try(async () => { await AppServices.Sales.VoidSaleAsync(row.Id, AppServices.Session, reason); await RefreshAsync(); });
    }
}

/// <summary>Accounts receivable: invoices, quotes and layaways with payments, late charges and statements.</summary>
public sealed class InvoicesView : UserControl, IRefreshable
{
    private readonly ComboBox _kind = new() { Width = 150, Margin = new Thickness(0, 0, 8, 6) };
    private readonly CheckBox _open = new() { Content = "Open only", IsChecked = true, Margin = new Thickness(0, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _search = new() { Width = 220, Margin = new Thickness(0, 0, 8, 6) };
    private readonly DataGrid _grid = Layout.Grid(
        Layout.Col("Number", nameof(InvoiceRow.InvoiceNumber), 90),
        Layout.Col("Kind", nameof(InvoiceRow.Kind), 80),
        Layout.Col("Date", nameof(InvoiceRow.InvoiceDate), 100),
        Layout.Col("Client", nameof(InvoiceRow.Name)),
        Layout.Col("Account", nameof(InvoiceRow.AccountNumber), 90),
        Layout.Col("Total", nameof(InvoiceRow.Total), 100, "N2", true),
        Layout.Col("Late charge", nameof(InvoiceRow.Penalty), 90, "N2", true),
        Layout.Col("Balance", nameof(InvoiceRow.Balance), 100, "N2", true),
        Layout.Col("Last paid", nameof(InvoiceRow.LastPaidDate), 100));
    private readonly TextBox _detail = new() { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), Width = 380, Margin = new Thickness(12, 0, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    public InvoicesView()
    {
        Content = Layout.Page("Invoices & A/R", out var bar, _grid, _detail);
        _kind.ItemsSource = new[] { "All", "Invoices", "Quotes", "Layaways" };
        _kind.SelectedIndex = 0;
        bar.Children.Add(_kind);
        bar.Children.Add(_open);
        bar.Children.Add(_search);
        bar.Children.Add(Layout.Button("Show", async (_, _) => await Ui.Try(RefreshAsync), true));
        bar.Children.Add(Layout.Button("Take payment", Pay_Click));
        bar.Children.Add(Layout.Button("Distribute payment", Distribute_Click));
        bar.Children.Add(Layout.Button("Sell quote / layaway", Sell_Click));
        bar.Children.Add(Layout.Button("Statement", Statement_Click));
        bar.Children.Add(Layout.Button("Late charges", LateCharges_Click));
        bar.Children.Add(Layout.Button("Void", Void_Click));
        _kind.SelectionChanged += async (_, _) => { if (IsLoaded) await Ui.Try(RefreshAsync); };
        _open.Click += async (_, _) => await Ui.Try(RefreshAsync);
        _search.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await Ui.Try(RefreshAsync); };
        _grid.SelectionChanged += async (_, _) => await ShowDetailAsync();
    }

    private InvoiceKind? Kind => _kind.SelectedIndex switch { 1 => InvoiceKind.Invoice, 2 => InvoiceKind.Quote, 3 => InvoiceKind.Layaway, _ => null };
    private InvoiceRow? Current => _grid.SelectedItem as InvoiceRow;

    public async Task RefreshAsync() => _grid.ItemsSource = await AppServices.Invoices.ListAsync(Kind, _open.IsChecked == true, _search.Text);

    private async Task ShowDetailAsync()
    {
        if (Current is not { } row) { _detail.Text = ""; return; }
        var inv = await AppServices.Invoices.GetAsync(row.Id);
        if (inv is null) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{inv.Kind} {inv.InvoiceNumber}   {inv.InvoiceDate:d}");
        sb.AppendLine(inv.DisplayName);
        sb.AppendLine(new string('-', 44));
        foreach (var l in inv.Lines) sb.AppendLine($"{Trim(l.Description, 24),-24} {l.Quantity,5:0.##} {l.LineTotal,12:N2}");
        if (inv.Lines.Count == 0 && !string.IsNullOrWhiteSpace(inv.Notes)) sb.AppendLine(inv.Notes);
        sb.AppendLine(new string('-', 44));
        sb.AppendLine($"{"Total",-30} {inv.Total,13:N2}");
        if (inv.Penalty != 0) sb.AppendLine($"{"Late charges",-30} {inv.Penalty,13:N2}");
        foreach (var p in inv.Payments) sb.AppendLine($"{p.PaidOn:d} {Trim(p.TenderType, 14),-14} {-p.Amount,18:N2}");
        sb.AppendLine($"{"Balance",-30} {inv.Balance,13:N2}");
        if (inv.IsVoided) sb.AppendLine("*** VOIDED ***");
        _detail.Text = sb.ToString();
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n];

    private async void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) return;
        var tender = await PickTenderAsync();
        if (tender is null) return;
        var amount = Ui.PromptDecimal($"Payment on {row.Kind} {row.InvoiceNumber} (balance {row.Balance:N2}):", "Take payment", row.Balance);
        if (amount is null) return;
        await Ui.Try(async () =>
        {
            await AppServices.Invoices.ApplyPaymentAsync(row.Id, amount.Value, tender, DateOnly.FromDateTime(DateTime.Today), AppServices.Session);
            PosViewModel.OpenDrawer();
            await RefreshAsync();
        });
    }

    private static async Task<string?> PickTenderAsync()
    {
        var tenders = (await AppServices.Setup.GetTenderTypesAsync()).Where(t => t.Kind != TenderKind.OnAccount).ToList();
        int i = Ui.Choose("Paid by:", tenders.Select(t => t.Name).ToList(), "Payment");
        return i < 0 ? null : tenders[i].Name;
    }

    private async void Distribute_Click(object sender, RoutedEventArgs e)
    {
        int? clientId = Current?.ClientId;
        if (clientId is null)
        {
            var picker = new PartyPickerWindow();
            if (picker.ShowDialog() != true || picker.Selected is null) return;
            clientId = picker.Selected.Id;
        }
        var tender = await PickTenderAsync();
        if (tender is null) return;
        var amount = Ui.PromptDecimal("Payment amount (spread over the open invoices, oldest first):", "Distribute payment");
        if (amount is null) return;
        await Ui.Try(async () =>
        {
            var left = await AppServices.Invoices.DistributePaymentAsync(clientId.Value, amount.Value, tender, DateOnly.FromDateTime(DateTime.Today), AppServices.Session);
            if (left > 0) Ui.Info($"{left:N2} more than owing was left as a credit on the newest invoice.");
            await RefreshAsync();
        });
    }

    private async void Sell_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { Kind: InvoiceKind.Quote } row) { Ui.Info("Select a quote to sell it at the Point of Sale. Layaway payments are taken with Take payment."); return; }
        if (Window.GetWindow(this) is not MainWindow main) return;
        await Ui.Try(async () =>
        {
            var draft = await AppServices.Invoices.QuoteToDraftAsync(row.Id, AppServices.Session.LocationId);
            main.Navigate("pos");
            await MainWindow.Settle();
            if (main.GetView<PosView>() is { } pos) await pos.LoadDraftAsync(draft);
        });
    }

    private async void Statement_Click(object sender, RoutedEventArgs e)
    {
        if (Current?.ClientId is not int clientId) { Ui.Info("Select an invoice of the client."); return; }
        await Ui.Try(async () =>
        {
            var (client, open, payments) = await AppServices.Invoices.StatementAsync(clientId);
            new TextPreviewWindow("Statement", ReceiptFormatter.Statement(client, open, payments, AppServices.Settings)).ShowDialog();
        });
    }

    private async void LateCharges_Click(object sender, RoutedEventArgs e)
    {
        if (AppServices.Settings.InterestRatePerMonth <= 0) { Ui.Info("Set the monthly interest rate in Setup › Options first."); return; }
        if (!Ui.Confirm($"Add late charges at {AppServices.Settings.InterestRatePerMonth:0.###}% a month to invoices overdue more than {AppServices.Settings.GracePeriodDays} days?")) return;
        await Ui.Try(async () =>
        {
            var (count, total) = await AppServices.Invoices.ApplyLateChargesAsync(DateOnly.FromDateTime(DateTime.Today));
            Ui.Info($"Late charges of {total:N2} added to {count} invoices.");
            await RefreshAsync();
        });
    }

    private async void Void_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row || !Ui.Confirm($"Void {row.Kind} {row.InvoiceNumber}? Its balance is taken off the client's account.")) return;
        if (!await LoginWindow.SupervisorApprovesAsync("Voiding an invoice")) return;
        await Ui.Try(async () => { await AppServices.Invoices.VoidAsync(row.Id); await RefreshAsync(); });
    }
}

/// <summary>Customer orders (back orders) and RMAs.</summary>
public sealed class CustomerOrdersView : UserControl, IRefreshable
{
    private readonly DataGrid _orders = Layout.Grid(
        Layout.Col("Date", nameof(CustomerOrder.OrderDate), 100),
        Layout.Col("Name", nameof(CustomerOrder.Name), 160),
        Layout.Col("Phone", nameof(CustomerOrder.Phone), 120),
        Layout.Col("Stock code", nameof(CustomerOrder.Plu), 120),
        Layout.Col("Description", nameof(CustomerOrder.Description)),
        Layout.Col("Qty", nameof(CustomerOrder.Quantity), 60, "0.###", true),
        Layout.Col("Price", nameof(CustomerOrder.UnitPrice), 90, "N2", true),
        Layout.Col("Paid", nameof(CustomerOrder.Paid), 60));
    private readonly DataGrid _rmas = Layout.Grid(
        Layout.Col("RMA", nameof(Rma.CustomerRmaNumber), 80),
        Layout.Col("Date", nameof(Rma.CustomerRmaDate), 100),
        Layout.Col("Customer", nameof(Rma.LastName), 140),
        Layout.Col("Stock code", nameof(Rma.Plu), 120),
        Layout.Col("Description", nameof(Rma.Description)),
        Layout.Col("Qty", nameof(Rma.Quantity), 60, "0.###", true),
        Layout.Col("Supplier", nameof(Rma.Supplier), 140),
        Layout.Col("Supplier RMA", nameof(Rma.SupplierRmaNumber), 100),
        Layout.Col("Active", nameof(Rma.IsActive), 60));
    private readonly CheckBox _openOnly = new() { Content = "Open only", IsChecked = true, Margin = new Thickness(0, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };

    public CustomerOrdersView()
    {
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Customer orders", Content = _orders });
        tabs.Items.Add(new TabItem { Header = "RMAs (returns to suppliers)", Content = _rmas });
        Content = Layout.Page("Orders & RMAs", out var bar, tabs);
        bar.Children.Add(_openOnly);
        bar.Children.Add(Layout.Button("New customer order", NewOrder_Click, true));
        bar.Children.Add(Layout.Button("Mark paid / unpaid", Paid_Click));
        bar.Children.Add(Layout.Button("Delete order", DeleteOrder_Click));
        bar.Children.Add(Layout.Button("New RMA", NewRma_Click));
        bar.Children.Add(Layout.Button("Close / reopen RMA", CloseRma_Click));
        _openOnly.Click += async (_, _) => await Ui.Try(RefreshAsync);
    }

    public async Task RefreshAsync()
    {
        _orders.ItemsSource = await AppServices.Sales.GetCustomerOrdersAsync(_openOnly.IsChecked == true);
        _rmas.ItemsSource = await AppServices.Sales.GetRmasAsync(_openOnly.IsChecked == true);
    }

    private async void NewOrder_Click(object sender, RoutedEventArgs e)
    {
        var client = new PartyPickerWindow();
        if (client.ShowDialog() != true || client.Selected is not { } c) return;
        var picker = new ItemPickerWindow();
        if (picker.ShowDialog() != true || picker.Selected is not { } item) return;
        var qty = Ui.PromptDecimal($"Quantity of {item.Name} ordered:", "Customer order", 1);
        if (qty is null) return;
        var full = await AppServices.Parties.GetClientAsync(c.Id);
        await Ui.Try(async () =>
        {
            await AppServices.Sales.SaveCustomerOrderAsync(new CustomerOrder
            {
                LocationId = AppServices.Session.LocationId, ClientId = c.Id, Name = c.DisplayName.Length > 30 ? c.DisplayName[..30] : c.DisplayName,
                Company = full?.Company, Phone = full?.WorkPhone ?? full?.HomePhone, OrderDate = DateOnly.FromDateTime(DateTime.Today), SoldBy = AppServices.Session.UserName,
                CustomerNumber = c.Number, ItemId = item.Id, Plu = item.Plu, Description = item.Name, Size = item.Size, UnitPrice = item.Price1, Quantity = qty.Value,
            });
            await RefreshAsync();
        });
    }

    private async void Paid_Click(object sender, RoutedEventArgs e)
    {
        if (_orders.SelectedItem is not CustomerOrder o) return;
        o.Paid = !o.Paid;
        await Ui.Try(async () => { await AppServices.Sales.SaveCustomerOrderAsync(o); await RefreshAsync(); });
    }

    private async void DeleteOrder_Click(object sender, RoutedEventArgs e)
    {
        if (_orders.SelectedItem is not CustomerOrder o || !Ui.Confirm($"Delete the order for {o.Description}?")) return;
        await Ui.Try(async () => { await AppServices.Sales.DeleteCustomerOrderAsync(o.Id); await RefreshAsync(); });
    }

    private async void NewRma_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ItemPickerWindow();
        if (picker.ShowDialog() != true || picker.Selected is not { } item) return;
        var qty = Ui.PromptDecimal("Quantity returned:", "RMA", 1);
        if (qty is null) return;
        var customer = Ui.Prompt("Customer last name (leave empty for a return of stock to the supplier):", "RMA") ?? "";
        await Ui.Try(async () =>
        {
            await AppServices.Sales.SaveRmaAsync(new Rma
            {
                Plu = item.Plu, Description = item.Name, Size = item.Size, Quantity = qty.Value, UnitPrice = item.Price1, LastName = customer.Length > 16 ? customer[..16] : customer,
                IsCustomerRma = customer.Length > 0, CustomerRmaDate = DateOnly.FromDateTime(DateTime.Today), Supplier = item.Supplier,
                IsSupplierRma = true, SupplierRmaDate = DateOnly.FromDateTime(DateTime.Today), IsActive = true, SetDate = DateOnly.FromDateTime(DateTime.Today),
            });
            await RefreshAsync();
        });
    }

    private async void CloseRma_Click(object sender, RoutedEventArgs e)
    {
        if (_rmas.SelectedItem is not Rma r) return;
        r.IsActive = !r.IsActive;
        await Ui.Try(async () => { await AppServices.Sales.SaveRmaAsync(r); await RefreshAsync(); });
    }
}
