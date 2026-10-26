using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.App.Dialogs;

/// <summary>The Item Detail window: every tab of an inventory item.</summary>
public partial class ItemEditorWindow : Window
{
    private readonly Item _item;
    private readonly ObservableCollection<ItemSupplier> _suppliers;
    private readonly ObservableCollection<ItemVariant> _variants;
    private readonly ObservableCollection<KitComponent> _kit;
    private readonly ObservableCollection<ItemSerial> _serials;

    public ItemEditorWindow(Item item)
    {
        InitializeComponent();
        Views.Layout.AddIcons(this);
        _item = item;
        DataContext = item;
        HeaderText.Text = item.Id == 0 ? "New item" : $"{item.Plu} — {item.Name}";
        Title = item.Id == 0 ? "New item" : $"Item {item.Plu}";
        TypeBox.ItemsSource = Enum.GetValues<ItemType>();
        WeightUnitBox.ItemsSource = new[] { "g", "kg", "ml", "L", "oz", "lb" };
        Tax1Label.Text = AppServices.Settings.Tax1Name ?? "Tax 1";
        Tax2Label.Text = AppServices.Settings.Tax2Name ?? "Tax 2";
        OnHandBox.IsEnabled = item.Id == 0;
        _suppliers = new(item.Suppliers.OrderBy(s => s.Rank));
        _variants = new(item.Variants.OrderBy(v => v.Ordinal));
        _kit = new(item.KitComponents.OrderBy(k => k.Ordinal));
        _serials = new(item.Serials);
        SuppliersGrid.ItemsSource = _suppliers;
        VariantsGrid.ItemsSource = _variants;
        KitGrid.ItemsSource = _kit;
        SerialsGrid.ItemsSource = _serials;
        var headings = item.MatrixColumns.OrderBy(c => c.Ordinal).Select(c => c.Heading).ToList();
        Heading1.Text = headings.ElementAtOrDefault(0) ?? "";
        Heading2.Text = headings.ElementAtOrDefault(1) ?? "";
        Heading3.Text = headings.ElementAtOrDefault(2) ?? "";
        if (Heading1.Text.Length > 0) V1Col.Header = Heading1.Text;
        if (Heading2.Text.Length > 0) V2Col.Header = Heading2.Text;
        if (Heading3.Text.Length > 0) V3Col.Header = Heading3.Text;
        Loaded += async (_, _) => await Ui.Try(LoadListsAsync);
        UpdateMargin();
        Tabs.SelectionChanged += (_, _) => UpdateMargin();
    }

    private async Task LoadListsAsync()
    {
        DeptBox.ItemsSource = await AppServices.Setup.GetDepartmentsAsync();
        CatBox.ItemsSource = await AppServices.Setup.GetCategoriesAsync();
        SupplierBox.ItemsSource = await AppServices.Parties.GetSupplierNamesAsync();
        DeptBox.Text = _item.Department?.Name ?? "";
        CatBox.Text = _item.Category?.Name ?? "";
        SupplierBox.Text = _item.Supplier?.Company ?? "";
        PluBox.Focus();
        if (_item.Id != 0)
        {
            int year = DateTime.Today.Year;
            ThisYearTitle.Text = $"{year} by month";
            LastYearTitle.Text = $"{year - 1} by month";
            ThisYearGrid.ItemsSource = await AppServices.Inventory.GetPeriodSalesAsync(_item.Id, year, SalesPeriodKind.Month);
            LastYearGrid.ItemsSource = await AppServices.Inventory.GetPeriodSalesAsync(_item.Id, year - 1, SalesPeriodKind.Month);
            await LoadTagsAsync();
        }
    }

    private async Task LoadTagsAsync()
    {
        var tags = await AppServices.RfidData.GetTagsAsync(_item.Id);
        TagsGrid.ItemsSource = tags;
        int inStock = tags.Count(t => t.Status == ItemTagStatus.InStock);
        TagSummary.Text = tags.Count == 0 ? "No tags yet" : $"{tags.Count} tag(s): {inStock} in stock, {tags.Count - inStock} sold or away";
    }

    private void SeeTags_Click(object sender, RoutedEventArgs e) => Tabs.SelectedItem = TagsTab;

    private async void MakeBarcode_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(BarcodeBox.Text) && !Ui.Confirm($"Replace barcode {BarcodeBox.Text} with a new in-store one?")) return;
        await Ui.Try(async () => BarcodeBox.Text = await AppServices.Inventory.NewInStoreBarcodeAsync());
    }

    private bool SavedFirst()
    {
        if (_item.Id != 0) return true;
        Ui.Info("Save the item first, then add its RFID tags.");
        return false;
    }

    private async void AddTag_Click(object sender, RoutedEventArgs e)
    {
        if (!SavedFirst()) return;
        var code = Ui.Prompt("Tag code (EPC) in hexadecimal, as printed on the tag or shown by the reader:", "Add RFID tag");
        if (string.IsNullOrWhiteSpace(code)) return;
        await Ui.Try(async () =>
        {
            await AppServices.RfidData.RegisterTagAsync(code, _item.Id);
            await LoadTagsAsync();
        });
    }

    private async void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (TagsGrid.SelectedItem is not ItemTag tag || !Ui.Confirm($"Remove tag {tag.Epc} from this item?")) return;
        await Ui.Try(async () =>
        {
            await AppServices.RfidData.RemoveTagAsync(tag.Id);
            await LoadTagsAsync();
        });
    }

    /// <summary>Listens to this station's antennas for ten seconds and gives every new tag it sees to this item.</summary>
    private async void ReadTags_Click(object sender, RoutedEventArgs e)
    {
        if (!SavedFirst()) return;
        if (AppServices.Rfid is not { } rfid)
        {
            Ui.Error("RFID is off at this station. Turn it on in Setup > This station, and give this station an antenna in RFID readers.");
            return;
        }
        if (!Ui.Confirm("Put only the tagged pieces of this item on the antenna of this counter, then press Yes. Reading takes 10 seconds.", "Read tags")) return;
        int added = 0, other = 0;
        var seen = new HashSet<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        TagsHint.Text = "Reading... keep the items on the antenna.";
        try
        {
            await foreach (var tag in rfid.ListenAsync(AppServices.Session.StationNumber, cts.Token))
            {
                if (!seen.Add(tag.Epc)) continue;
                try
                {
                    await AppServices.RfidData.RegisterTagAsync(tag.Epc, _item.Id);
                    added++;
                }
                catch (Data.Services.BusinessRuleException)
                {
                    other++;
                }
                TagsHint.Text = $"Reading... {added} tag(s) added" + (other > 0 ? $", {other} belong to other items" : "");
            }
        }
        catch (OperationCanceledException)
        {
        }
        TagsHint.Text = $"{added} tag(s) added" + (other > 0 ? $"; {other} tag(s) already belong to other items and were left alone." : ".");
        await Ui.Try(LoadTagsAsync);
    }

    private void UpdateMargin()
    {
        var cost = _item.AverageCost > 0 ? _item.AverageCost : _item.LastCost;
        MarginText.Text = _item.Price1 > 0 ? Money.MarginPercent(_item.Price1, cost).ToString("0.0", CultureInfo.CurrentCulture) + " %" : "—";
    }

    private void Suggest_Click(object sender, RoutedEventArgs e)
    {
        var cost = _item.AverageCost > 0 ? _item.AverageCost : _item.LastCost;
        if (cost <= 0) { Ui.Error("Enter the cost first."); return; }
        var margin = Ui.PromptDecimal("Gross margin you want, in percent:", "Suggest a price", _item.MarginPercent > 0 ? _item.MarginPercent : 40);
        if (margin is null) return;
        if (margin >= 100) { Ui.Error("A margin must be below 100%."); return; }
        _item.Price1 = Money.SuggestPrice(cost, margin.Value);
        DataContext = null;
        DataContext = _item;
        UpdateMargin();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        // Commit pending cell edits.
        SuppliersGrid.CommitEdit(); VariantsGrid.CommitEdit(); KitGrid.CommitEdit(); SerialsGrid.CommitEdit();
        _item.Suppliers = [.. _suppliers];
        _item.Variants = [.. _variants];
        _item.KitComponents = [.. _kit];
        _item.Serials = [.. _serials];
        _item.MatrixColumns = new[] { Heading1.Text, Heading2.Text, Heading3.Text }.Select((h, i) => new ItemMatrixColumn { Ordinal = i + 1, Heading = h }).ToList();
        if (await Ui.Try(() => AppServices.Inventory.SaveAsync(_item, DeptBox.Text, CatBox.Text, SupplierBox.Text)))
            DialogResult = true;
    }
}
