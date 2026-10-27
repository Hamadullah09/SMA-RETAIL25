using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Dialogs;

/// <summary>A gift card sold at the POS: its number, the amount put on it and, for a new number, card or certificate.</summary>
public sealed record GiftCardEntry(string Number, decimal Amount, GiftCardKind Kind);

/// <summary>
/// Selling a gift card item: scan or type the card's number and the amount. A number this till does not know is a new
/// card (or a certificate, which can be given the next number); a known card is reloaded.
/// </summary>
public sealed class GiftCardSaleWindow : Window
{
    private readonly TextBox _number = new() { FontSize = 22, MinHeight = 54 };
    private readonly TextBox _amount = new() { FontSize = 22, MinHeight = 54, HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly RadioButton _card = new() { Content = "Gift card (can be reloaded)", IsChecked = true, Margin = new Thickness(0, 0, 18, 0), GroupName = "kind" };
    private readonly RadioButton _certificate = new() { Content = "Gift certificate (once)", GroupName = "kind" };
    private readonly TextBlock _status = new() { FontSize = 17, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly System.Windows.Threading.DispatcherTimer _lookup = new() { Interval = TimeSpan.FromMilliseconds(350) };

    public GiftCardEntry? Result { get; private set; }

    public GiftCardSaleWindow(string itemName, decimal? amount = null)
    {
        Title = itemName;
        Width = 640;
        SizeToContent = SizeToContent.Height;
        Owner = Ui.Owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        root.Children.Add(new TextBlock { Text = "Sell a gift card", Style = (Style)Application.Current.FindResource("SectionTitle") });
        root.Children.Add(new TextBlock { Text = "Card number (scan it, or type it)", Margin = new Thickness(0, 4, 0, 6) });
        var numberRow = new DockPanel();
        var newNumber = new Button { Content = "Next number", MinHeight = 54, Margin = new Thickness(10, 0, 0, 0), ToolTip = "For a paper certificate without a number of its own" };
        newNumber.Click += async (_, _) => await Ui.Try(async () =>
        {
            _number.Text = await AppServices.GiftCards.NewNumberAsync();
            _certificate.IsChecked = true;
        });
        DockPanel.SetDock(newNumber, Dock.Right);
        numberRow.Children.Add(newNumber);
        numberRow.Children.Add(_number);
        root.Children.Add(numberRow);
        var kinds = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        kinds.Children.Add(_card);
        kinds.Children.Add(_certificate);
        root.Children.Add(kinds);
        root.Children.Add(new TextBlock { Text = "Amount to put on it", Margin = new Thickness(0, 14, 0, 6) });
        _amount.Text = amount is > 0 ? amount.Value.ToString("0.00", CultureInfo.CurrentCulture) : "";
        root.Children.Add(_amount);
        if (AppServices.Settings.GiftCardMaximum > 0)
            root.Children.Add(new TextBlock { Text = $"A card can hold at most {AppServices.Settings.GiftCardMaximum.ToString("N2", CultureInfo.CurrentCulture)}.", Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 6, 0, 0) });
        root.Children.Add(_status);
        root.Children.Add(new TextBlock { Text = GiftCardService.OfflineNote, Foreground = Look.Brush("Muted"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var ok = new Button { Content = "Add to the sale", IsDefault = true, MinHeight = 54, MinWidth = 170, Margin = new Thickness(0, 0, 10, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        ok.Click += async (_, _) => await AcceptAsync();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinHeight = 54, MinWidth = 130 });
        root.Children.Add(buttons);
        Content = root;
        Views.Layout.AddIcons(this);
        _lookup.Tick += async (_, _) => { _lookup.Stop(); await LookUpAsync(); };
        _number.TextChanged += (_, _) => { _lookup.Stop(); _lookup.Start(); };
        Loaded += (_, _) => _number.Focus();
        Closed += (_, _) => _lookup.Stop();
    }

    /// <summary>Says whether the number is a new card or one to reload, and what is on it.</summary>
    private async Task LookUpAsync()
    {
        var number = GiftCardService.Normalize(_number.Text);
        if (number.Length == 0) { _status.Text = ""; return; }
        try
        {
            var card = await AppServices.GiftCards.FindAsync(number);
            _card.IsEnabled = _certificate.IsEnabled = card is null;
            if (card is null)
            {
                _status.Text = $"{number} is a new card.";
                _status.Foreground = Look.Brush("Ok");
                return;
            }
            (card.Card.Kind == GiftCardKind.Card ? _card : _certificate).IsChecked = true;
            bool usable = card.Card.IsActive && card.Card.Kind == GiftCardKind.Card;
            _status.Text = usable ? $"Reload: the card has {card.Balance.ToString("N2", CultureInfo.CurrentCulture)} on it."
                : card.Card.Kind == GiftCardKind.Certificate ? $"{number} is a gift certificate already sold; a certificate is not reloaded."
                : $"Card {number} is deactivated.";
            _status.Foreground = Look.Brush(usable ? "Ink" : "Danger");
        }
        catch (Exception ex) when (ex is BusinessRuleException or InvalidOperationException)
        {
            _status.Text = ex.Message;
        }
    }

    private async Task AcceptAsync()
    {
        var number = GiftCardService.Normalize(_number.Text);
        if (number.Length == 0) { Ui.Error("Scan or type the card number."); return; }
        if (!decimal.TryParse(_amount.Text, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            Ui.Error("Type the amount to put on the card, for example 50.");
            return;
        }
        if (!await Ui.Try(() => AppServices.GiftCards.CheckSaleAsync(number, amount))) return;
        Result = new GiftCardEntry(number, amount, _certificate.IsChecked == true ? GiftCardKind.Certificate : GiftCardKind.Card);
        DialogResult = true;
    }
}
