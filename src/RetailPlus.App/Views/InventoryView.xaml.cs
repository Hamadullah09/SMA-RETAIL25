using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Views;

public partial class InventoryView : UserControl, IRefreshable
{
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _loading;

    public InventoryView()
    {
        InitializeComponent();
        Layout.AddIcons(this);
        FieldBox.ItemsSource = Enum.GetValues<ItemSearchField>();
        FieldBox.SelectedIndex = 0;
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await Ui.Try(RefreshAsync); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                e.Handled = true;
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
        };
    }

    /// <summary>Enter on the list opens the chosen item.</summary>
    private void Grid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || Current is null) return;
        e.Handled = true;
        Edit_Click(sender, e);
    }

    private async void Labels_Click(object sender, RoutedEventArgs e)
    {
        if (!Approval.Allowed(Permissions.InventoryLabels, "Printing labels")) return;
        var chosen = ItemsGrid.SelectedItems.OfType<ItemRow>().Where(r => !r.IsDeleted).ToList();
        List<ItemRow> all = [];
        if (!await Ui.Try(async () => all = (await AppServices.Inventory.SearchAsync(LocationId, null, take: 100_000)).Where(r => !r.IsDeleted).ToList())) return;
        if (all.Count == 0) { Ui.Info("There are no items to print labels for."); return; }
        new LabelsWindow(all, chosen) { Owner = Window.GetWindow(this) }.ShowDialog();
        await Ui.Try(RefreshAsync);
    }

    private Guid LocationId => (LocationBox.SelectedItem as Location)?.Id ?? AppServices.Session.LocationId;

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
    private List<Guid> SelectedIds => ItemsGrid.SelectedItems.OfType<ItemRow>().Select(r => r.Id).ToList();

    /// <summary>New item, from the Ctrl+K list.</summary>
    public void NewItem() => New_Click(this, new RoutedEventArgs());

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        if (!Approval.Allowed(Permissions.InventoryEdit, "A new item")) return;
        var item = new Item { LocationId = LocationId, Taxable1 = true, Taxable2 = true, ItemType = ItemType.Standard };
        if (new ItemEditorWindow(item) { Owner = Window.GetWindow(this) }.ShowDialog() == true) await Ui.Try(RefreshAsync);
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null || !Approval.Allowed(Permissions.InventoryEdit, "Changing an item")) return;
        var item = await AppServices.Inventory.GetAsync(Current.Id);
        if (item is null) return;
        if (new ItemEditorWindow(item) { Owner = Window.GetWindow(this) }.ShowDialog() == true) await Ui.Try(RefreshAsync);
    }

    private async void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null || !Approval.Allowed(Permissions.InventoryEdit, "A new item")) return;
        var item = await AppServices.Inventory.GetAsync(Current.Id);
        if (item is null) return;
        item.Id = Guid.Empty;
        item.CreatedAtUtc = default;
        item.Version = Guid.Empty;
        item.Plu = "";
        item.Barcode = null;
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
        if (!Approval.Allowed(Permissions.InventoryEdit, "Deleting items")) return;
        await Ui.Try(async () => { foreach (var id in ids) await AppServices.Inventory.DeleteAsync(id); await RefreshAsync(); });
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var ids = ItemsGrid.SelectedItems.OfType<ItemRow>().Where(r => r.IsDeleted).Select(r => r.Id).ToList();
        if (ids.Count == 0) { Ui.Info("Tick Show deleted and select the items to restore."); return; }
        if (!Approval.Allowed(Permissions.InventoryEdit, "Undeleting items")) return;
        await Ui.Try(async () => { foreach (var id in ids) await AppServices.Inventory.RestoreAsync(id); await RefreshAsync(); });
    }

    private async void Adjust_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row || !Approval.Allowed(Permissions.InventoryAdjust, "Adjusting stock")) return;
        var mode = Ui.Choose($"{row.Name}: on hand {row.OnHand:0.###}", ["Add to (or take from) on hand", "Set the on-hand count (stock take)"], "Adjust stock");
        if (mode < 0) return;
        var qty = Ui.PromptDecimal(mode == 0 ? "Quantity to add (negative to remove):" : "Counted quantity:", "Adjust stock");
        if (qty is null) return;
        var reason = Ui.Prompt("Reason:", "Adjust stock", mode == 0 ? "Adjustment" : "Stock take");
        if (reason is null) return;
        await Ui.Try(async () =>
        {
            var after = await AppServices.Inventory.AdjustStockAsync(row.Id, qty.Value, reason, AppServices.Session.UserName, setTo: mode == 1, userId: AppServices.Session.UserId);
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

    /// <summary>Change prices: a rule, the items it reaches, and a preview of every new price before anything changes.</summary>
    private async void ChangePrices_Click(object sender, RoutedEventArgs e)
    {
        if (!Approval.Allowed(Permissions.InventoryPrices, "Changing prices")) return;
        int level = Ui.Choose("Which price?", ["Price 1", "Price 2", "Price 3", "Price 4"], "Change prices") + 1;
        if (level <= 0) return;
        var methods = new[] { PriceChangeMethod.Percent, PriceChangeMethod.Margin, PriceChangeMethod.Amount };
        var how = Ui.Choose($"How should Price {level} change?", ["By a percentage (e.g. +5 or -10)", "From cost, to a gross margin", "By an amount (e.g. +0.50 or -1)"], "Change prices");
        if (how < 0) return;
        var value = Ui.PromptDecimal(how switch { 0 => "Percent change:", 1 => "Gross margin in percent:", _ => "Amount to add (negative to take off):" }, "Change prices");
        if (value is null) return;
        decimal?[] endings = [null, 0.99m, 0.95m, 0.00m];
        int ending = Ui.Choose("Round the new prices?", ["No, keep them to the cent", "Up to end in .99", "Up to end in .95", "Up to a whole amount"], "Change prices");
        if (ending < 0) return;
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
        var rule = new PriceRule(methods[how], value.Value, endings[ending], level);
        var text = (how switch
        {
            0 => $"Price {level} {value.Value:+0.##;-0.##}%",
            1 => $"Price {level} from cost at a {value.Value:0.##}% margin",
            _ => $"Price {level} {value.Value:+0.00;-0.00}",
        }) + (endings[ending] is decimal end ? $", rounded up to end in {end:.00}" : "") + (scope == 0 ? ", flagged items" : dept is not null ? $", department {dept}" : ", every item");
        await Ui.Try(async () =>
        {
            var preview = await AppServices.Inventory.PreviewPriceChangesAsync(LocationId, rule, scope == 0, dept);
            if (preview.Count == 0)
            {
                Ui.Info("No price would change.");
                return;
            }
            var window = new PriceChangeWindow(preview, text) { Owner = Window.GetWindow(this) };
            if (window.ShowDialog() == true) ((MainWindow)Window.GetWindow(this)).SetStatus($"{window.Applied} price(s) changed.");
            await RefreshAsync();
        });
    }

    /// <summary>Break case: cases of the case item become units of the item that names it.</summary>
    private async void BreakCase_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) return;
        if (!AppServices.Can(Core.Rules.Permissions.InventoryAdjust)) { Ui.Error("Your role cannot change stock."); return; }
        CaseLink? link = null;
        if (!await Ui.Try(async () => link = await AppServices.Inventory.FindCaseLinkAsync(row.Id))) return;
        if (link is null)
        {
            Ui.Info($"{row.Name} is not part of a case. Open the unit item (the single piece), and on its Special tab enter the case item's stock code "
                    + "as Parent (case) item and how many units one case holds.");
            return;
        }
        var cases = Ui.PromptDecimal($"Break how many cases of {link.CasePlu} {link.CaseName} ({link.CasesOnHand:0.###} on hand)? "
                                     + $"Each holds {link.UnitsPerCase:0.###} x {link.UnitPlu} {link.UnitName}.", "Break case", 1);
        if (cases is null) return;
        await Ui.Try(async () =>
        {
            var after = await AppServices.Inventory.BreakCaseAsync(link.UnitItemId, cases.Value, AppServices.Session.UserName);
            ((MainWindow)Window.GetWindow(this)).SetStatus($"{link.CasePlu}: {after.CasesOnHand:0.###} case(s) left · {link.UnitPlu}: {after.UnitsOnHand:0.###} on hand");
            await RefreshAsync();
        });
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds;
        var targets = AppServices.Locations.Where(l => l.Id != LocationId).ToList();
        if (ids.Count == 0 || targets.Count == 0) { Ui.Info("Select items, and set up a second location in Setup first."); return; }
        if (!Approval.Allowed(Permissions.InventoryEdit, "Copying items")) return;
        int t = Ui.Choose("Copy the selected items to:", targets.Select(l => $"{l.Code} {l.Name}").ToList(), "Copy to location");
        if (t < 0) return;
        await Ui.Try(async () => Ui.Info($"{await AppServices.Inventory.CopyToLocationAsync(ids, targets[t].Id)} items copied."));
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var file = Ui.SaveFile($"inventory-{AppServices.Session.LocationCode}.csv", "CSV file|*.csv");
        if (file is null) return;
        // With a byte order mark, so Excel shows names with accents and symbols correctly.
        await Ui.Try(async () => { await File.WriteAllTextAsync(file, await AppServices.Csv.ExportItemsAsync(LocationId), new System.Text.UTF8Encoding(true)); Ui.Info("Exported to " + file); });
    }

    /// <summary>Saves the CSV template (every column, example rows and a notes row that the import skips) and offers to open it.</summary>
    private void Template_Click(object sender, RoutedEventArgs e) => SaveTemplate();

    private static void SaveTemplate()
    {
        var file = Ui.SaveFile("inventory-template.csv", "CSV file|*.csv");
        if (file is null) return;
        try
        {
            File.WriteAllText(file, Integrations.CsvExchange.ItemTemplate(), new System.Text.UTF8Encoding(true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error("The template could not be saved: " + ex.Message);
            return;
        }
        if (Ui.Confirm($"Template saved to\n{file}\n\nThe columns are Retail Plus's inventory columns. One row per tagged piece: an item with 3 tags is "
                       + "3 rows with the same PLU, each with its EPC; an item without tags is one row with no EPC. A new item needs PLU and PROD; "
                       + "leave other columns empty or delete them (a file with just PLU and UNITPRICE1 changes prices only). "
                       + "SSTATUS is 0 in stock or 1 sold, RECEIPT# the receipt it was sold on. The EXAMPLE rows and the notes row "
                       + "are never imported: replace them with your items.\n\nOpen the template now?", "CSV template"))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!Approval.Allowed(Permissions.ImportRun, "Importing items")) return;
        int how = Ui.Choose("Import items from a CSV file. Its first row names the columns (the template shows them all).",
            ["Choose the CSV file to import", "Save the CSV template first"], "Import CSV");
        if (how < 0) return;
        if (how == 1) { SaveTemplate(); return; }
        var file = Ui.OpenFile("CSV file|*.csv|All files|*.*");
        if (file is null) return;
        await Ui.Try(async () =>
        {
            var result = await AppServices.Csv.ImportItemsAsync(LocationId, await File.ReadAllTextAsync(file));
            Ui.Info($"{result.Added} item(s) added, {result.Updated} updated, {result.Skipped} row(s) skipped; {result.Tags} RFID tag(s) (EPC) registered."
                    + (result.Problems.Count > 0 ? "\n\n" + string.Join("\n", result.Problems.Take(15)) + (result.Problems.Count > 15 ? $"\n… and {result.Problems.Count - 15} more" : "") : ""));
            await RefreshAsync();
        });
    }
}
