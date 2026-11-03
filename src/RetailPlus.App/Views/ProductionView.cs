using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.App.Views;

/// <summary>
/// The shop floor: the production orders released to this station's location (it is their material or output location),
/// the most urgent first. On an order: start and finish its operations, issue material by scanning it, report time, good
/// and scrapped quantities, receive the output (and print its labels), and finish the order. It works without the server:
/// the orders and their copy of the bill came down with the master data, and what is done here goes up with the next send.
/// </summary>
public sealed class ProductionView : UserControl, IRefreshable
{
    /// <summary>An operation on screen.</summary>
    public sealed record OperationRow(int Sequence, string Name, string WorkCentre, string Status, decimal StandardMinutes, decimal Minutes, decimal Good, decimal Scrap,
        string? Instructions);

    /// <summary>A material on screen.</summary>
    public sealed record MaterialRow(string StockCode, string Description, decimal Planned, decimal Issued, decimal Left, string Method, string At);

    private readonly DataGrid _orders = Layout.Grid(
        Layout.Col("Order", nameof(ProductionOrderRow.Number), 100),
        Layout.Col("Product", nameof(ProductionOrderRow.ProductName)),
        Layout.Col("To make", nameof(ProductionOrderRow.Quantity), 75, "0.###", true),
        Layout.Col("Made", nameof(ProductionOrderRow.Received), 70, "0.###", true),
        Layout.Col("Priority", nameof(ProductionOrderRow.Priority), 65, null, true),
        Layout.Col("Start", nameof(ProductionOrderRow.PlannedStart), 95, "d"));
    private readonly DataGrid _operations = Layout.Grid(
        Layout.Col("Step", nameof(OperationRow.Sequence), 50, null, true),
        Layout.Col("Operation", nameof(OperationRow.Name)),
        Layout.Col("Work centre", nameof(OperationRow.WorkCentre), 120),
        Layout.Col("Status", nameof(OperationRow.Status), 80),
        Layout.Col("Minutes", nameof(OperationRow.Minutes), 70, "0.#", true),
        Layout.Col("Of", nameof(OperationRow.StandardMinutes), 60, "0.#", true),
        Layout.Col("Good", nameof(OperationRow.Good), 60, "0.###", true),
        Layout.Col("Scrap", nameof(OperationRow.Scrap), 60, "0.###", true));
    private readonly DataGrid _materials = Layout.Grid(
        Layout.Col("Stock code", nameof(MaterialRow.StockCode), 115),
        Layout.Col("Material", nameof(MaterialRow.Description)),
        Layout.Col("Needed", nameof(MaterialRow.Planned), 75, "0.###", true),
        Layout.Col("Issued", nameof(MaterialRow.Issued), 75, "0.###", true),
        Layout.Col("Still due", nameof(MaterialRow.Left), 75, "0.###", true),
        Layout.Col("How", nameof(MaterialRow.Method), 95),
        Layout.Col("At step", nameof(MaterialRow.At), 60));
    private readonly TextBox _code = new() { Width = 240, Margin = new Thickness(0, 0, 8, 6), FontSize = 19 };
    private readonly TextBox _quantity = new() { Width = 90, Margin = new Thickness(0, 0, 8, 6), Text = "1", FontSize = 19, HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _title = new() { Style = Layout.S("SectionTitle"), Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _summary = new() { Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _instructions = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10), FontSize = 17 };
    private readonly TextBlock _message = new() { Foreground = Look.Brush("Muted"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private OrderProgress? _current;

    private ProductionService Production => new(AppServices.Data);

    public ProductionView()
    {
        _orders.SelectionMode = DataGridSelectionMode.Single;
        _operations.SelectionMode = DataGridSelectionMode.Single;
        _materials.SelectionMode = DataGridSelectionMode.Single;
        _operations.MaxHeight = 260;

        var entry = new WrapPanel();
        entry.Children.Add(Layout.Label("Scan or type a material"));
        entry.Children.Add(_code);
        entry.Children.Add(Layout.Label("Quantity"));
        entry.Children.Add(_quantity);
        entry.Children.Add(Layout.Button("Issue it", async (_, _) => await Ui.Try(IssueEnteredAsync), true));

        var right = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
        var operationsTitle = new TextBlock { Text = "Operations", Style = Layout.S("FieldLabel"), Margin = new Thickness(0, 4, 0, 4) };
        var materialsTitle = new TextBlock { Text = "Materials", Style = Layout.S("FieldLabel"), Margin = new Thickness(0, 4, 0, 4) };
        foreach (var top in new UIElement[] { _title, _summary, operationsTitle, _operations, _instructions, entry, _message, materialsTitle })
        {
            DockPanel.SetDock(top, Dock.Top);
            right.Children.Add(top);
        }
        right.Children.Add(_materials);

        Content = Layout.Page("Production", out var bar, StockCountsView.Split(_orders, right));
        bar.Children.Add(Layout.Button("Refresh", async (_, _) => await Ui.Try(RefreshAsync)));
        bar.Children.Add(Layout.Button("Start operation", async (_, _) => await Ui.Try(() => StartAsync())));
        bar.Children.Add(Layout.Button("Finish operation", async (_, _) => await Ui.Try(() => FinishAsync())));
        bar.Children.Add(Layout.Button("Report work…", Report_Click));
        bar.Children.Add(Layout.Button("Issue the list", async (_, _) => await Ui.Try(IssueListAsync)));
        bar.Children.Add(Layout.Button("Scrap material…", Scrap_Click));
        bar.Children.Add(Layout.Button("Receive output…", Receive_Click));
        bar.Children.Add(Layout.Button("Finish order…", Complete_Click));
        bar.Children.Add(Layout.Button("Print labels…", async (_, _) => await Ui.Try(PrintLabelsAsync)));

        _orders.SelectionChanged += async (_, _) => await Ui.Try(LoadCurrentAsync);
        _operations.SelectionChanged += (_, _) => ShowInstructions();
        _code.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await Ui.Try(IssueEnteredAsync);
        };
        _message.Text = "Choose an order, then scan each material as it is used (or type its code and press Enter).";
        StockCountsView.RouteScans(this, _code);
    }

    /// <summary>The order chosen, when it may still be worked on.</summary>
    public ProductionOrder? CurrentOrder => _current?.Order;

    public async Task RefreshAsync()
    {
        var selected = (_orders.SelectedItem as ProductionOrderRow)?.Id ?? _current?.Order.Id;
        var rows = await Production.FloorOrdersAsync(AppServices.Session.LocationId);
        _orders.ItemsSource = rows;
        _orders.SelectedItem = rows.FirstOrDefault(r => r.Id == selected) ?? rows.FirstOrDefault();
        if (_orders.SelectedItem is null) await LoadCurrentAsync();
    }

    /// <summary>Chooses an order on the list by its id (the smoke check, and after an action).</summary>
    public async Task SelectAsync(Guid orderId)
    {
        await RefreshAsync();
        if (_orders.ItemsSource is IEnumerable<ProductionOrderRow> rows) _orders.SelectedItem = rows.FirstOrDefault(r => r.Id == orderId);
        await LoadCurrentAsync();
    }

    private async Task LoadCurrentAsync()
    {
        int? operation = (_operations.SelectedItem as OperationRow)?.Sequence;
        _current = _orders.SelectedItem is ProductionOrderRow row ? await Production.ProgressAsync(row.Id) : null;
        if (_current is null)
        {
            _title.Text = "No order chosen";
            _summary.Text = "Orders released for this location show here.";
            _operations.ItemsSource = null;
            _materials.ItemsSource = null;
            _instructions.Text = "";
            return;
        }
        var o = _current.Order;
        _title.Text = $"Order {o.Number} · {o.ProductCode} {o.ProductName}";
        _summary.Text = $"{o.Quantity:0.###} to make, {_current.Received:0.###} made" + (_current.ReportedScrap > 0 ? $", {_current.ReportedScrap:0.###} scrapped" : "")
                        + $" · {ProductionService.Describe(o.Status)} · from {o.MaterialLocation?.Code} to {o.OutputLocation?.Code}"
                        + (o.PlannedEnd is { } end ? $" · due {end.ToString("d", CultureInfo.CurrentCulture)}" : "") + (string.IsNullOrWhiteSpace(o.Notes) ? "" : " · " + o.Notes);
        var ops = _current.Operations.Select(p => new OperationRow(p.Sequence, p.Name, p.WorkCentre, p.Status.ToString(), p.StandardMinutes, p.Minutes, p.Good, p.Scrap,
            o.Operations.FirstOrDefault(x => x.Sequence == p.Sequence)?.Instructions)).ToList();
        _operations.ItemsSource = ops;
        _operations.SelectedItem = ops.FirstOrDefault(x => x.Sequence == operation) ?? ops.FirstOrDefault(x => x.Status != nameof(OperationStatus.Done)) ?? ops.FirstOrDefault();
        _materials.ItemsSource = _current.Lines.Where(l => l.Kind == ProductionLineKind.Component)
            .Select(l => new MaterialRow(l.StockCode, l.Description, l.Planned, l.Done, Math.Max(0, l.Planned - l.Done), l.IssueMethod == IssueMethod.Backflush ? "At the end" : "Scan",
                l.OperationSequence?.ToString(CultureInfo.CurrentCulture) ?? ""))
            .ToList();
        ShowInstructions();
    }

    private void ShowInstructions() =>
        _instructions.Text = _operations.SelectedItem is OperationRow { Instructions: { Length: > 0 } text } op ? $"{op.Sequence} {op.Name}: {text}" : "";

    private bool MayWork()
    {
        if (AppServices.Can(Permissions.ProductionFloor)) return true;
        Ui.Info("Your role can see production but not work on it.");
        return false;
    }

    private OrderProgress? Chosen()
    {
        if (_current is not null) return _current;
        Ui.Info("Choose an order first.");
        return null;
    }

    private int? ChosenOperation()
    {
        if (_operations.SelectedItem is OperationRow op) return op.Sequence;
        Ui.Info(_current?.Operations.Count == 0 ? "This order has no operations; report its output instead." : "Choose an operation first.");
        return null;
    }

    private async Task ReloadAsync() => await SelectAsync(_current!.Order.Id);

    public async Task StartAsync(int? sequence = null)
    {
        if (!MayWork() || Chosen() is not { } order || (sequence ?? ChosenOperation()) is not int step) return;
        await Production.StartOperationAsync(order.Order.Id, step);
        await ReloadAsync();
    }

    public async Task FinishAsync(int? sequence = null)
    {
        if (!MayWork() || Chosen() is not { } order || (sequence ?? ChosenOperation()) is not int step) return;
        await Production.FinishOperationAsync(order.Order.Id, step);
        await ReloadAsync();
    }

    /// <summary>Reports minutes, good and scrapped quantities on an operation, under the signed-in user's name.</summary>
    public async Task ReportAsync(int sequence, decimal minutes, decimal good, decimal scrap)
    {
        if (!MayWork() || Chosen() is not { } order) return;
        await Production.ReportAsync(order.Order.Id, sequence, minutes, good, scrap, operatorId: AppServices.Session.UserId, operatorName: AppServices.Session.UserName);
        _message.Text = $"Reported on step {sequence}: {minutes:0.#} minutes, {good:0.###} good" + (scrap > 0 ? $", {scrap:0.###} scrapped." : ".");
        await ReloadAsync();
    }

    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        if (!MayWork() || Chosen() is null || ChosenOperation() is not int step) return;
        var minutes = Ui.PromptDecimal($"Minutes worked on step {step}:", "Report work", 0);
        if (minutes is null) return;
        var good = Ui.PromptDecimal("Good quantity made at this step:", "Report work", 0);
        if (good is null) return;
        var scrap = Ui.PromptDecimal("Quantity scrapped at this step:", "Report work", 0);
        if (scrap is null) return;
        await Ui.Try(() => ReportAsync(step, minutes.Value, good.Value, scrap.Value));
    }

    /// <summary>Issues what was scanned or typed to the chosen order.</summary>
    public async Task IssueEnteredAsync()
    {
        if (!MayWork() || Chosen() is not { } order) return;
        var code = _code.Text.Trim();
        if (code.Length == 0) return;
        if (!decimal.TryParse(_quantity.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity) || quantity == 0)
        {
            Ui.Error("Type the quantity in numbers, for example 2.5 (a negative quantity gives material back).");
            return;
        }
        try
        {
            var movement = await Production.IssueAsync(order.Order.Id, code, quantity);
            _message.Text = $"{(quantity > 0 ? "Issued" : "Given back")} {Math.Abs(quantity):0.###} of {code}; {movement.OnHandAfter:0.###} left in stock.";
            _message.Foreground = Look.Brush("Muted");
            _code.Clear();
            _quantity.Text = "1";
        }
        catch (BusinessRuleException ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Look.Brush("Danger");
            System.Media.SystemSounds.Beep.Play();
            _code.SelectAll();
            return;
        }
        await ReloadAsync();
        _code.Focus();
    }

    /// <summary>Sets the code and quantity boxes (as a scan would) and issues them.</summary>
    public async Task IssueAsync(string code, decimal quantity)
    {
        _code.Text = code;
        _quantity.Text = quantity.ToString(CultureInfo.CurrentCulture);
        await IssueEnteredAsync();
    }

    private async Task IssueListAsync()
    {
        if (!MayWork() || Chosen() is not { } order) return;
        int step = (_operations.SelectedItem as OperationRow)?.Sequence ?? 0;
        bool onlyStep = order.Lines.Any(l => l.OperationSequence == step && l.IssueMethod == IssueMethod.Manual)
                        && Ui.Confirm($"Issue only the materials used at step {step}? (No issues every material still due.)");
        int issued = await Production.IssueRemainingAsync(order.Order.Id, onlyStep ? step : null);
        _message.Text = issued == 0 ? "Nothing is still due to issue by hand." : $"{issued} material(s) issued from the list.";
        await ReloadAsync();
    }

    private async void Scrap_Click(object sender, RoutedEventArgs e)
    {
        if (!MayWork() || Chosen() is not { } order) return;
        var code = _materials.SelectedItem is MaterialRow m ? m.StockCode : Ui.Prompt("Stock code of the material thrown away:", "Scrap material");
        if (string.IsNullOrWhiteSpace(code)) return;
        var quantity = Ui.PromptDecimal($"How much {code} was thrown away?", "Scrap material", 1);
        if (quantity is null) return;
        var reason = Ui.Prompt("Why (for example spilt, burnt):", "Scrap material", "Spoiled in production");
        if (reason is null) return;
        await Ui.Try(async () =>
        {
            await Production.ScrapMaterialAsync(order.Order.Id, code, quantity.Value, reason);
            _message.Text = $"{quantity:0.###} of {code} thrown away.";
            await ReloadAsync();
        });
    }

    /// <summary>Receives output into stock; with <paramref name="complete"/> the order is finished (backflush and by-products).</summary>
    public async Task ReceiveAsync(decimal quantity, bool complete, IReadOnlyList<ByProductQuantity>? byProducts = null)
    {
        if (!MayWork() || Chosen() is not { } order) return;
        var progress = await Production.ReceiveAsync(order.Order.Id, quantity, complete, byProducts);
        _message.Text = complete ? $"Order {order.Order.Number} finished: {progress.Received:0.###} made." : $"{quantity:0.###} received into {order.Order.OutputLocation?.Code}.";
        await RefreshAsync();
        _current = progress.Order.Status is ProductionOrderStatus.Released or ProductionOrderStatus.InProgress ? progress : null;
        if (_current is not null) await LoadCurrentAsync();
    }

    private async void Receive_Click(object sender, RoutedEventArgs e)
    {
        if (!MayWork() || Chosen() is not { } order) return;
        decimal left = Math.Max(0, order.Order.Quantity - order.Received);
        var quantity = Ui.PromptDecimal($"How many {order.Order.ProductName} were made and go into stock now?", "Receive output", left);
        if (quantity is null) return;
        var product = order.Order;
        if (await Ui.Try(() => ReceiveAsync(quantity.Value, complete: false)) && quantity > 0 && Ui.Confirm($"Print {quantity:0.###} label(s) for {product.ProductName}?"))
            await Ui.Try(() => PrintLabelsAsync(product));
    }

    private async void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (!MayWork() || Chosen() is not { } order) return;
        decimal left = Math.Max(0, order.Order.Quantity - order.Received);
        var quantity = Ui.PromptDecimal($"The last of the output: how many {order.Order.ProductName} go into stock with it? (0 when all were received)", "Finish order", left);
        if (quantity is null) return;
        var byProducts = new List<ByProductQuantity>();
        foreach (var line in order.Lines.Where(l => l.Kind == ProductionLineKind.ByProduct))
        {
            var made = Ui.PromptDecimal($"How much {line.Description} ({line.StockCode}) came out of the order?", "Finish order", line.Planned);
            if (made is null) return;
            byProducts.Add(new ByProductQuantity(line.StockCode, made.Value));
        }
        if (!Ui.Confirm($"Finish order {order.Order.Number}? Materials issued at the end are taken from stock now, and the order cannot be worked on afterwards.")) return;
        await Ui.Try(() => ReceiveAsync(quantity.Value, complete: true, byProducts));
    }

    private async Task PrintLabelsAsync() => await PrintLabelsAsync(Chosen()?.Order);

    /// <summary>Opens Print labels with the order's product, from its output location.</summary>
    private static async Task PrintLabelsAsync(ProductionOrder? order)
    {
        if (order is null) return;
        var rows = (await AppServices.Inventory.SearchAsync(order.OutputLocationId, order.ProductCode))
            .Where(r => string.Equals(r.Plu, order.ProductCode, StringComparison.OrdinalIgnoreCase)).ToList();
        if (rows.Count == 0)
        {
            Ui.Info($"There is no item {order.ProductCode} at the output location.");
            return;
        }
        new LabelsWindow(rows, rows) { Owner = Ui.Owner }.ShowDialog();
    }
}
