using System.Globalization;
using RetailPlus.Core.Model;

namespace RetailPlus.Core.Rules;

/// <summary>
/// A return already made of a sold line with the receipt (or one on the sale being rung up): how many came back and what
/// was refunded for them, both positive. Returns of a voided sale do not count.
/// </summary>
public sealed record EarlierReturn(Guid SoldLineId, decimal Quantity, decimal Refunded);

/// <summary>The department and category of an item, which a buy X get Y or a bundle may name.</summary>
public readonly record struct ItemGroups(Guid? DepartmentId, Guid? CategoryId);

/// <summary>
/// The refund for units of one sold line taken back with the receipt, and how it is made up. All amounts are positive and
/// before tax (taxes are worked out on the return as on any line).
/// </summary>
/// <param name="Line">The sold line.</param>
/// <param name="Quantity">How many come back.</param>
/// <param name="Left">How many of the line could still come back before this return.</param>
/// <param name="Refund">What the customer gets back for them.</param>
/// <param name="Regular">Their price before any promotion or sale-wide discount: price × quantity less their share of the
/// line's own discount (pricing rules, the cashier's).</param>
/// <param name="LineDiscount">Their share of the line's own discount.</param>
/// <param name="PromotionTakenBack">The line's promotion's part of what is not refunded: the share of a percent or amount
/// off these units had, or what a buy X get Y or a bundle no longer gives the goods kept. 0 on a line without one.</param>
/// <param name="SaleWideShare">Their share of the sale-wide discounts (the discount on the sale, a spend-over promotion's
/// line, a points reward), with the cents of rounding (so it may be -0.01).</param>
/// <param name="Explanation">For the cashier, in a few words.</param>
public sealed record ReturnQuote(SaleLine Line, decimal Quantity, decimal Left, decimal Refund, decimal Regular, decimal LineDiscount,
    decimal PromotionTakenBack, decimal SaleWideShare, string Explanation);

/// <summary>
/// What a customer gets back for goods returned with the receipt: what they paid for them, not the price rung up today.
/// <list type="bullet">
/// <item>The refund is the difference between what the customer paid for the goods of the receipt they still hold and
/// what the goods they keep would have cost them: <c>refund = paid − (regular price of what is kept − the promotion it
/// still earns)</c>, never below zero, never more than is left of what was paid, nor (the cent of rounding aside) more
/// than the regular price of what comes back.</item>
/// <item>No promotion, or a percent or amount off: the discount is the same on every unit, so each unit comes back at
/// what was paid for it (the line's total shared over its quantity).</item>
/// <item>Buy X get Y and bundles: the promotion is worked out again, with its terms, on the units of its lines the
/// customer keeps. What it no longer gives them is kept back from the refund: of 3 bought as buy 2 get 1, a unit returned
/// gives nothing back (the 2 kept cost what was paid for 3). The kept goods never earn more than the sale gave.</item>
/// <item>A sale-wide discount (the discount on the sale, a spend-over promotion's line, a points reward) is shared over the
/// goods sold by their value, and each refund is less its share.</item>
/// <item>Earlier returns of the receipt count: the refunds of a line (or of a promotion's lines) never add up to more
/// than was paid for it, and the last unit back brings the refunds to exactly what was paid.</item>
/// </list>
/// A return without a receipt cannot know any of this: it is refunded at the price rung up.
/// </summary>
public static class ReturnRefunds
{
    /// <summary>A line that can be taken back with the receipt: goods sold (not a gift card, coupon, return or text line).</summary>
    public static bool Returnable(SaleLine line) =>
        line.Kind == SaleLineKind.Sale && line.Quantity > 0 && line.UnitPrice >= 0 && line.LineTotal >= 0 && string.IsNullOrWhiteSpace(line.GiftCardNumber);

    /// <summary>How many of a sold line can still come back.</summary>
    public static decimal Left(SaleLine line, IEnumerable<EarlierReturn> earlier) =>
        Math.Max(0, line.Quantity - earlier.Where(e => e.SoldLineId == line.Id).Sum(e => e.Quantity));

    /// <summary>
    /// The part of their value the goods of a sale were paid after its sale-wide discounts: 1 with none, 0.9 when the
    /// discount on the sale, a spend-over promotion's line and a points reward took a tenth off the goods.
    /// </summary>
    public static decimal SaleWideFactor(Sale sale)
    {
        decimal goods = sale.Lines.Where(l => l.Kind == SaleLineKind.Sale && l.LineTotal > 0).Sum(l => l.LineTotal);
        decimal wide = sale.SubtotalDiscount + sale.Lines.Where(l => l.Kind == SaleLineKind.Coupon && l.LineTotal < 0).Sum(l => -l.LineTotal);
        if (goods <= 0 || wide <= 0) return 1;
        return Math.Clamp((goods - wide) / goods, 0, 1);
    }

    /// <summary>
    /// The refund for <paramref name="quantity"/> units of the sold line <paramref name="soldLineId"/> of
    /// <paramref name="sale"/>. <paramref name="earlier"/> are the returns of the sale already made (and those on the sale
    /// being rung up); <paramref name="promotions"/> the promotions the till knows, by id (a buy X get Y or bundle it does
    /// not know is shared over its units like a percent off); <paramref name="itemGroups"/> the department and category of
    /// the items, by item id, for promotions that name them.
    /// </summary>
    public static ReturnQuote Quote(Sale sale, Guid soldLineId, decimal quantity, IReadOnlyCollection<EarlierReturn> earlier,
        IReadOnlyDictionary<Guid, Promotion>? promotions = null, IReadOnlyDictionary<Guid, ItemGroups>? itemGroups = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        var sold = sale.Lines.FirstOrDefault(l => l.Id == soldLineId) ?? throw new ArgumentException("That line is not on the sale.", nameof(soldLineId));
        if (!Returnable(sold)) throw new ArgumentException($"{sold.Description} is not goods sold, so it is not returned with the receipt.", nameof(soldLineId));
        decimal left = Left(sold, earlier);
        if (quantity <= 0 || quantity > left)
            throw new ArgumentOutOfRangeException(nameof(quantity), $"{sold.Description}: {left.ToString("0.###", culture)} can still come back.");

        // A buy X get Y or a bundle is worked out over every line it took; any other line on its own.
        Promotion? multi = sold.PromotionId is Guid id && promotions?.GetValueOrDefault(id) is { Kind: PromotionKind.BuyXGetY or PromotionKind.BundlePrice } p ? p : null;
        List<SaleLine> group = multi is null ? [sold] : [.. sale.Lines.Where(l => l.PromotionId == multi.Id && Returnable(l))];
        var before = group.ToDictionary(l => l.Id, l => Left(l, earlier));
        var after = new Dictionary<Guid, decimal>(before) { [sold.Id] = before[sold.Id] - quantity };

        decimal factor = SaleWideFactor(sale);
        decimal paid = group.Sum(l => l.LineTotal);
        decimal paidAfterSaleWide = Money.Round(paid * factor);
        decimal refundedBefore = earlier.Where(e => group.Any(l => l.Id == e.SoldLineId)).Sum(e => e.Refunded);

        // What the customer will have been refunded for the lines once these units are back: what was paid less what the
        // goods kept would cost on their own (their regular price less what the promotion still gives them).
        decimal benefitBefore = Benefit(group, before, multi, itemGroups);
        decimal benefitAfter = Math.Min(benefitBefore, Benefit(group, after, multi, itemGroups));
        decimal keptCost = group.Sum(l => UnitRegular(l) * after[l.Id]) - benefitAfter;
        bool last = after.Values.All(k => k == 0);
        decimal refundedByThen = last ? paidAfterSaleWide : Money.Round(Math.Clamp(paid - keptCost, 0, paid) * factor);

        // Never more than is left of what was paid, nor (but for the cents of the last unit) than the units' regular price.
        decimal regular = Money.Round(UnitRegular(sold) * quantity);
        decimal most = paidAfterSaleWide - refundedBefore;
        if (!last) most = Math.Min(most, Math.Ceiling(UnitRegular(sold) * quantity * 100m) / 100m);
        decimal refund = Math.Clamp(refundedByThen - refundedBefore, 0, Math.Max(0, most));

        // What is not refunded of the regular price: the sale-wide discounts' share of the refund before them, and the rest
        // the promotion's (none on a line without one; the cents of rounding go with the sale-wide share).
        decimal saleWide = factor >= 1 ? 0 : factor <= 0 ? regular - refund : Money.Round(refund / factor - refund);
        saleWide = Math.Clamp(saleWide, 0, Math.Max(0, regular - refund));
        decimal takenBack = regular - refund - saleWide;
        if (takenBack < 0 || sold.PromotionId is null)
        {
            saleWide += takenBack;
            takenBack = 0;
        }
        decimal lineDiscount = Money.Round(sold.UnitPrice * quantity) - regular;

        string N(decimal d) => d.ToString("N2", culture);
        var why = new List<string>();
        if (sold.PromotionName is { } promotion)
        {
            if (multi is null) why.Add("after " + promotion);
            else if (takenBack > 0) why.Add($"less {N(takenBack)} of {promotion}");
            else why.Add($"{promotion} still given on what is kept");
        }
        if (saleWide >= 0.01m) why.Add($"less {N(saleWide)}, its share of the sale's discounts");
        string explanation = $"{N(refund)} back for {quantity.ToString("0.###", culture)} of invoice {sale.InvoiceNumber}"
                             + (why.Count > 0 ? " (" + string.Join("; ", why) + ")" : "");
        return new ReturnQuote(sold, quantity, left, refund, regular, lineDiscount, takenBack, saleWide, explanation);
    }

    /// <summary>A unit's price before the promotion (after the line's own discount): the line's total with the promotion's discount added back.</summary>
    private static decimal UnitRegular(SaleLine l) => (l.LineTotal + l.PromotionDiscount) / l.Quantity;

    /// <summary>
    /// What the line's promotion gives the units still held (<paramref name="held"/>, by line): the same share of it on
    /// every unit for a percent or amount off; for a buy X get Y or a bundle, what it gave on the sale while every unit is
    /// held, and otherwise what it gives the units held, worked out again (never more than it gave).
    /// </summary>
    private static decimal Benefit(List<SaleLine> group, Dictionary<Guid, decimal> held, Promotion? multi,
        IReadOnlyDictionary<Guid, ItemGroups>? itemGroups)
    {
        if (multi is null) return group.Sum(l => l.PromotionDiscount * held[l.Id] / l.Quantity);
        decimal given = group.Sum(l => l.PromotionDiscount);
        if (group.All(l => held[l.Id] == l.Quantity)) return given;
        var lines = group.Select((l, i) => (Line: l, Index: i)).Where(x => held[x.Line.Id] > 0).Select(x =>
        {
            var groups = x.Line.ItemId is Guid item && itemGroups is not null ? itemGroups.GetValueOrDefault(item) : default;
            decimal qty = held[x.Line.Id];
            return new PromotionLine(x.Index, x.Line.Plu, groups.DepartmentId, groups.CategoryId, qty, Money.Round(UnitRegular(x.Line) * qty));
        }).ToList();
        return Math.Min(given, PromotionEngine.DiscountOn(multi, lines));
    }

    /// <summary>
    /// The return line for a quote: the sold line's item, price, taxes and cost, a negative quantity, and its discounts
    /// such that it refunds <see cref="ReturnQuote.Refund"/>. The promotion's part (<see cref="ReturnQuote.PromotionTakenBack"/>)
    /// is the line's promotion discount, negative, so the books and the promotions report take that promotion back; the
    /// line's own discount and the share of the sale-wide ones are its other discount. The taxes are those the customer
    /// paid on the line (none when it was not taxed, say for a client exempt from a tax). When the whole line comes back
    /// at once, its RFID tags come back with it.
    /// </summary>
    public static SaleDraftLine DraftLine(Sale sale, ReturnQuote quote, ItemType itemType = ItemType.Standard, bool restock = true)
    {
        var l = quote.Line;
        bool whole = quote.Quantity == l.Quantity;
        return new SaleDraftLine
        {
            Kind = SaleLineKind.Return, ItemId = l.ItemId, ItemType = itemType, Plu = l.Plu, Description = l.Description, Quantity = -quote.Quantity,
            UnitPrice = l.UnitPrice, DiscountAmount = -(quote.LineDiscount + quote.SaleWideShare),
            PromotionId = l.PromotionId, PromotionName = l.PromotionName, PromotionDiscount = -quote.PromotionTakenBack,
            Taxable1 = l.Taxable1 && (l.Tax1 != 0 || l.LineTotal == 0), Taxable2 = l.Taxable2 && (l.Tax2 != 0 || l.LineTotal == 0),
            Department = l.Department, UnitCost = l.Cogs / l.Quantity, VariantId = l.VariantId, SerialNumber = l.SerialNumber, SupplierName = l.SupplierName,
            CommissionPercent = l.LineTotal == 0 ? 0 : Math.Round(l.Commission / l.LineTotal * 100m, 4), Restock = restock, AutoPriced = false,
            PriceExplanation = quote.Explanation, Epcs = whole ? [.. l.Tags.Select(t => t.Epc)] : [],
            ReturnedLineId = l.Id, ReturnedInvoice = sale.InvoiceNumber,
        };
    }
}
