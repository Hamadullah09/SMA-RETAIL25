using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Sales;

namespace Retail25.Application.Receipts;

/// <summary>
/// Builds a <see cref="ReceiptDocument"/> from a saved transaction.
/// <para>
/// It reads the sale's own snapshot rows — the frozen tax configuration, the frozen line prices, the
/// frozen names — and never re-runs the pricing engine. That is the whole point: a reprint six months
/// later must show the taxes and prices that were in force on the day (guide p.56), and the only way
/// to guarantee that is to never recalculate.
/// </para>
/// </summary>
public sealed class ReceiptBuilder
{
    private readonly IApplicationDbContext _db;

    public ReceiptBuilder(IApplicationDbContext db) => _db = db;

    public async Task<ReceiptDocument?> BuildAsync(
        long transactionId,
        ReceiptFormat format,
        bool isReprint,
        CancellationToken ct)
    {
        var transaction = await _db.SalesTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId, ct);
        if (transaction is null)
        {
            return null;
        }

        var lines = await _db.SaleLines.AsNoTracking()
            .Where(l => l.TransactionId == transactionId)
            .OrderBy(l => l.Sequence)
            .ToListAsync(ct);

        var adjustments = await _db.SaleAdjustments.AsNoTracking()
            .Where(a => a.TransactionId == transactionId)
            .ToListAsync(ct);

        var tenders = await _db.SaleTenders.AsNoTracking()
            .Where(t => t.TransactionId == transactionId)
            .ToListAsync(ct);

        var taxSnapshot = await _db.SaleTaxSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TransactionId == transactionId, ct);

        var location = await _db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == transaction.LocationId, ct);
        var business = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(b => b.LocationId == transaction.LocationId, ct);
        var station = await _db.Stations.AsNoTracking().FirstOrDefaultAsync(s => s.Id == transaction.StationId, ct);
        var currency = location is null
            ? null
            : await _db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.Code == location.BaseCurrencyCode, ct);

        var staffName = await _db.StaffProfiles.AsNoTracking()
            .Where(s => s.Id == transaction.StaffId)
            .Select(s => s.FullName)
            .FirstOrDefaultAsync(ct);

        var customer = transaction.CustomerId is { } customerId
            ? await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct)
            : null;

        var loyaltyBalance = customer is null
            ? 0
            : await _db.CustomerPricingProfiles.AsNoTracking()
                .Where(p => p.CustomerId == customer.Id)
                .Select(p => p.RewardPoints)
                .FirstOrDefaultAsync(ct);

        var tenderNames = await _db.TenderTypes.AsNoTracking()
            .ToDictionaryAsync(t => t.Id, t => t.DisplayName, ct);

        var policy = await _db.PosPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.LocationId == transaction.LocationId, ct);

        var hasCardTender = tenders.Any(t => t.Behaviour == Domain.Configuration.TenderBehaviour.Card);

        // A packing slip deliberately carries no money (guide p.12).
        var showMoney = format != ReceiptFormat.PackingSlip;

        return new ReceiptDocument(
            transaction.Id,
            transaction.TransactionNumber,
            format,
            business?.BusinessName ?? location?.Name ?? string.Empty,
            business?.Address.ToLines() ?? [],
            taxSnapshot?.TaxRegistrationNumber,
            station?.StationCode ?? string.Empty,
            staffName ?? string.Empty,
            customer?.FullName,
            customer?.BillingAddress.ToLines(),
            transaction.CompletedAt,
            GroupLines(lines, showMoney),
            showMoney ? adjustments.Select(a => new ReceiptAdjustment(a.Label, a.Amount)).ToList() : [],
            showMoney ? transaction.Subtotal : 0m,
            showMoney ? transaction.DiscountTotal : 0m,
            taxSnapshot?.Tax1Name ?? string.Empty,
            showMoney ? transaction.Tax1Total : 0m,
            taxSnapshot?.Tax2Name ?? string.Empty,
            showMoney ? transaction.Tax2Total : 0m,
            taxSnapshot?.AddOnName ?? string.Empty,
            showMoney ? transaction.AddOnChargeTotal : 0m,
            showMoney ? transaction.RoundingAdjustment : 0m,
            showMoney ? transaction.GrandTotal : 0m,
            showMoney
                ? tenders.Select(t => new ReceiptTender(
                    tenderNames.TryGetValue(t.TenderTypeId, out var name) ? name : "Tender",
                    t.Amount,
                    t.AmountTendered,
                    t.ChangeGiven,
                    t.Reference ?? t.AuthCode)).ToList()
                : [],
            showMoney ? transaction.ChangeGiven : 0m,
            transaction.LoyaltyPointsEarned,
            loyaltyBalance,
            currency?.Symbol ?? string.Empty,
            null,
            isReprint,
            transaction.Status == TransactionStatus.Voided,
            hasCardTender && (policy?.PrintCreditCardSignatureLine ?? true),
            transaction.IsTraining);
    }

    /// <summary>
    /// One receipt line per thing bought, not one per tag read.
    /// <para>
    /// Each tagged unit is its own sale line, deliberately: it is what lets one of three identical
    /// shirts be returned on its own, and what puts the right tag back into stock when it is. But a
    /// customer who bought three of a shirt was handed a receipt listing the shirt three times, which
    /// reads as three different things and invites the question of which one they were charged for.
    /// Tagged units of the same item at the same price now print as one line with the quantity and
    /// the tags beneath it.
    /// </para>
    /// <para>
    /// Only tagged units are gathered. Untagged items already merge into one line in the cart, and
    /// the lines that stay separate do so for a reason — two weighed packs of the same cheese are two
    /// packs, and the customer is entitled to see each weight. Nothing that makes two lines different
    /// to the customer is folded away: price, discount, price level, tax and line type are all part
    /// of what has to match. The totals are untouched, because they were never computed from these
    /// lines but frozen with the sale.
    /// </para>
    /// </summary>
    private static List<ReceiptLine> GroupLines(IReadOnlyList<SaleLine> lines, bool showMoney)
        => lines
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Epc)
                ? new LineKey(l.Sequence, 0, null, null, null, 0m, 0m, false, false, default, default, null)
                : new LineKey(-1, l.ProductId, l.VariantId, l.StockCodeSnapshot, l.NameSnapshot, l.UnitPrice, l.DiscountPct, l.Tax1Applies, l.Tax2Applies, l.LineType, l.PriceOrigin, l.Note))
            .Select(group =>
            {
                var first = group.First();
                var epcs = group.Select(l => l.Epc).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e!).ToList();

                return new ReceiptLine(
                    first.StockCodeSnapshot ?? string.Empty,
                    first.NameSnapshot ?? string.Empty,
                    group.Sum(l => l.Quantity),
                    showMoney ? first.UnitPrice : 0m,
                    showMoney ? group.Sum(l => l.ExtendedNet) : 0m,
                    DescribeOrigin(first.PriceOrigin),
                    first.Note,
                    first.Tax1Applies,
                    first.Tax2Applies,
                    first.LineType != LineType.Sale,
                    epcs.Count > 0 ? epcs : null);
            })
            .ToList();

    /// <summary>
    /// What two sale lines must share to print as one. An untagged line carries its own sequence, so
    /// it never matches anything but itself.
    /// </summary>
    private readonly record struct LineKey(
        int UntaggedSequence,
        long ProductId,
        long? VariantId,
        string? StockCode,
        string? Name,
        decimal UnitPrice,
        decimal DiscountPct,
        bool Tax1Applies,
        bool Tax2Applies,
        LineType LineType,
        PriceOrigin PriceOrigin,
        string? Note);

    /// <summary>
    /// A short badge explaining a non-regular price. Printing it is how a customer can see that the
    /// break-point or sale price they were promised actually applied.
    /// </summary>
    private static string? DescribeOrigin(PriceOrigin origin) => origin switch
    {
        PriceOrigin.Regular => null,
        PriceOrigin.Sale => "SALE",
        PriceOrigin.Break => "QTY",
        PriceOrigin.Bonus => "BONUS",
        PriceOrigin.Manual => "OVERRIDE",
        PriceOrigin.RandomWeight => "WEIGHED",
        PriceOrigin.ClientLevel => "CLIENT",
        PriceOrigin.Level2 => "LVL2",
        PriceOrigin.Level3 => "LVL3",
        PriceOrigin.Level4 => "LVL4",
        _ => null,
    };
}
