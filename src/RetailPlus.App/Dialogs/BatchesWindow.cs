using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Dialogs;

/// <summary>One batch of what arrived on a purchase order line, as the batches window edits it.</summary>
public sealed class BatchEntry
{
    public Guid LineId { get; init; }
    public string Plu { get; init; } = "";
    public string Description { get; init; } = "";
    public int ShelfLifeDays { get; init; }
    public string BatchNumber { get; set; } = "";
    public decimal Pieces { get; set; }
    public DateTime? ExpiresOn { get; set; }
    public DateTime? MadeOn { get; set; }

    /// <summary>
    /// One batch for each received line of an item kept in batches, with all the pieces received and the expiry date
    /// offered from the item's shelf life.
    /// </summary>
    public static List<BatchEntry> Start(IEnumerable<PurchaseOrderLine> lines, IReadOnlyList<BatchItem> kept, DateOnly today) =>
    [
        .. lines.Where(l => l.QuantityReceived > 0 && l.ItemId is Guid id && kept.Any(k => k.ItemId == id)).Select(l =>
        {
            int shelfLife = kept.First(k => k.ItemId == l.ItemId).ShelfLifeDays;
            return new BatchEntry
            {
                LineId = l.Id, Plu = l.Plu, Description = l.Description, ShelfLifeDays = shelfLife, Pieces = l.QuantityReceived,
                ExpiresOn = Fefo.SuggestedExpiry(shelfLife, null, today)?.ToDateTime(TimeOnly.MinValue),
            };
        }),
    ];

    /// <summary>What is wrong with the batches entered, in words for the person receiving, or null when they can be posted.</summary>
    public static string? Check(IReadOnlyList<BatchEntry> entries, IEnumerable<PurchaseOrderLine> lines)
    {
        if (entries.Any(e => string.IsNullOrWhiteSpace(e.BatchNumber))) return "Every batch needs its number, as printed on the goods.";
        if (entries.Any(e => e.Pieces <= 0)) return "Every batch needs the pieces that came of it.";
        if (entries.Any(e => e.MadeOn is DateTime made && e.ExpiresOn is DateTime expires && expires < made)) return "A batch cannot expire before it was made.";
        foreach (var line in lines)
        {
            var mine = entries.Where(e => e.LineId == line.Id).ToList();
            if (mine.Count == 0) continue;
            decimal total = mine.Sum(e => e.Pieces);
            if (total != line.QuantityReceived)
                return $"The batches of {line.Plu} add up to {total.ToString("0.###", CultureInfo.CurrentCulture)}, not the "
                       + $"{line.QuantityReceived.ToString("0.###", CultureInfo.CurrentCulture)} pieces received.";
        }
        return null;
    }

    /// <summary>The batches entered for one line, as the receipt takes them.</summary>
    public static IReadOnlyList<ReceivedBatch>? For(Guid lineId, IReadOnlyList<BatchEntry> entries)
    {
        var mine = entries.Where(e => e.LineId == lineId).ToList();
        return mine.Count == 0 ? null
            : [.. mine.Select(e => new ReceivedBatch(e.BatchNumber.Trim().ToUpperInvariant(), e.Pieces, e.ExpiresOn is DateTime x ? DateOnly.FromDateTime(x) : null,
                e.MadeOn is DateTime m ? DateOnly.FromDateTime(m) : null))];
    }
}

/// <summary>
/// Purchase Orders › Post shipment, for items kept in batches: the batch number printed on the goods, the pieces of each
/// batch and its expiry date (offered from the item's shelf life) and made-on date. A line that came in two batches gets
/// another row.
/// </summary>
public sealed class BatchesWindow : Window
{
    private readonly ObservableCollection<BatchEntry> _entries;
    private readonly IReadOnlyList<PurchaseOrderLine> _lines;
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, Height = 320,
        SelectionMode = DataGridSelectionMode.Single,
    };
    private readonly TextBlock _problem = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontSize = 17 };

    public BatchesWindow(long orderNumber, IReadOnlyList<PurchaseOrderLine> lines, List<BatchEntry> entries)
    {
        _lines = lines;
        _entries = new(entries);
        Title = $"Batches received on PO {orderNumber}";
        Width = 1040;
        SizeToContent = SizeToContent.Height;
        Owner = Ui.Owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        root.Children.Add(new TextBlock { Text = "Batches received", Style = (Style)Application.Current.FindResource("SectionTitle") });
        root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 0, 0, 10),
            Text = "These items are kept in batches. Type the batch number printed on the goods, how many pieces came of it and the "
                   + "date it expires. When a line came in two batches, press Another batch.",
        });
        void Text(string header, string path, double width, bool readOnly)
        {
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = header, IsReadOnly = readOnly, Width = width < 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
                Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, StringFormat = path == nameof(BatchEntry.Pieces) ? "0.###" : null },
            });
        }
        void Date(string header, string path)
        {
            var picker = new FrameworkElementFactory(typeof(DatePicker));
            picker.SetBinding(DatePicker.SelectedDateProperty, new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            _grid.Columns.Add(new DataGridTemplateColumn { Header = header, Width = new DataGridLength(170), CellTemplate = new DataTemplate { VisualTree = picker } });
        }
        Text("Stock code", nameof(BatchEntry.Plu), 130, true);
        Text("Description", nameof(BatchEntry.Description), -1, true);
        Text("Batch number", nameof(BatchEntry.BatchNumber), 160, false);
        Text("Pieces", nameof(BatchEntry.Pieces), 90, false);
        Date("Expires on", nameof(BatchEntry.ExpiresOn));
        Date("Made on", nameof(BatchEntry.MadeOn));
        _grid.ItemsSource = _entries;
        root.Children.Add(_grid);
        root.Children.Add(_problem);

        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        var another = new Button { Content = "Another batch", MinHeight = 54, MinWidth = 170, Margin = new Thickness(0, 0, 10, 0) };
        Look.SetShortcut(another, "Ins");
        another.Click += (_, _) => Another();
        var remove = new Button { Content = "Remove batch", MinHeight = 54, MinWidth = 170 };
        Look.SetShortcut(remove, "Ctrl+Del");
        remove.Click += (_, _) => Remove();
        buttons.Children.Add(another);
        buttons.Children.Add(remove);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = 54, MinWidth = 130 };
        DockPanel.SetDock(cancel, Dock.Right);
        var ok = new Button { Content = "Post the receipt", IsDefault = true, MinHeight = 54, MinWidth = 190, Margin = new Thickness(0, 0, 10, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        DockPanel.SetDock(ok, Dock.Right);
        ok.Click += (_, _) => Finish();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
        Views.Layout.AddIcons(this);
        Look.SetIcon(another, "");
        Look.SetIcon(ok, "");
        Loaded += (_, _) =>
        {
            _grid.Focus();
            if (_entries.Count > 0) _grid.CurrentCell = new DataGridCellInfo(_entries[0], _grid.Columns[2]);
        };
    }

    /// <summary>The batches as entered, once they add up.</summary>
    public IReadOnlyList<BatchEntry> Entries => _entries;

    /// <summary>A new row for the line of the chosen batch, with the pieces not yet given to a batch.</summary>
    private void Another()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        if ((_grid.SelectedItem ?? _grid.CurrentItem ?? _entries.LastOrDefault()) is not BatchEntry from) return;
        var line = _lines.FirstOrDefault(l => l.Id == from.LineId);
        decimal left = line is null ? 0 : line.QuantityReceived - _entries.Where(e => e.LineId == from.LineId).Sum(e => e.Pieces);
        var entry = new BatchEntry
        {
            LineId = from.LineId, Plu = from.Plu, Description = from.Description, ShelfLifeDays = from.ShelfLifeDays, Pieces = Math.Max(0, left),
            ExpiresOn = Fefo.SuggestedExpiry(from.ShelfLifeDays, null, DateOnly.FromDateTime(DateTime.Today))?.ToDateTime(TimeOnly.MinValue),
        };
        _entries.Insert(_entries.IndexOf(from) + 1, entry);
        _grid.SelectedItem = entry;
        _grid.CurrentCell = new DataGridCellInfo(entry, _grid.Columns[2]);
        _grid.Focus();
    }

    /// <summary>Removes the chosen row, as long as its line keeps another one.</summary>
    private void Remove()
    {
        if (_grid.SelectedItem is not BatchEntry entry) return;
        if (_entries.Count(e => e.LineId == entry.LineId) < 2)
        {
            _problem.Text = $"{entry.Plu} needs at least one batch.";
            return;
        }
        _entries.Remove(entry);
    }

    private void Finish()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        if (BatchEntry.Check(_entries, _lines) is { } problem)
        {
            _problem.Text = problem;
            return;
        }
        DialogResult = true;
    }
}
