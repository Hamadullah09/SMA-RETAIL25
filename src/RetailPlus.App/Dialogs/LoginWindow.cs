using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Dialogs;

/// <summary>
/// The fields of a sign-in: a user name and password, or a staff code and PIN (the quick sign-in at the till). F4
/// switches between the two.
/// </summary>
internal sealed class CredentialsPanel : StackPanel
{
    private readonly TextBlock _nameLabel = new() { FontSize = 18, Margin = new Thickness(2, 0, 0, 6) };
    private readonly TextBlock _secretLabel = new() { FontSize = 18, Margin = new Thickness(2, 14, 0, 6) };
    private readonly Button _switch = new() { MinHeight = 44, FontSize = 15, Padding = new Thickness(14, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };

    public TextBox NameBox { get; } = new() { FontSize = 20, MinHeight = 54 };
    public PasswordBox Secret { get; } = new() { FontSize = 20, MinHeight = 54 };

    /// <summary>True for a staff code and PIN, false for a user name and password.</summary>
    public bool UsesPin { get; private set; }

    public CredentialsPanel(bool pin = false)
    {
        Look.SetShortcut(_switch, "F4");
        Look.SetIcon(_switch, "");
        _switch.Click += (_, _) => { SetMode(!UsesPin); NameBox.Focus(); };
        Children.Add(_switch);
        Children.Add(_nameLabel);
        Children.Add(NameBox);
        Children.Add(_secretLabel);
        Children.Add(Secret);
        SetMode(pin);
    }

    public void SetMode(bool pin)
    {
        UsesPin = pin;
        _nameLabel.Text = pin ? "Staff code" : "User name";
        _secretLabel.Text = pin ? "PIN" : "Password";
        _switch.Content = pin ? "Use name and password" : "Use staff code and PIN";
        Secret.Clear();
    }

    /// <summary>Signs in with what was typed (failures are audited; a success is written as <paramref name="purpose"/> unless null).</summary>
    public Task<SignInResult> SignInAsync(string? purpose = AuditActions.SignIn) => UsesPin
        ? AppServices.Setup.SignInWithPinAsync(NameBox.Text, Secret.Password, purpose)
        : AppServices.Setup.SignInAsync(NameBox.Text, Secret.Password, purpose);
}

/// <summary>
/// Signing in: a user name and password, or a staff code and PIN. A user imported without a password, or given a
/// temporary one, chooses a new password here. Staff can also punch in and out of the time clock from this window.
/// </summary>
public sealed class LoginWindow : Window
{
    private readonly CredentialsPanel _credentials = new();
    private readonly TextBlock _message = new() { Foreground = Brushes.Firebrick, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 17 };

    public LoginWindow()
    {
        Title = Brand.Name + " — log in";
        Width = 760;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        var p = new StackPanel { Margin = new Thickness(36, 30, 36, 28) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(new Image { Source = Brand.Mark, Width = 96, Height = 96, Margin = new Thickness(0, 0, 14, 0) });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = Brand.Name, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 34, FontWeight = FontWeights.Bold });
        names.Children.Add(new TextBlock { Text = "by " + Brand.Company, Foreground = Look.Brush("Muted"), FontSize = 16 });
        brand.Children.Add(names);
        p.Children.Add(brand);
        p.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(AppServices.Settings.CompanyName) ? "Welcome" : "Welcome to " + AppServices.Settings.CompanyName,
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 26, Margin = new Thickness(0, 22, 0, 4) });
        p.Children.Add(new TextBlock { Text = "Sign in with your name and password, or your staff code and PIN.", Foreground = Look.Brush("Muted"), FontSize = 17,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        p.Children.Add(_credentials);
        p.Children.Add(_message);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        var ok = new Button { Content = "Log in", IsDefault = true, MinWidth = 140, MinHeight = 56, FontSize = 19, Margin = new Thickness(0, 0, 10, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        Look.SetIcon(ok, "");
        ok.Click += async (_, _) => await LoginAsync();
        buttons.Children.Add(ok);
        var punch = new Button { Content = "Punch in / out", MinWidth = 140, MinHeight = 56, FontSize = 19, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Time clock: start or end your shift" };
        Look.SetIcon(punch, "");
        Look.SetShortcut(punch, "Ctrl+T");
        punch.Click += async (_, _) => await PunchAsync();
        buttons.Children.Add(punch);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 140, MinHeight = 56, FontSize = 19 };
        Look.SetIcon(cancel, "");
        buttons.Children.Add(cancel);
        p.Children.Add(buttons);
        Content = p;
        Loaded += (_, _) => _credentials.NameBox.Focus();
    }

    private async Task LoginAsync()
    {
        var result = await _credentials.SignInAsync();
        if (result.User is not { } user)
        {
            _message.Text = result.Message;
            _credentials.Secret.Clear();
            return;
        }
        if (user.MustChangePassword || user.PasswordHash is null)
        {
            var first = Ui.Prompt($"Choose a new password for {user.UserName}:", "New password", password: true);
            if (string.IsNullOrEmpty(first)) return;
            var second = Ui.Prompt("Type the new password again:", "New password", password: true);
            if (first != second) { _message.Text = "The two passwords were not the same."; return; }
            if (!await Ui.Try(() => AppServices.Setup.SaveUserAsync(user, first))) return;
        }
        AppServices.Session.SignIn(user);
        DialogResult = true;
    }

    /// <summary>The time clock: whoever signs in here is punched in, or out when they are in.</summary>
    private async Task PunchAsync()
    {
        var result = await _credentials.SignInAsync(purpose: null);
        if (result.User is not { } user)
        {
            _message.Text = result.Message;
            _credentials.Secret.Clear();
            return;
        }
        TimeClockEntry? entry = null;
        if (await Ui.Try(async () => entry = await AppServices.Staff.PunchAsync(user.Id)) && entry is not null)
        {
            _message.Foreground = Look.Brush("Ok");
            _message.Text = entry.ClockOut is { } off
                ? $"{user.UserName} punched out at {off.ToString("t", CultureInfo.CurrentCulture)} after {Core.Rules.TimeClock.Hours(entry.ClockIn, off):0.##} hours."
                : $"{user.UserName} punched in at {entry.ClockIn.ToString("t", CultureInfo.CurrentCulture)}.";
            _credentials.NameBox.Clear();
            _credentials.Secret.Clear();
            _credentials.NameBox.Focus();
            return;
        }
        _message.Foreground = Brushes.Firebrick;
    }
}

/// <summary>
/// "Needs a supervisor": another user signs in here (name and password, or staff code and PIN) and is accepted when
/// their role may approve and may do the action themselves.
/// </summary>
public sealed class ApprovalWindow : Window
{
    private readonly CredentialsPanel _credentials = new();
    private readonly TextBlock _message = new() { Foreground = Brushes.Firebrick, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 17 };
    private readonly string _permission;
    private readonly string _action;

    public UserAccount? Approver { get; private set; }

    public ApprovalWindow(string permission, string action)
    {
        _permission = permission;
        _action = action;
        Title = "Approval needed";
        Owner = Ui.Owner;
        WindowStartupLocation = Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        var p = new StackPanel { Margin = new Thickness(30, 26, 30, 24) };
        p.Children.Add(new TextBlock { Text = "Approval needed", FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 26 });
        p.Children.Add(new TextBlock
        {
            Text = $"{action} needs a supervisor: someone who may approve it signs in here. {AppServices.Session.UserName} stays signed in.",
            TextWrapping = TextWrapping.Wrap, FontSize = 17, Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 6, 0, 16),
        });
        p.Children.Add(_credentials);
        p.Children.Add(_message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var ok = new Button { Content = "Approve", IsDefault = true, MinWidth = 140, MinHeight = 54, Margin = new Thickness(0, 0, 10, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        Look.SetIcon(ok, "");
        ok.Click += async (_, _) => await ApproveAsync();
        buttons.Children.Add(ok);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 140, MinHeight = 54 };
        Look.SetIcon(cancel, "");
        buttons.Children.Add(cancel);
        p.Children.Add(buttons);
        Content = p;
        Loaded += (_, _) => _credentials.NameBox.Focus();
    }

    private async Task ApproveAsync()
    {
        var (approver, problem) = await AppServices.Setup.ApproveAsync(_credentials.NameBox.Text, _credentials.Secret.Password, _credentials.UsesPin, _permission, _action);
        if (approver is null)
        {
            _message.Text = problem;
            _credentials.Secret.Clear();
            return;
        }
        Approver = approver;
        DialogResult = true;
    }
}

/// <summary>Permission checks with the "needs a supervisor" approval when the signed-in user lacks the permission.</summary>
public static class Approval
{
    /// <summary>True when the signed-in user may do <paramref name="action"/>, or someone who may approve it signed in for it.</summary>
    public static bool Allowed(string permission, string action) => SessionFor(permission, action) is not null;

    /// <summary>
    /// Who does the action: the signed-in user when their role allows it, the approver (working at this station) when
    /// someone approved it, or null when nobody did.
    /// </summary>
    public static SessionInfo? SessionFor(string permission, string action)
    {
        if (AppServices.Can(permission) || AppServices.Unattended) return AppServices.Session;
        var window = new ApprovalWindow(permission, action);
        return window.ShowDialog() == true && window.Approver is { } approver ? AppServices.Session.As(approver) : null;
    }
}
