using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Views;

public partial class InventoryView : UserControl, IRefreshable
{
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _loading;

    public InventoryView()
    {
        InitializeComponent();
        FieldBox.ItemsSource = Enum.GetValues<ItemSearchField>();
        FieldBox.SelectedIndex = 0;
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await Ui.Try(RefreshAsync); };
    }

    private int LocationId => (LocationBox.SelectedItem as Location)?.Id ?? AppServices.Session.LocationId;

    public async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            if (LocationBox.ItemsSource is null)
            {
                LocationBox.ItemsSource = AppServices.Locations;
                LocationBox.SelectedItem = AppServices.Locations.FirstOrDefault(l => l.Id == AppServices.Session.LocationId);
            }
            var rows = await AppServices.Inventory.SearchAsync(LocationId, SearchBox.Text, (ItemSearchField)FieldBox.SelectedItem,
                DeletedBox.IsChecked == true, FlaggedBox.IsChecked == true);
            ItemsGrid.ItemsSource = rows;
            var total = await AppServices.Inventory.CountAsync(LocationId);
            CountText.Text = rows.Count >= 2000 ? $"Showing the first 2,000 of {total:N0} items — refine the search" : $"{rows.Count:N0} shown · {total:N0} items";
        }
        finally
        {
            _loading = false;
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) { _searchTimer.Stop(); _searchTimer.Start(); }
    private async void Field_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await Ui.Try(RefreshAsync); }
    private async void Filter_Click(object sender, RoutedEventArgs e) => await Ui.Try(RefreshAsync);
    private async void Location_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await Ui.Try(RefreshAsync); }

    private ItemRow? Current => ItemsGrid.SelectedItem as ItemRow;
    private List<int> SelectedIds => ItemsGrid.SelectedItems.OfType<ItemRow>().Select(r => r.Id).ToList();

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        var item = new Item { LocationId = LocationId, Taxable1 = true, Taxable2 = true, ItemType = ItemType.Standard };
        if (new ItemEditorWindow(item) { Owner = Window.GetWindow(this) }.ShowDialog() == true) await Ui.Try(RefreshAsync);
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null) return;
        var item = await AppServices.Inventory.GetAsync(Current.Id);
        if (item is null) return;
        if (new ItemEditorWindow(item) { Owner = Window.GetWindow(this) }.ShowDialog() == true) await Ui.Try(RefreshAsync);
    }

    private async void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null) return;
        var item = await AppServices.Inventory.GetAsync(Current.Id);
        if (item is null) return;
        item.Id = 0;
        item.RowVersion = [];
        item.Plu = "";
        item.OnHand = 0;
        item.OnOrder = 0;
        item.Serials.Clear();
        foreach (var v in item.Variants) v.OnHand = 0;
        if (new ItemEditorWindow(item) { Owner = Window.GetWindow(this) }.ShowDialog() == true) await Ui.Try(RefreshAsync);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds;
        if (ids.Count == 0 || !Ui.Confirm($"Delete {ids.Count} item(s)? Deleted items can be restored with Undelete.")) return;
        await Ui.Try(async () => { foreach (var id in ids) await AppServices.Inventory.DeleteAsync(id); await RefreshAsync(); });
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var ids = ItemsGrid.SelectedItems.OfType<ItemRow>().Where(r => r.IsDeleted).Select(r => r.Id).ToList();
        if (ids.Count == 0) { Ui.Info("Tick Show deleted and select the items to restore."); return; }
        await Ui.Try(async () => { foreach (var id in ids) await AppServices.Inventory.RestoreAsync(id); await RefreshAsync(); });
    }

    private async void Adjust_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) return;
        var mode = Ui.Choose($"{row.Name}: on hand {row.OnHand:0.###}", ["Add to (or take from) on hand", "Set the on-hand count (stock take)"], "Adjust stock");
        if (mode < 0) return;
        var qty = Ui.PromptDecimal(mode == 0 ? "Quantity to add (negative to remove):" : "Counted quantity:", "Adjust stock");
        if (qty is null) return;
        var reason = Ui.Prompt("Reason:", "Adjust stock", mode == 0 ? "Adjustment" : "Stock take");
        if (reason is null) return;
        await Ui.Try(async () =>
        {
            var after = await AppServices.Inventory.AdjustStockAsync(row.Id, qty.Value, reason, AppServices.Session.UserName, setTo: mode == 1);
            ((MainWindow)Window.GetWindow(this)).SetStatus($"{row.Plu}: on hand is now {after:0.###}");
            await RefreshAsync();
        });
    }

    private async void Flag_Click(object sender, RoutedEventArgs e)
    {
        var rows = ItemsGrid.SelectedItems.OfType<ItemRow>().ToList();
        if (rows.Count == 0) return;
        bool flag = rows.Any(r => !r.IsFlagged);
        await Ui.Try(async () => { await AppServices.Inventory.SetFlagAsync(rows.Select(r => r.Id), flag); await RefreshAsync(); });
    }

    private async void ClearFlags_Click(object sender, RoutedEventArgs e) =>
        await Ui.Try(async () => { await AppServices.Inventory.ClearFlagsAsync(LocationId); await RefreshAsync(); });

    private async void ChangePrices_Click(object sender, RoutedEventArgs e)
    {
        var how = Ui.Choose("How should Price 1 change?", ["By a percentage (e.g. +5 or -10)", "From cost, to a gross margin"], "Change prices");
        if (how < 0) return;
        var value = Ui.PromptDecimal(how == 0 ? "Percent change:" : "Gross margin in percent:", "Change prices");
        if (value is null) return;
        var scope = Ui.Choose("Which items?", ["Flagged items", "One department", "All items in this location"], "Change prices");
        if (scope < 0) return;
        string? dept = null;
        if (scope == 1)
        {
            var depts = await AppServices.Setup.GetDepartmentsAsync();
            int d = Ui.Choose("Department:", depts, "Change prices");
            if (d < 0) return;
            dept = depts[d];
        }
        if (!Ui.Confirm("Change the prices now? This cannot be undone.")) return;
        await Ui.Try(async () =>
        {
            int n = await AppServices.Inventory.ChangePricesAsync(LocationId, value.Value, scope == 0, dept, fromMargin: how == 1, margin: value.Value);
            Ui.Info($"{n} prices changed.");
            await RefreshAsync();
        });
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds;
        var targets = AppServices.Locations.Where(l => l.Id != LocationId).ToList();
        if (ids.Count == 0 || targets.Count == 0) { Ui.Info("Select items, and set up a second location in Setup first."); return; }
        int t = Ui.Choose("Copy the selected items to:", targets.Select(l => $"{l.Code} {l.Name}").ToList(), "Copy to location");
        if (t < 0) return;
        await Ui.Try(async () => Ui.Info($"{await AppServices.Inventory.CopyToLocationAsync(ids, targets[t].Id)} items copied."));
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var file = Ui.SaveFile($"inventory-{AppServices.Session.LocationCode}.csv", "CSV file|*.csv");
        if (file is null) return;
        await Ui.Try(async () => { await File.WriteAllTextAsync(file, await AppServices.Csv.ExportItemsAsync(LocationId)); Ui.Info("Exported to " + file); });
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var file = Ui.OpenFile("CSV file|*.csv|All files|*.*");
        if (file is null) return;
        await Ui.Try(async () =>
        {
            var result = await AppServices.Csv.ImportItemsAsync(LocationId, await File.ReadAllTextAsync(file));
            Ui.Info($"{result.Added} added, {result.Updated} updated, {result.Skipped} skipped." + (result.Problems.Count > 0 ? "\n\n" + string.Join("\n", result.Problems.Take(15)) : ""));
            await RefreshAsync();
        });
    }
}
