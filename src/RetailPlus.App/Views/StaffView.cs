using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Views;

/// <summary>A time clock entry as the list shows it.</summary>
public sealed record TimeEntryRow(TimeClockEntry Entry)
{
    public string UserName => Entry.UserName;
    public DateTime ClockIn => Entry.ClockIn;
    public DateTime? ClockOut => Entry.ClockOut;
    public string Hours => Entry.ClockOut is { } o ? TimeClock.Hours(Entry.ClockIn, o).ToString("0.##", CultureInfo.CurrentCulture) : "in now";
    public string? EditedBy => Entry.EditedBy;
    public string? EditReason => Entry.EditReason;
}

/// <summary>A commission rule as the list shows it.</summary>
public sealed record RuleRow(CommissionRule Rule, string For, string AppliesTo)
{
    public string Name => Rule.Name;
    public string Rate => Rule.Basis == CommissionBasis.PercentOfNetSales
        ? Rule.Rate.ToString("0.###", CultureInfo.CurrentCulture) + "% of net sales"
        : Rule.Rate.ToString("N2", CultureInfo.CurrentCulture) + " a unit";
    public DateOnly? From => Rule.EffectiveFrom;
    public DateOnly? To => Rule.EffectiveTo;
    public bool IsActive => Rule.IsActive;
}

/// <summary>
/// Staff: the time clock (punching in and out, who is in, supervisor corrections), commission rules and each
/// salesperson's commission.
/// </summary>
public sealed class StaffView : UserControl, IRefreshable
{
    private readonly TabControl _tabs = new();
    private readonly TabItem _clockTab, _rulesTab, _commissionTab;

    // Time clock
    private readonly TextBlock _myStatus = new() { FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
    private readonly TextBlock _inNow = new() { Foreground = Look.Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) };
    private readonly DatePicker _from = new() { SelectedDate = DateTime.Today.AddDays(-13), Margin = new Thickness(0, 0, 8, 6) };
    private readonly DatePicker _to = new() { SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 6) };
    private readonly ComboBox _user = new() { Width = 200, Margin = new Thickness(0, 0, 8, 6), DisplayMemberPath = nameof(UserAccount.UserName) };
    private readonly TextBlock _total = new() { Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 8, 0, 0) };
    private readonly DataGrid _entries = Layout.Grid(
        Layout.Col("Staff", nameof(TimeEntryRow.UserName), 150),
        Layout.Col("In", nameof(TimeEntryRow.ClockIn), 150, "g"),
        Layout.Col("Out", nameof(TimeEntryRow.ClockOut), 150, "g"),
        Layout.Col("Hours", nameof(TimeEntryRow.Hours), 70, null, true),
        Layout.Col("Changed by", nameof(TimeEntryRow.EditedBy), 110),
        Layout.Col("Reason", nameof(TimeEntryRow.EditReason)));

    // Commission rules
    private readonly DataGrid _rules = Layout.Grid(
        Layout.Col("Rule", nameof(RuleRow.Name), 180),
        Layout.Col("For", nameof(RuleRow.For), 130),
        Layout.Col("Applies to", nameof(RuleRow.AppliesTo)),
        Layout.Col("Commission", nameof(RuleRow.Rate), 160),
        Layout.Col("From", nameof(RuleRow.From), 95),
        Layout.Col("To", nameof(RuleRow.To), 95),
        Layout.Col("Active", nameof(RuleRow.IsActive), 60));

    // Commission
    private readonly DatePicker _cFrom = new() { SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), Margin = new Thickness(0, 0, 8, 6) };
    private readonly DatePicker _cTo = new() { SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 6) };
    private readonly DataGrid _commission = Layout.Grid(
        Layout.Col("Staff", nameof(CommissionRow.Staff)),
        Layout.Col("Sales", nameof(CommissionRow.Sales), 70, null, true),
        Layout.Col("Units", nameof(CommissionRow.Units), 70, "0.###", true),
        Layout.Col("Net sales", nameof(CommissionRow.NetSales), 110, "N2", true),
        Layout.Col("By rules", nameof(CommissionRow.FromRules), 100, "N2", true),
        Layout.Col("By item rates", nameof(CommissionRow.FromItems), 110, "N2", true),
        Layout.Col("Commission", nameof(CommissionRow.Commission), 110, "N2", true));
    private readonly DataGrid _lines = Layout.Grid(
        Layout.Col("Sold", nameof(CommissionDetail.SoldAt), 140, "g"),
        Layout.Col("Invoice", nameof(CommissionDetail.InvoiceNumber), 100),
        Layout.Col("Stock code", nameof(CommissionDetail.Plu), 110),
        Layout.Col("Description", nameof(CommissionDetail.Description)),
        Layout.Col("Qty", nameof(CommissionDetail.Quantity), 60, "0.###", true),
        Layout.Col("Net", nameof(CommissionDetail.NetAmount), 90, "N2", true),
        Layout.Col("Rule", nameof(CommissionDetail.Rule), 150),
        Layout.Col("Commission", nameof(CommissionDetail.Commission), 100, "N2", true));

    public StaffView()
    {
        _clockTab = new TabItem { Header = "Time clock", Content = ClockPage() };
        _rulesTab = new TabItem { Header = "Commission rules", Content = RulesPage() };
        _commissionTab = new TabItem { Header = "Commission", Content = CommissionPage() };
        _tabs.Items.Add(_clockTab);
        _tabs.Items.Add(_rulesTab);
        _tabs.Items.Add(_commissionTab);
        Content = Layout.Page("Staff", out _, _tabs);
    }

    private DockPanel ClockPage()
    {
        var page = new DockPanel { Margin = new Thickness(12) };
        var me = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var punch = Layout.Button("Punch in / out", async (_, _) => await PunchMeAsync(), true);
        Look.SetIcon(punch, "");
        Look.SetShortcut(punch, "Ctrl+T");
        DockPanel.SetDock(punch, Dock.Right);
        var other = Layout.Button("Punch someone else", (_, _) => PunchOther());
        Look.SetIcon(other, "");
        DockPanel.SetDock(other, Dock.Right);
        me.Children.Add(other);
        me.Children.Add(punch);
        me.Children.Add(_myStatus);
        DockPanel.SetDock(me, Dock.Top);
        page.Children.Add(me);
        DockPanel.SetDock(_inNow, Dock.Top);
        page.Children.Add(_inNow);

        var bar = new WrapPanel { Style = Layout.S("Toolbar") };
        bar.Children.Add(Layout.Label("From"));
        bar.Children.Add(_from);
        bar.Children.Add(Layout.Label("To"));
        bar.Children.Add(_to);
        bar.Children.Add(_user);
        bar.Children.Add(Layout.Button("Show", async (_, _) => await Ui.Try(LoadEntriesAsync)));
        bar.Children.Add(Layout.Button("Change entry", async (_, _) => await EditEntryAsync()));
        bar.Children.Add(Layout.Button("Add entry", async (_, _) => await AddEntryAsync()));
        DockPanel.SetDock(bar, Dock.Top);
        page.Children.Add(bar);
        DockPanel.SetDock(_total, Dock.Bottom);
        page.Children.Add(_total);
        _entries.SelectionMode = DataGridSelectionMode.Single;
        _entries.MouseDoubleClick += async (_, _) => await EditEntryAsync();
        page.Children.Add(_entries);
        return page;
    }

    private DockPanel RulesPage()
    {
        var page = new DockPanel { Margin = new Thickness(12) };
        var bar = new WrapPanel { Style = Layout.S("Toolbar") };
        bar.Children.Add(Layout.Button("New rule", async (_, _) => await EditRuleAsync(null), true));
        bar.Children.Add(Layout.Button("Edit rule", async (_, _) => { if (_rules.SelectedItem is RuleRow r) await EditRuleAsync(r.Rule); }));
        bar.Children.Add(Layout.Button("Delete rule", async (_, _) => await DeleteRuleAsync()));
        DockPanel.SetDock(bar, Dock.Top);
        page.Children.Add(bar);
        var note = new TextBlock
        {
            Foreground = Look.Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
            Text = "The rule that fits a sales line best decides its commission: one item, then a category, then a department, then all items; "
                   + "a rule for the salesperson before one for everyone; then the latest start. Lines no rule covers keep the item's own commission percent.",
        };
        DockPanel.SetDock(note, Dock.Bottom);
        page.Children.Add(note);
        _rules.SelectionMode = DataGridSelectionMode.Single;
        _rules.MouseDoubleClick += async (_, _) => { if (_rules.SelectedItem is RuleRow r) await EditRuleAsync(r.Rule); };
        page.Children.Add(_rules);
        return page;
    }

    private DockPanel CommissionPage()
    {
        var page = new DockPanel { Margin = new Thickness(12) };
        var bar = new WrapPanel { Style = Layout.S("Toolbar") };
        bar.Children.Add(Layout.Label("From"));
        bar.Children.Add(_cFrom);
        bar.Children.Add(Layout.Label("To"));
        bar.Children.Add(_cTo);
        bar.Children.Add(Layout.Button("Show", async (_, _) => await Ui.Try(LoadCommissionAsync), true));
        DockPanel.SetDock(bar, Dock.Top);
        page.Children.Add(bar);
        _commission.Height = 220;
        _commission.SelectionMode = DataGridSelectionMode.Single;
        _commission.SelectionChanged += async (_, _) => await Ui.Try(LoadLinesAsync);
        DockPanel.SetDock(_commission, Dock.Top);
        page.Children.Add(_commission);
        var title = new TextBlock { Text = "The lines of the chosen salesperson", Style = Layout.S("SectionTitle") };
        DockPanel.SetDock(title, Dock.Top);
        page.Children.Add(title);
        page.Children.Add(_lines);
        return page;
    }

    public async Task RefreshAsync()
    {
        bool clock = AppServices.CanAny(Permissions.TimeClockPunch, Permissions.TimeClockEdit);
        _clockTab.Visibility = clock ? Visibility.Visible : Visibility.Collapsed;
        _rulesTab.Visibility = AppServices.Can(Permissions.TimeClockEdit) ? Visibility.Visible : Visibility.Collapsed;
        _commissionTab.Visibility = AppServices.Can(Permissions.ReportsStaff) ? Visibility.Visible : Visibility.Collapsed;
        if (_tabs.SelectedItem is not TabItem { Visibility: Visibility.Visible })
            _tabs.SelectedItem = _tabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Visibility == Visibility.Visible);
        if (clock)
        {
            var users = await AppServices.Setup.GetUsersAsync();
            var selected = (_user.SelectedItem as UserAccount)?.Id;
            _user.ItemsSource = new List<UserAccount> { new() { UserName = "Everyone" } }.Concat(users).ToList();
            _user.SelectedItem = ((List<UserAccount>)_user.ItemsSource).FirstOrDefault(u => u.Id == selected) ?? ((List<UserAccount>)_user.ItemsSource)[0];
            await LoadEntriesAsync();
        }
        if (AppServices.Can(Permissions.TimeClockEdit)) await LoadRulesAsync();
        if (AppServices.Can(Permissions.ReportsStaff)) await LoadCommissionAsync();
    }

    // Time clock -----------------------------------------------------------------------------------------------------

    private async Task LoadEntriesAsync()
    {
        var me = AppServices.Session.UserId is Guid id ? await AppServices.Staff.OpenEntryAsync(id) : null;
        _myStatus.Text = AppServices.Session.UserId is null ? "Sign in as a user to use the time clock."
            : me is null ? $"{AppServices.Session.UserName}, you are not punched in."
            : $"{AppServices.Session.UserName}, you are punched in since {me.ClockIn.ToString("t", CultureInfo.CurrentCulture)}.";
        var inNow = await AppServices.Staff.WhoIsInAsync();
        _inNow.Text = inNow.Count == 0 ? "Nobody is punched in." : "In now: " + string.Join(", ", inNow.Select(e => $"{e.UserName} (since {e.ClockIn.ToString("t", CultureInfo.CurrentCulture)})"));
        var from = DateOnly.FromDateTime(_from.SelectedDate ?? DateTime.Today);
        var to = DateOnly.FromDateTime(_to.SelectedDate ?? DateTime.Today);
        Guid? user = _user.SelectedItem is UserAccount { Id: var uid } && uid != Guid.Empty ? uid : null;
        // Users without time clock edits see their own entries only.
        if (!AppServices.Can(Permissions.TimeClockEdit)) user = AppServices.Session.UserId ?? Guid.Empty;
        var entries = await AppServices.Staff.GetEntriesAsync(from, to, user);
        _entries.ItemsSource = entries.Select(e => new TimeEntryRow(e)).ToList();
        var hours = (await AppServices.Staff.HoursByDayAsync(from, to, user)).Sum(h => h.Hours);
        _total.Text = $"{entries.Count} entries · {hours.ToString("0.##", CultureInfo.CurrentCulture)} hours in finished shifts between these days";
    }

    private async Task PunchMeAsync()
    {
        if (AppServices.Session.UserId is not Guid me) { Ui.Info("Sign in as a user to use the time clock."); return; }
        TimeClockEntry? entry = null;
        if (!await Ui.Try(async () => entry = await AppServices.Staff.PunchAsync(me)) || entry is null) return;
        (Window.GetWindow(this) as MainWindow)?.SetStatus(entry.ClockOut is { } off
            ? $"Punched out at {off.ToString("t", CultureInfo.CurrentCulture)} after {TimeClock.Hours(entry.ClockIn, off):0.##} hours."
            : $"Punched in at {entry.ClockIn.ToString("t", CultureInfo.CurrentCulture)}.");
        await Ui.Try(LoadEntriesAsync);
    }

    private async void PunchOther()
    {
        new PunchWindow { Owner = Window.GetWindow(this) }.ShowDialog();
        await Ui.Try(LoadEntriesAsync);
    }

    private async Task EditEntryAsync()
    {
        if (_entries.SelectedItem is not TimeEntryRow row) return;
        if (Approval.SessionFor(Permissions.TimeClockEdit, "Changing a time clock entry") is not { } editor) return;
        var window = new TimeEntryWindow(row.Entry, null) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true) return;
        await Ui.Try(async () =>
        {
            await AppServices.Staff.EditEntryAsync(row.Entry.Id, window.ClockIn, window.ClockOut, window.Reason, editor);
            await LoadEntriesAsync();
        });
    }

    private async Task AddEntryAsync()
    {
        if (Approval.SessionFor(Permissions.TimeClockEdit, "Adding a time clock entry") is not { } editor) return;
        var users = (await AppServices.Setup.GetUsersAsync()).Where(u => u.IsActive).ToList();
        var window = new TimeEntryWindow(null, users) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true || window.UserId is not Guid userId || window.ClockOut is not { } clockOut) return;
        await Ui.Try(async () =>
        {
            await AppServices.Staff.AddEntryAsync(userId, window.ClockIn, clockOut, window.Reason, editor);
            await LoadEntriesAsync();
        });
    }

    // Commission rules --------------------------------------------------------------------------------------------

    private async Task LoadRulesAsync()
    {
        var rules = await AppServices.Staff.GetRulesAsync();
        var users = (await AppServices.Setup.GetUsersAsync()).ToDictionary(u => u.Id, u => u.UserName);
        var (departments, categories, items) = await AppServices.Staff.GetRuleChoicesAsync();
        string AppliesTo(CommissionRule r) => r.Scope switch
        {
            CommissionScope.Department => "Department " + departments.FirstOrDefault(d => d.Id == r.DepartmentId)?.Name,
            CommissionScope.Category => "Category " + categories.FirstOrDefault(c => c.Id == r.CategoryId)?.Name,
            CommissionScope.Item => "Item " + (r.ItemId is Guid i ? items.GetValueOrDefault(i) : null),
            _ => "All items",
        };
        _rules.ItemsSource = rules.Select(r => new RuleRow(r, r.UserId is Guid u ? users.GetValueOrDefault(u, "?") : "Everyone", AppliesTo(r))).ToList();
    }

    private async Task EditRuleAsync(CommissionRule? rule)
    {
        if (Approval.SessionFor(Permissions.TimeClockEdit, "Changing commission rules") is not { } editor) return;
        var users = await AppServices.Setup.GetUsersAsync();
        var (departments, categories, items) = await AppServices.Staff.GetRuleChoicesAsync();
        var window = new RuleEditorWindow(rule, users, departments, categories, items) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true) return;
        await Ui.Try(async () => { await AppServices.Staff.SaveRuleAsync(window.Rule, editor); await LoadRulesAsync(); });
    }

    private async Task DeleteRuleAsync()
    {
        if (_rules.SelectedItem is not RuleRow row || !Ui.Confirm($"Delete the commission rule \"{row.Name}\"? Commission already shown in reports is worked out again without it.")) return;
        if (Approval.SessionFor(Permissions.TimeClockEdit, "Changing commission rules") is not { } editor) return;
        await Ui.Try(async () => { await AppServices.Staff.DeleteRuleAsync(row.Rule.Id, editor); await LoadRulesAsync(); });
    }

    // Commission ---------------------------------------------------------------------------------------------------

    private DateOnly CFrom => DateOnly.FromDateTime(_cFrom.SelectedDate ?? DateTime.Today);
    private DateOnly CTo => DateOnly.FromDateTime(_cTo.SelectedDate ?? DateTime.Today);

    private async Task LoadCommissionAsync()
    {
        _commission.ItemsSource = await AppServices.Staff.CommissionByStaffAsync(CFrom, CTo, AppServices.Session.LocationId);
        _lines.ItemsSource = null;
    }

    private async Task LoadLinesAsync()
    {
        if (_commission.SelectedItem is not CommissionRow row) { _lines.ItemsSource = null; return; }
        _lines.ItemsSource = await AppServices.Staff.CommissionLinesAsync(CFrom, CTo, AppServices.Session.LocationId, row.Key);
    }
}

/// <summary>Punches whoever signs in here in or out, without changing who is signed in to the program.</summary>
public sealed class PunchWindow : Window
{
    private readonly CredentialsPanel _credentials = new();
    private readonly TextBlock _message = new() { Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 17 };

    public PunchWindow()
    {
        Title = "Time clock";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var p = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
        p.Children.Add(new TextBlock { Text = "Time clock", FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 24 });
        p.Children.Add(new TextBlock { Text = "Sign in to punch in, or out when you are in.", Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 4, 0, 14) });
        p.Children.Add(_credentials);
        p.Children.Add(_message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var punch = new Button { Content = "Punch in / out", IsDefault = true, MinWidth = 150, MinHeight = 52, Margin = new Thickness(0, 0, 10, 0), Style = Layout.S("AccentButton") };
        Look.SetIcon(punch, "");
        punch.Click += async (_, _) => await PunchAsync();
        buttons.Children.Add(punch);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 130, MinHeight = 52 };
        Look.SetIcon(close, "");
        buttons.Children.Add(close);
        p.Children.Add(buttons);
        Content = p;
        Loaded += (_, _) => _credentials.NameBox.Focus();
    }

    private async Task PunchAsync()
    {
        var result = await _credentials.SignInAsync(purpose: null);
        _credentials.Secret.Clear();
        if (result.User is not { } user)
        {
            _message.Foreground = Look.Brush("Danger");
            _message.Text = result.Message;
            return;
        }
        TimeClockEntry? entry = null;
        if (!await Ui.Try(async () => entry = await AppServices.Staff.PunchAsync(user.Id)) || entry is null) return;
        _message.Foreground = Look.Brush("Ok");
        _message.Text = entry.ClockOut is { } off
            ? $"{user.UserName} punched out at {off.ToString("t", CultureInfo.CurrentCulture)} after {TimeClock.Hours(entry.ClockIn, off):0.##} hours."
            : $"{user.UserName} punched in at {entry.ClockIn.ToString("t", CultureInfo.CurrentCulture)}.";
        _credentials.NameBox.Clear();
        _credentials.NameBox.Focus();
    }
}

/// <summary>A supervisor's correction of a time clock entry, or a shift added by hand; a reason is required.</summary>
public sealed class TimeEntryWindow : Window
{
    private readonly ComboBox _user = new() { DisplayMemberPath = nameof(UserAccount.UserName) };
    private readonly DatePicker _inDay = new() { Width = 180 };
    private readonly TextBox _inTime = new() { Width = 100, Margin = new Thickness(8, 0, 0, 0) };
    private readonly DatePicker _outDay = new() { Width = 180 };
    private readonly TextBox _outTime = new() { Width = 100, Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox _stillIn = new() { Content = "Still punched in", Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _reason = new();

    public Guid? UserId { get; private set; }
    public DateTime ClockIn { get; private set; }
    public DateTime? ClockOut { get; private set; }
    public string Reason => _reason.Text.Trim();

    public TimeEntryWindow(TimeClockEntry? entry, IReadOnlyList<UserAccount>? users)
    {
        Title = entry is null ? "Add a time clock entry" : $"Change {entry.UserName}'s entry";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var p = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
        p.Children.Add(new TextBlock { Text = Title, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 24, Margin = new Thickness(0, 0, 0, 14) });
        if (entry is null)
        {
            _user.ItemsSource = users;
            _user.SelectedIndex = users is { Count: > 0 } ? 0 : -1;
            p.Children.Add(Row("Staff", _user));
        }
        UserId = entry?.UserId;
        var start = entry?.ClockIn ?? DateTime.Today.AddHours(9);
        var end = entry?.ClockOut ?? (entry is null ? DateTime.Today.AddHours(17) : DateTime.Now);
        _inDay.SelectedDate = start.Date;
        _inTime.Text = start.ToString("t", CultureInfo.CurrentCulture);
        _outDay.SelectedDate = end.Date;
        _outTime.Text = end.ToString("t", CultureInfo.CurrentCulture);
        p.Children.Add(Row("In", Pair(_inDay, _inTime)));
        p.Children.Add(Row("Out", Pair(_outDay, _outTime)));
        if (entry is not null)
        {
            _stillIn.IsChecked = entry.ClockOut is null;
            p.Children.Add(Row("", _stillIn));
        }
        p.Children.Add(Row("Reason", _reason));
        _reason.Text = entry?.EditReason ?? "";
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var ok = new Button { Content = "Save", IsDefault = true, MinWidth = 130, MinHeight = 52, Margin = new Thickness(0, 0, 10, 0), Style = Layout.S("AccentButton") };
        Look.SetIcon(ok, "");
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 130, MinHeight = 52 };
        Look.SetIcon(cancel, "");
        buttons.Children.Add(cancel);
        p.Children.Add(buttons);
        Content = p;
        Loaded += (_, _) => _reason.Focus();
    }

    private static StackPanel Pair(UIElement a, UIElement b)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal };
        s.Children.Add(a);
        s.Children.Add(b);
        return s;
    }

    private static DockPanel Row(string label, UIElement input)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var l = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center, Foreground = Look.Brush("Muted") };
        DockPanel.SetDock(l, Dock.Left);
        row.Children.Add(l);
        row.Children.Add(input);
        return row;
    }

    private static DateTime? Combine(DatePicker day, TextBox time) =>
        day.SelectedDate is { } d && TimeOnly.TryParse(time.Text, CultureInfo.CurrentCulture, out var t) ? d.Date + t.ToTimeSpan() : null;

    private void Accept()
    {
        if (_user.ItemsSource is not null) UserId = (_user.SelectedItem as UserAccount)?.Id;
        if (UserId is null) { Ui.Error("Choose the staff member."); return; }
        if (Combine(_inDay, _inTime) is not { } clockIn) { Ui.Error("Enter the day and time in, for example 08:30."); return; }
        DateTime? clockOut = null;
        if (_stillIn.IsChecked != true)
        {
            clockOut = Combine(_outDay, _outTime);
            if (clockOut is null) { Ui.Error("Enter the day and time out, for example 17:00."); return; }
        }
        if (Reason.Length == 0) { Ui.Error("Say why the entry is changed."); return; }
        ClockIn = clockIn;
        ClockOut = clockOut;
        DialogResult = true;
    }
}

/// <summary>Adds or changes a commission rule.</summary>
public sealed class RuleEditorWindow : Window
{
    private static readonly string[] Scopes = ["All items", "A department", "A category", "One item"];
    private static readonly string[] Bases = ["Percent of net sales", "Amount per unit sold"];
    private readonly TextBox _name = new();
    private readonly ComboBox _for = new();
    private readonly ComboBox _scope = new() { ItemsSource = Scopes };
    private readonly ComboBox _department = new() { DisplayMemberPath = nameof(Department.Name) };
    private readonly ComboBox _category = new() { DisplayMemberPath = nameof(Category.Name) };
    private readonly Button _item = new() { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 260 };
    private readonly ComboBox _basis = new() { ItemsSource = Bases };
    private readonly TextBox _rate = new() { Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly DatePicker _from = new() { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly DatePicker _to = new() { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _active = new() { Content = "Active" };
    private readonly IReadOnlyList<UserAccount?> _users;
    private readonly DockPanel _departmentRow, _categoryRow, _itemRow;
    private Guid? _itemId;

    public CommissionRule Rule { get; private set; } = new();

    public RuleEditorWindow(CommissionRule? rule, IReadOnlyList<UserAccount> users, IReadOnlyList<Department> departments, IReadOnlyList<Category> categories,
        IReadOnlyDictionary<Guid, string> items)
    {
        Title = rule is null ? "New commission rule" : "Commission rule " + rule.Name;
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _users = [null, .. users];
        _for.ItemsSource = _users.Select(u => u is null ? "Everyone" : u.UserName).ToList();
        _department.ItemsSource = departments;
        _category.ItemsSource = categories;
        _item.Click += (_, _) =>
        {
            var picker = new ItemPickerWindow();
            if (picker.ShowDialog() != true || picker.Selected is not { } row) return;
            _itemId = row.Id;
            _item.Content = $"{row.Plu} {row.Name}";
        };

        var p = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
        p.Children.Add(new TextBlock { Text = Title, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 24, Margin = new Thickness(0, 0, 0, 14) });
        p.Children.Add(Row("Name", _name));
        p.Children.Add(Row("For", _for));
        p.Children.Add(Row("Applies to", _scope));
        p.Children.Add(_departmentRow = Row("Department", _department));
        p.Children.Add(_categoryRow = Row("Category", _category));
        p.Children.Add(_itemRow = Row("Item", _item));
        p.Children.Add(Row("Commission", _basis));
        p.Children.Add(Row("Rate", _rate));
        p.Children.Add(Row("First day", _from));
        p.Children.Add(Row("Last day", _to));
        p.Children.Add(Row("", _active));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var ok = new Button { Content = "Save", IsDefault = true, MinWidth = 130, MinHeight = 52, Margin = new Thickness(0, 0, 10, 0), Style = Layout.S("AccentButton") };
        Look.SetIcon(ok, "");
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 130, MinHeight = 52 };
        Look.SetIcon(cancel, "");
        buttons.Children.Add(cancel);
        p.Children.Add(buttons);
        Content = p;

        var r = rule ?? new CommissionRule();
        Rule.Id = r.Id;
        _name.Text = r.Name;
        _for.SelectedIndex = Math.Max(0, _users.ToList().FindIndex(u => u?.Id == r.UserId));
        _scope.SelectedIndex = (int)r.Scope;
        _department.SelectedItem = departments.FirstOrDefault(d => d.Id == r.DepartmentId);
        _category.SelectedItem = categories.FirstOrDefault(c => c.Id == r.CategoryId);
        _itemId = r.ItemId;
        _item.Content = r.ItemId is Guid i && items.TryGetValue(i, out var name) ? name : "Choose the item…";
        _basis.SelectedIndex = (int)r.Basis;
        _rate.Text = r.Rate.ToString("0.###", CultureInfo.CurrentCulture);
        _from.SelectedDate = r.EffectiveFrom?.ToDateTime(TimeOnly.MinValue);
        _to.SelectedDate = r.EffectiveTo?.ToDateTime(TimeOnly.MinValue);
        _active.IsChecked = r.IsActive;
        _scope.SelectionChanged += (_, _) => ShowScope();
        ShowScope();
        Loaded += (_, _) => _name.Focus();
    }

    private void ShowScope()
    {
        _departmentRow.Visibility = _scope.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        _categoryRow.Visibility = _scope.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        _itemRow.Visibility = _scope.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static DockPanel Row(string label, UIElement input)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var l = new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center, Foreground = Look.Brush("Muted") };
        DockPanel.SetDock(l, Dock.Left);
        row.Children.Add(l);
        row.Children.Add(input);
        return row;
    }

    private void Accept()
    {
        if (!decimal.TryParse(_rate.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var rate)) { Ui.Error("Enter the rate as a number."); return; }
        Rule = new CommissionRule
        {
            Id = Rule.Id, Name = _name.Text, UserId = _users[Math.Max(0, _for.SelectedIndex)]?.Id, Scope = (CommissionScope)Math.Max(0, _scope.SelectedIndex),
            DepartmentId = (_department.SelectedItem as Department)?.Id, CategoryId = (_category.SelectedItem as Category)?.Id, ItemId = _itemId,
            Basis = (CommissionBasis)Math.Max(0, _basis.SelectedIndex), Rate = rate,
            EffectiveFrom = _from.SelectedDate is { } f ? DateOnly.FromDateTime(f) : null, EffectiveTo = _to.SelectedDate is { } t ? DateOnly.FromDateTime(t) : null,
            IsActive = _active.IsChecked == true,
        };
        DialogResult = true;
    }
}
