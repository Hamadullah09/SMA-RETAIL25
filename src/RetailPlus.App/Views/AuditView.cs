using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Views;

/// <summary>An audit line as the list shows it, in local time.</summary>
public sealed record AuditRow(AuditEntry Entry)
{
    public DateTime When => DateTime.SpecifyKind(Entry.AtUtc, DateTimeKind.Utc).ToLocalTime();
    public string? UserName => Entry.UserName;
    public int Station => Entry.StationNumber;
    public string Action => Entry.Action;
    public string? Table => Entry.EntityName;
    public string? Details => Entry.Details;
}

/// <summary>The audit log: who changed what and when, with the values before and after. Needs audit.view.</summary>
public sealed class AuditView : UserControl, IRefreshable
{
    private readonly DatePicker _from = new() { SelectedDate = DateTime.Today.AddDays(-7), Margin = new Thickness(0, 0, 8, 6) };
    private readonly DatePicker _to = new() { SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 6) };
    private readonly ComboBox _user = new() { Width = 170, Margin = new Thickness(0, 0, 8, 6), DisplayMemberPath = nameof(UserAccount.UserName) };
    private readonly ComboBox _action = new() { Width = 170, Margin = new Thickness(0, 0, 8, 6) };
    private readonly TextBox _text = new() { Width = 200, Margin = new Thickness(0, 0, 8, 6) };
    private readonly TextBlock _count = new() { Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 8, 0, 0) };
    private readonly DataGrid _grid = Layout.Grid(
        Layout.Col("When", nameof(AuditRow.When), 140, "g"),
        Layout.Col("User", nameof(AuditRow.UserName), 110),
        Layout.Col("Station", nameof(AuditRow.Station), 60),
        Layout.Col("Action", nameof(AuditRow.Action), 120),
        Layout.Col("Table", nameof(AuditRow.Table), 110),
        Layout.Col("Details", nameof(AuditRow.Details)));
    private readonly TextBlock _detailTitle = new() { Style = Layout.S("SectionTitle"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    private readonly DataGrid _changes = Layout.Grid(
        Layout.Col("What", nameof(AuditChange.Property), 130),
        Layout.Col("Before", nameof(AuditChange.Before)),
        Layout.Col("After", nameof(AuditChange.After)));

    public AuditView()
    {
        var detail = new DockPanel { Width = 440, Margin = new Thickness(14, 0, 0, 0) };
        DockPanel.SetDock(_detailTitle, Dock.Top);
        detail.Children.Add(_detailTitle);
        detail.Children.Add(_changes);
        var body = new DockPanel();
        DockPanel.SetDock(_count, Dock.Bottom);
        body.Children.Add(_count);
        body.Children.Add(_grid);
        Content = Layout.Page("Audit log", out var bar, body, detail);
        Look.SetPlaceholder(_text, "Words to find");
        bar.Children.Add(Layout.Label("From"));
        bar.Children.Add(_from);
        bar.Children.Add(Layout.Label("To"));
        bar.Children.Add(_to);
        bar.Children.Add(_user);
        bar.Children.Add(_action);
        bar.Children.Add(_text);
        bar.Children.Add(Layout.Button("Show", async (_, _) => await Ui.Try(LoadAsync), true));
        _text.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await Ui.Try(LoadAsync); };
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.SelectionChanged += (_, _) => ShowDetail();
    }

    public async Task RefreshAsync()
    {
        var selectedUser = (_user.SelectedItem as UserAccount)?.Id;
        var users = new List<UserAccount> { new() { UserName = "Every user" } };
        users.AddRange(await AppServices.Setup.GetUsersAsync());
        _user.ItemsSource = users;
        _user.SelectedItem = users.FirstOrDefault(u => u.Id == selectedUser) ?? users[0];
        var selectedAction = _action.SelectedItem as string;
        var actions = new List<string> { "Every action" };
        actions.AddRange(await AppServices.Audit.GetActionsAsync());
        _action.ItemsSource = actions;
        _action.SelectedItem = actions.Contains(selectedAction ?? "") ? selectedAction : actions[0];
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var filter = new AuditFilter(
            DateOnly.FromDateTime(_from.SelectedDate ?? DateTime.Today), DateOnly.FromDateTime(_to.SelectedDate ?? DateTime.Today),
            _user.SelectedItem is UserAccount { Id: var id } && id != Guid.Empty ? id : null,
            _action.SelectedIndex > 0 ? _action.SelectedItem as string : null,
            string.IsNullOrWhiteSpace(_text.Text) ? null : _text.Text);
        const int most = 2000;
        var rows = (await AppServices.Audit.SearchAsync(filter, most)).Select(e => new AuditRow(e)).ToList();
        _grid.ItemsSource = rows;
        _count.Text = rows.Count >= most ? $"The newest {most:N0} lines; narrow the search to see older ones." : $"{rows.Count:N0} lines";
        if (rows.Count > 0) _grid.SelectedIndex = 0; else ShowDetail();
    }

    private void ShowDetail()
    {
        if (_grid.SelectedItem is not AuditRow row)
        {
            _detailTitle.Text = "Choose a line to see what changed.";
            _changes.ItemsSource = null;
            return;
        }
        _detailTitle.Text = $"{row.Action} {row.Table} {row.Details}".Trim() + $"\n{row.When.ToString("F", CultureInfo.CurrentCulture)} · {row.UserName ?? "nobody signed in"} · station {row.Station}";
        _changes.ItemsSource = AuditService.Changes(row.Entry);
    }
}
