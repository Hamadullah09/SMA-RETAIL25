using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RetailPlus.App.Infrastructure;
using RetailPlus.Data.Services;
using RetailPlus.Settings;

namespace RetailPlus.App.Dialogs;

/// <summary>Shown while the database opens.</summary>
public sealed class SplashWindow : Window
{
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0) };

    public SplashWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 420;
        Height = 200;
        ShowInTaskbar = true;
        Title = Brand.Name;
        var panel = new StackPanel { Margin = new Thickness(28), VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Image { Source = Brand.Mark, Width = 64, Height = 64, Margin = new Thickness(0, 0, 12, 0) });
        head.Children.Add(new TextBlock { Text = Brand.Name, FontSize = 30, FontWeight = FontWeights.Bold, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(head);
        panel.Children.Add(_status);
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 12, 0, 0) });
        Content = new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = panel, Background = Brushes.White };
    }

    public string Status { set => _status.Text = value; }
}

/// <summary>Asks where the database is when it cannot be opened.</summary>
public sealed class DatabaseWindow : Window
{
    private readonly TextBox _connection = new();
    private readonly TextBox _localDb = new();
    private readonly TextBox _station = new();
    private readonly TextBox _location = new();

    public StationConfig Config { get; }

    public DatabaseWindow(StationConfig config, string error)
    {
        Config = config;
        Title = Brand.Name + " — database";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        var p = new StackPanel { Margin = new Thickness(20) };
        p.Children.Add(new TextBlock { Text = "The database could not be opened", FontSize = 18, FontWeight = FontWeights.SemiBold });
        p.Children.Add(new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("Danger"), Margin = new Thickness(0, 6, 0, 12) });
        p.Children.Add(new TextBlock { Text = "SQL Server connection string (leave empty to use LocalDB on this computer):" });
        _connection.Text = config.ConnectionString ?? "";
        p.Children.Add(_connection);
        p.Children.Add(new TextBlock { Text = "LocalDB database name:", Margin = new Thickness(0, 10, 0, 0) });
        _localDb.Text = config.LocalDbDatabase;
        p.Children.Add(_localDb);
        p.Children.Add(new TextBlock { Text = "POS station number (1–999):", Margin = new Thickness(0, 10, 0, 0) });
        _station.Text = config.StationNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        p.Children.Add(_station);
        p.Children.Add(new TextBlock { Text = "Location code:", Margin = new Thickness(0, 10, 0, 0) });
        _location.Text = config.LocationCode;
        p.Children.Add(_location);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Connect", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        ok.Click += (_, _) =>
        {
            Config.ConnectionString = string.IsNullOrWhiteSpace(_connection.Text) ? null : _connection.Text.Trim();
            Config.LocalDbDatabase = string.IsNullOrWhiteSpace(_localDb.Text) ? "RetailPlus_App" : _localDb.Text.Trim();
            Config.StationNumber = int.TryParse(_station.Text, out var n) && n is >= 1 and <= 999 ? n : 1;
            Config.LocationCode = string.IsNullOrWhiteSpace(_location.Text) ? "POS" : _location.Text.Trim().ToUpperInvariant();
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Exit", IsCancel = true, MinWidth = 90 });
        p.Children.Add(buttons);
        Content = p;
    }
}

/// <summary>User name and password; a user imported without a password sets one on first login.</summary>
public sealed class LoginWindow : Window
{
    private readonly TextBox _user = new();
    private readonly PasswordBox _password = new();
    private readonly TextBlock _message = new() { Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };

    public LoginWindow()
    {
        Title = Brand.Name + " — log in";
        Width = 540;
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
        p.Children.Add(new TextBlock { Text = "Please type your name and password.", Foreground = Look.Brush("Muted"), FontSize = 17, Margin = new Thickness(0, 0, 0, 18) });
        p.Children.Add(new TextBlock { Text = "User name", FontSize = 18, Margin = new Thickness(2, 0, 0, 6) });
        _user.FontSize = 20;
        _user.MinHeight = 54;
        p.Children.Add(_user);
        p.Children.Add(new TextBlock { Text = "Password", FontSize = 18, Margin = new Thickness(2, 14, 0, 6) });
        _password.FontSize = 20;
        _password.MinHeight = 54;
        p.Children.Add(_password);
        _message.FontSize = 17;
        p.Children.Add(_message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        var ok = new Button { Content = "Log in", IsDefault = true, MinWidth = 150, MinHeight = 56, FontSize = 19, Margin = new Thickness(0, 0, 10, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        Look.SetIcon(ok, "");
        ok.Click += async (_, _) => await LoginAsync();
        buttons.Children.Add(ok);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 150, MinHeight = 56, FontSize = 19 };
        Look.SetIcon(cancel, "");
        buttons.Children.Add(cancel);
        p.Children.Add(buttons);
        Content = p;
        Loaded += (_, _) => _user.Focus();
    }

    private async Task LoginAsync()
    {
        var user = await AppServices.Setup.AuthenticateAsync(_user.Text, _password.Password);
        if (user is null)
        {
            _message.Text = "The user name or password is not right.";
            _password.Clear();
            return;
        }
        if (user.MustChangePassword || user.PasswordHash is null)
        {
            var first = Ui.Prompt($"Choose a new password for {user.UserName}:", "New password", password: true);
            if (string.IsNullOrEmpty(first)) return;
            var second = Ui.Prompt("Type the new password again:", "New password", password: true);
            if (first != second) { _message.Text = "The two passwords were not the same."; return; }
            await AppServices.Setup.SaveUserAsync(user, first);
        }
        AppServices.Session.UserName = user.UserName;
        AppServices.Session.AccessLevel = user.AccessLevel;
        DialogResult = true;
    }

    /// <summary>Asks a supervisor (level 3 or higher) to approve an action. True when approved.</summary>
    public static async Task<bool> SupervisorApprovesAsync(string action)
    {
        if (AppServices.Can(3) || AppServices.Unattended) return true;
        var name = Ui.Prompt($"{action} needs a supervisor. Supervisor user name:", "Supervisor");
        if (string.IsNullOrWhiteSpace(name)) return false;
        var password = Ui.Prompt("Supervisor password:", "Supervisor", password: true);
        if (password is null) return false;
        UserAccountCheck? check = await AppServices.Setup.AuthenticateAsync(name, password) is { } u ? new UserAccountCheck(u.AccessLevel, u.PasswordHash is not null) : null;
        if (check is { Level: >= 3, HasPassword: true }) return true;
        Ui.Error("That user cannot approve this.");
        return false;
    }

    private sealed record UserAccountCheck(int Level, bool HasPassword);
}

/// <summary>Picks an item by searching the inventory.</summary>
public sealed class ItemPickerWindow : Window
{
    private readonly TextBox _search = new() { FontSize = 15 };
    private readonly DataGrid _grid = new() { Style = (Style)Application.Current.FindResource("Grid"), SelectionMode = DataGridSelectionMode.Single };

    public ItemRow? Selected { get; private set; }

    public ItemPickerWindow(string? initial = null)
    {
        Title = "Find item";
        Width = 900;
        Height = 600;
        Owner = Ui.Owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var g = new Grid { Margin = new Thickness(14) };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(_search);
        _grid.Columns.Add(new DataGridTextColumn { Header = "Stock code", Binding = new System.Windows.Data.Binding(nameof(ItemRow.Plu)), Width = 130 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = new System.Windows.Data.Binding(nameof(ItemRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Size", Binding = new System.Windows.Data.Binding(nameof(ItemRow.Size)), Width = 70 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Department", Binding = new System.Windows.Data.Binding(nameof(ItemRow.Department)), Width = 120 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "On hand", Binding = new System.Windows.Data.Binding(nameof(ItemRow.OnHand)) { StringFormat = "0.###" }, Width = 80 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Price", Binding = new System.Windows.Data.Binding(nameof(ItemRow.Price1)) { StringFormat = "N2" }, Width = 90 });
        Grid.SetRow(_grid, 1);
        _grid.Margin = new Thickness(0, 10, 0, 0);
        g.Children.Add(_grid);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Select", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 });
        Grid.SetRow(buttons, 2);
        g.Children.Add(buttons);
        Content = g;
        _search.Text = initial ?? "";
        _grid.MouseDoubleClick += (_, _) => Accept();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += async (_, _) => { timer.Stop(); await LoadAsync(); };
        _search.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        _search.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Down) { _grid.Focus(); _grid.SelectedIndex = 0; } };
        Loaded += async (_, _) => { _search.Focus(); await LoadAsync(); };
    }

    private async Task LoadAsync()
    {
        _grid.ItemsSource = await AppServices.Inventory.SearchAsync(AppServices.Session.LocationId, _search.Text, take: 300);
        if (_grid.Items.Count > 0) _grid.SelectedIndex = 0;
    }

    private void Accept()
    {
        Selected = _grid.SelectedItem as ItemRow;
        if (Selected is not null) DialogResult = true;
    }
}

/// <summary>Picks a client (or a supplier) by searching.</summary>
public sealed class PartyPickerWindow : Window
{
    private readonly TextBox _search = new() { FontSize = 15 };
    private readonly DataGrid _grid = new() { Style = (Style)Application.Current.FindResource("Grid"), SelectionMode = DataGridSelectionMode.Single };
    private readonly bool _suppliers;

    public PartyRow? Selected { get; private set; }

    public PartyPickerWindow(bool suppliers = false)
    {
        _suppliers = suppliers;
        Title = suppliers ? "Find supplier" : "Find client";
        Width = 860;
        Height = 560;
        Owner = Ui.Owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var g = new Grid { Margin = new Thickness(14) };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(_search);
        void Col(string h, string p, double w, string? f = null) => _grid.Columns.Add(new DataGridTextColumn
        {
            Header = h, Binding = new System.Windows.Data.Binding(p) { StringFormat = f },
            Width = w < 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(w),
        });
        Col("Number", nameof(PartyRow.Number), 90);
        Col("Name", nameof(PartyRow.DisplayName), -1);
        Col("City", nameof(PartyRow.City), 120);
        Col("Phone", nameof(PartyRow.WorkPhone), 120);
        if (!suppliers) Col("Balance", nameof(PartyRow.Balance), 90, "N2");
        Grid.SetRow(_grid, 1);
        _grid.Margin = new Thickness(0, 10, 0, 0);
        g.Children.Add(_grid);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Select", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 });
        Grid.SetRow(buttons, 2);
        g.Children.Add(buttons);
        Content = g;
        _grid.MouseDoubleClick += (_, _) => Accept();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += async (_, _) => { timer.Stop(); await LoadAsync(); };
        _search.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        _search.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Down) { _grid.Focus(); _grid.SelectedIndex = 0; } };
        Loaded += async (_, _) => { _search.Focus(); await LoadAsync(); };
    }

    private async Task LoadAsync()
    {
        _grid.ItemsSource = _suppliers ? await AppServices.Parties.SearchSuppliersAsync(_search.Text, take: 300) : await AppServices.Parties.SearchClientsAsync(_search.Text, take: 300);
        if (_grid.Items.Count > 0) _grid.SelectedIndex = 0;
    }

    private void Accept()
    {
        Selected = _grid.SelectedItem as PartyRow;
        if (Selected is not null) DialogResult = true;
    }
}

/// <summary>Shows a receipt, statement or drawer report as text with Print and Save.</summary>
public sealed class TextPreviewWindow : Window
{
    public TextPreviewWindow(string title, string text)
    {
        Title = title;
        Width = 560;
        Height = 720;
        Owner = Ui.Owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var g = new DockPanel { Margin = new Thickness(14) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var print = new Button { Content = "Print", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        print.Click += (_, _) => Ui.PrintText(text, title);
        var receipt = new Button { Content = "Receipt printer", MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        receipt.Click += (_, _) => { try { AppServices.Printer.Print(text, false); } catch (Exception ex) { Ui.Error(ex.Message); } };
        var save = new Button { Content = "Save…", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) => { var f = Ui.SaveFile(title + ".txt", "Text|*.txt"); if (f is not null) System.IO.File.WriteAllText(f, text); };
        buttons.Children.Add(print);
        buttons.Children.Add(receipt);
        buttons.Children.Add(save);
        buttons.Children.Add(new Button { Content = "Close", IsCancel = true, MinWidth = 90 });
        g.Children.Add(buttons);
        g.Children.Add(new TextBox
        {
            Text = text, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap,
        });
        Content = g;
    }
}
