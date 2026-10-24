using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;
using RetailPlus.Reports;

namespace RetailPlus.App.Views;

/// <summary>The Clients and Suppliers browse screens (same layout, different columns and actions).</summary>
public abstract class PartiesView : UserControl, IRefreshable
{
    private readonly TextBox _search = new() { Width = 300, Margin = new Thickness(0, 0, 8, 6) };
    private readonly CheckBox _deleted = new() { Content = "Show deleted", Margin = new Thickness(0, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _count = new() { Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    protected DataGrid Grid { get; } = new() { Style = (Style)Application.Current.FindResource("Grid") };
    protected WrapPanel Actions { get; } = new() { Style = (Style)Application.Current.FindResource("Toolbar") };
    protected abstract bool IsClients { get; }

    protected PartiesView(string title)
    {
        var root = new DockPanel();
        var head = new DockPanel();
        DockPanel.SetDock(_count, Dock.Right);
        head.Children.Add(_count);
        head.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("PageTitle") });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        var filters = new WrapPanel { Style = (Style)Application.Current.FindResource("Toolbar") };
        filters.Children.Add(_search);
        filters.Children.Add(_deleted);
        DockPanel.SetDock(filters, Dock.Top);
        root.Children.Add(filters);
        DockPanel.SetDock(Actions, Dock.Top);
        root.Children.Add(Actions);
        root.Children.Add(Grid);
        Content = root;
        AddButton("New", New_Click, accent: true);
        AddButton("Edit", Edit_Click);
        AddButton("Delete", Delete_Click);
        _timer.Tick += async (_, _) => { _timer.Stop(); await Ui.Try(RefreshAsync); };
        _search.TextChanged += (_, _) => { _timer.Stop(); _timer.Start(); };
        _deleted.Click += async (_, _) => await Ui.Try(RefreshAsync);
        Grid.MouseDoubleClick += Edit_Click;
        Col("Number", nameof(PartyRow.Number), 90);
        Col("Name", nameof(PartyRow.DisplayName), -1);
        Col("City", nameof(PartyRow.City), 130);
        Col("Phone", nameof(PartyRow.WorkPhone), 130);
        Col("E-mail", nameof(PartyRow.Email), 200);
    }

    protected void AddButton(string text, RoutedEventHandler click, bool accent = false)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.FindResource(accent ? "AccentButton" : "ToolButton") };
        b.Click += click;
        Actions.Children.Add(b);
    }

    protected void Col(string header, string path, double width, string? format = null) => Grid.Columns.Add(new DataGridTextColumn
    {
        Header = header, Binding = new Binding(path) { StringFormat = format },
        Width = width < 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
    });

    protected PartyRow? Current => Grid.SelectedItem as PartyRow;

    public async Task RefreshAsync()
    {
        var rows = IsClients
            ? await AppServices.Parties.SearchClientsAsync(_search.Text, _deleted.IsChecked == true)
            : await AppServices.Parties.SearchSuppliersAsync(_search.Text, _deleted.IsChecked == true);
        Grid.ItemsSource = rows;
        _count.Text = $"{rows.Count:N0} shown";
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        Party p = IsClients ? new Client { Country = AppServices.Settings.Country } : new Supplier();
        if (IsClients && AppServices.Settings.CarryOverCityStateZip)
        {
            p.City = AppServices.Settings.City;
            p.State = AppServices.Settings.State;
            p.Zip = AppServices.Settings.Zip;
        }
        if (new PartyEditorWindow(p) { Owner = Window.GetWindow(this) }.ShowDialog() == true) await Ui.Try(RefreshAsync);
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null) return;
        Party? p = IsClients ? await AppServices.Parties.GetClientAsync(Current.Id) : await AppServices.Parties.GetSupplierAsync(Current.Id);
        if (p is null) return;
        if (new PartyEditorWindow(p) { Owner = Window.GetWindow(this) }.ShowDialog() == true) await Ui.Try(RefreshAsync);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row || !Ui.Confirm($"Delete {row.DisplayName}?")) return;
        await Ui.Try(async () =>
        {
            if (IsClients) await AppServices.Parties.DeleteClientAsync(row.Id); else await AppServices.Parties.DeleteSupplierAsync(row.Id);
            await RefreshAsync();
        });
    }
}

public sealed class ClientsView : PartiesView
{
    protected override bool IsClients => true;

    public ClientsView() : base("Clients")
    {
        Col("Balance", nameof(PartyRow.Balance), 90, "N2");
        Col("Points", nameof(PartyRow.Points), 70, "0.##");
        Col("Last purchase", nameof(PartyRow.LastPurchase), 110);
        AddButton("Sell to client", SellTo_Click);
        AddButton("Statement", Statement_Click);
        AddButton("Flag / unflag", Flag_Click);
        AddButton("Export CSV", Export_Click);
        AddButton("Import CSV", Import_Click);
    }

    private async void SellTo_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row || Window.GetWindow(this) is not MainWindow main) return;
        main.Navigate("pos");
        await MainWindow.Settle();
        if (main.GetView<PosView>() is { } pos) await Ui.Try(() => pos.Vm.SetClientAsync(row.Id));
    }

    private async void Statement_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) return;
        await Ui.Try(async () =>
        {
            var (client, open, payments) = await AppServices.Invoices.StatementAsync(row.Id);
            new TextPreviewWindow($"Statement {row.DisplayName}", ReceiptFormatter.Statement(client, open, payments, AppServices.Settings)).ShowDialog();
        });
    }

    private async void Flag_Click(object sender, RoutedEventArgs e)
    {
        var rows = Grid.SelectedItems.OfType<PartyRow>().ToList();
        if (rows.Count == 0) return;
        await Ui.Try(async () => { await AppServices.Parties.SetClientFlagAsync(rows.Select(r => r.Id), rows.Any(r => !r.IsFlagged)); await RefreshAsync(); });
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var file = Ui.SaveFile("clients.csv", "CSV file|*.csv");
        if (file is null) return;
        await Ui.Try(async () => { await File.WriteAllTextAsync(file, await AppServices.Csv.ExportClientsAsync()); Ui.Info("Exported to " + file); });
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var file = Ui.OpenFile("CSV file|*.csv|All files|*.*");
        if (file is null) return;
        await Ui.Try(async () =>
        {
            var r = await AppServices.Csv.ImportClientsAsync(await File.ReadAllTextAsync(file));
            Ui.Info($"{r.Added} added, {r.Updated} updated, {r.Skipped} skipped." + (r.Problems.Count > 0 ? "\n\n" + string.Join("\n", r.Problems.Take(15)) : ""));
            await RefreshAsync();
        });
    }
}

public sealed class SuppliersView : PartiesView
{
    protected override bool IsClients => false;

    public SuppliersView() : base("Suppliers") { }
}
