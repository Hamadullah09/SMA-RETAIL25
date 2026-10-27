using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Views;

/// <summary>
/// Gift cards and certificates: find a card by its number (or its client), see its balance and history, deactivate it,
/// set when it expires, or correct its balance (a supervisor).
/// </summary>
public sealed class GiftCardsView : UserControl, IRefreshable
{
    private readonly TextBox _search = new() { Width = 340, Margin = new Thickness(0, 0, 12, 6), FontSize = 19 };
    private readonly CheckBox _activeOnly = new() { Content = "Active only", Margin = new Thickness(0, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid _cards = Layout.Grid(
        Layout.Col("Number", nameof(GiftCardRow.Number), 140),
        Layout.Col("Balance", nameof(GiftCardRow.Balance), 85, "N2", true),
        Layout.Col("Kind", nameof(GiftCardRow.Kind), 80),
        Layout.Col("Client", nameof(GiftCardRow.Client)),
        Layout.Col("Active", nameof(GiftCardRow.IsActive), 55),
        Layout.Col("Issued", nameof(GiftCardRow.IssuedAt), 90, "d"),
        Layout.Col("Expires", nameof(GiftCardRow.ExpiresOn), 90));
    private readonly DataGrid _history = Layout.Grid(
        Layout.Col("When", nameof(GiftCardTransaction.At), 125, "g"),
        Layout.Col("What", nameof(GiftCardTransaction.Kind), 70),
        Layout.Col("Amount", nameof(GiftCardTransaction.Amount), 85, "N2", true),
        Layout.Col("Invoice", nameof(GiftCardTransaction.InvoiceNumber), 90),
        Layout.Col("Station", nameof(GiftCardTransaction.StationNumber), 55),
        Layout.Col("By", nameof(GiftCardTransaction.User), 80),
        Layout.Col("Note", nameof(GiftCardTransaction.Note)));
    private readonly TextBlock _balance = new() { FontFamily = new System.Windows.Media.FontFamily("Century Gothic, Segoe UI"), FontSize = 40 };
    private readonly TextBlock _cardText = new() { Foreground = Look.Brush("Muted"), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _count = new() { Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 8, 0, 0) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private GiftCardDetail? _current;

    public GiftCardsView()
    {
        var left = new DockPanel();
        DockPanel.SetDock(_count, Dock.Bottom);
        left.Children.Add(_count);
        left.Children.Add(_cards);

        var right = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = "Balance", Foreground = Look.Brush("Muted") });
        head.Children.Add(_balance);
        head.Children.Add(_cardText);
        head.Children.Add(new TextBlock { Text = "History", Style = Layout.S("SectionTitle") });
        DockPanel.SetDock(head, Dock.Top);
        right.Children.Add(head);
        var note = new TextBlock { Text = GiftCardService.OfflineNote, Foreground = Look.Brush("Muted"), TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(note, Dock.Bottom);
        right.Children.Add(note);
        right.Children.Add(_history);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
        Grid.SetColumn(right, 1);
        body.Children.Add(left);
        body.Children.Add(right);
        Content = Layout.Page("Gift cards", out var bar, body);
        bar.Children.Add(Layout.Label("Card number or client"));
        bar.Children.Add(_search);
        bar.Children.Add(_activeOnly);
        bar.Children.Add(Layout.Button("Show", async (_, _) => await Ui.Try(RefreshAsync), true));
        bar.Children.Add(Layout.Button("Deactivate / activate", Active_Click));
        bar.Children.Add(Layout.Button("Expiry date…", Expiry_Click));
        bar.Children.Add(Layout.Button("Adjust balance…", Adjust_Click));

        _timer.Tick += async (_, _) => { _timer.Stop(); await Ui.Try(RefreshAsync); };
        _search.TextChanged += (_, _) => { _timer.Stop(); _timer.Start(); };
        _activeOnly.Click += async (_, _) => await Ui.Try(RefreshAsync);
        _cards.SelectionChanged += async (_, _) => await Ui.Try(LoadCurrentAsync);
    }

    public async Task RefreshAsync()
    {
        var selected = (_cards.SelectedItem as GiftCardRow)?.Id;
        var rows = await AppServices.GiftCards.SearchAsync(_search.Text, _activeOnly.IsChecked == true);
        _cards.ItemsSource = rows;
        _cards.SelectedItem = rows.FirstOrDefault(r => r.Id == selected) ?? rows.FirstOrDefault();
        var c = CultureInfo.CurrentCulture;
        _count.Text = $"{rows.Count:N0} card(s) shown · {rows.Sum(r => r.Balance).ToString("N2", c)} on them";
        if (_cards.SelectedItem is null) await LoadCurrentAsync();
    }

    private async Task LoadCurrentAsync()
    {
        _current = _cards.SelectedItem is GiftCardRow row ? await AppServices.GiftCards.FindAsync(row.Number) : null;
        var c = CultureInfo.CurrentCulture;
        if (_current is null)
        {
            _balance.Text = "";
            _cardText.Text = "Scan or type a card number to find it.";
            _history.ItemsSource = null;
            return;
        }
        var card = _current.Card;
        _balance.Text = _current.Balance.ToString("N2", c);
        _cardText.Text = $"{card.Kind} {card.Number} · issued {card.IssuedAt:d}"
                         + (card.ExpiresOn is DateOnly e ? $" · expires {e:d}" : " · does not expire")
                         + (card.IsActive ? "" : " · DEACTIVATED")
                         + (card.Client is { } client ? $" · {client.Company ?? $"{client.FirstName} {client.LastName}".Trim()}" : "");
        _history.ItemsSource = _current.History;
    }

    private async void Active_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { } card) return;
        bool active = !card.Card.IsActive;
        if (!active && !Ui.Confirm($"Deactivate card {card.Card.Number}? It cannot be spent or reloaded until it is made active again.")) return;
        if (!await LoginWindow.SupervisorApprovesAsync(active ? "Making a gift card active" : "Deactivating a gift card")) return;
        await Ui.Try(async () => { await AppServices.GiftCards.SetActiveAsync(card.Card.Id, active); await RefreshAsync(); });
    }

    private async void Expiry_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { } card) return;
        var text = Ui.Prompt("Expires on (a date, or leave empty for no expiry):", "Expiry date", card.Card.ExpiresOn?.ToString("d", CultureInfo.CurrentCulture) ?? "");
        if (text is null) return;
        DateOnly? expires = null;
        if (text.Trim().Length > 0)
        {
            if (!DateOnly.TryParse(text, CultureInfo.CurrentCulture, out var date)) { Ui.Error("Type a date, for example " + DateTime.Today.AddYears(1).ToString("d", CultureInfo.CurrentCulture) + "."); return; }
            expires = date;
        }
        await Ui.Try(async () => { await AppServices.GiftCards.SetExpiryAsync(card.Card.Id, expires); await RefreshAsync(); });
    }

    private async void Adjust_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { } card) return;
        if (!AppServices.Can(Permissions.PosApprove) && !await LoginWindow.SupervisorApprovesAsync("Correcting a gift card balance")) return;
        var amount = Ui.PromptDecimal($"Add to card {card.Card.Number} (a negative amount takes off; it has {card.Balance:N2}):", "Adjust balance");
        if (amount is null || amount == 0) return;
        var reason = Ui.Prompt("Reason:", "Adjust balance");
        if (string.IsNullOrWhiteSpace(reason)) return;
        await Ui.Try(async () => { await AppServices.GiftCards.AdjustAsync(card.Card.Id, amount.Value, reason, AppServices.Session.UserName); await RefreshAsync(); });
    }
}
