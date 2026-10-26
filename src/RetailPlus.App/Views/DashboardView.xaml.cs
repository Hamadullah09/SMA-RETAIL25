using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Views;

/// <summary>The home screen: today's sales, the week, the drawer, stock health and what needs attention.</summary>
public partial class DashboardView : UserControl, IRefreshable
{
    private static CultureInfo C => CultureInfo.CurrentCulture;

    public DashboardView() => InitializeComponent();

    private MainWindow? Main => Window.GetWindow(this) as MainWindow;

    public async Task RefreshAsync()
    {
        var d = await new DashboardService(AppServices.Data).LoadAsync(AppServices.Session);
        var s = AppServices.Session;
        int hour = DateTime.Now.Hour;
        string part = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        Greeting.Text = $"{part}, {FirstName(s.UserName)}";
        Subtitle.Text = d.CustomersToday == 0
            ? "Nothing sold yet today. Everything you need is one click away."
            : $"{d.CustomersToday} customer{(d.CustomersToday == 1 ? "" : "s")} so far today" + Compare(d.SalesToday, d.SalesYesterday);
        StatSales.Text = d.SalesToday.ToString("N2", C);
        StatCustomers.Text = d.CustomersToday.ToString(C);
        StatItems.Text = d.ItemCount.ToString("N0", C);
        BigInitials.Text = string.Concat(s.UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        CardUser.Text = s.UserName;
        CardLevel.Text = $"Station {s.StationNumber:000}  ·  {s.LocationCode}";

        BuildPayments(d);
        BuildWeek(d);
        BuildDrawer(d);
        BuildStock(d);
        BuildGlance(d);
        BuildRecent(d);
    }

    private static string FirstName(string user) => user.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts
        ? C.TextInfo.ToTitleCase(parts[0].ToLower(C)) : user;

    private static string Compare(decimal today, decimal yesterday)
    {
        if (yesterday <= 0) return ".";
        var change = (today - yesterday) / yesterday * 100m;
        return change >= 0 ? $", {change:0}% more than yesterday." : $", {-change:0}% less than yesterday.";
    }

    private void BuildPayments(DashboardData d)
    {
        PaymentsBar.Children.Clear();
        PaymentsBar.ColumnDefinitions.Clear();
        PaymentsCaption.Text = "Average sale today";
        AverageSale.Text = d.AverageSaleToday.ToString("N2", C);
        var shares = d.Payments.Take(4).ToList();
        if (shares.Count == 0)
        {
            shares = [new TenderShare("Cash", 0, 0), new TenderShare("Card", 0, 0), new TenderShare("On account", 0, 0)];
        }
        Brush[] fills = [Look.Brush("Charcoal"), Look.Brush("Sun"), Look.Brush("Stripes"), Brushes.Transparent];
        Brush[] texts = [Brushes.White, Look.Brush("Ink"), Look.Brush("Ink"), Look.Brush("Ink")];
        for (int i = 0; i < shares.Count; i++)
        {
            var share = shares[i];
            double weight = Math.Max(14, (double)share.Percent);
            PaymentsBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star), MinWidth = 120 });
            var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = share.Name + (share.Amount != 0 ? $"  ·  {share.Amount.ToString("N2", C)}" : ""),
                Foreground = Look.Brush("Muted"), Margin = new Thickness(4, 0, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var pill = new Border
            {
                Background = fills[i], CornerRadius = new CornerRadius(24), MinHeight = 48, Padding = new Thickness(18, 0, 18, 0),
                BorderBrush = i == 3 ? Look.Brush("Stone") : null, BorderThickness = new Thickness(i == 3 ? 1.5 : 0),
                ToolTip = d.PaymentsAreToday ? "Share of today's payments" : "Share of the last 30 days' payments",
            };
            pill.Child = new TextBlock { Text = share.Percent.ToString("0", C) + "%", Foreground = texts[i], FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(pill);
            Grid.SetColumn(panel, i);
            PaymentsBar.Children.Add(panel);
        }
    }

    private sealed record Bar(string Label, double BarHeight, Brush BarBrush, bool IsToday, string ValueText, string ToolTip);

    private void BuildWeek(DashboardData d)
    {
        decimal max = Math.Max(1, d.Week.Max(x => x.Total));
        WeekTotal.Text = d.Week.Sum(x => x.Total).ToString("N2", C);
        var today = DateOnly.FromDateTime(DateTime.Today);
        WeekBars.ItemsSource = d.Week.Select(x => new Bar(
            x.Day.ToString("ddd", C)[..Math.Min(3, x.Day.ToString("ddd", C).Length)],
            x.Total <= 0 ? 8 : 12 + (double)(x.Total / max) * 130,
            x.Day == today ? Look.Brush("Sun") : x.Total <= 0 ? Look.Brush("StoneLight") : Look.Brush("Charcoal"),
            x.Day == today,
            x.Total.ToString("N0", C),
            $"{x.Day.ToString("D", C)}: {x.Total.ToString("N2", C)} from {x.Customers} sale(s)")).ToList();
    }

    private void BuildDrawer(DashboardData d)
    {
        DrawerCash.Text = d.ExpectedCash.ToString("N2", C);
        if (d.DrawerOpenedAt is { } opened)
        {
            var open = DateTime.Now - opened;
            DrawerRing.Fraction = Math.Min(1, open.TotalHours / 12);
            DrawerCaption.Text = $"Open since {opened.ToString("t", C)} ({(int)open.TotalHours}h {open.Minutes:00}m)\nTaken in: {d.DrawerReceipts.ToString("N2", C)}";
        }
        else
        {
            DrawerRing.Fraction = 0;
            DrawerCaption.Text = "The drawer opens with the first sale of the day.";
        }
    }

    private sealed record ReorderRow(string Glyph, string Name, string Detail, Brush StatusBrush);

    private void BuildStock(DashboardData d)
    {
        int stocked = Math.Max(1, d.ItemsInStock + d.ItemsLow + d.ItemsOut);
        int okPct = (int)Math.Round(d.ItemsInStock * 100.0 / stocked);
        int lowPct = (int)Math.Round(d.ItemsLow * 100.0 / stocked);
        int outPct = Math.Max(0, 100 - okPct - lowPct);
        StockPercent.Text = $"{okPct}%";
        StockBar.Children.Clear();
        StockBar.ColumnDefinitions.Clear();
        (string Label, int Pct, int Count, Brush Fill, Brush Text)[] parts =
        [
            ("In stock", okPct, d.ItemsInStock, Look.Brush("Sun"), Look.Brush("Ink")),
            ("Low", lowPct, d.ItemsLow, Look.Brush("Charcoal"), Brushes.White),
            ("Out", outPct, d.ItemsOut, Look.Brush("Stone"), Brushes.White),
        ];
        int col = 0;
        foreach (var p in parts)
        {
            StockBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(24, p.Pct), GridUnitType.Star) });
            var stack = new DockPanel { Margin = new Thickness(0, 0, 4, 0) };
            var label = new TextBlock { Text = p.Label, Foreground = Look.Brush("Muted"), FontSize = 14, Margin = new Thickness(2, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis };
            DockPanel.SetDock(label, Dock.Top);
            stack.Children.Add(label);
            var bar = new Border { Background = p.Fill, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 0, 8, 0), ToolTip = $"{p.Count} items" };
            bar.Child = new TextBlock { Text = $"{p.Pct}%", Foreground = p.Text, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            stack.Children.Add(bar);
            Grid.SetColumn(stack, col++);
            StockBar.Children.Add(stack);
        }
        int need = d.ItemsLow + d.ItemsOut;
        ReorderCount.Text = $"{need}/{d.ItemsInStock + need}";
        NoReorder.Visibility = d.ToReorder.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReorderList.ItemsSource = d.ToReorder.Take(6).Select(i => new ReorderRow(
            i.OnHand <= 0 ? "" : "", i.Name,
            i.OnHand <= 0 ? $"{i.Plu} · none left" : $"{i.Plu} · {i.OnHand:0.##} left, reorder at {i.ReorderPoint:0.##}",
            i.OnHand <= 0 ? Look.Brush("Danger") : Look.Brush("Sun"))).ToList();
    }

    private void BuildGlance(DashboardData d)
    {
        Glance.Children.Clear();
        Glance.Children.Add(new TextBlock { Text = "Needs attention", Style = (Style)FindResource("SectionTitle"), Margin = new Thickness(8, 4, 0, 6) });
        Add("", "Money owed to you", d.OwedToStore.ToString("N2", C) + (d.OpenInvoices > 0 ? $"  ({d.OpenInvoices})" : ""), "invoices");
        Add("", "Open purchase orders", d.OpenPurchaseOrders.ToString(C), "po");
        Add("", "Customer orders", d.OpenCustomerOrders.ToString(C), "orders");
        Add("", "Sales put on hold", d.SuspendedSales.ToString(C), "pos");

        void Add(string glyph, string text, string value, string target)
        {
            var b = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(8, 0, 16, 0), MinHeight = 54 };
            var row = new DockPanel();
            var chevron = new TextBlock { Text = "", Style = (Style)FindResource("Icon"), FontSize = 14, Margin = new Thickness(10, 0, 0, 0) };
            DockPanel.SetDock(chevron, Dock.Right);
            row.Children.Add(chevron);
            var val = new TextBlock { Text = value, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(val, Dock.Right);
            row.Children.Add(val);
            var circle = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = Look.Brush("SunLight"), Margin = new Thickness(0, 0, 12, 0) };
            circle.Child = new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 16 };
            DockPanel.SetDock(circle, Dock.Left);
            row.Children.Add(circle);
            row.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            b.Content = row;
            b.Click += (_, _) => Main?.Navigate(target);
            Glance.Children.Add(b);
        }
    }

    private sealed record RecentRow(string Time, string Title, string Detail, string TotalText, Brush PillBrush, Brush TextBrush);

    private void BuildRecent(DashboardData d)
    {
        NoSales.Visibility = d.Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentList.ItemsSource = d.Recent.Select((r, i) => new RecentRow(
            r.SoldAt.Date == DateTime.Today ? r.SoldAt.ToString("t", C) : r.SoldAt.ToString("d MMM", C),
            $"Sale {r.InvoiceNumber}" + (string.IsNullOrWhiteSpace(r.ClientName) ? "" : " · " + r.ClientName),
            $"{r.Lines} line{(r.Lines == 1 ? "" : "s")} · paid by {(string.IsNullOrWhiteSpace(r.Tenders) ? "—" : r.Tenders)}",
            r.Total.ToString("N2", C),
            i == 0 ? Look.Brush("Charcoal") : Look.Brush("Cream"),
            i == 0 ? Brushes.White : Look.Brush("Ink"))).ToList();
    }

    private void NewSale_Click(object sender, RoutedEventArgs e) => Main?.Navigate("pos");
    private void Reports_Click(object sender, RoutedEventArgs e) => Main?.Navigate("reports");
    private void Journal_Click(object sender, RoutedEventArgs e) => Main?.Navigate("journal");
    private void Reorder_Click(object sender, RoutedEventArgs e) => Main?.Navigate("po");

    private async void FindItem_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ItemPickerWindow();
        if (picker.ShowDialog() != true || picker.Selected is not { } row || Main is not { } main) return;
        main.Navigate("pos");
        await MainWindow.Settle();
        if (main.GetView<PosView>() is { } pos) await Ui.Try(() => pos.Vm.AddCodeAsync(row.Plu));
    }

    private async void NewClient_Click(object sender, RoutedEventArgs e)
    {
        var client = new Client { Country = AppServices.Settings.Country };
        if (new PartyEditorWindow(client) { Owner = Window.GetWindow(this) }.ShowDialog() == true) await Ui.Try(RefreshAsync);
    }
}
