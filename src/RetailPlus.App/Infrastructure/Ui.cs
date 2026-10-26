using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RetailPlus.Data.Services;
using RetailPlus.Reports;

namespace RetailPlus.App.Infrastructure;

/// <summary>Message boxes, input prompts, file dialogs and printing, in one place so automated runs can skip them.</summary>
public static class Ui
{
    public static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    public static void Info(string message, string title = "Retail Plus")
    {
        if (AppServices.Unattended) { Log.Write("INFO " + message); return; }
        new MessageWindow(title, message, MessageKind.Info).ShowDialog();
    }

    public static void Error(string message, string title = "Retail Plus")
    {
        if (AppServices.Unattended) { Log.Write("ERROR " + message); return; }
        new MessageWindow(title, message, MessageKind.Warning).ShowDialog();
    }

    public static bool Confirm(string message, string title = "Retail Plus")
    {
        if (AppServices.Unattended) return true;
        return new MessageWindow(title, message, MessageKind.Question).ShowDialog() == true;
    }

    /// <summary>Runs an operation and turns the errors a user can act on into messages.</summary>
    public static async Task<bool> Try(Func<Task> action)
    {
        var owner = Owner;
        try
        {
            if (owner is not null) owner.Cursor = Cursors.Wait;
            await action();
            return true;
        }
        catch (BusinessRuleException ex) { Error(ex.Message); }
        catch (ConcurrencyConflictException ex) { Error(ex.Message, "Changed at another station"); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) { Error("The database refused the change: " + (ex.InnerException?.Message ?? ex.Message)); Log.Write(ex.ToString()); }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Error("Database error: " + ex.Message); Log.Write(ex.ToString()); }
        catch (Exception ex) { Error("Something went wrong: " + ex.Message); Log.Write(ex.ToString()); }
        finally
        {
            if (owner is not null) owner.Cursor = null;
        }
        return false;
    }

    /// <summary>A one-line input box. Returns null when cancelled.</summary>
    public static string? Prompt(string question, string title = "Retail Plus", string initial = "", bool password = false)
    {
        var win = new Window
        {
            Title = title, Owner = Owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, MinWidth = 520,
        };
        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
        panel.Children.Add(new TextBlock { Text = title, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 24, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(new TextBlock { Text = question, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap, MaxWidth = 560, FontSize = 18 });
        Control input = password ? new PasswordBox { FontSize = 20, MinHeight = 52 } : new TextBox { Text = initial, FontSize = 20, MinHeight = 52 };
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 130, MinHeight = 54, Margin = new Thickness(0, 0, 10, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        Look.SetIcon(ok, "");
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 130, MinHeight = 54 };
        ok.Click += (_, _) => win.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        win.Content = panel;
        win.Loaded += (_, _) =>
        {
            input.Focus();
            if (input is TextBox tb) tb.SelectAll();
        };
        if (win.ShowDialog() != true) return null;
        return input is PasswordBox pb ? pb.Password : ((TextBox)input).Text;
    }

    public static decimal? PromptDecimal(string question, string title = "Retail Plus", decimal? initial = null)
    {
        while (true)
        {
            var text = Prompt(question, title, initial?.ToString("0.##", CultureInfo.CurrentCulture) ?? "");
            if (text is null) return null;
            if (decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.CurrentCulture, out var d)) return d;
            Error("Please enter a number.");
        }
    }

    /// <summary>Picks one entry from a list. Returns the index or -1.</summary>
    public static int Choose(string question, IReadOnlyList<string> options, string title = "Retail Plus")
    {
        var win = new Window
        {
            Title = title, Owner = Owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 600, Height = 560, ShowInTaskbar = false,
        };
        var grid = new Grid { Margin = new Thickness(24, 20, 24, 20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = question, Margin = new Thickness(0, 0, 0, 12), FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 22, TextWrapping = TextWrapping.Wrap });
        var list = new ListBox { ItemsSource = options, SelectedIndex = options.Count > 0 ? 0 : -1, FontSize = 19, Background = (Brush)Application.Current.FindResource("Card") };
        Grid.SetRow(list, 1);
        grid.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Choose", IsDefault = true, MinWidth = 130, MinHeight = 54, Margin = new Thickness(0, 0, 10, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        Look.SetIcon(ok, "");
        ok.Click += (_, _) => win.DialogResult = list.SelectedIndex >= 0;
        list.MouseDoubleClick += (_, _) => win.DialogResult = list.SelectedIndex >= 0;
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 130, MinHeight = 54 });
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        win.Content = grid;
        win.Loaded += (_, _) => list.Focus();
        return win.ShowDialog() == true ? list.SelectedIndex : -1;
    }

    public static string? SaveFile(string defaultName, string filter)
    {
        var dlg = new SaveFileDialog { FileName = defaultName, Filter = filter, InitialDirectory = EnsureFolder(AppServices.Config.ExportFolderOrDefault) };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public static string? OpenFile(string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public static string? PickFolder(string title)
    {
        var dlg = new OpenFolderDialog { Title = title };
        return dlg.ShowDialog(Owner) == true ? dlg.FolderName : null;
    }

    private static string EnsureFolder(string folder)
    {
        try { Directory.CreateDirectory(folder); } catch (IOException) { }
        return folder;
    }

    /// <summary>Prints monospaced text (receipts, statements, drawer reports) on a page printer.</summary>
    public static void PrintText(string text, string title)
    {
        if (AppServices.Unattended) return;
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        var doc = new FlowDocument(new Paragraph(new Run(text)) { FontFamily = new FontFamily("Consolas"), FontSize = 11 })
        {
            PagePadding = new Thickness(48), ColumnWidth = double.PositiveInfinity, PageWidth = dlg.PrintableAreaWidth, PageHeight = dlg.PrintableAreaHeight,
        };
        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, title);
    }

    /// <summary>Prints a report as a table.</summary>
    public static void PrintReport(ReportTable report)
    {
        if (AppServices.Unattended) return;
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        var culture = CultureInfo.CurrentCulture;
        var doc = new FlowDocument { PagePadding = new Thickness(40), ColumnWidth = double.PositiveInfinity, FontFamily = new FontFamily("Segoe UI"), FontSize = 10,
            PageWidth = dlg.PrintableAreaWidth, PageHeight = dlg.PrintableAreaHeight };
        doc.Blocks.Add(new Paragraph(new Run(report.Title)) { FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0) });
        doc.Blocks.Add(new Paragraph(new Run($"{report.Subtitle} · {AppServices.Settings.CompanyName} · {report.GeneratedAt:g}")) { Foreground = Brushes.Gray });
        var table = new Table { CellSpacing = 0 };
        foreach (var _ in report.Columns) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        group.Rows.Add(Row(report.Columns.Select(c => c.Header), report, true));
        foreach (var r in report.Rows) group.Rows.Add(Row(r.Select((v, i) => report.Columns[i].Format(v, culture)), report, false));
        if (report.HasTotals) group.Rows.Add(Row(report.TotalsRow().Select((v, i) => report.Columns[i].Format(v, culture)), report, true));
        table.RowGroups.Add(group);
        doc.Blocks.Add(table);
        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, report.Title);
    }

    private static TableRow Row(IEnumerable<string> cells, ReportTable report, bool bold)
    {
        var row = new TableRow();
        int i = 0;
        foreach (var c in cells)
        {
            var p = new Paragraph(new Run(c)) { Margin = new Thickness(2), TextAlignment = report.Columns[i].RightAligned ? TextAlignment.Right : TextAlignment.Left };
            if (bold) p.FontWeight = FontWeights.SemiBold;
            row.Cells.Add(new TableCell(p) { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 0.5) });
            i++;
        }
        return row;
    }
}

/// <summary>A small log file next to the station settings, for errors and automated runs.</summary>
public static class Log
{
    public static string FilePath { get; set; } = Path.Combine(Settings.StationConfig.DefaultFolder, "retailplus.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }
}
