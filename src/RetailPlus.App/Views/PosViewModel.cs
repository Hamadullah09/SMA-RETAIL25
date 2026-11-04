using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RetailPlus.App.Dialogs;
using RetailPlus.App.Infrastructure;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Devices;
using RetailPlus.Reports;
using RetailPlus.Rfid;

namespace RetailPlus.App.Views;

/// <summary>The sale being rung up on the Point of Sale screen.</summary>
public sealed partial class PosViewModel : ObservableObject
{
    public ObservableCollection<SaleDraftLine> Lines { get; } = [];
    public ObservableCollection<FastKey> FastKeys { get; } = [];

    private SaleDraft _draft = new();
    private Sale? _lastSale;

    [ObservableProperty] private string _codeText = "";
    [ObservableProperty] private string _clientText = "Walk-in customer";
    [ObservableProperty] private string _clientDetail = "";
    [ObservableProperty] private string _subtotalText = "0.00";
    [ObservableProperty] private string _discountText = "0.00";
    [ObservableProperty] private string _tax1Text = "0.00";
    [ObservableProperty] private string _tax2Text = "0.00";
    [ObservableProperty] private string _tax1Label = "Tax 1";
    [ObservableProperty] private string _tax2Label = "Tax 2";
    [ObservableProperty] private string _totalText = "0.00";
    /// <summary>What promotions take off the sale ("2 promotions save 150.00"); empty when none applies.</summary>
    [ObservableProperty] private string _promotionText = "";
    [ObservableProperty] private string _message = "Ready for the next customer. Scan an item, or type its code and press Enter.";
    /// <summary>The picture of the item on the chosen line (normally the one just rung up), shown beside the message.</summary>
    [ObservableProperty] private System.Windows.Media.ImageSource? _linePicture;
    /// <summary>The message is a problem the cashier must see (red), not information (yellow).</summary>
    [ObservableProperty] private bool _messageIsAlert;
    [ObservableProperty] private bool _returnMode;
    [ObservableProperty] private bool _hasClient;
    [ObservableProperty] private bool _canReward;
    [ObservableProperty] private string _modeText = "";
    [ObservableProperty] private SaleDraftLine? _selectedLine;

    private static CultureInfo C => CultureInfo.CurrentCulture;
    private static SessionInfo Session => AppServices.Session;
    private static StoreSettings Settings => AppServices.Settings;

    public SaleDraft Draft => _draft;

    public async Task LoadAsync()
    {
        FastKeys.Clear();
        foreach (var k in await AppServices.Setup.GetFastKeysAsync(0)) FastKeys.Add(k);
        Tax1Label = Settings.Tax1Name ?? "Tax 1";
        Tax2Label = Settings.Tax2Name ?? "Tax 2";
        ModeText = Session.IsTrainee ? "TRAINING MODE — sales are not saved" : "";
        await LoadPromotionsAsync();
        Recalculate();
    }

    public void Recalculate()
    {
        ApplyPromotions();
        _draft.Lines = Lines.ToList();
        var totals = _draft.Calculate(_draft.EffectiveTax(Settings));
        SubtotalText = totals.Subtotal.ToString("N2", C);
        DiscountText = totals.SubtotalDiscount.ToString("N2", C);
        Tax1Text = totals.Tax1.ToString("N2", C);
        Tax2Text = totals.Tax2.ToString("N2", C);
        TotalText = totals.Total.ToString("N2", C);
        CanReward = _draft.ClientId is not null && _clientPoints is decimal p && BonusPoints.QualifiesForReward(Settings, p - _draft.PointsRedeemed, _draft.SubtotalDiscountPercent > 0) && _draft.PointsRedeemed == 0;
        AppServices.Pole(Lines.LastOrDefault()?.Description ?? "", $"TOTAL {TotalText}");
    }

    public decimal Total => _draft.Calculate(_draft.EffectiveTax(Settings)).Total;

    // Adding items -----------------------------------------------------------------------------------------------

    public async Task EnterCodeAsync()
    {
        var text = CodeText.Trim();
        CodeText = "";
        if (text.Length == 0) return;
        decimal qty = 1;
        int star = text.IndexOf('*', StringComparison.Ordinal);
        if (star > 0 && decimal.TryParse(text[..star], NumberStyles.Number, C, out var q))
        {
            qty = q;
            text = text[(star + 1)..].Trim();
        }
        await AddCodeAsync(text, qty);
    }

    public async Task AddCodeAsync(string code, decimal qty = 1)
    {
        var (item, variant) = await AppServices.Inventory.FindByCodeAsync(Session.LocationId, code);
        decimal? embeddedPrice = null;
        if (item is null && Settings.ScanRandomWeightBarcodes && RandomWeightBarcode.TryParse(code, out var itemCode, out var price))
        {
            (item, variant) = await AppServices.Inventory.FindByCodeAsync(Session.LocationId, itemCode);
            embeddedPrice = price;
        }
        if (item is null && IsCouponCode(code))
        {
            await EnterCouponAsync(code);
            return;
        }
        if (item is null)
        {
            Alert($"No item has the code {code}.");
            System.Media.SystemSounds.Beep.Play();
            return;
        }
        if (embeddedPrice is decimal ep) qty = RandomWeightBarcode.Quantity(ep, item.Price1);
        await AddItemAsync(item, variant, qty);
    }

    public async Task AddItemAsync(Item item, ItemVariant? variant, decimal qty, string? epc = null, string? tagSerial = null)
    {
        if (item.ItemType == ItemType.Matrix && variant is null && item.Variants.Count > 0)
        {
            var options = item.Variants.OrderBy(v => v.Ordinal).Select(v => $"{v.Value1} {v.Value2} {v.Value3}".Trim() + $"   (on hand {v.OnHand.ToString("0.###", C)})").ToList();
            int pick = Ui.Choose($"Which {item.Name}?", options, "Matrix item");
            if (pick < 0) return;
            variant = item.Variants.OrderBy(v => v.Ordinal).ElementAt(pick);
        }
        // A tagged item's pieces in stock are known exactly, so a scan cannot ring up more of them than there are.
        if (epc is null && !ReturnMode && await TaggedPiecesAsync(item) is { } pieces && qty > pieces.Left)
        {
            Alert(NoMorePieces(item, pieces) + " The scan was not added.");
            System.Media.SystemSounds.Beep.Play();
            return;
        }
        string? serial = null;
        if (item.ItemType == ItemType.Serialized && epc is not null)
        {
            // A tagged piece: its serial number comes from the tag registration, if any.
            serial = tagSerial;
            qty = 1;
        }
        else if (item.ItemType == ItemType.Serialized)
        {
            var inStock = item.Serials.Where(s => s.Status is null || s.Status.StartsWith("IN", StringComparison.OrdinalIgnoreCase)).Select(s => s.SerialNumber).ToList();
            if (inStock.Count > 0 && !ReturnMode)
            {
                int pick = Ui.Choose($"Serial number of {item.Name}:", inStock, "Serial number");
                if (pick < 0) return;
                serial = inStock[pick];
            }
            else serial = Ui.Prompt($"Serial number of {item.Name}:", "Serial number");
            qty = 1;
        }
        if (item.ReadScale)
        {
            decimal? weight = null;
            if (!string.IsNullOrWhiteSpace(AppServices.Config.ScalePort))
            {
                try { weight = new SerialScale(AppServices.Config.ScalePort!).ReadWeight(); } catch (Exception ex) { Alert("Scale: " + ex.Message); }
            }
            weight ??= Ui.PromptDecimal($"Weight of {item.Name}:", "Scale");
            if (weight is null) return;
            qty = weight.Value;
        }

        if (item.ItemType == ItemType.GiftCard)
        {
            if (ReturnMode) Alert("A gift card is not taken back for money at the till; a supervisor corrects its balance on the Gift cards screen.");
            else await AddGiftCardAsync(item);
            return;
        }

        var kind = ReturnMode ? SaleLineKind.Return : SaleLineKind.Sale;
        if (serial is null && SaleDraft.LineToJoin(Lines, item, variant?.Id, kind, qty) is { } same)
        {
            // The same item again: its line gets the quantity (and the piece's tag), so the bill shows it once.
            _itemCache[item.Id] = item;
            same.Quantity += kind == SaleLineKind.Return ? -Math.Abs(qty) : qty;
            if (epc is not null) same.Epcs.Add(epc);
            ReturnMode = false;
            Price(same);
            SelectedLine = same;
            Message = $"{item.Name} x {Math.Abs(same.Quantity).ToString("0.###", C)} — {same.LineTotal.ToString("N2", C)}"
                + (epc is null && same.Epcs.Count > 0 ? "   (if this piece was already read by RFID, lower the quantity with F5)" : "");
            RefreshLines();
            if (!string.IsNullOrWhiteSpace(item.TagAlongCode) && kind == SaleLineKind.Sale)
                await AddCodeAsync(item.TagAlongCode!, qty);
            return;
        }

        var line = new SaleDraftLine
        {
            Kind = kind,
            ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = ReturnMode ? -Math.Abs(qty) : qty,
            Taxable1 = item.Taxable1, Taxable2 = item.Taxable2, Department = item.Department?.Name,
            UnitCost = item.AverageCost > 0 ? item.AverageCost : item.LastCost, VariantId = variant?.Id,
            VariantText = variant is null ? null : $"{variant.Value1} {variant.Value2} {variant.Value3}".Trim(), SerialNumber = serial,
            SupplierName = item.Supplier?.Company, CommissionPercent = item.Commission, ThcWeight = item.ThcWeight, Epcs = epc is null ? [] : [epc],
        };
        _itemCache[item.Id] = item;
        Price(line);
        if (line.UnitPrice == 0 && item.ItemType is ItemType.GiftCard or ItemType.NonStock or ItemType.Service or ItemType.Standard)
        {
            var p = Ui.PromptDecimal($"Price of {item.Name}:", "Price");
            if (p is null) return;
            if (item.ItemType == ItemType.GiftCard && Settings.GiftCardMaximum > 0 && p > Settings.GiftCardMaximum)
            {
                Ui.Error($"A gift card can be at most {Settings.GiftCardMaximum.ToString("N2", C)}.");
                return;
            }
            line.UnitPrice = p.Value;
            line.AutoPriced = false;
        }
        if (ReturnMode)
        {
            line.Restock = Ui.Confirm($"Put {item.Name} back into stock?", "Return");
            ReturnMode = false;
        }
        Lines.Add(line);
        SelectedLine = line;
        Message = !string.IsNullOrWhiteSpace(item.PosMessage) ? item.PosMessage! : $"{item.Name} — {line.LineTotal.ToString("N2", C)}" + (line.PriceExplanation is { } why && why != "regular price" ? $" ({why})" : "")
            + (item.OnHand - qty < 0 && SalesService.IsStocked(item.ItemType) ? "   ⚠ stock goes below zero" : "");
        Recalculate();
        if (!string.IsNullOrWhiteSpace(item.TagAlongCode) && line.Kind == SaleLineKind.Sale)
            await AddCodeAsync(item.TagAlongCode!, qty);
    }

    /// <summary>
    /// Asks for the number and amount of a gift card being sold. The automated check sets its own; otherwise the gift card
    /// window asks the cashier.
    /// </summary>
    public Func<Item, Task<GiftCardEntry?>>? GiftCardPrompt { get; set; }

    /// <summary>A gift card item: the card's number and the amount put on it (issued, or reloaded when the card is known).</summary>
    private async Task AddGiftCardAsync(Item item)
    {
        if (!AppServices.Can(Permissions.GiftCardsIssue))
        {
            Alert("Your role cannot sell gift cards.");
            return;
        }
        GiftCardEntry? entry;
        if (GiftCardPrompt is { } prompt) entry = await prompt(item);
        else
        {
            var window = new GiftCardSaleWindow(item.Name, item.Price1 > 0 ? item.Price1 : null);
            entry = window.ShowDialog() == true ? window.Result : null;
        }
        if (entry is null) return;
        if (Lines.Any(l => l.GiftCardNumber == entry.Number))
        {
            Alert($"Card {entry.Number} is already on this sale.");
            return;
        }
        var line = new SaleDraftLine
        {
            Kind = SaleLineKind.Sale, ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = $"{item.Name} {entry.Number}",
            Quantity = 1, UnitPrice = entry.Amount, AutoPriced = false, Taxable1 = item.Taxable1, Taxable2 = item.Taxable2, Department = item.Department?.Name,
            GiftCardNumber = entry.Number, GiftCardKind = entry.Kind, CommissionPercent = item.Commission,
        };
        _itemCache[item.Id] = item;
        Lines.Add(line);
        SelectedLine = line;
        Message = $"{(entry.Kind == GiftCardKind.Certificate ? "Gift certificate" : "Gift card")} {entry.Number}: {entry.Amount.ToString("N2", C)}";
        Recalculate();
    }

    private readonly Dictionary<Guid, Item> _itemCache = [];
    private readonly Dictionary<Guid, System.Windows.Media.ImageSource?> _pictures = [];

    partial void OnSelectedLineChanged(SaleDraftLine? value) => _ = ShowLinePictureAsync(value);

    private async Task ShowLinePictureAsync(SaleDraftLine? line)
    {
        if (line?.ItemId is not Guid id) { LinePicture = null; return; }
        if (!_pictures.TryGetValue(id, out var picture))
        {
            try
            {
                picture = Pictures.ToImage(await AppServices.Inventory.GetPictureAsync(id)) ?? Pictures.FromPhotoPath(_itemCache.GetValueOrDefault(id)?.PhotoPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Write("Item picture: " + ex.Message);
                picture = null;
            }
            _pictures[id] = picture;
        }
        if (ReferenceEquals(SelectedLine, line)) LinePicture = picture;
    }

    /// <summary>How long a red alert stays before the message bar goes back to what it said.</summary>
    private static readonly TimeSpan AlertTime = TimeSpan.FromSeconds(3);
    private System.Windows.Threading.DispatcherTimer? _alertTimer;
    private bool _alerting;
    private string _infoMessage = "Ready for the next customer. Scan an item, or type its code and press Enter.";

    // Any new message is information again (and ends a showing alert) until Alert says otherwise.
    partial void OnMessageChanged(string value)
    {
        if (_alerting) return;
        _alertTimer?.Stop();
        MessageIsAlert = false;
        _infoMessage = value;
    }

    /// <summary>
    /// Shows a problem in red for three seconds (a refused scan, an unknown code or tag, a failed printout), then the
    /// message bar shows its last information again.
    /// </summary>
    public void Alert(string text)
    {
        _alerting = true;
        try
        {
            Message = text;
        }
        finally
        {
            _alerting = false;
        }
        MessageIsAlert = true;
        if (_alertTimer is null)
        {
            _alertTimer = new System.Windows.Threading.DispatcherTimer { Interval = AlertTime };
            _alertTimer.Tick += (_, _) =>
            {
                _alertTimer.Stop();
                if (!MessageIsAlert) return;
                Message = _infoMessage;
                MessageIsAlert = false;
            };
        }
        _alertTimer.Stop();
        _alertTimer.Start();
    }

    /// <summary>
    /// For an item whose pieces carry RFID tags: the pieces in stock (tags in stock, or the stock count if higher) and
    /// how many of them are not on this sale yet. Null for items without tags, which may go below zero with a warning.
    /// </summary>
    private async Task<(decimal InStock, decimal Left)?> TaggedPiecesAsync(Item item)
    {
        if (!SalesService.IsStocked(item.ItemType)) return null;
        int tagged = await AppServices.RfidData.CountTagsAsync(item.Id, ItemTagStatus.InStock);
        if (tagged == 0) return null;
        decimal inStock = Math.Max(tagged, item.OnHand);
        decimal onSale = Lines.Where(l => l.ItemId == item.Id && l.Kind == SaleLineKind.Sale).Sum(l => l.Quantity);
        return (inStock, inStock - onSale);
    }

    private static string NoMorePieces(Item item, (decimal InStock, decimal Left) pieces) => pieces.Left <= 0
        ? $"{item.Name}: all {pieces.InStock.ToString("0.###", C)} in stock are already on this sale (read by RFID or scanned)."
        : $"{item.Name}: only {pieces.Left.ToString("0.###", C)} more in stock; the other {(pieces.InStock - pieces.Left).ToString("0.###", C)} are already on this sale.";

    private void Price(SaleDraftLine line)
    {
        if (!line.AutoPriced || line.ItemId is not Guid id || !_itemCache.TryGetValue(id, out var item)) return;
        var r = PricingEngine.Price(item, line.Quantity, DateOnly.FromDateTime(DateTime.Today), _draft.ClientPriceLevel, _draft.ClientDiscountPercent);
        line.UnitPrice = r.UnitPrice;
        line.DiscountAmount = r.DiscountAmount;
        line.PriceExplanation = r.Explanation;
    }

    private void Reprice()
    {
        foreach (var l in Lines) Price(l);
        RefreshLines();
    }

    private void RefreshLines()
    {
        var selected = SelectedLine;
        var copy = Lines.ToList();
        Lines.Clear();
        foreach (var l in copy) Lines.Add(l);
        SelectedLine = selected;
        Recalculate();
    }

    public void AddOpenItem(string description, decimal price, bool taxable1, bool taxable2, SaleLineKind kind = SaleLineKind.Sale)
    {
        var line = new SaleDraftLine
        {
            Kind = kind, Description = description, Quantity = kind == SaleLineKind.Sale ? 1 : -1, UnitPrice = price, Taxable1 = taxable1, Taxable2 = taxable2, AutoPriced = false,
            ItemType = ItemType.NonStock,
        };
        if (ReturnMode) { line.Kind = SaleLineKind.Return; line.Quantity = -1; ReturnMode = false; }
        Lines.Add(line);
        SelectedLine = line;
        Recalculate();
    }

    public async Task FastKeyAsync(FastKey key)
    {
        if (key.IsCategory)
        {
            var subs = await AppServices.Setup.GetFastKeysAsync(key.KeyNumber);
            if (subs.Count == 0) { Message = $"No keys under {key.Name}."; return; }
            int pick = Ui.Choose(key.Name ?? "Choose", subs.Select(s => s.Name ?? s.Plu ?? "").ToList(), "Fast keys");
            if (pick < 0) return;
            key = subs[pick];
        }
        if (!string.IsNullOrWhiteSpace(key.Plu)) { await AddCodeAsync(key.Plu!); return; }
        decimal price = key.FixedPrice;
        if (price == 0)
        {
            var p = Ui.PromptDecimal($"Amount for {key.Name}:", "Fast key");
            if (p is null) return;
            price = p.Value;
        }
        AddOpenItem(key.Name ?? "Item", price, key.Taxable1, key.Taxable2, key.IsCredit ? SaleLineKind.Credit : SaleLineKind.Sale);
    }

    // Line changes --------------------------------------------------------------------------------------------

    /// <summary>
    /// Discounts and price changes: open to everyone when the store lets staff discount, else for roles that may
    /// discount, or with a supervisor's approval.
    /// </summary>
    private static bool MayDiscount(string action) => Settings.StaffMayDiscount || Approval.Allowed(Permissions.PosDiscount, action);

    /// <summary>Return mode needs the returns permission, or a supervisor's approval.</summary>
    partial void OnReturnModeChanged(bool value)
    {
        if (value && !Approval.Allowed(Permissions.PosReturn, "A return")) ReturnMode = false;
    }

    public async Task ChangeQuantityAsync()
    {
        if (SelectedLine is not { } line) return;
        var q = Ui.PromptDecimal($"Quantity of {line.Description}:", "Quantity", line.Quantity);
        if (q is null || q == 0) return;
        if (line.SerialNumber is not null && Math.Abs(q.Value) != 1) { Ui.Error("A serialized item is sold one at a time."); return; }
        if (line.Kind == SaleLineKind.Sale && q > line.Quantity && line.ItemId is Guid itemId && _itemCache.TryGetValue(itemId, out var item)
            && await TaggedPiecesAsync(item) is { } pieces && q - line.Quantity > pieces.Left)
        {
            Ui.Error(NoMorePieces(item, pieces) + $" This line can have at most {(line.Quantity + pieces.Left).ToString("0.###", C)}.");
            return;
        }
        line.Quantity = line.Kind == SaleLineKind.Return ? -Math.Abs(q.Value) : q.Value;
        // Fewer pieces than tags: the tags taken off are not rung up again while they lie on the antenna.
        while (line.Epcs.Count > Math.Abs(line.Quantity))
        {
            _rfidIgnored.Add(line.Epcs[^1]);
            line.Epcs.RemoveAt(line.Epcs.Count - 1);
        }
        Price(line);
        RefreshLines();
    }

    public void ChangePrice()
    {
        if (SelectedLine is not { } line || !MayDiscount("Changing a price")) return;
        var p = Ui.PromptDecimal($"Price of {line.Description}:", "Price", line.UnitPrice);
        if (p is null) return;
        line.UnitPrice = p.Value;
        line.AutoPriced = false;
        line.DiscountAmount = 0;
        RefreshLines();
    }

    public void LineDiscount()
    {
        if (SelectedLine is not { } line || !MayDiscount("A discount on a line")) return;
        var p = Ui.PromptDecimal($"Discount on {line.Description} in percent (e.g. 10):", "Line discount");
        if (p is null) return;
        line.AutoPriced = false;
        line.DiscountAmount = Money.Round(line.UnitPrice * line.Quantity * p.Value / 100m);
        RefreshLines();
    }

    public void SubtotalDiscount()
    {
        if (Lines.Count == 0 || !MayDiscount("A discount on the sale")) return;
        var p = Ui.PromptDecimal("Discount on the whole sale in percent:", "Subtotal discount", _draft.SubtotalDiscountPercent);
        if (p is null) return;
        if (p < 0 || p > 100) { Ui.Error("Enter 0 to 100."); return; }
        _draft.SubtotalDiscountPercent = p.Value;
        Recalculate();
    }

    public Task RemoveLineAsync()
    {
        if (SelectedLine is not { } line) return Task.CompletedTask;
        if (line.IsPromotionLine)
        {
            // A sale-wide promotion's line comes back while the sale qualifies, unless its coupon is taken off.
            var code = _promotions.FirstOrDefault(p => p.Id == line.PromotionId)?.CouponCode;
            if (code is null) Alert($"{line.Description}: the sale qualifies for this promotion, so its line stays.");
            else RemoveCoupon(code);
            return Task.CompletedTask;
        }
        if (Settings.SupervisorForVoids && !Approval.Allowed(Permissions.PosVoidLine, "Removing a line")) return Task.CompletedTask;
        Lines.Remove(line);
        _rfidIgnored.UnionWith(line.Epcs);
        SelectedLine = Lines.LastOrDefault();
        Recalculate();
        return Task.CompletedTask;
    }

    public Task ClearSaleAsync(bool ask = true)
    {
        if (ask && Lines.Count > 0)
        {
            if (!Ui.Confirm("Cancel this whole sale?")) return Task.CompletedTask;
            if (Settings.SupervisorForVoids && !Approval.Allowed(Permissions.PosVoidLine, "Cancelling a sale")) return Task.CompletedTask;
        }
        Lines.Clear();
        _draft = new SaleDraft();
        _itemCache.Clear();
        _pictures.Clear();
        _tagCache.Clear();
        _rfidIgnored.Clear();
        _clientPoints = null;
        ClientText = "Walk-in customer";
        ClientDetail = "";
        HasClient = false;
        ReturnMode = false;
        Recalculate();
        return Task.CompletedTask;
    }

    // Client -------------------------------------------------------------------------------------------------

    private decimal? _clientPoints;

    public async Task SetClientAsync(Guid clientId)
    {
        var c = await AppServices.Parties.GetClientAsync(clientId);
        if (c is null) return;
        _draft.ClientId = c.Id;
        _draft.ClientName = !string.IsNullOrWhiteSpace(c.Company) ? c.Company : $"{c.FirstName} {c.LastName}".Trim();
        _draft.CustomerNumber = c.Number;
        _draft.ClientPriceLevel = c.PriceLevel;
        _draft.ClientDiscountPercent = c.UsualDiscountPercent;
        _draft.ExemptTax1 = c.ExemptTax1;
        _draft.ExemptTax2 = c.ExemptTax2;
        _clientPoints = c.Points;
        HasClient = true;
        ClientText = $"{_draft.ClientName}  #{c.Number}";
        var parts = new List<string>();
        if (c.Balance != 0) parts.Add($"Balance {c.Balance.ToString("N2", C)}" + (c.CreditLimit > 0 ? $" of {c.CreditLimit.ToString("N2", C)}" : ""));
        if (Settings.BonusPointsEnabled) parts.Add($"Points {c.Points.ToString("0.##", C)}");
        if (c.PriceLevel >= 2) parts.Add($"Price level {c.PriceLevel}");
        if (c.UsualDiscountPercent > 0) parts.Add($"Discount {c.UsualDiscountPercent.ToString("0.##", C)}%");
        if (c.ExemptTax1 || c.ExemptTax2) parts.Add("Tax exempt");
        if (c.WhatsAppAskedAt is not null || c.WhatsAppOptIn) parts.Add(c.WhatsAppOptIn ? "WhatsApp offers: yes" : "WhatsApp offers: no");
        ClientDetail = string.Join(" · ", parts);
        Reprice();
        if (c.WhatsAppAskedAt is null && !c.WhatsAppOptIn && !Session.IsTrainee) await AskWhatsAppAsync(c);
    }

    /// <summary>
    /// Asks a client who never answered whether they agree to WhatsApp messages. The automated check gives its own
    /// answer; otherwise the WhatsApp window asks the cashier to put the question.
    /// </summary>
    public Func<Client, Task<WhatsAppAnswer?>>? WhatsAppPrompt { get; set; }

    /// <summary>
    /// The WhatsApp question for the client just chosen. Yes (with the number) or No is recorded on the client, with the
    /// time, and goes to the server with the next sync; campaigns go only to clients who said yes. Ask later records nothing.
    /// </summary>
    private async Task AskWhatsAppAsync(Client c)
    {
        WhatsAppAnswer? answer;
        if (WhatsAppPrompt is { } prompt) answer = await prompt(c);
        else if (AppServices.Unattended) return;
        else
        {
            var window = new WhatsAppConsentWindow(_draft.ClientName ?? "The customer", c.WhatsAppNumber);
            answer = window.ShowDialog() == true ? window.Answer : null;
        }
        if (answer is null) return;
        Client? saved = null;
        if (!await Ui.Try(async () => saved = await AppServices.Parties.RecordWhatsAppAnswerAsync(c.Id, answer.Agrees, answer.Number)) || saved is null) return;
        if (_draft.ClientId != c.Id) return;
        ClientDetail = string.Join(" · ", ClientDetail.Split(" · ", StringSplitOptions.RemoveEmptyEntries).Where(p => !p.StartsWith("WhatsApp", StringComparison.Ordinal))
            .Append(saved.WhatsAppOptIn ? "WhatsApp offers: yes" : "WhatsApp offers: no"));
        Message = saved.WhatsAppOptIn
            ? $"{_draft.ClientName} agreed to WhatsApp messages on {saved.WhatsAppNumber}. Thank you."
            : $"{_draft.ClientName} does not want WhatsApp messages. That is noted, and they will not be asked again.";
    }

    public void ClearClient()
    {
        _draft.ClientId = null; _draft.ClientName = null; _draft.CustomerNumber = null; _draft.ClientPriceLevel = 0; _draft.ClientDiscountPercent = 0;
        _draft.ExemptTax1 = false; _draft.ExemptTax2 = false; _draft.PointsRedeemed = 0; _clientPoints = null;
        HasClient = false;
        ClientText = "Walk-in customer";
        ClientDetail = "";
        Reprice();
    }

    public void TakeReward()
    {
        if (!CanReward) return;
        var subtotal = Lines.Where(l => l.Kind == SaleLineKind.Sale).Sum(l => l.LineTotal);
        var reward = BonusPoints.RewardAmount(Settings, subtotal);
        if (reward <= 0) return;
        _draft.PointsRedeemed = Settings.PointsRequired;
        Lines.Add(new SaleDraftLine { Kind = SaleLineKind.Coupon, Description = "Bonus points reward", Quantity = 1, UnitPrice = -reward, Taxable1 = true, Taxable2 = true, AutoPriced = false, ItemType = ItemType.NonStock });
        Recalculate();
    }

    // Finishing --------------------------------------------------------------------------------------------------

    public async Task<bool> PayAsync(bool layaway = false)
    {
        if (Lines.Count == 0) return false;
        if (layaway && _draft.ClientId is null) { Ui.Error("Select the client for the layaway first."); return false; }
        Recalculate();
        _draft.IsLayaway = layaway;
        _draft.Staff = Session.UserName;
        _draft.StaffId = Session.UserId;
        var tenders = await AppServices.Setup.GetTenderTypesAsync();
        var rates = await AppServices.Setup.GetFxRatesAsync();
        while (true)
        {
            var pay = new PayWindow(Total, tenders, Settings.MinimumTender, _draft.ClientId is not null, layaway, rates);
            if (pay.ShowDialog() == true) return await PostAsync(pay.Tenders);
            // A coupon entered at the Pay window: the promotions are worked out again and the window opens with the new total.
            if (pay.CouponCode is { } code)
            {
                await EnterCouponAsync(code);
                continue;
            }
            _draft.IsLayaway = false;
            return false;
        }
    }

    /// <summary>Saves the sale with the given payments, prints the receipt and opens the drawer.</summary>
    public async Task<bool> PostAsync(IReadOnlyList<TenderEntry> tenders)
    {
        Sale? sale = null;
        bool ok = await Ui.Try(async () => sale = await AppServices.Sales.PostSaleAsync(_draft, tenders, Session));
        if (!ok || sale is null)
        {
            // A coupon used up at another till meanwhile: its discount goes, and the total shows what is due now.
            await LoadPromotionsAsync();
            Recalculate();
            return false;
        }
        _lastSale = sale;
        foreach (var t in sale.Lines.SelectMany(l => l.Tags)) _recentlySold[t.Epc] = DateTime.Now;
        var change = tenders.Sum(t => t.Change);
        var kinds = await AppServices.Setup.GetTenderTypesAsync(false);
        bool open = tenders.Any(t => kinds.FirstOrDefault(k => k.Name == t.TenderType)?.OpensDrawer ?? t.Kind == TenderKind.Cash);
        await PrintReceiptAsync(sale, open);
        await ClearSaleAsync(ask: false);
        await LoadPromotionsAsync();
        Message = (Session.IsTrainee ? "Training sale (not saved). " : $"Sale {sale.InvoiceNumber} saved. ") + (change > 0 ? $"CHANGE {change.ToString("N2", C)}" : "");
        AppServices.Pole($"TOTAL {sale.Total.ToString("N2", C)}", change > 0 ? $"CHANGE {change.ToString("N2", C)}" : "THANK YOU");
        return true;
    }

    private async Task PrintReceiptAsync(Sale sale, bool openDrawer)
    {
        try
        {
            var receipt = await ReceiptWindow.ForSaleAsync(sale, Session.IsTrainee ? "TRAINING" : null);
            int width = AppServices.Config.ReceiptWidth;
            bool print = AppServices.Config.PrintReceipts && (!AppServices.Config.AskBeforePrinting || Ui.Confirm("Print a receipt?"));
            if (print)
                AppServices.Printer.Print(EscPosReceipt.Build(receipt, width, ReceiptLogo.Raster()), ReceiptFormatter.Text(receipt, width), openDrawer && AppServices.Config.DrawerOnPrinter);
            else if (openDrawer) OpenDrawer();
        }
        catch (Exception ex)
        {
            Alert("The receipt could not be printed: " + ex.Message);
            Log.Write(ex.ToString());
        }
    }

    public static void OpenDrawer()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(AppServices.Config.DrawerSerialPort)) new SerialCashDrawer(AppServices.Config.DrawerSerialPort!).Open();
            else if (AppServices.Config.DrawerOnPrinter) AppServices.Printer.OpenDrawer();
        }
        catch (Exception ex)
        {
            Log.Write("Drawer: " + ex.Message);
        }
    }

    public async Task ReprintLastAsync()
    {
        if (_lastSale is null) { Message = "No sale yet on this screen. Use the Sales Journal to reprint older sales."; return; }
        new ReceiptWindow(await ReceiptWindow.ForSaleAsync(_lastSale, "COPY"), $"Invoice {_lastSale.InvoiceNumber}").ShowDialog();
    }

    public async Task SuspendAsync()
    {
        if (Lines.Count == 0) return;
        var label = Ui.Prompt("A name for this sale (to find it again):", "Suspend", _draft.ClientName ?? "");
        if (label is null) return;
        _draft.Lines = Lines.ToList();
        await AppServices.Sales.SuspendAsync(_draft, label, Session.StationNumber);
        await ClearSaleAsync(ask: false);
        Message = $"Sale \"{label}\" suspended.";
    }

    public async Task RecallAsync()
    {
        var list = await AppServices.Sales.ListSuspendedAsync();
        if (list.Count == 0) { Message = "There are no suspended sales."; return; }
        int pick = Ui.Choose("Recall which sale?", list.Select(s => $"{s.Label}   ({s.SuspendedAt:g}, station {s.StationNumber})").ToList(), "Recall");
        if (pick < 0) return;
        if (Lines.Count > 0) await SuspendAsync();
        var draft = await AppServices.Sales.RecallAsync(list[pick].Id);
        if (draft is null) { Message = "That sale was already recalled at another station."; return; }
        await LoadDraftAsync(draft);
    }

    public async Task LoadDraftAsync(SaleDraft draft)
    {
        await ClearSaleAsync(ask: false);
        var lines = draft.Lines;
        _draft = draft;
        foreach (var l in lines)
        {
            if (l.ItemId is Guid id && await AppServices.Inventory.GetAsync(id) is { } item) _itemCache[id] = item;
            Lines.Add(l);
        }
        if (draft.ClientId is Guid cid)
        {
            var keep = (draft.SubtotalDiscountPercent, draft.PointsRedeemed);
            await SetClientAsync(cid);
            (_draft.SubtotalDiscountPercent, _draft.PointsRedeemed) = keep;
        }
        Recalculate();
    }

    public async Task SaveQuoteAsync()
    {
        if (Lines.Count == 0) return;
        if (_draft.ClientId is null) { Ui.Error("Select the client for the quote first."); return; }
        _draft.Lines = Lines.ToList();
        Invoice? quote = null;
        if (await Ui.Try(async () => quote = await AppServices.Invoices.SaveQuoteAsync(_draft, Session)) && quote is not null)
        {
            await ClearSaleAsync(ask: false);
            Message = $"Quote {quote.InvoiceNumber} saved. Load it again from Invoices & A/R.";
        }
    }

    // Promotions and coupons -------------------------------------------------------------------------------------

    private List<Promotion> _promotions = [];
    private IReadOnlyDictionary<Guid, int> _couponUses = new Dictionary<Guid, int>();
    private PromotionOutcome _lastOutcome = PromotionOutcome.None;

    /// <summary>
    /// Reads the promotions that may run at this store (they come down from the server with the sync) and how often each
    /// coupon was used at every till this one has heard from. Done when the screen opens, after each sale and when a
    /// coupon is entered.
    /// </summary>
    public async Task LoadPromotionsAsync()
    {
        if (AppServices.Data.Station?.StoreId is not Guid store) return;
        try
        {
            _promotions = await AppServices.Promotions.ForStoreAsync(store, DateOnly.FromDateTime(DateTime.Now));
            _couponUses = await AppServices.Promotions.CouponUsesAsync([.. _promotions.Where(p => p.CouponCode != null).Select(p => p.Id)]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The sale goes on at full price rather than stopping the till.
            Log.Write("Promotions: " + ex.Message);
        }
    }

    /// <summary>
    /// Works the promotions out again for the lines on the sale with <see cref="PromotionEngine.Apply"/>, the same rules as
    /// the server's: each line's promotion and discount, and the line of a sale-wide one. The grid is drawn again when
    /// what the promotions give has changed.
    /// </summary>
    private void ApplyPromotions()
    {
        if (AppServices.Data?.Station?.StoreId is not Guid store) return;
        var oldLines = Lines.ToList();
        var before = oldLines.Select(l => (l.PromotionId, l.PromotionDiscount)).ToList();
        _draft.Lines = oldLines.ToList();
        var outcome = PromotionEngine.Apply(_draft, _promotions, new PromotionContext(store, DateTime.Now, _draft.CouponCodes, _couponUses));
        if (!oldLines.SequenceEqual(_draft.Lines) || !before.SequenceEqual(_draft.Lines.Select(l => (l.PromotionId, l.PromotionDiscount))))
        {
            var selected = SelectedLine;
            Lines.Clear();
            foreach (var l in _draft.Lines) Lines.Add(l);
            SelectedLine = selected is not null && Lines.Contains(selected) ? selected : Lines.LastOrDefault(l => !l.IsPromotionLine);
        }
        _lastOutcome = outcome;
        var coupons = _draft.CouponCodes.Count == 0 ? "" : "Coupon " + string.Join(", ", _draft.CouponCodes);
        int count = outcome.Applied.Count;
        PromotionText = outcome.Total <= 0 ? coupons
            : (count == 1 ? "Promotion saves " : $"{count} promotions save ") + outcome.Total.ToString("N2", C) + (coupons.Length > 0 ? "   ·   " + coupons : "");
    }

    /// <summary>True when a promotion that may run here has this coupon code.</summary>
    private bool IsCouponCode(string code) =>
        _promotions.Any(p => p.CouponCode is { } c && c == PromotionEngine.NormalizeCode(code));

    /// <summary>The Coupon button (Ctrl+J): asks for the code.</summary>
    public async Task CouponAsync()
    {
        var code = Ui.Prompt("Scan or type the coupon code:", "Coupon");
        if (string.IsNullOrWhiteSpace(code)) return;
        await EnterCouponAsync(code);
    }

    /// <summary>
    /// A coupon code scanned or typed (in the code box, with the Coupon button or at the Pay window). It stays on the sale,
    /// and its promotion applies as soon as the sale has what it is for. An unknown, used-up or out-of-date code is
    /// refused in red; a code already on the sale is offered to be taken off.
    /// </summary>
    public async Task<bool> EnterCouponAsync(string code)
    {
        code = PromotionEngine.NormalizeCode(code);
        if (code.Length == 0) return false;
        if (_draft.CouponCodes.Contains(code))
        {
            if (Ui.Confirm($"Coupon {code} is already on this sale. Take it off?", "Coupon")) RemoveCoupon(code);
            return false;
        }
        await LoadPromotionsAsync();
        _draft.CouponCodes.Add(code);
        Recalculate();
        var refused = _lastOutcome.Notes.FirstOrDefault(n => n.StartsWith($"Coupon {code} ", StringComparison.Ordinal) && !n.EndsWith("does not apply to this sale.", StringComparison.Ordinal));
        if (refused is not null)
        {
            _draft.CouponCodes.Remove(code);
            Recalculate();
            Alert(refused);
            System.Media.SystemSounds.Beep.Play();
            return false;
        }
        var promotion = _promotions.FirstOrDefault(p => p.CouponCode == code && _lastOutcome.Applied.Contains(p.Id));
        Message = promotion is not null
            ? $"Coupon {code}: {promotion.Name}. {PromotionText}"
            : $"Coupon {code} is on the sale. It applies once the sale has what it is for.";
        return true;
    }

    private void RemoveCoupon(string code)
    {
        _draft.CouponCodes.Remove(code);
        Recalculate();
        Message = $"Coupon {code} taken off the sale.";
    }

    // RFID ---------------------------------------------------------------------------------------------------------

    [ObservableProperty] private string _rfidText = "";
    [ObservableProperty] private bool _rfidVisible;
    [ObservableProperty] private System.Windows.Media.Brush _rfidBrush = System.Windows.Media.Brushes.Gray;

    private CancellationTokenSource? _rfidCts;
    private bool _rfidPaused;
    private readonly Dictionary<string, TagLookup?> _tagCache = [];
    private readonly HashSet<string> _rfidIgnored = [];
    private readonly HashSet<string> _rfidNotices = [];
    private readonly Dictionary<string, DateTime> _recentlySold = [];

    /// <summary>Starts listening to this station's antennas (while the POS screen is shown).</summary>
    public void StartRfid()
    {
        StopRfid();
        if (AppServices.Rfid is not { } rfid)
        {
            RfidVisible = false;
            return;
        }
        RfidVisible = true;
        if (_rfidPaused || !AppServices.Config.RfidAutoAdd)
        {
            RfidText = _rfidPaused ? "RFID paused" : "RFID auto-add off";
            RfidBrush = Look.Brush("Stone");
            return;
        }
        _rfidCts = new CancellationTokenSource();
        _ = ListenAsync(rfid, _rfidCts.Token);
    }

    public void StopRfid()
    {
        _rfidCts?.Cancel();
        _rfidCts?.Dispose();
        _rfidCts = null;
    }

    /// <summary>The RFID pill: pause or resume reading at this counter.</summary>
    public void ToggleRfid()
    {
        _rfidPaused = !_rfidPaused;
        StartRfid();
    }

    private async Task ListenAsync(IRfidControl rfid, CancellationToken cancellationToken)
    {
        try
        {
            var antennas = await AppServices.RfidData.GetAntennasAsync(Session.StationNumber);
            int n = antennas.Count(a => a.IsEnabled && a.Purpose == RfidAntennaPurpose.PointOfSale);
            RfidText = n == 0 ? "RFID: no antenna for this counter" : $"RFID on · {n} antenna{(n == 1 ? "" : "s")}";
            RfidBrush = n == 0 ? Look.Brush("Sun") : Look.Brush("Ok");
            await foreach (var tag in rfid.ListenAsync(Session.StationNumber, cancellationToken))
                await OnTagAsync(tag);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            RfidText = "RFID offline";
            RfidBrush = Look.Brush("Danger");
            Log.Write("RFID at POS: " + ex.Message);
        }
    }

    /// <summary>A tag read at this counter: add its item once, unless it was just sold or taken off this sale.</summary>
    public async Task OnTagAsync(RfidTagEvent tag)
    {
        if (Lines.Any(l => l.Epcs.Contains(tag.Epc)) || _rfidIgnored.Contains(tag.Epc)) return;
        if (!ReturnMode && _recentlySold.TryGetValue(tag.Epc, out var soldAt) && DateTime.Now - soldAt < TimeSpan.FromMinutes(10)) return;
        if (!_tagCache.TryGetValue(tag.Epc, out var found))
        {
            found = await AppServices.RfidData.ResolveAsync(Session.LocationId, tag.Epc);
            _tagCache[tag.Epc] = found;
        }
        if (Lines.Any(l => l.Epcs.Contains(tag.Epc))) return;
        if (found is null)
        {
            if (_rfidNotices.Add(tag.Epc)) Alert($"Unknown RFID tag {tag.Epc}. Give it to an item in Inventory (item > RFID tags).");
            return;
        }
        if (found.Tag is { Status: ItemTagStatus.Sold } sold && !ReturnMode)
        {
            if (_rfidNotices.Add(tag.Epc))
                Alert($"{found.Item.Name}: this tag was already sold on {sold.SoldAt:g} (invoice {sold.SoldOnInvoice}). Use Return / refund to take it back.");
            return;
        }
        // A piece already rung up by its barcode: the tag joins that piece instead of adding another one.
        if (!ReturnMode && found.Item.ItemType != ItemType.Serialized
            && Lines.FirstOrDefault(l => l.ItemId == found.Item.Id && l.VariantId == found.Variant?.Id && l.Kind == SaleLineKind.Sale
                                         && l.SerialNumber is null && l.Quantity > l.Epcs.Count) is { } scanned)
        {
            scanned.Epcs.Add(tag.Epc);
            Message = $"{found.Item.Name}: the RFID tag belongs to the piece already scanned by barcode.";
            RefreshLines();
            Sounds.TagRead();
            return;
        }
        await AddItemAsync(found.Item, found.Variant, 1, tag.Epc, found.Tag?.SerialNumber);
        if (Lines.Any(l => l.Epcs.Contains(tag.Epc))) Sounds.TagRead();
    }
}
