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
    private readonly TextBox _amount = new() { FontSize = 26, HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _due = new() { FontSize = 34, FontWeight = FontWeights.Bold };
    private readonly TextBlock _dueLabel = new() { Foreground = Brushes.Gray };
    private readonly TextBlock _change = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkGreen };
    private readonly Button _finish = new() { Content = "Finish sale", MinWidth = 140, FontSize = 16, Padding = new Thickness(10, 8, 10, 8), IsEnabled = false };
    private static CultureInfo C => CultureInfo.CurrentCulture;

    public IReadOnlyList<TenderEntry> Tenders => _tenders;

    public PayWindow(decimal total, IReadOnlyList<TenderType> tenderTypes, decimal minimumTender, bool hasClient, bool layaway)
    {
        _total = total;
        _minimumTender = minimumTender;
        _hasClient = hasClient;
        _layaway = layaway;
        Title = layaway ? "Layaway deposit" : "Pay";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        Owner = Infrastructure.Ui.Owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(_dueLabel);
        root.Children.Add(_due);
        root.Children.Add(new TextBlock { Text = "Amount tendered", Margin = new Thickness(0, 12, 0, 4) });
        root.Children.Add(_amount);
        var keys = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var t in tenderTypes)
        {
            var b = new Button { Content = t.Name, MinWidth = 108, Padding = new Thickness(8, 12, 8, 12), Margin = new Thickness(0, 0, 8, 8), Tag = t, FontSize = 14 };
            if (t.Kind == TenderKind.Cash) b.Style = (Style)Application.Current.FindResource("AccentButton");
            b.Click += (_, _) => AddTender((TenderType)b.Tag);
            keys.Children.Add(b);
        }
        root.Children.Add(keys);
        var list = new ListBox { ItemsSource = _tenders, Height = 110, Margin = new Thickness(0, 6, 0, 0) };
        list.ItemTemplate = new DataTemplate { VisualTree = TenderTemplate() };
        root.Children.Add(list);
        root.Children.Add(_change);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var clear = new Button { Content = "Remove last payment", Margin = new Thickness(0, 0, 8, 0) };
        clear.Click += (_, _) => { if (_tenders.Count > 0) _tenders.RemoveAt(_tenders.Count - 1); Update(); };
        _finish.Style = (Style)Application.Current.FindResource("AccentButton");
        _finish.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(clear);
        buttons.Children.Add(_finish);
        buttons.Children.Add(new Button { Content = "Back to sale", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) });
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
        f.SetValue(TextBlock.FontSizeProperty, 14.0);
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
        _change.Text = change > 0 ? $"Change: {change.ToString("N2", C)}" : "";
        bool settled = Settled;
        _finish.IsEnabled = settled || (_layaway && _tenders.Count > 0) || (_total == 0);
        if (_layaway) _finish.Content = settled ? "Finish layaway" : "Leave rest owing";
    }
}
