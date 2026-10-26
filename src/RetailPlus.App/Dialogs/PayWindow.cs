using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.App.Dialogs;

/// <summary>
/// The Pay window: one or more tenders (split payment), change for cash, cash rounding to the minimum tender when the
/// whole sale is paid in cash, and partial payment for layaways.
/// </summary>
public sealed class PayWindow : Window
{
    private readonly decimal _total;
    private readonly decimal _minimumTender;
    private readonly bool _hasClient;
    private readonly bool _layaway;
    private readonly ObservableCollection<TenderEntry> _tenders = [];
    private readonly TextBox _amount = new() { FontSize = 32, MinHeight = 64, HorizontalContentAlignment = HorizontalAlignment.Right, Padding = new Thickness(14, 8, 14, 8) };
    private readonly TextBlock _due = new() { FontSize = 56, FontFamily = new FontFamily("Century Gothic, Segoe UI"), Foreground = Infrastructure.Look.Brush("Sun") };
    private readonly TextBlock _dueLabel = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xB5, 0xAC)), FontSize = 18 };
    private readonly TextBlock _change = new() { FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Infrastructure.Look.Brush("Ok"), Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _finish = new() { Content = "Finish sale", MinWidth = 190, MinHeight = 60, FontSize = 19, IsEnabled = false };
    private static CultureInfo C => CultureInfo.CurrentCulture;

    public IReadOnlyList<TenderEntry> Tenders => _tenders;

    public PayWindow(decimal total, IReadOnlyList<TenderType> tenderTypes, decimal minimumTender, bool hasClient, bool layaway)
    {
        _total = total;
        _minimumTender = minimumTender;
        _hasClient = hasClient;
        _layaway = layaway;
        Title = layaway ? "Layaway deposit" : "Pay";
        Width = 760;
        SizeToContent = SizeToContent.Height;
        Owner = Infrastructure.Ui.Owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        var dark = new Border { Style = (Style)Application.Current.FindResource("DarkCard"), Padding = new Thickness(26, 18, 26, 18) };
        var darkPanel = new StackPanel();
        darkPanel.Children.Add(_dueLabel);
        darkPanel.Children.Add(_due);
        dark.Child = darkPanel;
        root.Children.Add(dark);
        root.Children.Add(new TextBlock { Text = "How much did the customer give?", FontSize = 18, Margin = new Thickness(4, 18, 0, 6) });
        root.Children.Add(_amount);
        root.Children.Add(new TextBlock { Text = "Then touch how they paid:", FontSize = 18, Margin = new Thickness(4, 16, 0, 6) });
        var keys = new WrapPanel();
        foreach (var t in tenderTypes)
        {
            var b = new Button { Content = t.Name, MinWidth = 150, MinHeight = 76, Margin = new Thickness(0, 0, 10, 10), Tag = t, FontSize = 18 };
            b.Style = (Style)Application.Current.FindResource(t.Kind == TenderKind.Cash ? "AccentButton" : "ToolButton");
            b.Margin = new Thickness(0, 0, 10, 10);
            Infrastructure.Look.SetCornerRadius(b, new CornerRadius(20));
            Infrastructure.Look.SetIcon(b, t.Kind switch
            {
                TenderKind.Cash => "",
                TenderKind.Card => "",
                TenderKind.Cheque => "",
                TenderKind.OnAccount => "",
                TenderKind.GiftCard => "",
                _ => "",
            });
            b.Click += (_, _) => AddTender((TenderType)b.Tag);
            keys.Children.Add(b);
        }
        root.Children.Add(keys);
        var list = new ListBox { ItemsSource = _tenders, MaxHeight = 170, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        _tenders.CollectionChanged += (_, _) => list.Visibility = _tenders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        list.ItemTemplate = new DataTemplate { VisualTree = TenderTemplate() };
        root.Children.Add(list);
        root.Children.Add(_change);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var clear = new Button { Content = "Undo last payment", MinHeight = 60, Margin = new Thickness(0, 0, 10, 0) };
        Infrastructure.Look.SetIcon(clear, "");
        clear.Click += (_, _) => { if (_tenders.Count > 0) _tenders.RemoveAt(_tenders.Count - 1); Update(); };
        _finish.Style = (Style)Application.Current.FindResource("DarkButton");
        _finish.MinHeight = 60;
        _finish.Margin = new Thickness(0, 0, 10, 0);
        Infrastructure.Look.SetIcon(_finish, "");
        _finish.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(clear);
        buttons.Children.Add(_finish);
        var back = new Button { Content = "Back to sale", IsCancel = true, MinHeight = 60 };
        Infrastructure.Look.SetIcon(back, "");
        buttons.Children.Add(back);
        root.Children.Add(buttons);
        Content = root;
        Update();
        Loaded += (_, _) => { _amount.Focus(); _amount.SelectAll(); };
        _amount.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            var cash = tenderTypes.FirstOrDefault(t => t.Kind == TenderKind.Cash);
            if (Remaining(cash?.Kind ?? TenderKind.Cash) == 0 && _finish.IsEnabled) DialogResult = true;
            else if (cash is not null) AddTender(cash);
        };
    }

    private static FrameworkElementFactory TenderTemplate()
    {
        var f = new FrameworkElementFactory(typeof(TextBlock));
        f.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new TenderText() });
        f.SetValue(TextBlock.FontSizeProperty, 19.0);
        f.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas"));
        return f;
    }

    private sealed class TenderText : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) => value is TenderEntry e
            ? $"{e.TenderType,-14} {e.Amount.ToString("N2", c),10}" + (e.Change > 0 ? $"   (tendered {e.Tendered.ToString("N2", c)}, change {e.Change.ToString("N2", c)})" : "")
            : "";
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    private bool AllCash(TenderKind next) => next == TenderKind.Cash && _tenders.All(t => t.Kind == TenderKind.Cash);

    /// <summary>What is still owing if the next tender is of the given kind (cash-only sales are rounded).</summary>
    private decimal Remaining(TenderKind next)
    {
        decimal target = AllCash(next) ? Money.RoundToMinimumTender(_total, _minimumTender) : _total;
        return target - _tenders.Sum(t => t.Amount);
    }

    private void AddTender(TenderType type)
    {
        if (type.Kind == TenderKind.OnAccount && !_hasClient)
        {
            Infrastructure.Ui.Error("Select a client before charging to an account.");
            return;
        }
        decimal remaining = Remaining(type.Kind);
        if (!decimal.TryParse(_amount.Text, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, C, out var entered)) entered = remaining;
        if (remaining == 0 && _total != 0) return;
        TenderEntry entry;
        if (remaining < 0 || _total < 0)
        {
            // A refund: pay the customer back.
            entry = new TenderEntry(type.Name, type.Kind, remaining, remaining);
        }
        else if (type.Kind == TenderKind.Cash)
        {
            decimal amount = Math.Min(entered, remaining);
            entry = new TenderEntry(type.Name, type.Kind, amount, Math.Max(entered, amount));
        }
        else
        {
            if (entered > remaining) entered = remaining;
            string? auth = type.Kind == TenderKind.Card ? Infrastructure.Ui.Prompt("Authorization number (optional):", type.Name) : null;
            entry = new TenderEntry(type.Name, type.Kind, entered, entered, string.IsNullOrWhiteSpace(auth) ? null : auth);
        }
        if (entry.Amount == 0) return;
        _tenders.Add(entry);
        Update();
        if (Settled && !_layaway) DialogResult = true;
    }

    /// <summary>Paid in full: to the rounded total when everything was cash, to the cent otherwise.</summary>
    private bool Settled
    {
        get
        {
            if (_tenders.Count == 0) return false;
            bool allCash = _tenders.All(t => t.Kind == TenderKind.Cash);
            decimal target = allCash ? Money.RoundToMinimumTender(_total, _minimumTender) : _total;
            return Math.Abs(target - _tenders.Sum(t => t.Amount)) < 0.005m;
        }
    }

    private void Update()
    {
        decimal paid = _tenders.Sum(t => t.Amount);
        bool cashSoFar = _tenders.Count == 0 || _tenders.All(t => t.Kind == TenderKind.Cash);
        decimal target = cashSoFar ? Money.RoundToMinimumTender(_total, _minimumTender) : _total;
        decimal remaining = (_tenders.Count == 0 ? _total : target) - paid;
        _dueLabel.Text = _tenders.Count == 0 ? (_total < 0 ? "Refund due" : "Amount due") : remaining < 0 ? "Refund due" : "Still owing";
        _due.Text = remaining.ToString("N2", C);
        if (cashSoFar && target != _total && _tenders.Count == 0) _dueLabel.Text += $"  (cash: {target.ToString("N2", C)})";
        _amount.Text = Math.Max(0, remaining).ToString("0.00", C);
        var change = _tenders.Sum(t => t.Change);
        _change.Text = change > 0 ? $"Give back (change):  {change.ToString("N2", C)}" : "";
        bool settled = Settled;
        _finish.IsEnabled = settled || (_layaway && _tenders.Count > 0) || (_total == 0);
        if (_layaway) _finish.Content = settled ? "Finish layaway" : "Leave rest owing";
    }
}
