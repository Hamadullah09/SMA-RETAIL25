using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RetailPlus.App.Help;
using RetailPlus.App.Infrastructure;
using RetailPlus.App.Views;
using RetailPlus.Core.Rules;
using RetailPlus.Reports;

namespace RetailPlus.App.Dialogs;

/// <summary>Something the Ctrl+K list can do: open a screen, run a report, show a help topic or start an action.</summary>
public sealed record PaletteCommand(string Title, string Kind, string Keywords, Action Run, string? Shortcut = null)
{
    public override string ToString() => Title;
}

/// <summary>
/// Ctrl+K: type a few letters to jump to any screen, report, help topic or common action the signed-in user may use;
/// Enter runs the first match.
/// </summary>
public sealed class CommandPalette : Window
{
    private readonly TextBox _search = new() { FontSize = 22, MinHeight = 58 };
    private readonly ListBox _list = new() { FontSize = 18, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly IReadOnlyList<PaletteCommand> _commands;

    public CommandPalette(MainWindow main)
    {
        Title = "Go to";
        Width = 760;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        _commands = Commands(main);

        var root = new DockPanel { Margin = new Thickness(22, 20, 22, 16) };
        var head = new TextBlock { Text = "Go to", FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 24, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        Look.SetPlaceholder(_search, "Type a screen, report, action or question…");
        DockPanel.SetDock(_search, Dock.Top);
        root.Children.Add(_search);
        var hint = new TextBlock { Text = "↑ ↓ to choose · Enter to go · Esc to close", Foreground = Look.Brush("Muted"), Margin = new Thickness(2, 8, 0, 0) };
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(hint);
        _list.ItemTemplate = RowTemplate();
        _list.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(_list);
        Content = root;

        _search.TextChanged += (_, _) => Filter();
        PreviewKeyDown += OnKey;
        _list.MouseDoubleClick += (_, _) => RunSelected();
        Loaded += (_, _) => _search.Focus();
        Filter();
    }

    /// <summary>The commands as shown for <paramref name="text"/>, best first (used by the automated check too).</summary>
    public IReadOnlyList<PaletteCommand> Matches(string? text) => Match(_commands, text);

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (_list.Items.Count > 0) _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                if (_list.Items.Count > 0) _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                RunSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void Filter()
    {
        var found = Match(_commands, _search.Text);
        _list.ItemsSource = found.Take(60).ToList();
        _list.SelectedIndex = found.Count > 0 ? 0 : -1;
    }

    private void RunSelected()
    {
        if (_list.SelectedItem is not PaletteCommand command) return;
        Close();
        command.Run();
    }

    /// <summary>
    /// Every word typed must start a word of the command (or appear in its title); commands whose title starts with what
    /// was typed come first, then screens, actions, reports and help. Nothing typed lists the screens and actions.
    /// </summary>
    public static IReadOnlyList<PaletteCommand> Match(IReadOnlyList<PaletteCommand> commands, string? text)
    {
        var words = (text ?? "").ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return commands.Where(c => c.Kind is "Screen" or "Action").ToList();
        var typed = string.Join(' ', words);
        return commands
            .Select(c =>
            {
                var title = c.Title.ToUpperInvariant();
                var all = (title + " " + c.Keywords.ToUpperInvariant()).Split([' ', ':', '/', '-', '(', ')', '&', ','], StringSplitOptions.RemoveEmptyEntries);
                if (!words.All(w => title.Contains(w, StringComparison.Ordinal) || all.Any(a => a.StartsWith(w, StringComparison.Ordinal)))) return (c, -1);
                int score = title.StartsWith(typed, StringComparison.Ordinal) || title.Contains(": " + typed, StringComparison.Ordinal) || title.Contains("TO " + typed, StringComparison.Ordinal) ? 100 : 0;
                score += words.Count(w => title.Split(' ').Any(t => t.StartsWith(w, StringComparison.Ordinal))) * 10;
                score += c.Kind switch { "Screen" => 4, "Action" => 3, "Report" => 2, _ => 1 };
                return (c, score);
            })
            .Where(x => x.Item2 >= 0)
            .OrderByDescending(x => x.Item2)
            .Select(x => x.c)
            .ToList();
    }

    private static DataTemplate RowTemplate()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.SetValue(MarginProperty, new Thickness(4, 6, 4, 6));
        var kind = new FrameworkElementFactory(typeof(TextBlock));
        kind.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PaletteCommand.Kind)));
        kind.SetValue(DockPanel.DockProperty, Dock.Right);
        kind.SetValue(TextBlock.ForegroundProperty, Application.Current.FindResource("Muted"));
        kind.SetValue(TextBlock.FontSizeProperty, 14.0);
        kind.SetValue(TextBlock.MarginProperty, new Thickness(12, 0, 0, 0));
        kind.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(kind);
        var key = new FrameworkElementFactory(typeof(TextBlock));
        key.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PaletteCommand.Shortcut)));
        key.SetValue(DockPanel.DockProperty, Dock.Right);
        key.SetValue(TextBlock.ForegroundProperty, Application.Current.FindResource("Muted"));
        key.SetValue(TextBlock.FontSizeProperty, 14.0);
        key.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(key);
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PaletteCommand.Title)));
        title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        row.AppendChild(title);
        return new DataTemplate { VisualTree = row };
    }

    /// <summary>What the signed-in user may go to and do.</summary>
    private static List<PaletteCommand> Commands(MainWindow main)
    {
        var list = new List<PaletteCommand>();
        bool Has(string key) => main.AllowedEntries.Any(e => e.Key == key);
        T? Open<T>(string key) where T : UserControl => main.Navigate(key) ? main.GetView<T>() : null;

        foreach (var entry in main.AllowedEntries)
            list.Add(new PaletteCommand("Go to " + entry.Title, "Screen", entry.Key, () => main.Navigate(entry.Key), entry.Shortcut));

        if (Has("pos"))
        {
            list.Add(new PaletteCommand("New sale", "Action", "sell till point of sale", () => main.Navigate("pos")));
            if (AppServices.Can(Permissions.PosEndOfDay)) list.Add(new PaletteCommand("End of day", "Action", "close drawer z report", () => Open<PosView>("pos")?.EndOfDay()));
            if (AppServices.Can(Permissions.PosDrawer)) list.Add(new PaletteCommand("Drawer report", "Action", "x report cash", () => Open<PosView>("pos")?.DrawerReport()));
            if (AppServices.Can(Permissions.PosNoSale)) list.Add(new PaletteCommand("Open the drawer (no sale)", "Action", "no sale cash", () => Open<PosView>("pos")?.NoSale()));
        }
        if (Has("inventory") && AppServices.Can(Permissions.InventoryEdit))
            list.Add(new PaletteCommand("New item", "Action", "add product inventory stock", () => Open<InventoryView>("inventory")?.NewItem()));
        if (Has("clients") && AppServices.Can(Permissions.ClientsEdit))
            list.Add(new PaletteCommand("New client", "Action", "add customer", () => Open<ClientsView>("clients")?.NewParty()));
        if (Has("tools") && AppServices.Can(Permissions.BackupManage))
            list.Add(new PaletteCommand("Back up the database now", "Action", "backup copy save", () => Open<ToolsView>("tools")?.BackUpNow()));
        if (AppServices.Can(Permissions.TimeClockPunch))
            list.Add(new PaletteCommand("Punch in or out", "Action", "time clock shift start end", () => new PunchWindow { Owner = main }.ShowDialog(), "Ctrl+T"));
        list.Add(new PaletteCommand("Switch user", "Action", "log in sign in out", main.SwitchUser, "Ctrl+Shift+U"));
        list.Add(new PaletteCommand("Keyboard keys", "Action", "shortcuts hotkeys", main.ShowKeys, "Ctrl+Shift+K"));

        if (Has("reports"))
            foreach (var report in ReportService.All.Where(d => AppServices.Can(d.Permission ?? Permissions.ReportsView)))
                list.Add(new PaletteCommand("Report: " + report.Name, "Report", report.Group + " report", () => _ = Open<ReportsView>("reports")?.ShowReportAsync(report.Key)));

        foreach (var topic in HelpTopics.All)
            list.Add(new PaletteCommand("Help: " + topic.Title, "Help", topic.Keywords + " how", () => Open<HelpView>("help")?.ShowTopic(topic.Key)));

        // Supply chain (phase E2)
        if (Has("po"))
            list.Add(new PaletteCommand("Purchase requisitions", "Action", "requisition request ask approve buy order",
                () => Open<PurchaseOrdersView>("po")?.ShowRequisitions()));
        return list;
    }
}
