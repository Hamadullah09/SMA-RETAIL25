using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.App.Views;

/// <summary>A user as the Users list shows it.</summary>
public sealed record UserRow(UserAccount User, string Role, string Store, bool HasPin, bool IsLocked)
{
    public string UserName => User.UserName;
    public string? FullName => User.FullName;
    public bool IsActive => User.IsActive;
    public string? StaffCode => User.StaffCode;
    public bool MustChangePassword => User.MustChangePassword;
}

/// <summary>Setup › Users: who may sign in, with their role, home store, staff code and PIN.</summary>
public sealed class UsersPanel : DockPanel
{
    private readonly DataGrid _grid = Layout.Grid(
        Layout.Col("User", nameof(UserRow.UserName), 150),
        Layout.Col("Full name", nameof(UserRow.FullName)),
        Layout.Col("Role", nameof(UserRow.Role), 150),
        Layout.Col("Store", nameof(UserRow.Store), 130),
        Layout.Col("Staff code", nameof(UserRow.StaffCode), 90),
        Layout.Col("Active", nameof(UserRow.IsActive), 60),
        Layout.Col("Locked", nameof(UserRow.IsLocked), 60),
        Layout.Col("Must set password", nameof(UserRow.MustChangePassword), 130));

    public UsersPanel()
    {
        Margin = new Thickness(12);
        var bar = new WrapPanel { Style = Layout.S("Toolbar") };
        bar.Children.Add(Layout.Button("Add user", async (_, _) => await EditAsync(null), true));
        bar.Children.Add(Layout.Button("Edit user", async (_, _) => { if (Current is { } u) await EditAsync(u); }));
        bar.Children.Add(Layout.Button("Reset password", async (_, _) => await ResetAsync()));
        bar.Children.Add(Layout.Button("Delete user", async (_, _) => await DeleteAsync()));
        SetDock(bar, Dock.Top);
        Children.Add(bar);
        var note = new TextBlock
        {
            Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
            Text = "What a user may do comes from their role (Roles tab). A staff code and PIN let them sign in quickly at the till. "
                   + "Five wrong passwords or PINs lock the account for five minutes; Reset password unlocks it. When there are no users the program starts without a login.",
        };
        SetDock(note, Dock.Bottom);
        Children.Add(note);
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.MouseDoubleClick += async (_, _) => { if (Current is { } u) await EditAsync(u); };
        Children.Add(_grid);
    }

    private UserAccount? Current => (_grid.SelectedItem as UserRow)?.User;

    public async Task RefreshAsync()
    {
        var stores = (await AppServices.Setup.GetStoresAsync()).ToDictionary(s => s.Id, s => s.Name);
        var now = DateTime.UtcNow;
        _grid.ItemsSource = (await AppServices.Setup.GetUsersAsync())
            .Select(u => new UserRow(u, u.Role?.Name ?? "", u.StoreId is Guid s ? stores.GetValueOrDefault(s, "?") : "Every store", u.PinHash is not null, u.LockedUntilUtc > now))
            .ToList();
    }

    private async Task EditAsync(UserAccount? user)
    {
        var roles = await AppServices.Setup.GetRolesAsync();
        var stores = await AppServices.Setup.GetStoresAsync();
        var editor = new UserEditorWindow(user, roles, stores) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true) await Ui.Try(RefreshAsync);
    }

    private async Task ResetAsync()
    {
        if (Current is not { } u) return;
        var temporary = Ui.Prompt($"A temporary password for {u.UserName}. They choose their own at the next sign-in, and the account is unlocked.", "Reset password", password: true);
        if (string.IsNullOrEmpty(temporary)) return;
        await Ui.Try(async () => { await AppServices.Setup.ResetPasswordAsync(u.Id, temporary); await RefreshAsync(); });
    }

    private async Task DeleteAsync()
    {
        if (Current is not { } u || !Ui.Confirm($"Delete user {u.UserName}? A user with time clock entries can only be made not active.")) return;
        await Ui.Try(async () => { await AppServices.Setup.DeleteUserAsync(u.Id); await RefreshAsync(); });
    }
}

/// <summary>Adds or changes a user: names, role, home store, active, password, staff code and PIN.</summary>
public sealed class UserEditorWindow : Window
{
    private readonly UserAccount? _user;
    private readonly TextBox _name = new();
    private readonly TextBox _fullName = new();
    private readonly ComboBox _role = new() { DisplayMemberPath = nameof(Role.Name) };
    private readonly ComboBox _store = new();
    private readonly CheckBox _active = new() { Content = "Active (may sign in)" };
    private readonly PasswordBox _password = new();
    private readonly CheckBox _mustChange = new() { Content = "Must choose a new password at the next sign-in" };
    private readonly TextBox _staffCode = new() { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly PasswordBox _pin = new() { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly IReadOnlyList<Store?> _stores;

    public UserEditorWindow(UserAccount? user, IReadOnlyList<Role> roles, IReadOnlyList<Store> stores)
    {
        _user = user;
        Title = user is null ? "Add user" : "User " + user.UserName;
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _stores = [null, .. stores];
        _store.ItemsSource = _stores.Select(s => s is null ? "Every store" : $"{s.Code}  {s.Name}").ToList();
        _role.ItemsSource = roles;

        var p = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
        p.Children.Add(new TextBlock { Text = Title, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 24, Margin = new Thickness(0, 0, 0, 14) });
        p.Children.Add(Row("User name", _name));
        p.Children.Add(Row("Full name", _fullName));
        p.Children.Add(Row("Role", _role));
        p.Children.Add(Row("Home store", _store));
        p.Children.Add(Row("", _active));
        p.Children.Add(Row(user is null ? "Password" : "New password", _password, user is null ? null : "Leave empty to keep the password."));
        p.Children.Add(Row("", _mustChange));
        p.Children.Add(Row("Staff code", _staffCode, "For the quick sign-in at the till."));
        p.Children.Add(Row(user?.PinHash is null ? "PIN" : "New PIN", _pin, user?.PinHash is null ? "At least 4 digits." : "Leave empty to keep the PIN; clear the staff code to remove both."));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var ok = new Button { Content = "Save", IsDefault = true, MinWidth = 130, MinHeight = 52, Margin = new Thickness(0, 0, 10, 0), Style = Layout.S("AccentButton") };
        Look.SetIcon(ok, "");
        ok.Click += async (_, _) => await SaveAsync();
        buttons.Children.Add(ok);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 130, MinHeight = 52 };
        Look.SetIcon(cancel, "");
        buttons.Children.Add(cancel);
        p.Children.Add(buttons);
        Content = p;

        _name.Text = user?.UserName ?? "";
        _fullName.Text = user?.FullName ?? "";
        _role.SelectedItem = roles.FirstOrDefault(r => r.Id == user?.RoleId) ?? roles.FirstOrDefault(r => r.Name == Permissions.CashierRole);
        _store.SelectedIndex = Math.Max(0, _stores.ToList().FindIndex(s => s?.Id == user?.StoreId));
        _active.IsChecked = user?.IsActive ?? true;
        _mustChange.IsChecked = user?.MustChangePassword ?? false;
        _staffCode.Text = user?.StaffCode ?? "";
        Loaded += (_, _) => _name.Focus();
    }

    private static DockPanel Row(string label, Control input, string? hint = null)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var l = new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center, Foreground = Look.Brush("Muted") };
        DockPanel.SetDock(l, Dock.Left);
        row.Children.Add(l);
        if (hint is null)
        {
            row.Children.Add(input);
            return row;
        }
        var stack = new StackPanel();
        stack.Children.Add(input);
        stack.Children.Add(new TextBlock { Text = hint, Foreground = Look.Brush("Muted"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 3, 0, 0) });
        row.Children.Add(stack);
        return row;
    }

    private async Task SaveAsync()
    {
        if (_role.SelectedItem is not Role role) { Ui.Error("Choose a role."); return; }
        var code = _staffCode.Text.Trim();
        bool codeChanged = !string.Equals(code, _user?.StaffCode ?? "", StringComparison.Ordinal);
        if (codeChanged && code.Length > 0 && _pin.Password.Length == 0) { Ui.Error("Type a PIN for the staff code."); return; }
        var edited = new UserAccount
        {
            Id = _user?.Id ?? Guid.Empty, UserName = _name.Text, FullName = _fullName.Text, RoleId = role.Id, AccessLevel = role.Level,
            StoreId = _stores[Math.Max(0, _store.SelectedIndex)]?.Id, IsActive = _active.IsChecked == true, MustChangePassword = _mustChange.IsChecked == true,
        };
        bool saved = await Ui.Try(async () =>
        {
            var user = await AppServices.Setup.SaveUserAsync(edited, _password.Password.Length == 0 ? null : _password.Password);
            if (_password.Password.Length > 0 && _mustChange.IsChecked == true) await AppServices.Setup.ResetPasswordAsync(user.Id, _password.Password);
            if (code.Length == 0 && codeChanged) await AppServices.Setup.SetPinAsync(user.Id, null, null);
            else if (_pin.Password.Length > 0) await AppServices.Setup.SetPinAsync(user.Id, code, _pin.Password);
        });
        if (saved) DialogResult = true;
    }
}

/// <summary>A role as the Roles list shows it.</summary>
public sealed record RoleRow(Role Role, int Users)
{
    public override string ToString() => $"{Role.Name}  ·  level {Role.Level}  ·  {Users} user{(Users == 1 ? "" : "s")}" + (Role.IsSystem ? "  ·  standard" : "");
}

/// <summary>
/// Setup › Roles: what each role may do, as permissions grouped like <see cref="Permissions.All"/>. Custom roles can be
/// added, renamed and deleted; the standard roles can be changed but keep their names and cannot be deleted.
/// </summary>
public sealed class RolesPanel : DockPanel
{
    private static readonly string[] Levels = ["0 · trainee (sales are not kept)", "1 · sales", "2 · sales and stock", "3 · supervisor", "4 · administrator"];
    private readonly ListBox _list = new() { Width = 380, Margin = new Thickness(0, 0, 16, 0) };
    private readonly TextBox _name = new();
    private readonly TextBox _description = new();
    private readonly ComboBox _level = new() { ItemsSource = Levels, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<string, CheckBox> _boxes = [];
    private readonly Button _delete;
    private Role _current = new();

    public RolesPanel()
    {
        Margin = new Thickness(12);
        var bar = new WrapPanel { Style = Layout.S("Toolbar") };
        bar.Children.Add(Layout.Button("New role", (_, _) => Show(new Role { Level = 1 }), true));
        bar.Children.Add(Layout.Button("Save role", async (_, _) => await SaveAsync()));
        _delete = Layout.Button("Delete role", async (_, _) => await DeleteAsync());
        bar.Children.Add(_delete);
        SetDock(bar, Dock.Top);
        Children.Add(bar);
        SetDock(_list, Dock.Left);
        Children.Add(_list);
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is RoleRow row) Show(row.Role); };

        var editor = new StackPanel();
        editor.Children.Add(Field("Name", _name));
        editor.Children.Add(Field("Description", _description));
        editor.Children.Add(Field("Access level", _level));
        foreach (var group in Permissions.All.GroupBy(p => p.Group))
        {
            editor.Children.Add(new TextBlock { Text = group.Key, Style = Layout.S("SectionTitle") });
            var boxes = new WrapPanel();
            foreach (var permission in group)
            {
                var box = new CheckBox { Content = permission.Description, ToolTip = permission.Key, Width = 380, Margin = new Thickness(0, 0, 12, 8) };
                _boxes[permission.Key] = box;
                boxes.Children.Add(box);
            }
            editor.Children.Add(boxes);
        }
        Children.Add(new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }

    private static DockPanel Field(string label, Control input)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var l = new TextBlock { Text = label, Width = 140, VerticalAlignment = VerticalAlignment.Center, Foreground = Look.Brush("Muted") };
        SetDock(l, Dock.Left);
        row.Children.Add(l);
        row.Children.Add(input);
        return row;
    }

    public async Task RefreshAsync()
    {
        var selected = _current.Id;
        var usage = await AppServices.Setup.GetRoleUsageAsync();
        var rows = (await AppServices.Setup.GetRolesAsync()).Select(r => new RoleRow(r, usage.GetValueOrDefault(r.Id))).ToList();
        _list.ItemsSource = rows;
        _list.SelectedItem = rows.FirstOrDefault(r => r.Role.Id == selected) ?? rows.FirstOrDefault();
        if (_list.SelectedItem is null) Show(new Role { Level = 1 });
    }

    private void Show(Role role)
    {
        _current = role;
        if (role.Id == Guid.Empty) _list.SelectedItem = null;
        _name.Text = role.Name;
        _name.IsReadOnly = role.IsSystem;
        _name.ToolTip = role.IsSystem ? "A standard role keeps its name." : null;
        _description.Text = role.Description ?? "";
        _level.SelectedIndex = Math.Clamp(role.Level, 0, 4);
        _delete.IsEnabled = role.Id != Guid.Empty && !role.IsSystem;
        var granted = role.Permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, box) in _boxes) box.IsChecked = granted.Contains(key);
    }

    private async Task SaveAsync()
    {
        var role = new Role { Id = _current.Id, Name = _name.Text, Description = string.IsNullOrWhiteSpace(_description.Text) ? null : _description.Text.Trim(), Level = Math.Max(0, _level.SelectedIndex) };
        var keys = _boxes.Where(b => b.Value.IsChecked == true).Select(b => b.Key).ToList();
        await Ui.Try(async () =>
        {
            _current = await AppServices.Setup.SaveRoleAsync(role, keys);
            await RefreshAsync();
            // The signed-in user's own role may have changed.
            await RefreshSessionAsync();
        });
    }

    /// <summary>Reloads the signed-in user's permissions after their role changed.</summary>
    private async Task RefreshSessionAsync()
    {
        if (AppServices.Session.UserId is not Guid me) return;
        var user = (await AppServices.Setup.GetUsersAsync()).FirstOrDefault(u => u.Id == me);
        if (user?.RoleId is not Guid roleId) return;
        var role = (await AppServices.Setup.GetRolesAsync()).FirstOrDefault(r => r.Id == roleId);
        if (role is null) return;
        user.Role = role;
        AppServices.Session.SignIn(user);
    }

    private async Task DeleteAsync()
    {
        if (_current.Id == Guid.Empty || !Ui.Confirm($"Delete the role {_current.Name}?")) return;
        await Ui.Try(async () => { await AppServices.Setup.DeleteRoleAsync(_current.Id); _current = new Role(); await RefreshAsync(); });
    }
}
