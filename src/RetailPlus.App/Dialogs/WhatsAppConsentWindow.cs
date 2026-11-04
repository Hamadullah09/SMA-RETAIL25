using System.Windows;
using System.Windows.Controls;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Rules;

namespace RetailPlus.App.Dialogs;

/// <summary>A client's answer to "may we send you offers on WhatsApp?": yes with the number, or no.</summary>
public sealed record WhatsAppAnswer(bool Agrees, string? Number);

/// <summary>
/// Asked at the POS when a client who never answered is chosen for a sale: the cashier puts the question and records the
/// answer. Yes needs the WhatsApp number in international form; No is kept too, so the client is not asked again; Ask
/// later records nothing. Messages go only to clients who said yes (phase E4).
/// </summary>
public sealed class WhatsAppConsentWindow : Window
{
    private readonly TextBox _number = new() { FontSize = 22, MinHeight = 54 };
    private readonly TextBlock _status = new() { FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public WhatsAppAnswer? Answer { get; private set; }

    public WhatsAppConsentWindow(string clientName, string? number)
    {
        Title = "WhatsApp messages";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        Owner = Ui.Owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        root.Children.Add(new TextBlock { Text = "Please ask the customer", Style = (Style)Application.Current.FindResource("SectionTitle") });
        root.Children.Add(new TextBlock
        {
            Text = $"\"May we send you our offers and news on WhatsApp?\"\n\n{clientName} can say no, and can change their mind at any time.",
            FontSize = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 14),
        });
        root.Children.Add(new TextBlock { Text = "Their WhatsApp number, with + and the country code (for example +92 300 1234567)", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        _number.Text = number ?? "";
        root.Children.Add(_number);
        root.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var yes = new Button { Content = "Yes, they agree", IsDefault = true, MinHeight = 54, MinWidth = 170, Margin = new Thickness(0, 0, 10, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        Look.SetIcon(yes, "");
        yes.Click += (_, _) => Accept();
        var no = new Button { Content = "No", MinHeight = 54, MinWidth = 110, Margin = new Thickness(0, 0, 10, 0) };
        Look.SetShortcut(no, "Ctrl+N");
        Look.SetIcon(no, "");
        no.Click += (_, _) => { Answer = new WhatsAppAnswer(false, null); DialogResult = true; };
        var later = new Button { Content = "Ask later", IsCancel = true, MinHeight = 54, MinWidth = 130 };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        buttons.Children.Add(later);
        root.Children.Add(buttons);
        Content = root;
        Views.Layout.AddIcons(this);
        Loaded += (_, _) => _number.Focus();
    }

    private void Accept()
    {
        string? number;
        try
        {
            number = WhatsAppNumbers.Normalize(_number.Text);
        }
        catch (FormatException ex)
        {
            _status.Text = ex.Message;
            _status.Foreground = Look.Brush("Danger");
            return;
        }
        if (number is null)
        {
            _status.Text = "Type the number they get WhatsApp messages on.";
            _status.Foreground = Look.Brush("Danger");
            _number.Focus();
            return;
        }
        Answer = new WhatsAppAnswer(true, number);
        DialogResult = true;
    }
}
