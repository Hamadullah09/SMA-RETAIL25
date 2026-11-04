using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Views;

/// <summary>
/// Purchase Orders › Requisitions: staff ask for goods (a draft, then sent for approval); someone who may approve purchases
/// approves or rejects it, here or on the web (a supervisor can sign in for it at this till); an approved requisition then
/// becomes purchase orders, one per supplier, on the Orders tab.
/// </summary>
public sealed class RequisitionsPanel : DockPanel
{
    private readonly DataGrid _list = Layout.Grid(
        Layout.Col("Number", nameof(RequisitionRow.Number), 85),
        Layout.Col("Made", nameof(RequisitionRow.CreatedAt), 95, "d"),
        Layout.Col("Status", nameof(RequisitionRow.Status), 95),
        Layout.Col("Lines", nameof(RequisitionRow.Lines), 45, null, true),
        Layout.Col("Total", nameof(RequisitionRow.Total), 85, "N2", true));
    private readonly DataGrid _lines = new()
    {
        AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single, Margin = new Thickness(12, 0, 0, 0),
    };
    private readonly TextBlock _title = new() { Style = Layout.S("SectionTitle"), Margin = new Thickness(12, 0, 0, 2) };
    private readonly TextBlock _state = new() { Foreground = Look.Brush("Muted"), Margin = new Thickness(12, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _totals = new() { Foreground = Look.Brush("Muted"), Margin = new Thickness(12, 6, 0, 0) };
    private PurchaseRequisition? _current;

    /// <summary>Raised after an approved requisition became purchase orders, with the first of them.</summary>
    public event Action<Guid>? OrdersMade;

    /// <summary>The requisition shown, as last read.</summary>
    public PurchaseRequisition? Current => _current;

    public RequisitionsPanel()
    {
        _list.Width = 440;
        _list.SelectionMode = DataGridSelectionMode.Single;
        void Ro(string h, string p, double w, string? f = null) => _lines.Columns.Add(new DataGridTextColumn
        {
            Header = h, Binding = new Binding(p) { StringFormat = f }, IsReadOnly = true,
            Width = w < 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(w),
        });
        void Rw(string h, string p, double w) => _lines.Columns.Add(new DataGridTextColumn
        {
            Header = h, Binding = new Binding(p) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, StringFormat = "0.###" }, Width = w,
        });
        Ro("Stock code", nameof(PurchaseRequisitionLine.Plu), 130);
        Ro("Description", nameof(PurchaseRequisitionLine.Description), -1);
        Ro("Supplier", nameof(PurchaseRequisitionLine.SupplierName), 150);
        Rw("Pieces", nameof(PurchaseRequisitionLine.Quantity), 95);
        Rw("Cost each", nameof(PurchaseRequisitionLine.CostEach), 105);
        Ro("Needed by", nameof(PurchaseRequisitionLine.NeededBy), 110, "d");

        var toolbar = new WrapPanel { Style = Layout.S("Toolbar") };
        DockPanel.SetDock(toolbar, Dock.Top);
        Children.Add(toolbar);
        toolbar.Children.Add(Layout.Button("New requisition…", New_Click, true));
        toolbar.Children.Add(Keyed(Layout.Button("Add item", AddItem_Click), "Ins"));
        toolbar.Children.Add(Keyed(Layout.Button("Remove line", RemoveLine_Click), "Ctrl+Del"));
        toolbar.Children.Add(Layout.Button("Save changes", async (_, _) => await Ui.Try(SaveAsync)));
        toolbar.Children.Add(Keyed(Layout.Button("Send for approval", async (_, _) => await Ui.Try(SubmitAsync)), "F6"));
        toolbar.Children.Add(Keyed(Layout.Button("Approve", async (_, _) => await Ui.Try(ApproveAsync)), "F7"));
        toolbar.Children.Add(Keyed(Layout.Button("Reject…", Reject_Click), "F8"));
        toolbar.Children.Add(Keyed(Layout.Button("Make purchase orders", async (_, _) => await Ui.Try(async () => await OrderAsync())), "F9"));
        toolbar.Children.Add(Layout.Button("Delete requisition", Delete_Click));

        var right = new DockPanel();
        foreach (var top in new UIElement[] { _title, _state })
        {
            DockPanel.SetDock(top, Dock.Top);
            right.Children.Add(top);
        }
        DockPanel.SetDock(_totals, Dock.Bottom);
        right.Children.Add(_totals);
        right.Children.Add(_lines);
        var body = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left);
        body.Children.Add(_list);
        body.Children.Add(right);
        Children.Add(body);
        _list.SelectionChanged += async (_, _) => await Ui.Try(LoadCurrentAsync);
    }

    private static Button Keyed(Button button, string key)
    {
        Look.SetShortcut(button, key);
        return button;
    }

    public async Task RefreshAsync()
    {
        var selected = (_list.SelectedItem as RequisitionRow)?.Id ?? _current?.Id;
        var rows = await AppServices.Requisitions.ListAsync(locationId: AppServices.Session.LocationId);
        _list.ItemsSource = rows;
        _list.SelectedItem = rows.FirstOrDefault(r => r.Id == selected) ?? rows.FirstOrDefault();
        await LoadCurrentAsync();
    }

    private async Task LoadCurrentAsync()
    {
        if (_list.SelectedItem is not RequisitionRow row)
        {
            _current = null;
            _lines.ItemsSource = null;
            _title.Text = _state.Text = _totals.Text = "";
            return;
        }
        _current = await AppServices.Requisitions.GetAsync(row.Id);
        if (_current is null) return;
        _lines.ItemsSource = _current.Lines;
        _lines.IsReadOnly = _current.Status is not (RequisitionStatus.Draft or RequisitionStatus.Rejected);
        _title.Text = $"Requisition {_current.Number}" + (string.IsNullOrWhiteSpace(_current.Note) ? "" : " · " + _current.Note);
        _state.Text = StateText(_current);
        _totals.Text = $"{_current.Lines.Count} lines · {_current.Lines.Sum(l => l.Quantity):0.###} pieces · about {RequisitionService.Total(_current):N2}";
    }

    /// <summary>Where the requisition stands and what comes next, in words.</summary>
    private static string StateText(PurchaseRequisition r) => r.Status switch
    {
        RequisitionStatus.Draft => $"Draft, asked for by {r.RequestedBy ?? "this till"}. Add what is needed, then Send for approval (F6).",
        RequisitionStatus.Submitted => $"Waiting for approval since {r.SubmittedAt:g}. Someone who may approve purchases approves (F7) or rejects (F8) it.",
        RequisitionStatus.Approved => $"Approved by {r.DecidedBy} on {r.DecidedAt:g}" + (r.DecisionNote is null ? "" : $" ({r.DecisionNote})")
                                      + ". Make purchase orders (F9) to order it.",
        RequisitionStatus.Rejected => $"Rejected by {r.DecidedBy} on {r.DecidedAt:g}: {r.DecisionNote}. Change it and send it again, or delete it.",
        _ => $"Ordered on {r.OrderedAt:g}: the purchase orders are on the Orders tab.",
    };

    /// <summary>Selects a requisition (after it was made or changed) and shows it.</summary>
    public async Task ShowAsync(Guid id)
    {
        var rows = await AppServices.Requisitions.ListAsync(locationId: AppServices.Session.LocationId);
        _list.ItemsSource = rows;
        _current = null;
        _list.SelectedItem = rows.FirstOrDefault(r => r.Id == id);
        await LoadCurrentAsync();
    }

    // Actions, also run by the smoke check ----------------------------------------------------------------------------

    /// <summary>A new draft requisition for this till's location.</summary>
    public async Task<PurchaseRequisition> NewAsync(string? note)
    {
        var made = await AppServices.Requisitions.CreateAsync(AppServices.Session.LocationId, null, note);
        await ShowAsync(made.Id);
        return made;
    }

    /// <summary>Adds an item to the draft (its pieces to the line already there), keeping the other lines as they are.</summary>
    public async Task AddLineAsync(Guid itemId, decimal pieces)
    {
        if (_current is null) return;
        _lines.CommitEdit(DataGridEditingUnit.Row, true);
        var inputs = Inputs(_current).ToList();
        int at = inputs.FindIndex(i => i.ItemId == itemId);
        if (at >= 0) inputs[at] = inputs[at] with { Quantity = inputs[at].Quantity + pieces };
        else inputs.Add(new RequisitionLineInput(itemId, null, pieces));
        await AppServices.Requisitions.SaveAsync(_current.Id, _current.Version, _current.NeededBy, _current.Note, inputs);
        await ShowAsync(_current.Id);
    }

    public async Task SaveAsync()
    {
        if (_current is null) return;
        _lines.CommitEdit(DataGridEditingUnit.Row, true);
        await AppServices.Requisitions.SaveAsync(_current.Id, _current.Version, _current.NeededBy, _current.Note, [.. Inputs(_current)]);
        await ShowAsync(_current.Id);
    }

    public async Task SubmitAsync()
    {
        if (_current is null) return;
        if (_current.Status is RequisitionStatus.Draft or RequisitionStatus.Rejected && !_lines.IsReadOnly) await SaveAsync();
        await AppServices.Requisitions.SubmitAsync(_current!.Id, _current.Version);
        await ShowAsync(_current.Id);
    }

    /// <summary>Approves it as the signed-in user, or as a supervisor who signs in for it.</summary>
    public async Task ApproveAsync()
    {
        if (_current is null) return;
        if (Approval.SessionFor(Permissions.PurchasingApprove, $"Approving requisition {_current.Number}") is not { } approver) return;
        await As(approver).ApproveAsync(_current.Id, _current.Version);
        await ShowAsync(_current.Id);
    }

    public async Task RejectAsync(string reason)
    {
        if (_current is null) return;
        if (Approval.SessionFor(Permissions.PurchasingApprove, $"Rejecting requisition {_current.Number}") is not { } approver) return;
        await As(approver).RejectAsync(_current.Id, reason, _current.Version);
        await ShowAsync(_current.Id);
    }

    /// <summary>Puts an approved requisition on purchase orders, one per supplier, and says which.</summary>
    public async Task<RequisitionOrders?> OrderAsync()
    {
        if (_current is null) return null;
        var made = await AppServices.Requisitions.OrderAsync(_current.Id, _current.Version);
        await ShowAsync(_current.Id);
        Ui.Info("Purchase orders made: " + string.Join(", ", made.Orders.Select(o => $"PO {o.Number}" + (o.Supplier is null ? "" : $" ({o.Supplier})")))
                + ". They are drafts on the Orders tab: check them, then Post order.");
        if (made.Orders.Count > 0) OrdersMade?.Invoke(made.Orders[0].Id);
        return made;
    }

    private static RequisitionService As(SessionInfo approver) =>
        ReferenceEquals(approver, AppServices.Session) ? AppServices.Requisitions : new RequisitionService(AppServices.Data.For(AppServices.Data.Station, approver));

    private static IEnumerable<RequisitionLineInput> Inputs(PurchaseRequisition r) =>
        r.Lines.OrderBy(l => l.LineNumber).Select(l => new RequisitionLineInput(l.ItemId, l.ItemId is null ? l.Plu : null, l.Quantity, l.NeededBy, l.SupplierId, l.CostEach, l.Note));

    // Buttons ---------------------------------------------------------------------------------------------------------

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        var note = Ui.Prompt("What is it for? A few words for the person who approves it (or leave it empty):", "New requisition");
        if (note is null) return;
        if (!await Ui.Try(async () => await NewAsync(note))) return;
        AddItem_Click(sender, e);
    }

    private async void AddItem_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) { Ui.Info("Make a requisition first (New requisition, Ctrl+N)."); return; }
        if (_current.Status is not (RequisitionStatus.Draft or RequisitionStatus.Rejected)) { Ui.Info($"Requisition {_current.Number} is {Word(_current)}; only a draft is changed."); return; }
        var picker = new ItemPickerWindow();
        if (picker.ShowDialog() != true || picker.Selected is not { } item) return;
        var qty = Ui.PromptDecimal("Pieces needed:", "Add item", 1);
        if (qty is null) return;
        await Ui.Try(() => AddLineAsync(item.Id, qty.Value));
    }

    private async void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _lines.IsReadOnly || _lines.SelectedItem is not PurchaseRequisitionLine line) return;
        _current.Lines.Remove(line);
        await Ui.Try(SaveAsync);
    }

    private async void Reject_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var reason = Ui.Prompt($"Why is requisition {_current.Number} rejected? The person who asked sees this.", "Reject requisition");
        if (string.IsNullOrWhiteSpace(reason)) return;
        await Ui.Try(() => RejectAsync(reason));
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || !Ui.Confirm($"Delete requisition {_current.Number}?")) return;
        await Ui.Try(async () =>
        {
            await AppServices.Requisitions.DeleteAsync(_current.Id);
            _current = null;
            await RefreshAsync();
        });
    }

    private static string Word(PurchaseRequisition r) => r.Status == RequisitionStatus.Submitted ? "waiting for approval" : r.Status.ToString().ToLowerInvariant();
}
