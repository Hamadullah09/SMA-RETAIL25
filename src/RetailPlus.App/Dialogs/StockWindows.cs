using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Dialogs;

/// <summary>
/// Reads RFID tags for a stock count: while it is open it listens to the antennas of this station (a handheld or shelf
/// antenna assigned to it), and it also takes tag codes typed or pasted from a handheld reader's file, one per line.
/// Each tag counts once, however often it is read.
/// </summary>
public sealed class RfidCountWindow : Window
{
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);
    private readonly ObservableCollection<string> _recent = [];
    private readonly TextBlock _count = new() { FontFamily = new FontFamily("Century Gothic, Segoe UI"), FontSize = 44 };
    private readonly TextBox _paste = new() { AcceptsReturn = true, Height = 130, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private CancellationTokenSource? _listening;

    /// <summary>The tags read and pasted, once each.</summary>
    public IReadOnlyCollection<string> Tags { get; private set; } = [];

    public RfidCountWindow(string title)
    {
        Title = "Read RFID tags";
        Width = 680;
        SizeToContent = SizeToContent.Height;
        Owner = Ui.Owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        root.Children.Add(new TextBlock { Text = title + ": RFID tags", Style = (Style)Application.Current.FindResource("SectionTitle") });
        root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 0, 0, 10),
            Text = AppServices.Rfid is null
                ? "RFID is off at this station. Paste the tag codes read with a handheld reader below, one per line."
                : $"Move the antenna of station {AppServices.Session.StationNumber:000} along the shelves: every tag it reads is counted once. "
                  + "Tag codes from a handheld reader can be pasted below as well.",
        });
        var counter = new StackPanel { Orientation = Orientation.Horizontal };
        counter.Children.Add(_count);
        counter.Children.Add(new TextBlock { Text = " tags read", FontSize = 20, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) });
        root.Children.Add(counter);
        root.Children.Add(new ListBox { ItemsSource = _recent, Height = 140, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 6, 0, 10) });
        root.Children.Add(new TextBlock { Text = "Tag codes (one per line)", Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(_paste);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Add to the count", MinHeight = 54, MinWidth = 170, Margin = new Thickness(0, 0, 10, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        ok.Click += (_, _) => Finish();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinHeight = 54, MinWidth = 130 });
        root.Children.Add(buttons);
        Content = root;
        Views.Layout.AddIcons(this);
        _count.Text = "0";
        Loaded += async (_, _) => await ListenAsync();
        Closed += (_, _) => _listening?.Cancel();
    }

    private async Task ListenAsync()
    {
        if (AppServices.Rfid is not { } rfid) return;
        _listening = new CancellationTokenSource();
        try
        {
            await foreach (var tag in rfid.ListenAsync(AppServices.Session.StationNumber, _listening.Token))
                Add(tag.Epc);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _recent.Insert(0, "RFID stopped: " + ex.Message);
        }
    }

    /// <summary>A tag read (also used by the automated check).</summary>
    public void Add(string epc)
    {
        if (Epc.Normalize(epc) is not { } code || !_read.Add(code)) return;
        _recent.Insert(0, code);
        while (_recent.Count > 50) _recent.RemoveAt(_recent.Count - 1);
        _count.Text = _read.Count.ToString(CultureInfo.CurrentCulture);
        Sounds.TagRead();
    }

    private void Finish()
    {
        foreach (var line in _paste.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            if (Epc.Normalize(line) is { } code) _read.Add(code);
        Tags = _read.ToList();
        DialogResult = true;
    }
}

/// <summary>
/// Change prices with a preview: every item the rule reaches with its old and new price and margin. Untick an item
/// to leave it out, or type another new price, then apply.
/// </summary>
public sealed class PriceChangeWindow : Window
{
    private readonly List<PriceChangePreview> _rows;
    private readonly DataGrid _grid = new() { Style = (Style)Application.Current.FindResource("Grid"), IsReadOnly = false, SelectionMode = DataGridSelectionMode.Single };
    private readonly TextBlock _summary = new() { Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 8, 0, 0) };

    /// <summary>The number of prices changed, after Apply.</summary>
    public int Applied { get; private set; }

    public PriceChangeWindow(List<PriceChangePreview> rows, string rule)
    {
        _rows = rows;
        Title = "Change prices";
        Width = 1180;
        Height = 760;
        Owner = Ui.Owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Change", Width = 80,
            Binding = new System.Windows.Data.Binding(nameof(PriceChangePreview.Apply)) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
        });
        void Ro(string header, string path, double width, string? format = null, bool right = false)
        {
            var c = Views.Layout.Col(header, path, width, format, right);
            c.IsReadOnly = true;
            _grid.Columns.Add(c);
        }
        Ro("Stock code", nameof(PriceChangePreview.Plu), 115);
        Ro("Description", nameof(PriceChangePreview.Name), -1);
        Ro("Department", nameof(PriceChangePreview.Department), 105);
        Ro("Cost", nameof(PriceChangePreview.Cost), 75, "N2", true);
        Ro("Old price", nameof(PriceChangePreview.OldPrice), 80, "N2", true);
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "New price", Width = 100,
            Binding = new System.Windows.Data.Binding(nameof(PriceChangePreview.NewPrice)) { StringFormat = "N2", UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus },
        });
        Ro("Old margin %", nameof(PriceChangePreview.OldMargin), 95, "0.0", true);
        Ro("New margin %", nameof(PriceChangePreview.NewMargin), 95, "0.0", true);
        _grid.ItemsSource = _rows;
        _grid.RowEditEnding += (_, _) => Dispatcher.BeginInvoke(() => { _grid.Items.Refresh(); Summarize(); }, System.Windows.Threading.DispatcherPriority.Background);
        _grid.CurrentCellChanged += (_, _) => Summarize();

        var root = new DockPanel { Margin = new Thickness(18) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = "Check the new prices", Style = (Style)Application.Current.FindResource("SectionTitle") });
        head.Children.Add(new TextBlock { Text = rule + ". Untick an item to leave it as it is, or type another new price.", Foreground = Look.Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var apply = new Button { Content = "Apply the new prices", MinHeight = 54, Margin = new Thickness(0, 0, 10, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        apply.Click += async (_, _) => await ApplyAsync();
        var all = new Button { Content = "Tick all", MinHeight = 54, Margin = new Thickness(0, 0, 10, 0) };
        all.Click += (_, _) => Tick(true);
        var none = new Button { Content = "Untick all", MinHeight = 54, Margin = new Thickness(0, 0, 10, 0) };
        none.Click += (_, _) => Tick(false);
        buttons.Children.Add(all);
        buttons.Children.Add(none);
        buttons.Children.Add(apply);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinHeight = 54, MinWidth = 120 });
        var foot = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        foot.Children.Add(buttons);
        foot.Children.Add(_summary);
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);
        root.Children.Add(_grid);
        Content = root;
        Views.Layout.AddIcons(this);
        Summarize();
    }

    private void Tick(bool on)
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (var r in _rows) r.Apply = on;
        _grid.Items.Refresh();
        Summarize();
    }

    private void Summarize()
    {
        var chosen = _rows.Where(r => r.Apply).ToList();
        var c = CultureInfo.CurrentCulture;
        _summary.Text = $"{chosen.Count} of {_rows.Count} item(s) will change; on average by {(chosen.Count == 0 ? 0 : chosen.Average(r => r.Change)).ToString("N2", c)}.";
    }

    private async Task ApplyAsync()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        if (_rows.Any(r => r.Apply && r.NewPrice < 0)) { Ui.Error("A price cannot be below zero."); return; }
        if (!Ui.Confirm($"Change {_rows.Count(r => r.Apply)} price(s) now?")) return;
        if (await Ui.Try(async () => Applied = await AppServices.Inventory.ApplyPriceChangesAsync(_rows))) DialogResult = true;
    }
}
